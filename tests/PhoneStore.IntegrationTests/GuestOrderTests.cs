using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhoneStore.Api.Data;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Auth;
using PhoneStore.Api.Services.Notifications;

namespace PhoneStore.IntegrationTests;

[CollectionDefinition("Guest SQL")]
public sealed class GuestSqlCollection : ICollectionFixture<SqlFixture> { }
public sealed class OrderTestClock : TimeProvider
{
    public DateTimeOffset Current { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Current;
}
public sealed class OrderTestMailbox : IOrderEmailSender
{
    public bool IsConfigured { get; set; } = true;
    public bool Fail { get; set; }
    public bool CrashAfterAccept { get; set; }
    public Func<Task>? BeforeSend { get; set; }
    public ConcurrentDictionary<string, OrderEmail> Messages { get; } = new();
    public async Task SendAsync(OrderEmail message, CancellationToken ct)
    {
        if (BeforeSend is not null) await BeforeSend();
        if (Fail) throw new IOException("secret-sender-error-marker");
        Messages.TryAdd(message.MessageKey, message);
        if (CrashAfterAccept) throw new IOException("secret-sender-error-marker");
    }
}
[Collection("Guest SQL")]
public sealed class GuestOrderTests(SqlFixture sql)
{
    private static async Task<HttpResponseMessage> Post(HttpClient browser, string route, object? body = null, string? key = null)
    {
        var csrf = (await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/" + route);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-CSRF-TOKEN", csrf); if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return await browser.SendAsync(request);
    }
    private WebApplicationFactory<Program> Server(AuthFactory host, OrderTestMailbox mailbox, OrderTestClock clock) => host.WithWebHostBuilder(b => b.ConfigureTestServices(s => {
        s.RemoveAll<IOrderEmailSender>(); s.AddSingleton<IOrderEmailSender>(mailbox); s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(clock);
    }));
    private static HttpClient Browser(WebApplicationFactory<Program> host) => host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
    private async Task<(long Id, string Number, string Email)> Order(HttpClient browser, bool consent = false, string? email = null)
    {
        await using var db = sql.CreateContext(); var tag = Guid.NewGuid().ToString("N");
        var variant = new ProductVariant { Sku = tag, Color = "Black", StorageGb = 128, RamGb = 8, Price = 1000000,
            Product = new Product { Name = "Synthetic", Slug = tag, Brand = new Brand { Name = tag, Slug = tag }, Category = new Category { Name = tag, Slug = tag } }, Inventory = new Inventory { OnHand = 5 } };
        db.ProductVariants.Add(variant); await db.SaveChangesAsync();
        email ??= tag + "@example.invalid";
        var key = (await (await Post(browser, "checkout/sessions")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("checkoutKey").GetString();
        var items = new[] { new { variantId = variant.Id.ToString(), quantity = 1 } };
        var quote = await Post(browser, "checkout/quote", new { items, shippingAddress = new { province = "Đà Nẵng", countryCode = "VN" } }); Assert.Equal(HttpStatusCode.OK, quote.StatusCode);
        var hash = (await quote.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteHash").GetString();
        var response = await Post(browser, "orders", new { items, email, recipientName = "Synthetic", phone = "0000000000", addressLine = "Synthetic street", province = "Đà Nẵng", countryCode = "VN", paymentMethod = "BankTransfer", quoteHash = hash, createAccountConsent = consent, consentTextVersion = "account-create-v1" }, key);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (long.Parse(dto.GetProperty("id").GetString()!), dto.GetProperty("orderNumber").GetString()!, email);
    }
    private static async Task Drain(WebApplicationFactory<Program> host)
    {
        for (var i = 0; i < 300; i++) { using var scope = host.Services.CreateScope(); if (!await scope.ServiceProvider.GetRequiredService<OrderOutboxProcessor>().ProcessOneAsync(default)) return; }
        throw new InvalidOperationException("Test outbox did not drain.");
    }
    private static string Secret(OrderEmail mail) => mail.ActionPath!.Split("token=")[1].Split('&')[0];
    private static OrderEmail Mail(OrderTestMailbox mailbox, string number, string purpose) => mailbox.Messages.Values.Last(m => m.OrderNumber == number && m.Purpose == purpose);
    private static async Task<string> Access(WebApplicationFactory<Program> host, HttpClient browser, OrderTestMailbox mailbox,
        (long Id, string Number, string Email) order, string purpose)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await Post(browser, "guest/order-access-requests", new { orderNumber = order.Number, email = order.Email, purpose })).StatusCode);
        await Drain(host); return Secret(Mail(mailbox, order.Number, purpose));
    }
    private static async Task<Guid> Account(AuthFactory host, HttpClient browser, string email, bool verify = true, bool admin = false)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await Post(browser, "auth/register", new { email, password = "Synthetic!Password123", fullName = "Synthetic" })).StatusCode);
        var mail = host.Mailbox.Messages.Single(m => m.Email == email);
        if (!verify) return mail.UserId;
        Assert.Equal(HttpStatusCode.NoContent, (await Post(browser, "auth/verify-email", new { mail.UserId, mail.Token })).StatusCode);
        if (admin) { using var scope = host.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<AuthBootstrap>().RunAsync(email, default); }
        Assert.Equal(HttpStatusCode.NoContent, (await Post(browser, "auth/login", new { email, password = "Synthetic!Password123" })).StatusCode);
        return mail.UserId;
    }
    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
    { Assert.Equal(status, response.StatusCode); Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()); }

    [Fact]
    public async Task Guest_email_access_is_single_use_scoped_cookie_and_snapshot_only()
    {
        using var auth = new AuthFactory(sql.ConnectionString); var mailbox = new OrderTestMailbox(); var clock = new OrderTestClock(); using var host = Server(auth, mailbox, clock);
        using var guest = Browser(host); var order = await Order(guest); await Drain(host); var secret = Secret(Mail(mailbox, order.Number, "ViewOrder"));
        await Error(await guest.GetAsync("/api/v1/guest/order"), HttpStatusCode.Unauthorized, "GUEST_ACCESS_REQUIRED");
        await using var db = sql.CreateContext(); var token = await db.OrderAccessTokens.SingleAsync(t => t.OrderId == order.Id);
        Assert.Equal(SHA256.HashData(Convert.FromHexString(secret)), token.TokenHash); Assert.Null(token.UsedAt); Assert.Equal(token.CreatedAt.AddHours(1), token.ExpiresAt);
        var persisted = await db.OutboxMessages.SingleAsync(m => m.EventKey == "OrderPlacedMail:" + order.Id); Assert.DoesNotContain(secret, persisted.PayloadJson); Assert.DoesNotContain(order.Email, persisted.PayloadJson);
        var exchange = await Post(guest, "guest/order-access/exchange", new { token = secret, purpose = "ViewOrder" }); Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var cookie = exchange.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("PhoneStore.GuestOrder=")); Assert.Contains("httponly", cookie.ToLowerInvariant()); Assert.Contains("secure", cookie.ToLowerInvariant()); Assert.Contains("samesite=strict", cookie.ToLowerInvariant()); Assert.Contains("path=/api/v1/guest", cookie);
        var view = await guest.GetAsync("/api/v1/guest/order"); Assert.Equal(HttpStatusCode.OK, view.StatusCode); Assert.True(view.Headers.CacheControl!.NoStore);
        var dto = await view.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(order.Id.ToString(), dto.GetProperty("id").GetString()); Assert.Equal(1030000, dto.GetProperty("grandTotal").GetDecimal()); Assert.Equal("Synthetic", dto.GetProperty("items")[0].GetProperty("productName").GetString()); Assert.False(dto.GetProperty("createAccountConsent").GetBoolean());
        Assert.False(dto.TryGetProperty("userId", out _)); Assert.False(dto.GetProperty("timeline")[0].TryGetProperty("reason", out _));
        await Error(await Post(guest, "guest/order-access/exchange", new { token = secret, purpose = "ViewOrder" }), HttpStatusCode.BadRequest, "INVALID_ORDER_TOKEN");
        using var stranger = Browser(host); await Error(await stranger.GetAsync("/api/v1/guest/order?orderId=" + order.Id), HttpStatusCode.Unauthorized, "GUEST_ACCESS_REQUIRED");
        clock.Current = clock.Current.AddMinutes(31); await Error(await guest.GetAsync("/api/v1/guest/order"), HttpStatusCode.Unauthorized, "GUEST_ACCESS_REQUIRED");
    }
    [Fact]
    public async Task Access_requests_are_neutral_and_cannot_create_accounts_or_enumerate_orders()
    {
        using var auth = new AuthFactory(sql.ConnectionString); var mailbox = new OrderTestMailbox(); using var host = Server(auth, mailbox, new()); using var guest = Browser(host);
        var order = await Order(guest); await Drain(host); await using var db = sql.CreateContext(); var users = await db.Users.CountAsync(); var tokens = await db.OrderAccessTokens.CountAsync(t => t.OrderId == order.Id);
        foreach (var request in new[] { new { orderNumber = order.Number, email = "wrong@example.invalid", purpose = "ViewOrder" }, new { orderNumber = "missing", email = order.Email, purpose = "ViewOrder" } })
        { var response = await Post(guest, "guest/order-access-requests", request); Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); Assert.Equal("", await response.Content.ReadAsStringAsync()); }
        Assert.Equal(tokens, await db.OrderAccessTokens.CountAsync(t => t.OrderId == order.Id)); Assert.Equal(users, await db.Users.CountAsync()); Assert.Null((await db.Orders.SingleAsync(o => o.Id == order.Id)).UserId);
        await Error(await Post(guest, "guest/order-access-requests", new { orderNumber = order.Number, email = order.Email, purpose = "ViewOrder", userId = Guid.NewGuid() }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }
    [Fact]
    public async Task Wrong_expired_and_cross_purpose_tokens_are_rejected_without_consuming_valid_tokens()
    {
        using var auth = new AuthFactory(sql.ConnectionString); var mailbox = new OrderTestMailbox(); var clock = new OrderTestClock(); using var host = Server(auth, mailbox, clock); using var guest = Browser(host);
        var order = await Order(guest); var cancel = await Access(host, guest, mailbox, order, "CancelOrder");
        await Error(await Post(guest, "guest/order-access/exchange", new { token = cancel, purpose = "ViewOrder" }), HttpStatusCode.BadRequest, "INVALID_ORDER_TOKEN");
        await Error(await Post(guest, "guest/order-access/exchange", new { token = new string('0', 64), purpose = "CancelOrder" }), HttpStatusCode.BadRequest, "INVALID_ORDER_TOKEN");
        Assert.Equal(HttpStatusCode.OK, (await Post(guest, "guest/order-access/exchange", new { token = cancel, purpose = "CancelOrder" })).StatusCode);
        var claim = await Access(host, guest, mailbox, order, "ClaimOrder");
        await Error(await Post(guest, "guest/order-access/exchange", new { token = claim, purpose = "ClaimOrder" }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var view = await Access(host, guest, mailbox, order, "ViewOrder"); clock.Current = clock.Current.AddHours(2);
        await Error(await Post(guest, "guest/order-access/exchange", new { token = view, purpose = "ViewOrder" }), HttpStatusCode.BadRequest, "INVALID_ORDER_TOKEN");
    }
    [Fact]
    public async Task Claim_requires_verified_matching_account_and_revokes_guest_access_without_snapshot_changes()
    {
        using var auth = new AuthFactory(sql.ConnectionString); var mailbox = new OrderTestMailbox(); using var host = Server(auth, mailbox, new()); using var guest = Browser(host);
        var order = await Order(guest); await Drain(host); var view = Secret(Mail(mailbox, order.Number, "ViewOrder"));
        Assert.Equal(HttpStatusCode.OK, (await Post(guest, "guest/order-access/exchange", new { token = view, purpose = "ViewOrder" })).StatusCode);
        var claim = await Access(host, guest, mailbox, order, "ClaimOrder");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(guest, "me/order-claims", new { token = claim })).StatusCode);
        using var wrong = Browser(host); await Account(auth, wrong, Guid.NewGuid().ToString("N") + "@example.invalid");
        await Error(await Post(wrong, "me/order-claims", new { token = claim }), HttpStatusCode.BadRequest, "INVALID_ORDER_TOKEN");
        using var account = Browser(host); var userId = await Account(auth, account, order.Email);
        await Error(await Post(account, "me/order-claims", new { token = view }), HttpStatusCode.BadRequest, "INVALID_ORDER_TOKEN");
        var success = await Post(account, "me/order-claims", new { token = claim }); Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Equal(order.Id.ToString(), (await success.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Post(account, "me/order-claims", new { token = claim })).StatusCode);
        await Error(await guest.GetAsync("/api/v1/guest/order"), HttpStatusCode.Unauthorized, "GUEST_ACCESS_REQUIRED");
        await using var db = sql.CreateContext(); var saved = await db.Orders.SingleAsync(o => o.Id == order.Id); Assert.Equal(userId, saved.UserId); Assert.Null(saved.TierIdAtPurchase); Assert.Equal("Synthetic street", saved.AddressLine); Assert.Equal(1030000, saved.GrandTotal);
        Assert.False(await db.CustomerSpendEntries.AnyAsync(e => e.OrderId == order.Id)); Assert.Single(await db.OutboxMessages.Where(m => m.EventKey == "OrderClaimed:" + order.Id).ToListAsync());
        var before = await db.OrderAccessTokens.CountAsync(t => t.OrderId == order.Id); Assert.Equal(HttpStatusCode.Accepted, (await Post(guest, "guest/order-access-requests", new { orderNumber = order.Number, email = order.Email, purpose = "ViewOrder" })).StatusCode); Assert.Equal(before, await db.OrderAccessTokens.CountAsync(t => t.OrderId == order.Id));
    }
    [Fact]
    public async Task Completed_claim_backfills_net_merchandise_and_refunds_once_under_concurrent_replay()
    {
        using var auth = new AuthFactory(sql.ConnectionString); var mailbox = new OrderTestMailbox(); using var host = Server(auth, mailbox, new()); using var guest = Browser(host); var order = await Order(guest);
        var secret = await Access(host, guest, mailbox, order, "ClaimOrder"); using var account = Browser(host); var userId = await Account(auth, account, order.Email);
        await using (var db = sql.CreateContext())
        {
            var saved = await db.Orders.SingleAsync(o => o.Id == order.Id); saved.Status = OrderStatus.Completed; saved.CompletedAt = DateTime.UtcNow;
            var payment = new Payment { OrderId = order.Id, Method = PaymentMethod.BankTransfer, Status = PaymentStatus.Confirmed, Amount = 1030000 };
            payment.Refunds.Add(new Refund { Status = RefundStatus.Completed, MerchandiseAmount = 200000, ShippingAmount = 30000, CreatedByUserId = userId, Reason = "Synthetic", EventKey = Guid.NewGuid().ToString(), RequestHash = new byte[32] });
            payment.Refunds.Add(new Refund { Status = RefundStatus.Pending, MerchandiseAmount = 100000, CreatedByUserId = userId, Reason = "Synthetic pending", EventKey = Guid.NewGuid().ToString(), RequestHash = new byte[32] });
            db.Payments.Add(payment); await db.SaveChangesAsync();
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Post(account, "me/order-claims", new { token = secret }))); Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await using var after = sql.CreateContext(); var entries = await after.CustomerSpendEntries.Where(e => e.OrderId == order.Id).ToListAsync(); Assert.Equal(2, entries.Count); Assert.Equal(800000, entries.Sum(e => e.Amount)); Assert.Contains(entries, e => e.EventKey == $"order:{order.Id}:completed"); Assert.Contains(entries, e => e.Kind == SpendEntryKind.Refund && e.Amount == -200000);
        var profile = await after.CustomerProfiles.SingleAsync(p => p.UserId == userId); Assert.Equal(800000, profile.EligibleSpend); Assert.Null((await after.Orders.SingleAsync(o => o.Id == order.Id)).TierIdAtPurchase);
        Assert.Equal(HttpStatusCode.OK, (await Post(account, "me/order-claims", new { token = secret })).StatusCode); Assert.Equal(2, await after.CustomerSpendEntries.CountAsync(e => e.OrderId == order.Id));
    }
    [Fact]
    public async Task Consent_controls_setup_invitation_and_existing_email_never_auto_claims()
    {
        using var auth = new AuthFactory(sql.ConnectionString); var mailbox = new OrderTestMailbox(); using var host = Server(auth, mailbox, new()); using var account = Browser(host);
        var email = Guid.NewGuid().ToString("N") + "@example.invalid"; await Account(auth, account, email); await using var db = sql.CreateContext(); var count = await db.Users.CountAsync();
        using var guest = Browser(host); var no = await Order(guest, email: email); await Drain(host); Assert.Equal(HttpStatusCode.OK, (await Post(guest, "guest/order-access/exchange", new { token = Secret(Mail(mailbox, no.Number, "ViewOrder")), purpose = "ViewOrder" })).StatusCode);
        await Error(await Post(guest, "guest/account-setup-requests"), HttpStatusCode.Forbidden, "CONSENT_REQUIRED");
        var yes = await Order(guest, true, email); await Drain(host); Assert.Equal(HttpStatusCode.OK, (await Post(guest, "guest/order-access/exchange", new { token = Secret(Mail(mailbox, yes.Number, "ViewOrder")), purpose = "ViewOrder" })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Post(guest, "guest/account-setup-requests")).StatusCode); await Drain(host); Assert.True(Mail(mailbox, yes.Number, "ClaimOrder").AccountSetup);
        Assert.Equal(count, await db.Users.CountAsync()); Assert.Null((await db.Orders.SingleAsync(o => o.Id == yes.Id)).UserId); Assert.Null((await db.Orders.SingleAsync(o => o.Id == no.Id)).UserId);
    }
    [Fact]
    public async Task Worker_failure_retries_deduplicates_notifications_and_never_runs_mail_in_transaction()
    {
        using var auth = new AuthFactory(sql.ConnectionString); var mailbox = new OrderTestMailbox(); var clock = new OrderTestClock(); using var host = Server(auth, mailbox, clock);
        using var admin = Browser(host); var adminId = await Account(auth, admin, Guid.NewGuid().ToString("N") + "@example.invalid", admin: true);
        using var guest = Browser(host); var order = await Order(guest); mailbox.Fail = true; await Drain(host);
        await using var db = sql.CreateContext(); Assert.True(await db.Orders.AnyAsync(o => o.Id == order.Id)); var eventKey = "OrderPlaced:" + order.Id;
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.UserId == adminId && n.EventKey == eventKey));
        var token = await db.OrderAccessTokens.SingleAsync(t => t.OrderId == order.Id); var delivery = await db.OutboxMessages.SingleAsync(m => m.EventKey == "OrderPlacedMail:" + order.Id);
        Assert.Null(delivery.ProcessedAt); Assert.Equal("ORDER_DELIVERY_FAILED", delivery.LastError); Assert.Null(delivery.LockOwner); Assert.True(delivery.Attempts > 0); Assert.DoesNotContain(mailbox.Messages.Values, m => m.OrderNumber == order.Number);
        mailbox.Fail = false; clock.Current = clock.Current.AddMinutes(1);
        using (var scope = host.Services.CreateScope())
        {
            var processingDb = scope.ServiceProvider.GetRequiredService<AppDbContext>(); mailbox.BeforeSend = () => { Assert.Null(processingDb.Database.CurrentTransaction); return Task.CompletedTask; };
            Assert.True(await scope.ServiceProvider.GetRequiredService<OrderOutboxProcessor>().ProcessOneAsync(default));
        }
        mailbox.BeforeSend = null; await Drain(host); Assert.Single(mailbox.Messages.Values, m => m.OrderNumber == order.Number);
        await db.OutboxMessages.Where(m => m.EventKey == eventKey).ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, (DateTime?)null)); await Drain(host);
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.UserId == adminId && n.EventKey == eventKey));
        Assert.Single(mailbox.Messages.Values, m => m.OrderNumber == order.Number); Assert.Equal(1, await db.OrderAccessTokens.CountAsync(t => t.OrderId == order.Id));
    }
    [Fact]
    public async Task Expired_lease_is_reclaimed_and_crash_after_mail_acceptance_uses_stable_message_key()
    {
        using var auth = new AuthFactory(sql.ConnectionString); var mailbox = new OrderTestMailbox(); var clock = new OrderTestClock(); using var host = Server(auth, mailbox, clock); using var guest = Browser(host); var order = await Order(guest);
        await using var db = sql.CreateContext(); await db.OutboxMessages.Where(m => m.EventKey == "OrderPlaced:" + order.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.LockOwner, "crashed-worker").SetProperty(m => m.LockedUntil, clock.Current.UtcDateTime.AddMinutes(1)));
        await Drain(host); Assert.False(await db.OrderAccessTokens.AnyAsync(t => t.OrderId == order.Id));
        clock.Current = clock.Current.AddMinutes(2); mailbox.CrashAfterAccept = true; await Drain(host); Assert.Single(mailbox.Messages.Values, m => m.OrderNumber == order.Number);
        clock.Current = clock.Current.AddMinutes(1); mailbox.CrashAfterAccept = false; await Task.WhenAll(Drain(host), Drain(host));
        Assert.Single(mailbox.Messages.Values, m => m.OrderNumber == order.Number); var token = await db.OrderAccessTokens.SingleAsync(t => t.OrderId == order.Id); Assert.NotNull((await db.OutboxMessages.SingleAsync(m => m.EventKey == "OrderPlacedMail:" + order.Id)).ProcessedAt);
    }
    [Fact]
    public async Task Csrf_invalid_cookie_unverified_account_and_rate_limits_do_not_grant_access()
    {
        using var auth = new AuthFactory(sql.ConnectionString); var mailbox = new OrderTestMailbox(); using var host = Server(auth, mailbox, new()); using var browser = Browser(host);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsJsonAsync("/api/v1/guest/order-access-requests", new { orderNumber = "missing", email = "a@example.invalid", purpose = "ViewOrder" })).StatusCode);
        using var badCookie = new HttpRequestMessage(HttpMethod.Get, "/api/v1/guest/order"); badCookie.Headers.Add("Cookie", "PhoneStore.GuestOrder=tampered"); await Error(await browser.SendAsync(badCookie), HttpStatusCode.Unauthorized, "GUEST_ACCESS_REQUIRED");
        var order = await Order(browser); var claim = await Access(host, browser, mailbox, order, "ClaimOrder"); using var unverified = Browser(host); await Account(auth, unverified, order.Email, verify: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(unverified, "auth/login", new { email = order.Email, password = "Synthetic!Password123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(unverified, "me/order-claims", new { token = claim })).StatusCode);
        for (var i = 0; i < 25; i++) await Post(browser, "guest/order-access-requests", new { orderNumber = "missing", email = "a@example.invalid", purpose = "ViewOrder" });
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Post(browser, "guest/order-access-requests", new { orderNumber = "missing", email = "a@example.invalid", purpose = "ViewOrder" })).StatusCode);
    }
    [Fact]
    public async Task Claim_failure_rolls_back_owner_token_spend_profile_and_event_before_safe_retry()
    {
        using var auth = new AuthFactory(sql.ConnectionString); var mailbox = new OrderTestMailbox(); using var host = Server(auth, mailbox, new()); using var guest = Browser(host); var order = await Order(guest);
        var secret = await Access(host, guest, mailbox, order, "ClaimOrder"); using var account = Browser(host); var userId = await Account(auth, account, order.Email);
        await using var db = sql.CreateContext(); var saved = await db.Orders.SingleAsync(o => o.Id == order.Id); saved.Status = OrderStatus.Completed; saved.CompletedAt = DateTime.UtcNow; await db.SaveChangesAsync();
        var trigger = "Test_Claim_" + Guid.NewGuid().ToString("N"); await db.Database.OpenConnectionAsync();
        await using var ddl = db.Database.GetDbConnection().CreateCommand();
        try
        {
            ddl.CommandText = $"CREATE TRIGGER [{trigger}] ON [CustomerSpendEntries] AFTER INSERT AS BEGIN THROW 51098, 'Synthetic claim rollback', 1; END;"; await ddl.ExecuteNonQueryAsync();
            await Error(await Post(account, "me/order-claims", new { token = secret }), HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
            Assert.Null((await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).UserId);
            Assert.Null((await db.OrderAccessTokens.SingleAsync(t => t.OrderId == order.Id && t.Purpose == OrderAccessTokenPurpose.ClaimOrder)).UsedAt);
            Assert.False(await db.CustomerSpendEntries.AnyAsync(e => e.OrderId == order.Id)); Assert.False(await db.OutboxMessages.AnyAsync(m => m.EventKey == "OrderClaimed:" + order.Id));
            Assert.Equal(0, (await db.CustomerProfiles.SingleAsync(p => p.UserId == userId)).EligibleSpend);
        }
        finally { ddl.CommandText = $"DROP TRIGGER IF EXISTS [{trigger}];"; await ddl.ExecuteNonQueryAsync(); }
        Assert.Equal(HttpStatusCode.OK, (await Post(account, "me/order-claims", new { token = secret })).StatusCode); Assert.Equal(1, await db.CustomerSpendEntries.CountAsync(e => e.OrderId == order.Id));
    }
    [Fact]
    public async Task Notification_failure_rolls_back_tokens_mail_and_processing_marker_then_parallel_retry_deduplicates()
    {
        using var auth = new AuthFactory(sql.ConnectionString); var mailbox = new OrderTestMailbox(); var clock = new OrderTestClock(); using var host = Server(auth, mailbox, clock);
        using var admin = Browser(host); var adminId = await Account(auth, admin, Guid.NewGuid().ToString("N") + "@example.invalid", admin: true);
        using var guest = Browser(host); var order = await Order(guest); var key = "OrderPlaced:" + order.Id;
        await using var db = sql.CreateContext(); var trigger = "Test_Notification_" + Guid.NewGuid().ToString("N"); await db.Database.OpenConnectionAsync();
        await using var ddl = db.Database.GetDbConnection().CreateCommand();
        try
        {
            ddl.CommandText = $"CREATE TRIGGER [{trigger}] ON [Notifications] AFTER INSERT AS BEGIN THROW 51097, 'Synthetic notification rollback', 1; END;"; await ddl.ExecuteNonQueryAsync();
            await Drain(host);
            Assert.False(await db.Notifications.AnyAsync(n => n.EventKey == key)); Assert.False(await db.OrderAccessTokens.AnyAsync(t => t.OrderId == order.Id)); Assert.False(await db.OutboxMessages.AnyAsync(m => m.EventKey == "OrderPlacedMail:" + order.Id));
            var failed = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.EventKey == key); Assert.Null(failed.ProcessedAt); Assert.Equal("ORDER_DELIVERY_FAILED", failed.LastError);
        }
        finally { ddl.CommandText = $"DROP TRIGGER IF EXISTS [{trigger}];"; await ddl.ExecuteNonQueryAsync(); }
        clock.Current = clock.Current.AddMinutes(1); await Task.WhenAll(Drain(host), Drain(host), Drain(host));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.UserId == adminId && n.EventKey == key)); Assert.Equal(1, await db.OrderAccessTokens.CountAsync(t => t.OrderId == order.Id)); Assert.Single(mailbox.Messages.Values, m => m.OrderNumber == order.Number);
    }
    [Fact]
    public async Task Per_order_quota_is_neutral_and_expired_undelivered_token_can_be_requested_again()
    {
        using var auth = new AuthFactory(sql.ConnectionString); var mailbox = new OrderTestMailbox { IsConfigured = false }; var clock = new OrderTestClock(); using var host = Server(auth, mailbox, clock); using var guest = Browser(host); var order = await Order(guest);
        await Drain(host); await using var db = sql.CreateContext(); Assert.True(await db.Orders.AnyAsync(o => o.Id == order.Id)); Assert.DoesNotContain(mailbox.Messages.Values, m => m.OrderNumber == order.Number);
        for (var i = 0; i < 7; i++) Assert.Equal(HttpStatusCode.Accepted, (await Post(guest, "guest/order-access-requests", new { orderNumber = order.Number, email = order.Email, purpose = "ViewOrder" })).StatusCode);
        Assert.Equal(5, await db.OrderAccessTokens.CountAsync(t => t.OrderId == order.Id));
        clock.Current = clock.Current.AddHours(2); mailbox.IsConfigured = true; await Drain(host); Assert.DoesNotContain(mailbox.Messages.Values, m => m.OrderNumber == order.Number);
        var token = await Access(host, guest, mailbox, order, "ViewOrder"); Assert.Equal(HttpStatusCode.OK, (await Post(guest, "guest/order-access/exchange", new { token, purpose = "ViewOrder" })).StatusCode);
    }
    [Fact]
    public async Task Expired_claim_does_not_consume_token_and_guest_cookie_cannot_select_another_order()
    {
        using var auth = new AuthFactory(sql.ConnectionString); var mailbox = new OrderTestMailbox(); var clock = new OrderTestClock(); using var host = Server(auth, mailbox, clock); using var guest = Browser(host);
        var first = await Order(guest); await Drain(host);
        Assert.Equal(HttpStatusCode.OK, (await Post(guest, "guest/order-access/exchange", new { token = Secret(Mail(mailbox, first.Number, "ViewOrder")), purpose = "ViewOrder" })).StatusCode);
        var second = await Order(guest); var selected = await guest.GetFromJsonAsync<JsonElement>("/api/v1/guest/order?orderId=" + second.Id); Assert.Equal(first.Id.ToString(), selected.GetProperty("id").GetString());
        var secret = await Access(host, guest, mailbox, first, "ClaimOrder"); using var account = Browser(host); await Account(auth, account, first.Email); clock.Current = clock.Current.AddHours(2);
        await Error(await Post(account, "me/order-claims", new { token = secret }), HttpStatusCode.BadRequest, "INVALID_ORDER_TOKEN");
        await using var db = sql.CreateContext(); Assert.Null((await db.Orders.SingleAsync(o => o.Id == first.Id)).UserId); Assert.Null((await db.OrderAccessTokens.SingleAsync(t => t.OrderId == first.Id && t.Purpose == OrderAccessTokenPurpose.ClaimOrder)).UsedAt);
        var item = await db.OrderItems.SingleAsync(i => i.OrderId == first.Id); var inventory = await db.Inventory.SingleAsync(i => i.VariantId == item.VariantId); Assert.Equal(5, inventory.OnHand); Assert.Equal(1, inventory.Reserved);
    }
}
