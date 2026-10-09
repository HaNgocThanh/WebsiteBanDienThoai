using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Auth;

namespace PhoneStore.IntegrationTests;
[Collection("SQL")]
public sealed class OrderReadTests(SqlFixture sql)
{
    private async Task<long> Variant(int onHand = 10)
    {
        await using var db = sql.CreateContext(); var tag = Guid.NewGuid().ToString("N");
        var v = new ProductVariant { Sku = "CHECKOUT-" + tag, Color = "Black", StorageGb = 128, RamGb = 8, Price = 1000000,
            Product = new Product { Name = "Checkout " + tag, Slug = "checkout-" + tag, Brand = new Brand { Name = tag, Slug = tag }, Category = new Category { Name = tag, Slug = tag } }, Inventory = new Inventory { OnHand = onHand } };
        db.ProductVariants.Add(v); await db.SaveChangesAsync(); return v.Id;
    }
    private static async Task<HttpResponseMessage> Send(HttpClient browser, string route, object? body = null, string? key = null, string? csrf = null)
    {
        csrf ??= (await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/" + route);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-CSRF-TOKEN", csrf); if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return await browser.SendAsync(request);
    }
    private static async Task<string> Session(HttpClient browser)
    {
        var response = await Send(browser, "checkout/sessions"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.EndsWith("Z", result.GetProperty("expiresAt").GetString());
        return result.GetProperty("checkoutKey").GetString()!;
    }
    private static async Task<Dictionary<string, object?>> Payload(HttpClient browser, long id, int quantity = 1, string province = "Thành phố Hồ Chí Minh", string email = "guest@example.invalid", string method = "COD")
    {
        var items = new[] { new { variantId = id.ToString(), quantity } };
        var response = await Send(browser, "checkout/quote", new { items, shippingAddress = new { province, countryCode = "VN" } }); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new() { ["items"] = items, ["email"] = email, ["recipientName"] = "Synthetic recipient", ["phone"] = "0000000000", ["addressLine"] = "Synthetic street", ["province"] = province, ["countryCode"] = "VN", ["paymentMethod"] = method, ["quoteHash"] = quote.GetProperty("quoteHash").GetString(), ["createAccountConsent"] = false, ["consentTextVersion"] = "account-create-v1" };
    }
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode); var data = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(code, data.GetProperty("code").GetString()); Assert.True(response.Headers.CacheControl!.NoStore);
    }
    private static async Task<(string Email, Guid Id)> Login(AuthFactory host, HttpClient browser, bool admin = false)
    {
        var email = Guid.NewGuid().ToString("N") + "@example.invalid";
        Assert.Equal(HttpStatusCode.Accepted, (await Send(browser, "auth/register", new { email, password = "Synthetic!Password123", fullName = "Synthetic checkout user" })).StatusCode);
        var mail = host.Mailbox.Messages.Single(m => m.Email == email);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(browser, "auth/verify-email", new { mail.UserId, mail.Token })).StatusCode);
        if (admin) { using var scope = host.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<AuthBootstrap>().RunAsync(email, default); }
        Assert.Equal(HttpStatusCode.NoContent, (await Send(browser, "auth/login", new { email, password = "Synthetic!Password123" })).StatusCode);
        return (email, mail.UserId);
    }

    private static async Task<JsonElement> Place(HttpClient browser, long variant, string email)
    {
        var response = await Send(browser, "orders", await Payload(browser, variant, email: email), await Session(browser)); Assert.Equal(HttpStatusCode.Created, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    [Fact]
    public async Task Owner_admin_guest_access_and_immutable_snapshots_never_expose_internal_content()
    {
        var v = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var a = host.Browser(); using var b = host.Browser(); using var admin = host.Browser(); using var guest = host.Browser();
        var account = await Login(host, a); await Login(host, b); await Login(host, admin, true);
        var order = await Place(a, v, account.Email); var id = order.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/v1/me/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.GetAsync("/api/v1/admin/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/api/v1/me/orders/" + id)).StatusCode);
        var before = await a.GetFromJsonAsync<JsonElement>("/api/v1/me/orders/" + id);
        var op = Guid.NewGuid(); Assert.Equal(HttpStatusCode.OK, (await Send(admin, "admin/orders/" + id + "/notes", new { text = "PRIVATE SYNTHETIC NOTE", operationKey = op })).StatusCode);
        await using (var db = sql.CreateContext()) {
            var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == v); variant.Price = 1200000; variant.Product.Name = "Changed live catalog";
            var profile = await db.Users.SingleAsync(x => x.Id == account.Id); profile.FullName = "Changed profile";
            var history = await db.OrderStatusHistories.SingleAsync(x => x.OrderId == long.Parse(id)); history.Reason = "PRIVATE SYNTHETIC REASON"; await db.SaveChangesAsync();
        }
        var after = await a.GetFromJsonAsync<JsonElement>("/api/v1/me/orders/" + id);
        Assert.Equal(before.GetProperty("snapshot").ToString(), after.GetProperty("snapshot").ToString()); Assert.DoesNotContain("PRIVATE", after.ToString()); Assert.False(after.TryGetProperty("notes", out _)); Assert.False(after.TryGetProperty("customerEmail", out _)); Assert.Empty(after.GetProperty("allowedActions").EnumerateArray());
        var detail = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/orders/" + id); Assert.Contains("PRIVATE SYNTHETIC NOTE", detail.ToString()); Assert.Contains("PRIVATE SYNTHETIC REASON", detail.ToString()); Assert.Single(detail.GetProperty("reservations").EnumerateArray());
        var list = await b.GetFromJsonAsync<JsonElement>("/api/v1/me/orders"); Assert.DoesNotContain(id, list.GetProperty("items").ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await a.GetAsync("/api/v1/me/orders/01")).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await a.GetAsync("/api/v1/me/orders/9223372036854775808")).StatusCode);
    }
    [Fact]
    public async Task Filters_use_utc_inclusive_from_exclusive_to_and_stable_paging()
    {
        var v = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser(); using var admin = host.Browser(); var user = await Login(host, browser); await Login(host, admin, true);
        var ids = new List<long>(); for (var i = 0; i < 3; i++) ids.Add(long.Parse((await Place(browser, v, user.Email)).GetProperty("id").GetString()!));
        await using (var db = sql.CreateContext()) { for (var i = 0; i < 3; i++) { var o = await db.Orders.SingleAsync(o => o.Id == ids[i]); o.CreatedAt = DateTime.Parse("2026-10-08T17:00:00Z").ToUniversalTime().AddDays(i == 2 ? 1 : 0); } await db.SaveChangesAsync(); }
        var query = "?from=2026-10-08T17:00:00Z&to=2026-10-09T17:00:00Z&pageSize=1&status=Placed";
        var first = await browser.GetFromJsonAsync<JsonElement>("/api/v1/me/orders" + query); var next = await browser.GetFromJsonAsync<JsonElement>("/api/v1/me/orders" + query + "&page=2");
        Assert.Equal(2, first.GetProperty("totalCount").GetInt32()); Assert.Equal(ids[1].ToString(), first.GetProperty("items")[0].GetProperty("id").GetString()); Assert.Equal(ids[0].ToString(), next.GetProperty("items")[0].GetProperty("id").GetString());
        var filtered = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/orders" + query + "&customerId=" + user.Id + "&email=" + Uri.EscapeDataString(user.Email)); Assert.Equal(2, filtered.GetProperty("totalCount").GetInt32());
        foreach (var invalid in new[] { "?status=0", "?status=placed", "?from=2026-10-09", "?from=2026-10-09T00:00:00%2B07:00", "?from=2026-10-10T00:00:00Z&to=2026-10-09T00:00:00Z", "?page=0", "?page=2147483647", "?pageSize=101", "?customerId=" + user.Id }) Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync("/api/v1/me/orders" + invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/admin/orders?customerId=not-uuid")).StatusCode);
    }
    [Fact]
    public async Task Admin_notes_append_idempotently_under_race_and_do_not_mutate_order_or_inventory()
    {
        var v = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var admin = host.Browser(); var user = await Login(host, admin, true); var placed = await Place(admin, v, user.Email); var id = placed.GetProperty("id").GetString()!; var route = "admin/orders/" + id + "/notes"; var op = Guid.NewGuid(); var csrf = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString()!;
        using var noCsrf = new HttpRequestMessage(HttpMethod.Post, "/api/v1/" + route) { Content = JsonContent.Create(new { text = "synthetic", operationKey = op }) }; Assert.Equal(HttpStatusCode.Forbidden, (await admin.SendAsync(noCsrf)).StatusCode);
        var before = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/orders/" + id);
        var results = await Task.WhenAll(Send(admin, route, new { text = "Synthetic note", operationKey = op }, csrf: csrf), Send(admin, route, new { text = "Synthetic note", operationKey = op }, csrf: csrf)); foreach (var r in results) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal((await results[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString(), (await results[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, route, new { text = "Different", operationKey = op })).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, route, new { text = "  ", operationKey = Guid.NewGuid() })).StatusCode);
        var after = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/orders/" + id); Assert.Equal(before.GetProperty("version").GetString(), after.GetProperty("version").GetString()); Assert.Single(after.GetProperty("notes").EnumerateArray());
        await using var db = sql.CreateContext(); Assert.Equal(1, await db.OrderInternalNotes.CountAsync(n => n.OrderId == long.Parse(id))); Assert.Equal(1, (await db.Inventory.SingleAsync(x => x.VariantId == v)).Reserved);
    }
    [Fact]
    public async Task Notifications_are_owner_scoped_and_concurrent_read_keeps_first_timestamp()
    {
        using var host = new AuthFactory(sql.ConnectionString); using var a = host.Browser(); using var b = host.Browser(); var user = await Login(host, a); await Login(host, b); long id;
        await using (var db = sql.CreateContext()) { var n = new Notification { UserId = user.Id, Type = "Synthetic", Title = "Synthetic title", Body = "Synthetic body", EventKey = Guid.NewGuid().ToString(), CreatedAt = DateTime.UtcNow }; db.Notifications.Add(n); await db.SaveChangesAsync(); id = n.Id; }
        var list = await a.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications?unreadOnly=true&pageSize=1"); Assert.Equal(1, list.GetProperty("unreadCount").GetInt32()); Assert.Equal(id.ToString(), list.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Send(b, "me/notifications/" + id + "/read")).StatusCode);
        var csrf = (await a.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString(); foreach (var r in await Task.WhenAll(Send(a, "me/notifications/" + id + "/read", csrf: csrf), Send(a, "me/notifications/" + id + "/read", csrf: csrf))) Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        await using var verify = sql.CreateContext(); var time = (await verify.Notifications.AsNoTracking().SingleAsync(n => n.Id == id)).ReadAt; Assert.NotNull(time); await Send(a, "me/notifications/" + id + "/read"); Assert.Equal(time, (await verify.Notifications.AsNoTracking().SingleAsync(n => n.Id == id)).ReadAt);
        var unread = await a.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications?unreadOnly=true"); Assert.Equal(0, unread.GetProperty("unreadCount").GetInt32()); Assert.Empty(unread.GetProperty("items").EnumerateArray());
    }
    [Fact]
    public async Task Payment_summary_only_counts_confirmed_receipts_and_separates_pending_and_completed_refunds()
    {
        var v = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser(); var user = await Login(host, browser); var id = long.Parse((await Place(browser, v, user.Email)).GetProperty("id").GetString()!);
        await using (var db = sql.CreateContext()) {
            var payment = new Payment { OrderId = id, Amount = 600000, Status = PaymentStatus.Confirmed, Method = PaymentMethod.COD, Note = "PRIVATE PAYMENT NOTE", Reference = "PRIVATE REFERENCE", ProofUrl = "PRIVATE PROOF", CreatedAt = DateTime.UtcNow };
            db.Payments.AddRange(payment, new Payment { OrderId = id, Amount = 400000, Status = PaymentStatus.Pending, CreatedAt = DateTime.UtcNow }, new Payment { OrderId = id, Amount = 200000, Status = PaymentStatus.Rejected, CreatedAt = DateTime.UtcNow }); await db.SaveChangesAsync();
            db.Refunds.AddRange(new Refund { PaymentId = payment.Id, Status = RefundStatus.Completed, MerchandiseAmount = 100000, CreatedByUserId = user.Id, Reason = "Synthetic", EventKey = Guid.NewGuid().ToString(), RequestHash = new byte[32], CreatedAt = DateTime.UtcNow }, new Refund { PaymentId = payment.Id, Status = RefundStatus.Pending, ShippingAmount = 50000, CreatedByUserId = user.Id, Reason = "Synthetic", EventKey = Guid.NewGuid().ToString(), RequestHash = new byte[32], CreatedAt = DateTime.UtcNow }); await db.SaveChangesAsync();
        }
        var detail = await browser.GetFromJsonAsync<JsonElement>("/api/v1/me/orders/" + id); var money = detail.GetProperty("paymentSummary"); Assert.Equal(600000, money.GetProperty("confirmed").GetDecimal()); Assert.Equal(400000, money.GetProperty("remaining").GetDecimal()); Assert.Equal(100000, money.GetProperty("refunded").GetDecimal()); Assert.Equal(50000, money.GetProperty("refundPending").GetDecimal()); Assert.DoesNotContain("PRIVATE", detail.ToString());
    }

    [Fact]
    public async Task Admin_keeps_customer_permissions_but_shop_notifications_and_personal_feed_are_separate()
    {
        var v = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var customer = host.Browser(); using var admin = host.Browser(); using var guest = host.Browser(); var a = await Login(host, customer); var manager = await Login(host, admin, true);
        var customerOrder = long.Parse((await Place(customer, v, a.Email)).GetProperty("id").GetString()!); var ownOrder = long.Parse((await Place(admin, v, manager.Email)).GetProperty("id").GetString()!); long otherNote;
        await using (var db = sql.CreateContext()) {
            var other = new Notification { UserId = manager.Id, OrderId = customerOrder, Type = "OrderPlaced", Title = "Shop order", Body = "Synthetic", EventKey = Guid.NewGuid().ToString(), CreatedAt = DateTime.UtcNow }; db.Notifications.AddRange(other, new Notification { UserId = manager.Id, OrderId = ownOrder, Type = "OrderPlaced", Title = "Own order", Body = "Synthetic", EventKey = Guid.NewGuid().ToString(), CreatedAt = DateTime.UtcNow }); await db.SaveChangesAsync(); otherNote = other.Id;
        }
        var session = await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/session"); Assert.Contains("Admin", session.GetProperty("roles").EnumerateArray().Select(x => x.GetString())); Assert.Contains("Customer", session.GetProperty("roles").EnumerateArray().Select(x => x.GetString()));
        var personal = await admin.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications"); Assert.Equal(1, personal.GetProperty("totalCount").GetInt32()); Assert.Equal(ownOrder.ToString(), personal.GetProperty("items")[0].GetProperty("orderId").GetString());
        var shop = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/notifications"); Assert.Equal(2, shop.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await Send(admin, "me/notifications/" + otherNote + "/read")).StatusCode); Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, "admin/notifications/" + otherNote + "/read")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/admin/notifications")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Send(customer, "admin/notifications/" + otherNote + "/read")).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/v1/admin/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/me/orders/" + ownOrder)).StatusCode);
    }

}
