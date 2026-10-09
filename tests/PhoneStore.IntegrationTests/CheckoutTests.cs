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
public sealed class CheckoutTests(SqlFixture sql)
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
    [Fact]
    public async Task Guest_order_snapshots_inventory_consent_history_outbox_and_replay_are_atomic()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); host.Mailbox.FailDelivery = true; using var browser = host.Browser();
        var key = await Session(browser); var body = await Payload(browser, id, 2); var response = await Send(browser, "orders", body, key);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); var placed = await response.Content.ReadFromJsonAsync<JsonElement>(); var orderId = long.Parse(placed.GetProperty("id").GetString()!);
        Assert.Equal(2000000m, placed.GetProperty("grandTotal").GetDecimal()); Assert.Equal("Placed", placed.GetProperty("status").GetString()); Assert.Equal(JsonValueKind.Null, placed.GetProperty("paymentDueAt").ValueKind);
        await using var db = sql.CreateContext(); var order = await db.Orders.Include(o => o.Items).ThenInclude(i => i.StockReservation).Include(o => o.StatusHistories).Include(o => o.AccountConsent).SingleAsync(o => o.Id == orderId);
        Assert.Null(order.UserId); Assert.Null(order.TierIdAtPurchase); Assert.False(order.AccountConsent!.Accepted); Assert.Equal("account-create-v1", order.AccountConsent.ConsentTextVersion);
        Assert.Equal("Synthetic recipient", order.RecipientName); Assert.Equal("Synthetic street", order.AddressLine); var item = Assert.Single(order.Items);
        Assert.Equal(2, item.Quantity); Assert.Equal(2000000m, item.LineTotal); Assert.Contains("Black · 128 GB", item.VariantSnapshot); Assert.Equal(StockReservationStatus.Active, item.StockReservation!.Status); Assert.Null(item.StockReservation.ExpiresAt);
        Assert.Single(order.StatusHistories); var inventory = await db.Inventory.SingleAsync(i => i.VariantId == id); Assert.Equal(10, inventory.OnHand); Assert.Equal(2, inventory.Reserved);
        var movement = await db.InventoryMovements.SingleAsync(m => m.OrderItemId == item.Id); Assert.Equal(InventoryMovementKind.Reserve, movement.Kind); Assert.Equal(0, movement.OnHandDelta); Assert.Equal(2, movement.ReservedDelta);
        var outbox = await db.OutboxMessages.SingleAsync(m => m.EventKey == "OrderPlaced:" + orderId); Assert.Null(outbox.ProcessedAt); Assert.Equal("OrderPlaced", outbox.Type); Assert.DoesNotContain("guest@example.invalid", outbox.PayloadJson); Assert.Empty(host.Mailbox.Messages);
        var savedKey = await db.IdempotencyRequests.SingleAsync(k => k.Id == Guid.Parse(key)); Assert.Equal(IdempotencyRequestStatus.Completed, savedKey.Status); Assert.Equal(orderId, savedKey.OrderId); Assert.Equal(32, savedKey.GuestSessionHash!.Length); Assert.Equal(32, savedKey.RequestHash!.Length);
        var variant = await db.ProductVariants.Include(v => v.Product).SingleAsync(v => v.Id == id); variant.Price = 2000000; variant.Product.Name = "Changed name"; await db.SaveChangesAsync();
        var retry = await Send(browser, "orders", body, key); Assert.Equal(HttpStatusCode.OK, retry.StatusCode); Assert.Equal(placed.GetRawText(), (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        var snapshot = await db.OrderItems.AsNoTracking().SingleAsync(i => i.Id == item.Id); Assert.Equal(1000000, snapshot.UnitPrice); Assert.NotEqual("Changed name", snapshot.ProductNameSnapshot);
        Assert.Equal(1, await db.InventoryMovements.CountAsync(m => m.OrderItemId == item.Id)); Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.EventKey == outbox.EventKey));
    }
    [Fact]
    public async Task Transfer_keeps_exact_24_hour_deadline_and_replays_identical_utc_response()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser();
        var key = await Session(browser); var body = await Payload(browser, id, province: "Đà Nẵng", method: "BankTransfer");
        var first = await Send(browser, "orders", body, key); Assert.Equal(HttpStatusCode.Created, first.StatusCode); var dto = await first.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(1030000m, dto.GetProperty("grandTotal").GetDecimal());
        await using var db = sql.CreateContext(); var order = await db.Orders.SingleAsync(o => o.Id == long.Parse(dto.GetProperty("id").GetString()!)); var reserve = await db.StockReservations.SingleAsync(r => r.OrderItem.OrderId == order.Id);
        Assert.Equal(order.CreatedAt.AddHours(24), order.PaymentDueAt); Assert.Equal(order.PaymentDueAt, reserve.ExpiresAt); Assert.Equal(DateTimeKind.Utc, reserve.ExpiresAt!.Value.Kind);
        var retry = await Send(browser, "orders", body, key); Assert.Equal(HttpStatusCode.OK, retry.StatusCode); Assert.Equal(dto.GetRawText(), (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        using var missingConfig = host.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Checkout:BankTransferHoldHours"] = "" })));
        using var other = missingConfig.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true }); var newKey = await Session(other);
        await Problem(await Send(other, "orders", body, newKey), HttpStatusCode.ServiceUnavailable, "CHECKOUT_UNAVAILABLE");
        Assert.Equal(IdempotencyRequestStatus.Issued, (await db.IdempotencyRequests.AsNoTracking().SingleAsync(k => k.Id == Guid.Parse(newKey))).Status);
    }
    [Fact]
    public async Task Different_owner_payload_and_identity_change_cannot_reuse_a_key()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var first = host.Browser(); using var other = host.Browser();
        var key = await Session(first); var body = await Payload(first, id); await Session(other);
        await Problem(await Send(other, "orders", body, key), HttpStatusCode.NotFound, "CHECKOUT_NOT_FOUND");
        Assert.Equal(HttpStatusCode.Created, (await Send(first, "orders", body, key)).StatusCode); body["note"] = "Changed payload";
        await Problem(await Send(first, "orders", body, key), HttpStatusCode.Conflict, "IDEMPOTENCY_PAYLOAD_MISMATCH");
        await Login(host, first); await Problem(await Send(first, "orders", body, key), HttpStatusCode.NotFound, "CHECKOUT_NOT_FOUND");
    }
    [Fact]
    public async Task Authenticated_order_uses_verified_account_and_tier_and_never_guest_email_owner()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser(); var account = await Login(host, browser);
        var key = await Session(browser); var body = await Payload(browser, id);
        await Problem(await Send(browser, "orders", body, key), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        body.Remove("email"); body.Remove("createAccountConsent"); body.Remove("consentTextVersion");
        var response = await Send(browser, "orders", body, key); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = sql.CreateContext(); var issued = await db.IdempotencyRequests.SingleAsync(k => k.Id == Guid.Parse(key)); var order = await db.Orders.SingleAsync(o => o.Id == issued.OrderId);
        Assert.Equal(account.Id, order.UserId); Assert.Equal(account.Email, order.CustomerEmail); Assert.Equal(CustomerTierCode.Bronze, order.TierCodeSnapshot); Assert.NotNull(order.TierIdAtPurchase);
        using var outsider = host.Browser(); await Login(host, outsider); await Problem(await Send(outsider, "orders", body, key), HttpStatusCode.NotFound, "CHECKOUT_NOT_FOUND");
        Assert.Equal(HttpStatusCode.NoContent, (await Send(browser, "auth/logout")).StatusCode); await Problem(await Send(browser, "orders", body, key), HttpStatusCode.NotFound, "CHECKOUT_NOT_FOUND");
    }
    [Fact]
    public async Task Last_available_stock_race_has_one_order_and_one_reservation()
    {
        var id = await Variant(1); using var host = new AuthFactory(sql.ConnectionString); using var a = host.Browser(); using var b = host.Browser(); var ka = await Session(a); var kb = await Session(b);
        var body = await Payload(a, id); var replies = await Task.WhenAll(Send(a, "orders", body, ka), Send(b, "orders", body, kb));
        Assert.Equal(1, replies.Count(r => r.StatusCode == HttpStatusCode.Created)); Assert.Equal(1, replies.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        await using var db = sql.CreateContext(); var inventory = await db.Inventory.SingleAsync(i => i.VariantId == id); Assert.Equal(1, inventory.OnHand); Assert.Equal(1, inventory.Reserved);
        Assert.Equal(1, await db.OrderItems.CountAsync(i => i.VariantId == id)); Assert.Equal(1, await db.StockReservations.CountAsync(r => r.OrderItem.VariantId == id)); Assert.Equal(1, await db.InventoryMovements.CountAsync(m => m.VariantId == id));
    }
    [Fact]
    public async Task Same_checkout_key_concurrent_retries_create_once_and_changed_payload_race_is_rejected()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser(); var key = await Session(browser); var body = await Payload(browser, id, 2);
        var csrf = (await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
        var replies = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Send(browser, "orders", body, key, csrf)));
        Assert.Equal(1, replies.Count(r => r.StatusCode == HttpStatusCode.Created)); Assert.Equal(4, replies.Count(r => r.StatusCode == HttpStatusCode.OK));
        var ids = await Task.WhenAll(replies.Select(async r => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString())); Assert.Single(ids.Distinct());
        await using var db = sql.CreateContext(); Assert.Equal(2, (await db.Inventory.SingleAsync(i => i.VariantId == id)).Reserved); Assert.Equal(1, await db.InventoryMovements.CountAsync(m => m.VariantId == id));
        var secondKey = await Session(browser); var altered = new Dictionary<string, object?>(body) { ["note"] = "Other" };
        var race = await Task.WhenAll(Send(browser, "orders", body, secondKey, csrf), Send(browser, "orders", altered, secondKey, csrf)); Assert.Equal(1, race.Count(r => r.StatusCode == HttpStatusCode.Created)); Assert.Equal(1, race.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }
    [Fact]
    public async Task Price_change_and_bad_second_line_roll_back_then_same_key_accepts_corrected_cart()
    {
        var good = await Variant(); var bad = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser(); var key = await Session(browser); var body = await Payload(browser, good);
        await using (var db = sql.CreateContext()) { var v = await db.ProductVariants.SingleAsync(v => v.Id == good); v.Price = 2000000; await db.SaveChangesAsync(); }
        await Problem(await Send(browser, "orders", body, key), HttpStatusCode.Conflict, "PRICE_CHANGED");
        var refreshed = await Payload(browser, good); var wrong = new Dictionary<string, object?>(refreshed) { ["items"] = new[] { new { variantId = good.ToString(), quantity = 1 }, new { variantId = bad.ToString(), quantity = 11 } } };
        await Problem(await Send(browser, "orders", wrong, key), HttpStatusCode.Conflict, "OUT_OF_STOCK");
        await using var check = sql.CreateContext(); Assert.Equal(0, (await check.Inventory.SingleAsync(i => i.VariantId == good)).Reserved); Assert.Equal(0, (await check.Inventory.SingleAsync(i => i.VariantId == bad)).Reserved);
        Assert.False(await check.OrderItems.AnyAsync(i => i.VariantId == good || i.VariantId == bad)); Assert.False(await check.InventoryMovements.AnyAsync(i => i.VariantId == good || i.VariantId == bad));
        Assert.Equal(IdempotencyRequestStatus.Issued, (await check.IdempotencyRequests.SingleAsync(k => k.Id == Guid.Parse(key))).Status);
        Assert.Equal(HttpStatusCode.Created, (await Send(browser, "orders", refreshed, key)).StatusCode);
    }
    private sealed class CheckoutClock : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Current;
    }
    [Fact]
    public async Task Sessions_have_secure_cookie_quota_expiry_and_completed_replay_survives_session_deadline()
    {
        var id = await Variant(); var clock = new CheckoutClock(); using var host = new AuthFactory(sql.ConnectionString);
        using var timed = host.WithWebHostBuilder(b => b.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
        using var browser = timed.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var response = await Send(browser, "checkout/sessions"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("PhoneStore.CheckoutGuest="));
        Assert.Contains("httponly", cookie.ToLowerInvariant()); Assert.Contains("secure", cookie.ToLowerInvariant()); Assert.Contains("samesite=strict", cookie.ToLowerInvariant()); Assert.Contains("path=/api/v1", cookie);
        var first = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("checkoutKey").GetString()!;
        for (var i = 0; i < 4; i++) await Session(browser);
        await Problem(await Send(browser, "checkout/sessions"), HttpStatusCode.TooManyRequests, "CHECKOUT_SESSION_LIMIT");
        var body = await Payload(browser, id); clock.Current = clock.Current.AddMinutes(31);
        await Problem(await Send(browser, "orders", body, first), HttpStatusCode.Conflict, "CHECKOUT_EXPIRED");
        var second = await Session(browser); Assert.Equal(HttpStatusCode.Created, (await Send(browser, "orders", body, second)).StatusCode);
        clock.Current = clock.Current.AddMinutes(31); Assert.Equal(HttpStatusCode.OK, (await Send(browser, "orders", body, second)).StatusCode);
        using var tampered = timed.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true }); tampered.DefaultRequestHeaders.Add("Cookie", "PhoneStore.CheckoutGuest=invalid-protected-cookie");
        await Problem(await Send(tampered, "orders", body, second), HttpStatusCode.NotFound, "CHECKOUT_NOT_FOUND");
    }
    [Fact]
    public async Task Explicit_consent_true_or_false_never_creates_or_claims_an_existing_account()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var accountBrowser = host.Browser(); var account = await Login(host, accountBrowser);
        await using var db = sql.CreateContext(); var before = await db.Users.CountAsync();
        using var guest = host.Browser();
        foreach (var accepted in new[] { false, true })
        {
            var key = await Session(guest); var body = await Payload(guest, id, email: account.Email); body["createAccountConsent"] = accepted;
            Assert.Equal(HttpStatusCode.Created, (await Send(guest, "orders", body, key)).StatusCode);
            var issued = await db.IdempotencyRequests.AsNoTracking().SingleAsync(k => k.Id == Guid.Parse(key)); var order = await db.Orders.Include(o => o.AccountConsent).SingleAsync(o => o.Id == issued.OrderId);
            Assert.Null(order.UserId); Assert.Equal(accepted, order.AccountConsent!.Accepted); Assert.Null(order.TierIdAtPurchase);
        }
        Assert.Equal(before, await db.Users.CountAsync()); Assert.Equal(2, await db.Orders.CountAsync(o => o.NormalizedCustomerEmail == account.Email.ToUpperInvariant() && o.UserId == null));
    }
    [Fact]
    public async Task Tampering_missing_consent_csrf_and_invalid_checkout_keys_are_rejected_without_writes()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser(); var key = await Session(browser); var body = await Payload(browser, id);
        await Problem(await browser.PostAsJsonAsync("/api/v1/orders", body), HttpStatusCode.Forbidden, "CSRF_INVALID");
        await Problem(await browser.PostAsync("/api/v1/checkout/sessions", null), HttpStatusCode.Forbidden, "CSRF_INVALID");
        foreach (var badKey in new string?[] { null, "bad", Guid.Empty.ToString(), key.ToUpperInvariant() })
            await Problem(await Send(browser, "orders", body, badKey), HttpStatusCode.BadRequest, "INVALID_CHECKOUT_KEY");
        foreach (var field in new[] { "userId", "role", "tier", "unitPrice", "grandTotal", "shippingFee", "discountTotal" })
        {
            var invalid = new Dictionary<string, object?>(body) { [field] = 0 };
            await Problem(await Send(browser, "orders", invalid, key), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        }
        foreach (var field in new[] { "email", "createAccountConsent", "consentTextVersion", "quoteHash" })
        {
            var missing = new Dictionary<string, object?>(body); missing.Remove(field);
            await Problem(await Send(browser, "orders", missing, key), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        }
        var invalidConsent = new Dictionary<string, object?>(body) { ["consentTextVersion"] = "unknown" };
        await Problem(await Send(browser, "orders", invalidConsent, key), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await using var db = sql.CreateContext(); Assert.False(await db.OrderItems.AnyAsync(i => i.VariantId == id)); Assert.Equal(0, (await db.Inventory.SingleAsync(i => i.VariantId == id)).Reserved);
    }
    [Fact]
    public async Task Failed_outbox_insert_rolls_back_saved_order_inventory_history_reservation_and_key()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser(); var key = await Session(browser); var body = await Payload(browser, id);
        await using var db = sql.CreateContext(); var trigger = "PhoneStore_Test_FailCheckout_" + Guid.NewGuid().ToString("N");
        var orders = await db.Orders.CountAsync(); var items = await db.OrderItems.CountAsync(); var histories = await db.OrderStatusHistories.CountAsync(); var consents = await db.OrderAccountConsents.CountAsync(); var messages = await db.OutboxMessages.CountAsync();
        await db.Database.OpenConnectionAsync(); await using var ddl = db.Database.GetDbConnection().CreateCommand();
        try
        {
            // Identifier consists only of this fixed test prefix and a locally generated GUID, never request data.
            ddl.CommandText = $"CREATE TRIGGER [{trigger}] ON [OutboxMessages] AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE [Type]='OrderPlaced') THROW 51099, 'Synthetic checkout rollback test', 1; END;";
            await ddl.ExecuteNonQueryAsync();
            await Problem(await Send(browser, "orders", body, key), HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
            Assert.Equal(orders, await db.Orders.CountAsync()); Assert.Equal(items, await db.OrderItems.CountAsync()); Assert.Equal(histories, await db.OrderStatusHistories.CountAsync()); Assert.Equal(consents, await db.OrderAccountConsents.CountAsync()); Assert.Equal(messages, await db.OutboxMessages.CountAsync());
            Assert.Equal(0, (await db.Inventory.SingleAsync(i => i.VariantId == id)).Reserved); Assert.False(await db.StockReservations.AnyAsync(r => r.OrderItem.VariantId == id)); Assert.False(await db.InventoryMovements.AnyAsync(m => m.VariantId == id));
            var issued = await db.IdempotencyRequests.AsNoTracking().SingleAsync(k => k.Id == Guid.Parse(key)); Assert.Equal(IdempotencyRequestStatus.Issued, issued.Status); Assert.Null(issued.OrderId); Assert.Null(issued.RequestHash);
        }
        finally { ddl.CommandText = $"DROP TRIGGER IF EXISTS [{trigger}];"; await ddl.ExecuteNonQueryAsync(); }
        Assert.Equal(HttpStatusCode.Created, (await Send(browser, "orders", body, key)).StatusCode);
    }
    [Fact]
    public async Task Opposite_item_order_has_consistent_locking_and_duplicates_merge_for_replay()
    {
        var a = await Variant(2); var b = await Variant(2); using var host = new AuthFactory(sql.ConnectionString); using var first = host.Browser(); using var second = host.Browser();
        var ka = await Session(first); var kb = await Session(second); var body = await Payload(first, a);
        var items = new[] { new { variantId = a.ToString(), quantity = 1 }, new { variantId = b.ToString(), quantity = 1 } };
        var quote = await Send(first, "checkout/quote", new { items, shippingAddress = new { province = "Thành phố Hồ Chí Minh", countryCode = "VN" } }); Assert.Equal(HttpStatusCode.OK, quote.StatusCode);
        body["items"] = items; body["quoteHash"] = (await quote.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteHash").GetString();
        var reversed = new Dictionary<string, object?>(body) { ["items"] = items.Reverse().ToArray() };
        var responses = await Task.WhenAll(Send(first, "orders", body, ka), Send(second, "orders", reversed, kb)); Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(HttpStatusCode.OK, (await Send(first, "orders", reversed, ka)).StatusCode);
        await using var db = sql.CreateContext(); Assert.Equal(2, (await db.Inventory.SingleAsync(i => i.VariantId == a)).Reserved); Assert.Equal(2, (await db.Inventory.SingleAsync(i => i.VariantId == b)).Reserved);
    }
    [Fact]
    public async Task Concurrent_manual_inventory_reduction_and_checkout_cannot_oversell()
    {
        var id = await Variant(1); using var host = new AuthFactory(sql.ConnectionString); using var admin = host.Browser(); await Login(host, admin, true); using var guest = host.Browser();
        var key = await Session(guest); var body = await Payload(guest, id);
        var results = await Task.WhenAll(Send(guest, "orders", body, key), Send(admin, $"admin/inventory/{id}/adjustments", new { operationKey = Guid.NewGuid().ToString(), quantityDelta = -1, reason = "Synthetic last-stock reduction" }));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created)); Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        await using var db = sql.CreateContext(); var balance = await db.Inventory.SingleAsync(i => i.VariantId == id); Assert.True(balance.Reserved <= balance.OnHand); Assert.Equal(0, balance.OnHand - balance.Reserved);
        Assert.Equal(1, await db.InventoryMovements.CountAsync(m => m.VariantId == id));
    }
    [Fact]
    public async Task Duplicate_lines_canonical_replay_and_profile_changes_do_not_rewrite_order_snapshot()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser(); var account = await Login(host, browser);
        var key = await Session(browser); var body = await Payload(browser, id, 2, email: account.Email);
        var duplicate = new Dictionary<string, object?>(body) { ["items"] = new[] { new { variantId = id.ToString(), quantity = 1 }, new { variantId = id.ToString(), quantity = 1 } } };
        Assert.Equal(HttpStatusCode.Created, (await Send(browser, "orders", duplicate, key)).StatusCode);
        await using var db = sql.CreateContext(); var issued = await db.IdempotencyRequests.SingleAsync(k => k.Id == Guid.Parse(key)); var saved = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == issued.OrderId);
        var address = new Address { UserId = account.Id, RecipientName = "Old profile recipient", Phone = "0000000000", AddressLine = "Old profile street", Province = "Old profile province", CountryCode = "VN", CreatedAt = DateTime.UtcNow };
        db.Addresses.Add(address); var user = await db.Users.SingleAsync(u => u.Id == account.Id); user.FullName = "Changed profile name"; user.PhoneNumber = "1111111111";
        var variant = await db.ProductVariants.Include(v => v.Product).SingleAsync(v => v.Id == id); variant.Price = 9999999; variant.Sku = "CHANGED-" + Guid.NewGuid().ToString("N"); variant.Color = "White"; variant.Product.Name = "Changed catalog title"; await db.SaveChangesAsync();
        address.AddressLine = "Changed profile street"; address.Province = "Changed profile province"; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK, (await Send(browser, "orders", body, key)).StatusCode);
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == saved.Id); Assert.Equal("Synthetic street", order.AddressLine); Assert.Equal("Synthetic recipient", order.RecipientName); Assert.Equal("0000000000", order.Phone); Assert.Equal("Thành phố Hồ Chí Minh", order.Province);
        var item = Assert.Single(order.Items); Assert.Equal(2, item.Quantity); Assert.Equal(1000000m, item.UnitPrice); Assert.NotEqual(variant.Sku, item.SkuSnapshot); Assert.Contains("Black · 128 GB", item.VariantSnapshot); Assert.NotEqual(variant.Product.Name, item.ProductNameSnapshot);
    }
    [Fact]
    public async Task Checkout_validates_coded_address_and_detects_shipping_fee_change_before_any_reservation()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser();
        var body = await Payload(browser, id, province: "Thành phố Đà Nẵng");
        var wards = await browser.GetFromJsonAsync<JsonElement>("/api/v1/locations/provinces/48/wards"); var ward = wards.GetProperty("items")[0];
        body["provinceCode"] = "48"; body["wardCode"] = ward.GetProperty("code").GetString(); body["locality"] = ward.GetProperty("name").GetString();
        using var changed = host.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Pricing:OtherProvinceShippingFeeVnd"] = "40000" })));
        using var current = changed.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true }); var key = await Session(current);
        await Problem(await Send(current, "orders", body, key), HttpStatusCode.Conflict, "PRICE_CHANGED");
        var wrong = new Dictionary<string, object?>(body) { ["provinceCode"] = "79" }; await Problem(await Send(current, "orders", wrong, key), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await using var db = sql.CreateContext(); Assert.False(await db.OrderItems.AnyAsync(i => i.VariantId == id)); Assert.Equal(0, (await db.Inventory.SingleAsync(i => i.VariantId == id)).Reserved);
        var refreshed = await Payload(current, id, province: "Thành phố Đà Nẵng"); foreach (var field in new[] { "provinceCode", "wardCode", "locality" }) refreshed[field] = body[field];
        var result = await Send(current, "orders", refreshed, key); Assert.Equal(HttpStatusCode.Created, result.StatusCode); Assert.Equal(1040000m, (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("grandTotal").GetDecimal());
    }
    [Fact]
    public async Task Hidden_variant_or_unsafe_money_cannot_complete_or_hold_checkout()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser(); var key = await Session(browser); var body = await Payload(browser, id, 2);
        await using var db = sql.CreateContext(); var variant = await db.ProductVariants.SingleAsync(v => v.Id == id); variant.IsActive = false; await db.SaveChangesAsync();
        await Problem(await Send(browser, "orders", body, key), HttpStatusCode.Conflict, "VARIANT_UNAVAILABLE");
        variant.IsActive = true; variant.Price = 9007199254740991m; await db.SaveChangesAsync();
        await Problem(await Send(browser, "orders", body, key), HttpStatusCode.BadRequest, "QUOTE_LIMIT");
        Assert.Equal(0, (await db.Inventory.SingleAsync(i => i.VariantId == id)).Reserved); Assert.False(await db.OrderItems.AnyAsync(i => i.VariantId == id));
        Assert.Equal(IdempotencyRequestStatus.Issued, (await db.IdempotencyRequests.SingleAsync(k => k.Id == Guid.Parse(key))).Status);
    }
    [Fact]
    public async Task Receipt_recovery_is_owner_scoped_readonly_and_returns_committed_order_after_expiry()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser(); using var other = host.Browser();
        var key = await Session(browser); var url = "/api/v1/checkout/sessions/" + key + "/result";
        var issued = await browser.GetFromJsonAsync<JsonElement>(url); Assert.Equal("Issued", issued.GetProperty("state").GetString()); Assert.Equal(JsonValueKind.Null, issued.GetProperty("order").ValueKind);
        await Problem(await other.GetAsync(url), HttpStatusCode.NotFound, "CHECKOUT_NOT_FOUND"); await Session(other); await Problem(await other.GetAsync(url), HttpStatusCode.NotFound, "CHECKOUT_NOT_FOUND");
        await using var db = sql.CreateContext(); Assert.Equal(0, (await db.Inventory.SingleAsync(i => i.VariantId == id)).Reserved);
        var response = await Send(browser, "orders", await Payload(browser, id), key); Assert.Equal(HttpStatusCode.Created, response.StatusCode); var placed = await response.Content.ReadFromJsonAsync<JsonElement>();
        await db.IdempotencyRequests.Where(k => k.Id == Guid.Parse(key)).ExecuteUpdateAsync(s => s.SetProperty(k => k.ExpiresAt, DateTime.UtcNow.AddHours(-1)));
        var recovered = await browser.GetAsync(url); Assert.Equal(HttpStatusCode.OK, recovered.StatusCode); Assert.True(recovered.Headers.CacheControl!.NoStore);
        var result = await recovered.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("Completed", result.GetProperty("state").GetString()); Assert.Equal(placed.GetRawText(), result.GetProperty("order").GetRawText()); Assert.False(result.GetProperty("order").TryGetProperty("customerEmail", out _));
        var old = await Session(browser); await db.IdempotencyRequests.Where(k => k.Id == Guid.Parse(old)).ExecuteUpdateAsync(s => s.SetProperty(k => k.ExpiresAt, DateTime.UtcNow.AddHours(-1)));
        Assert.Equal("Expired", (await browser.GetFromJsonAsync<JsonElement>("/api/v1/checkout/sessions/" + old + "/result")).GetProperty("state").GetString());
        await Login(host, browser); await Problem(await browser.GetAsync(url), HttpStatusCode.NotFound, "CHECKOUT_NOT_FOUND");
        var userKey = await Session(browser); Assert.Equal("Issued", (await browser.GetFromJsonAsync<JsonElement>("/api/v1/checkout/sessions/" + userKey + "/result")).GetProperty("state").GetString());
        await Problem(await browser.GetAsync("/api/v1/checkout/sessions/invalid/result"), HttpStatusCode.BadRequest, "INVALID_CHECKOUT_KEY");
    }
}
