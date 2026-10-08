using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Auth;
using PhoneStore.Api.Services.InventoryManagement;

namespace PhoneStore.IntegrationTests;

[Collection("SQL")]
public sealed class InventoryTests(SqlFixture sql)
{
    private static async Task<HttpResponseMessage> Send(HttpClient client, string route, object body)
    {
        var token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/" + route) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token); return await client.SendAsync(request);
    }
    private static async Task<HttpClient> Account(AuthFactory host, bool admin = true)
    {
        var client = host.Browser(); var email = Guid.NewGuid().ToString("N") + "@example.invalid";
        Assert.Equal(HttpStatusCode.Accepted, (await Send(client, "auth/register", new { email, fullName = "Inventory test", password = "Synthetic!Password123" })).StatusCode);
        var mail = host.Mailbox.Messages.Single(x => x.Email == email);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, "auth/verify-email", new { mail.UserId, mail.Token })).StatusCode);
        if (admin) { using var scope = host.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<AuthBootstrap>().RunAsync(email, default); }
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, "auth/login", new { email, password = "Synthetic!Password123" })).StatusCode);
        return client;
    }
    private async Task<long> Variant(int onHand = 0, int reserved = 0)
    {
        await using var db = sql.CreateContext(); var key = Guid.NewGuid().ToString("N");
        var product = new Product { Name = "Inventory " + key, Slug = "inventory-" + key, Brand = new Brand { Name = key, Slug = key }, Category = new Category { Name = key, Slug = key } };
        var variant = new ProductVariant { Product = product, Sku = key, Color = "Black", StorageGb = 128, RamGb = 8, Price = 100, Inventory = new Inventory { OnHand = onHand, Reserved = reserved } };
        db.ProductVariants.Add(variant); await db.SaveChangesAsync(); return variant.Id;
    }
    private static object Receipt(string key, int quantity = 10, string reason = "Synthetic receive") => new { operationKey = key, quantity, reason };
    private static object Adjustment(string key, int quantityDelta, string reason = "Synthetic adjust") => new { operationKey = key, quantityDelta, reason };
    private async Task AssertState(long id, int onHand, int reserved, int movements)
    {
        await using var db = sql.CreateContext(); var inventory = await db.Inventory.SingleAsync(x => x.VariantId == id);
        Assert.Equal(onHand, inventory.OnHand); Assert.Equal(reserved, inventory.Reserved);
        Assert.Equal(movements, await db.InventoryMovements.CountAsync(x => x.VariantId == id));
        Assert.All(await db.InventoryMovements.Where(x => x.VariantId == id).ToListAsync(), x => { Assert.NotNull(x.ActorUserId); Assert.Equal(32, x.RequestHash?.Length); Assert.Equal(0, x.ReservedDelta); });
    }
    [Fact]
    public async Task Receive_adjust_replay_returns_original_movement_and_current_balance()
    {
        using var host = new AuthFactory(sql.ConnectionString); using var admin = await Account(host); var id = await Variant(); var key = Guid.NewGuid().ToString();
        var initial = await Send(admin, $"admin/inventory/{id}/receipts", Receipt(key)); Assert.Equal(HttpStatusCode.Created, initial.StatusCode);
        var first = await initial.Content.ReadFromJsonAsync<JsonElement>(); Assert.False(first.GetProperty("isReplay").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, (await Send(admin, $"admin/inventory/{id}/adjustments", Adjustment(Guid.NewGuid().ToString(), -3))).StatusCode);
        var replay = await Send(admin, $"admin/inventory/{id}/receipts", Receipt(key)); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var again = await replay.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(again.GetProperty("isReplay").GetBoolean());
        Assert.Equal(first.GetProperty("movement").GetProperty("id").GetString(), again.GetProperty("movement").GetProperty("id").GetString());
        Assert.Equal(7, again.GetProperty("inventory").GetProperty("onHand").GetInt32());
        await AssertState(id, 7, 0, 2);
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/inventory/{id}"); Assert.Equal(7, detail.GetProperty("available").GetInt32());
        var list = await admin.GetAsync("/api/v1/admin/inventory?search=" + detail.GetProperty("sku").GetString()); Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var page = await list.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        var history = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/inventory/{id}/movements?pageSize=1&page=2"); Assert.Single(history.GetProperty("items").EnumerateArray()); Assert.Equal(2, history.GetProperty("totalCount").GetInt32());
    }
    [Fact]
    public async Task Same_key_changed_payload_variant_kind_or_actor_conflicts()
    {
        using var host = new AuthFactory(sql.ConnectionString); using var admin = await Account(host); using var other = await Account(host);
        var id = await Variant(); var second = await Variant(); var key = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.Created, (await Send(admin, $"admin/inventory/{id}/receipts", Receipt(key))).StatusCode);
        foreach (var response in new[] { await Send(admin, $"admin/inventory/{id}/receipts", Receipt(key, 11)), await Send(admin, $"admin/inventory/{id}/receipts", Receipt(key, reason: "Changed")), await Send(admin, $"admin/inventory/{second}/receipts", Receipt(key)), await Send(admin, $"admin/inventory/{id}/adjustments", Adjustment(key, 10)), await Send(other, $"admin/inventory/{id}/receipts", Receipt(key)) })
        { Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Equal("OPERATION_KEY_CONFLICT", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()); }
        await AssertState(id, 10, 0, 1); await AssertState(second, 0, 0, 0);
    }
    [Fact]
    public async Task Concurrent_same_key_is_exactly_one_movement_and_new_keys_add_without_lost_updates()
    {
        using var host = new AuthFactory(sql.ConnectionString); using var admin = await Account(host); var id = await Variant(); var key = Guid.NewGuid().ToString();
        var token = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString(); admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        var same = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => admin.PostAsJsonAsync($"/api/v1/admin/inventory/{id}/receipts", Receipt(key))));
        Assert.Single(same, x => x.StatusCode == HttpStatusCode.Created); Assert.Equal(4, same.Count(x => x.StatusCode == HttpStatusCode.OK)); await AssertState(id, 10, 0, 1);
        var different = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => admin.PostAsJsonAsync($"/api/v1/admin/inventory/{id}/receipts", Receipt(Guid.NewGuid().ToString(), 1))));
        Assert.All(different, x => Assert.Equal(HttpStatusCode.Created, x.StatusCode)); await AssertState(id, 15, 0, 6);
    }
    [Fact]
    public async Task Reserved_bound_overflow_and_failed_operations_leave_balance_and_ledger_unchanged()
    {
        using var host = new AuthFactory(sql.ConnectionString); using var admin = await Account(host); var id = await Variant(10, 8);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, $"admin/inventory/{id}/adjustments", Adjustment(Guid.NewGuid().ToString(), -3))).StatusCode); await AssertState(id, 10, 8, 0);
        var token = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString(); admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        var race = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => admin.PostAsJsonAsync($"/api/v1/admin/inventory/{id}/adjustments", Adjustment(Guid.NewGuid().ToString(), -2))));
        Assert.Single(race, x => x.StatusCode == HttpStatusCode.Created); Assert.Single(race, x => x.StatusCode == HttpStatusCode.Conflict); await AssertState(id, 8, 8, 1);
        var max = await Variant(int.MaxValue); Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, $"admin/inventory/{max}/receipts", Receipt(Guid.NewGuid().ToString(), 1))).StatusCode); await AssertState(max, int.MaxValue, 0, 0);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, $"admin/inventory/{max}/adjustments", Adjustment(Guid.NewGuid().ToString(), int.MinValue))).StatusCode); await AssertState(max, int.MaxValue, 0, 0);
    }
    [Fact]
    public async Task Sql_failure_writing_ledger_rolls_back_balance_too()
    {
        var id = await Variant(5);
        await using var db = sql.CreateContext();
        var service = new InventoryService(db, TimeProvider.System);
        // Synthetic missing actor causes the real SQL FK to reject the ledger insert.
        await Assert.ThrowsAsync<DbUpdateException>(() => service.WriteAsync(id, Guid.NewGuid(), InventoryMovementKind.Receive, 4, "Synthetic SQL failure", Guid.NewGuid().ToString(), default));
        await AssertState(id, 5, 0, 0);
    }
    [Fact]
    public async Task Permissions_csrf_unknown_fields_and_invalid_commands_never_write()
    {
        using var host = new AuthFactory(sql.ConnectionString); using var anonymous = host.Browser(); using var customer = await Account(host, false); using var admin = await Account(host); var id = await Variant();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/inventory")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/admin/inventory")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(customer, $"admin/inventory/{id}/receipts", Receipt(Guid.NewGuid().ToString()))).StatusCode);
        var noCsrf = await admin.PostAsJsonAsync($"/api/v1/admin/inventory/{id}/receipts", Receipt(Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode); Assert.Equal("CSRF_INVALID", (await noCsrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        foreach (var body in new object[] { new { quantity = 1, reason = "Test", operationKey = Guid.NewGuid().ToString(), reserved = 9 }, Receipt(Guid.NewGuid().ToString(), 0), Receipt("unsafe-key"), Receipt(Guid.Empty.ToString()), Receipt(Guid.NewGuid().ToString(), reason: " ") })
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, $"admin/inventory/{id}/receipts", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, $"admin/inventory/{id}/adjustments", Adjustment(Guid.NewGuid().ToString(), 0))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/admin/inventory/9223372036854775807")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/admin/inventory?pageSize=101")).StatusCode);
        await AssertState(id, 0, 0, 0);
    }
}
