using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Entities;
using Microsoft.Extensions.DependencyInjection;
using PhoneStore.Api.Services.Auth;
namespace PhoneStore.IntegrationTests;
public sealed partial class PaymentTests
{
    [Fact]
    public async Task Bootstrap_role_grant_racing_customer_disable_never_creates_inactive_Admin()
    {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); using var customer = host.Browser(); await Login(host.Server, admin, true); var target = await Login(host.Server, customer);
        var profile = await CustomerProfile(admin, target.Id);
        async Task<bool> Promote() { using var scope = host.Server.Services.CreateScope(); try { await scope.ServiceProvider.GetRequiredService<AuthBootstrap>().RunAsync(target.Email, default); return true; } catch (InvalidOperationException) { return false; } }
        var promotion = Promote(); var disable = UpdateCustomer(admin, target.Id, new { fullName = "Synthetic racing status", phone = "000", isActive = false, version = profile.GetProperty("version").GetString(), operationKey = Guid.NewGuid() });
        await Task.WhenAll(promotion, disable); var response = await disable; Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed, HttpStatusCode.Conflict });
        await using var db = sql.CreateContext(); var user = await db.Users.SingleAsync(u => u.Id == target.Id); var adminRole = await db.Roles.SingleAsync(r => r.Name == "Admin"); var hasAdmin = await db.UserRoles.AnyAsync(r => r.UserId == target.Id && r.RoleId == adminRole.Id); Assert.True(user.IsActive || !hasAdmin); Assert.Equal(await promotion, hasAdmin);
    }
    private static async Task<HttpResponseMessage> UpdateCustomer(HttpClient client, Guid id, object body, bool csrf = true) {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/customers/" + id + "/profile") { Content = JsonContent.Create(body) };
        if (csrf) request.Headers.Add("X-CSRF-TOKEN", (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString()); return await client.SendAsync(request);
    }
    private static Task<JsonElement> CustomerProfile(HttpClient client, Guid id) => client.GetFromJsonAsync<JsonElement>("/api/v1/admin/customers/" + id + "/profile");
    [Fact]
    public async Task Admin_customer_profile_roles_CSRF_immutable_fields_stale_and_concurrent_retry_are_enforced()
    {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); using var customer = host.Browser(); using var guest = host.Browser(); var actor = await Login(host.Server, admin, true); var target = await Login(host.Server, customer);
        var route = "/api/v1/admin/customers/" + target.Id + "/profile"; var profile = await CustomerProfile(admin, target.Id); var version = profile.GetProperty("version").GetString(); var key = Guid.NewGuid();
        var update = new { fullName = "Synthetic supported customer", phone = "0900000000", isActive = true, version, operationKey = key };
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync(route)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(route)).StatusCode);
        Assert.True((await admin.GetAsync(route)).Headers.CacheControl!.NoStore);
        Assert.Equal(HttpStatusCode.Forbidden, (await UpdateCustomer(customer, target.Id, update)).StatusCode); await Problem(await UpdateCustomer(admin, target.Id, update, false), HttpStatusCode.Forbidden, "CSRF_INVALID");
        Assert.Equal(HttpStatusCode.BadRequest, (await UpdateCustomer(admin, target.Id, new { update.fullName, update.phone, update.isActive, update.version, update.operationKey, email = "changed@example.invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UpdateCustomer(admin, target.Id, new { update.fullName, update.phone, update.version, update.operationKey })).StatusCode);
        foreach (var result in await Task.WhenAll(UpdateCustomer(admin, target.Id, update), UpdateCustomer(admin, target.Id, update))) await Ok(result);
        Assert.Equal("Synthetic supported customer", (await CustomerProfile(admin, target.Id)).GetProperty("fullName").GetString());
        await Problem(await UpdateCustomer(admin, target.Id, new { fullName = "Different", phone = "0900000000", isActive = true, version, operationKey = key }), HttpStatusCode.Conflict, "OPERATION_KEY_MISMATCH");
        await Problem(await UpdateCustomer(admin, target.Id, new { fullName = "Stale", phone = "0900000000", isActive = true, version, operationKey = Guid.NewGuid() }), HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT");
        await using var db = sql.CreateContext(); var user = await db.Users.SingleAsync(u => u.Id == target.Id); Assert.Equal(target.Email, user.Email); Assert.True(user.EmailConfirmed); Assert.Equal(0, (await db.CustomerProfiles.SingleAsync(p => p.UserId == target.Id)).EligibleSpend);
        var audit = await db.AuditLogs.SingleAsync(a => a.CorrelationId == "Customer.Update:" + key); Assert.Equal(actor.Id, audit.ActorUserId); Assert.DoesNotContain(user.FullName, audit.Summary);
        var receipt = await db.OutboxMessages.SingleAsync(m => m.EventKey == "Customer.Update:" + key); Assert.DoesNotContain(user.PhoneNumber!, receipt.PayloadJson); Assert.DoesNotContain(user.FullName, receipt.PayloadJson);
        var csrfToken = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString(); using var config = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/customer-tiers") { Content = JsonContent.Create(new { }), }; config.Headers.Add("X-CSRF-TOKEN", csrfToken); Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.SendAsync(config)).StatusCode);
    }
    [Fact]
    public async Task Customer_disable_enable_revokes_old_sessions_and_protects_Admin_self_and_order_snapshots()
    {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); using var customer = host.Browser(); var actor = await Login(host.Server, admin, true); var target = await Login(host.Server, customer); var order = await Place(customer, target.Email, "COD");
        var before = await CustomerProfile(admin, target.Id);
        await Ok(await UpdateCustomer(admin, target.Id, new { fullName = "Synthetic changed name", phone = "000", isActive = false, version = before.GetProperty("version").GetString(), operationKey = Guid.NewGuid() }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.GetAsync("/api/v1/auth/session")).StatusCode);
        var locked = await CustomerProfile(admin, target.Id); await Ok(await UpdateCustomer(admin, target.Id, new { fullName = "Synthetic changed name", phone = "000", isActive = true, version = locked.GetProperty("version").GetString(), operationKey = Guid.NewGuid() }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.GetAsync("/api/v1/auth/session")).StatusCode);
        var self = await CustomerProfile(admin, actor.Id); Assert.False(self.GetProperty("canChangeStatus").GetBoolean());
        await Problem(await UpdateCustomer(admin, actor.Id, new { fullName = "Synthetic", phone = "000", isActive = false, version = self.GetProperty("version").GetString(), operationKey = Guid.NewGuid() }), HttpStatusCode.Conflict, "ADMIN_ACCOUNT_PROTECTED");
        await using var db = sql.CreateContext(); var snapshot = await db.Orders.SingleAsync(o => o.Id == long.Parse(order.Id)); Assert.Equal("Synthetic recipient", snapshot.RecipientName); Assert.Equal(target.Email, snapshot.CustomerEmail); Assert.Equal(OrderStatus.Reviewing, snapshot.Status);
        var notFound = await admin.GetAsync("/api/v1/admin/customers/" + Guid.NewGuid() + "/addresses"); Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }
}
