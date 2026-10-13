using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhoneStore.Api.Services.Membership;
using PhoneStore.Api.Entities;
namespace PhoneStore.IntegrationTests;
public sealed partial class PaymentTests
{
    [Fact]
    public async Task Two_orders_completed_concurrently_accumulate_both_amounts_and_history_once_per_actual_change()
    {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true);
        using (var scope = host.Server.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<MembershipService>().BootstrapAsync(default);
        var orders = new[] { await Place(admin, actor.Email, "COD"), await Place(admin, actor.Email, "COD") };
        foreach (var order in orders) { await Advance(admin, order.Id, "Approved"); await Advance(admin, order.Id, "Shipping"); await Advance(admin, order.Id, "Delivered"); await Ok(await Send(admin, "admin/orders/" + order.Id + "/cod-receipts", new { amount = 1000000, note = "Synthetic concurrent completion", operationKey = Guid.NewGuid() })); }
        var firstVersion = await OrderVersion(admin, orders[0].Id); var secondVersion = await OrderVersion(admin, orders[1].Id);
        foreach (var r in await Task.WhenAll(Transition(admin, orders[0].Id, "Completed", firstVersion), Transition(admin, orders[1].Id, "Completed", secondVersion))) await Ok(r);
        var membership = await admin.GetFromJsonAsync<JsonElement>("/api/v1/me/membership"); Assert.Equal(2000000, membership.GetProperty("eligibleSpend").GetDecimal()); Assert.Equal("Bronze", membership.GetProperty("tierCode").GetString());
        await using var db = sql.CreateContext(); Assert.Equal(2, await db.CustomerSpendEntries.CountAsync(e => e.UserId == actor.Id)); Assert.Single(await db.CustomerTierHistories.Where(h => h.UserId == actor.Id).ToListAsync());
    }
    [Fact]
    public async Task Configuration_racing_completion_leaves_profile_at_final_policy_without_lost_spend()
    {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true);
        using (var scope = host.Server.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<MembershipService>().BootstrapAsync(default);
        var order = await Place(admin, actor.Email, "COD"); await Advance(admin, order.Id, "Approved"); await Advance(admin, order.Id, "Shipping"); await Advance(admin, order.Id, "Delivered"); await Ok(await Send(admin, "admin/orders/" + order.Id + "/cod-receipts", new { amount = 1000000, note = "Synthetic", operationKey = Guid.NewGuid() }));
        var amounts = new decimal[] { 2000000, 20000000, 60000000, 120000000 };
        var version = await OrderVersion(admin, order.Id);
        async Task<HttpResponseMessage> ConfigureForTest() {
            using var scope = host.Server.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<MembershipService>(); var current = await service.ConfigurationAsync(default);
            await service.ConfigureAsync(new() { OperationKey = Guid.NewGuid(), Reason = "Synthetic internal config race", Tiers = current.Tiers.Select((t, i) => new PhoneStore.Api.DTOs.Membership.TierInput { Code = t.Code, Version = t.Version, MinimumSpend = amounts[i] }).ToArray() }, actor.Id, default);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { }) };
        }
        foreach (var r in await Task.WhenAll(ConfigureForTest(), Transition(admin, order.Id, "Completed", version))) await Ok(r);
        var membership = await admin.GetFromJsonAsync<JsonElement>("/api/v1/me/membership"); Assert.Equal(1000000, membership.GetProperty("eligibleSpend").GetDecimal()); Assert.Equal(JsonValueKind.Null, membership.GetProperty("tierCode").ValueKind); Assert.Equal(1000000, membership.GetProperty("remainingSpend").GetDecimal());
        using (var scope = host.Server.Services.CreateScope()) {
            var service = scope.ServiceProvider.GetRequiredService<MembershipService>(); var current = await service.ConfigurationAsync(default);
            await service.ConfigureAsync(new() { OperationKey = Guid.NewGuid(), Reason = "Synthetic reset fixture policy", Tiers = current.Tiers.Select((t, i) => new PhoneStore.Api.DTOs.Membership.TierInput { Code = t.Code, Version = t.Version, MinimumSpend = new decimal[] { 1000000, 10000000, 50000000, 100000000 }[i] }).ToArray() }, actor.Id, default);
        }
    }
    [Fact]
    public async Task Membership_owner_API_permissions_and_concurrent_completion_refund_update_once()
    {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); using var other = host.Browser(); using var guest = host.Browser(); var actor = await Login(host.Server, admin, true); await Login(host.Server, other);
        using (var scope = host.Server.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<MembershipService>().BootstrapAsync(default);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/v1/me/membership")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/v1/admin/customer-tiers")).StatusCode);
        var config = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/customer-tiers");
        using var noCsrf = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/customer-tiers") { Content = JsonContent.Create(new { tiers = config.GetProperty("tiers"), reason = "Synthetic", operationKey = Guid.NewGuid() }) };
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.SendAsync(noCsrf)).StatusCode);
        var order = await Place(admin, actor.Email, "COD", "Đà Nẵng"); await Advance(admin, order.Id, "Approved"); await Advance(admin, order.Id, "Shipping"); await Advance(admin, order.Id, "Delivered");
        Assert.Equal(0, (await admin.GetFromJsonAsync<JsonElement>("/api/v1/me/membership")).GetProperty("eligibleSpend").GetDecimal());
        await Ok(await Send(admin, "admin/orders/" + order.Id + "/cod-receipts", new { amount = 1030000, note = "Synthetic", operationKey = Guid.NewGuid() }));
        var key = Guid.NewGuid(); var version = await OrderVersion(admin, order.Id); foreach (var r in await Task.WhenAll(Transition(admin, order.Id, "Completed", version, key), Transition(admin, order.Id, "Completed", version, key))) await Ok(r);
        var membership = await admin.GetFromJsonAsync<JsonElement>("/api/v1/me/membership"); Assert.Equal(1000000, membership.GetProperty("eligibleSpend").GetDecimal()); Assert.Equal("Bronze", membership.GetProperty("tierCode").GetString());
        await using var db = sql.CreateContext(); var payment = await db.Payments.SingleAsync(p => p.OrderId == long.Parse(order.Id));
        var refund = await Ok(await After(admin, "admin/payments/" + payment.Id + "/refunds", await OrderVersion(admin, order.Id), new { reason = "Synthetic partial", merchandiseAmount = 1, shippingAmount = 30000, operationKey = Guid.NewGuid() }));
        var complete = new { reason = "Synthetic verified", reference = Guid.NewGuid().ToString("N"), operationKey = Guid.NewGuid() }; var path = "admin/refunds/" + refund.GetProperty("resourceId").GetString() + "/complete";
        foreach (var r in await Task.WhenAll(After(admin, path, refund.GetProperty("resourceVersion").GetString(), complete), After(admin, path, refund.GetProperty("resourceVersion").GetString(), complete))) await Ok(r);
        membership = await admin.GetFromJsonAsync<JsonElement>("/api/v1/me/membership"); Assert.Equal(999999, membership.GetProperty("eligibleSpend").GetDecimal()); Assert.Equal(JsonValueKind.Null, membership.GetProperty("tierCode").ValueKind);
        Assert.Equal(2, await db.CustomerSpendEntries.CountAsync(e => e.UserId == actor.Id)); Assert.Equal(2, await db.CustomerTierHistories.CountAsync(h => h.UserId == actor.Id));
        var otherSpend = await other.GetFromJsonAsync<JsonElement>("/api/v1/me/membership/spend"); Assert.Empty(otherSpend.GetProperty("items").EnumerateArray());
    }
}
