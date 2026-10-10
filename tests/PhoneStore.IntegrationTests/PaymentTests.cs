using System.Net;


using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhoneStore.Api.Services.Payments;
using PhoneStore.Api.Services.Catalog;
using PhoneStore.Api.Services.Notifications;
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
public sealed class PaymentTests(SqlFixture sql)
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
    private static async Task<(string Email, Guid Id)> Login(WebApplicationFactory<Program> host, HttpClient browser, bool admin = false)
    {
        var email = Guid.NewGuid().ToString("N") + "@example.invalid";
        Assert.Equal(HttpStatusCode.Accepted, (await Send(browser, "auth/register", new { email, password = "Synthetic!Password123", fullName = "Synthetic checkout user" })).StatusCode);
        var mail = ((TestAuthMailbox)host.Services.GetRequiredService<IAuthEmailSender>()).Messages.Single(m => m.Email == email);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(browser, "auth/verify-email", new { mail.UserId, mail.Token })).StatusCode);
        if (admin) { using var scope = host.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<AuthBootstrap>().RunAsync(email, default); }
        Assert.Equal(HttpStatusCode.NoContent, (await Send(browser, "auth/login", new { email, password = "Synthetic!Password123" })).StatusCode);
        return (email, mail.UserId);
    }


    private sealed class Host(string connection, bool configured = true) : IDisposable
    {
        private readonly AuthFactory auth = new(connection);
        public OrderTestClock Clock { get; } = new();
        private WebApplicationFactory<Program>? server;
        public WebApplicationFactory<Program> Server => server ??= auth.WithWebHostBuilder(builder => {
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string,string?> {
                ["SePay:Sandbox:MerchantId"] = configured ? "SYNTHETIC-MERCHANT" : "",
                ["SePay:Sandbox:SecretKey"] = "synthetic-signing-key", ["SePay:Sandbox:IpnSecret"] = "synthetic-ipn-key",
                ["SePay:Sandbox:PublicBaseUrl"] = "https://synthetic.example.invalid", ["Auth:RateLimit:payment"] = "1000", ["Auth:RateLimit:checkout"] = "1000", ["Auth:RateLimit:quote"] = "1000"
            }));
            builder.ConfigureTestServices(services => { services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Clock); });
        });
        public HttpClient Browser() => Server.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        public void Dispose() { server?.Dispose(); auth.Dispose(); }
    }
    private async Task<(string Id, string Key)> Place(HttpClient browser, string email, string method = "BankTransfer", string province = "Thành phố Hồ Chí Minh")
    {
        var key = await Session(browser); var response = await Send(browser, "orders", await Payload(browser, await Variant(), email: email, method: method, province: province), key);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return ((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!, key);
    }
    private static async Task<JsonElement> Ok(HttpResponseMessage response) { Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    private static object Notification(JsonElement form, string? transaction = null, string? amount = null, string currency = "VND", string method = "BANK_TRANSFER", Guid? transactionGuid = null) => new {
        notification_type = "ORDER_PAID", order = new { order_invoice_number = form.GetProperty("invoice").GetString(), order_status = "CAPTURED", order_currency = currency, order_amount = amount ?? form.GetProperty("amount").GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture) },
        transaction = new { id = transactionGuid ?? Guid.NewGuid(), transaction_id = transaction ?? Guid.NewGuid().ToString("N"), payment_method = method, transaction_type = "PAYMENT", transaction_status = "APPROVED", transaction_currency = currency, transaction_amount = amount ?? form.GetProperty("amount").GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture) }
    };
    private static async Task<HttpResponseMessage> Ipn(HttpClient client, object payload, string? secret = "synthetic-ipn-key") {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/sepay/ipn") { Content = JsonContent.Create(payload) }; if (secret != null) request.Headers.Add("X-Secret-Key", secret); return await client.SendAsync(request);
    }
    private static async Task<JsonElement> List(HttpClient client, string id, bool admin = false) => await client.GetFromJsonAsync<JsonElement>("/api/v1/" + (admin ? "admin" : "me") + "/orders/" + id + "/payments");
    [Fact]
    public async Task Initiation_is_owner_scoped_stable_signed_pending_and_never_reserves_twice()
    {
        using var host = new Host(sql.ConnectionString); using var owner = host.Browser(); using var other = host.Browser(); var user = await Login(host.Server, owner); await Login(host.Server, other);
        var order = await Place(owner, user.Email); var route = "me/orders/" + order.Id + "/sepay-checkout";
        var responses = await Task.WhenAll(Send(owner, route), Send(owner, route)); var form = await Ok(responses[0]); Assert.Equal(form.ToString(), (await Ok(responses[1])).ToString());
        Assert.Equal(SePaySandbox.CheckoutUrl, form.GetProperty("action").GetString()); Assert.DoesNotContain("synthetic-signing-key", form.ToString()); Assert.DoesNotContain(user.Email, form.ToString());
        var fields = form.GetProperty("fields").EnumerateArray().Select(f => new PhoneStore.Api.DTOs.Payments.SePayField(f.GetProperty("name").GetString()!, f.GetProperty("value").GetString()!)).ToList();
        Assert.Equal(SePaySandbox.Signature(fields[..^1], "synthetic-signing-key"), fields[^1].Value);
        await Problem(await Send(other, route), HttpStatusCode.NotFound, "NOT_FOUND"); await Problem(await Send(owner, route, csrf: "wrong"), HttpStatusCode.Forbidden, "CSRF_INVALID");
        await using var db = sql.CreateContext(); var id = long.Parse(order.Id); Assert.Single(await db.Payments.Where(p => p.OrderId == id).ToListAsync()); Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync(p => p.OrderId == id)).Status);
        Assert.Equal(0, (await List(owner, order.Id)).GetProperty("summary").GetProperty("confirmed").GetDecimal()); Assert.Equal(1, (await db.StockReservations.Include(r => r.OrderItem).SingleAsync(r => r.OrderItem.OrderId == id)).Quantity);
    }
    [Fact]
    public async Task Authenticated_IPN_replay_and_parallel_delivery_receive_once_clear_hold_keep_inventory()
    {
        using var host = new Host(sql.ConnectionString); using var owner = host.Browser(); using var provider = host.Browser(); var user = await Login(host.Server, owner); var order = await Place(owner, user.Email);
        var form = await Ok(await Send(owner, "me/orders/" + order.Id + "/sepay-checkout")); var payload = Notification(form);
        var results = await Task.WhenAll(Ipn(provider, payload), Ipn(provider, payload)); foreach (var result in results) await Ok(result); await Ok(await Ipn(provider, payload));
        await Problem(await Ipn(provider, Notification(form)), HttpStatusCode.Conflict, "OPERATION_KEY_MISMATCH");
        await using var db = sql.CreateContext(); var id = long.Parse(order.Id); var payment = await db.Payments.SingleAsync(p => p.OrderId == id); Assert.Equal(PaymentStatus.Confirmed, payment.Status); Assert.Null(payment.ConfirmedByUserId);
        var o = await db.Orders.SingleAsync(o => o.Id == id); Assert.Null(o.PaymentDueAt); Assert.Equal(OrderStatus.Placed, o.Status); Assert.Equal(1000000, o.GrandTotal);
        var reservation = await db.StockReservations.Include(r => r.OrderItem).SingleAsync(r => r.OrderItem.OrderId == id); Assert.Null(reservation.ExpiresAt); var inventory = await db.Inventory.SingleAsync(i => i.VariantId == reservation.OrderItem.VariantId); Assert.Equal(10, inventory.OnHand); Assert.Equal(1, inventory.Reserved);
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "SePayReceived" && a.EntityId == payment.Id.ToString())); Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.EventKey == "PaymentConfirmed:" + payment.Id)); Assert.Equal("Paid", (await List(owner, order.Id)).GetProperty("summary").GetProperty("status").GetString());
        await Ok(await Ipn(provider, new { notification_type = "TRANSACTION_VOID" })); Assert.Equal(1000000, (await List(owner, order.Id)).GetProperty("summary").GetProperty("confirmed").GetDecimal());
    }
    [Fact]
    public async Task Forged_or_mismatched_callbacks_leave_pending_and_secret_does_not_bypass_other_CSRF()
    {
        using var host = new Host(sql.ConnectionString); using var owner = host.Browser(); var user = await Login(host.Server, owner); var order = await Place(owner, user.Email); var form = await Ok(await Send(owner, "me/orders/" + order.Id + "/sepay-checkout"));
        await Problem(await Ipn(owner, Notification(form), null), HttpStatusCode.Unauthorized, "SEPAY_IPN_UNAUTHORIZED"); await Problem(await Ipn(owner, Notification(form), "wrong"), HttpStatusCode.Unauthorized, "SEPAY_IPN_UNAUTHORIZED");
        await Problem(await Ipn(owner, Notification(form, amount: "999999")), HttpStatusCode.Conflict, "PAYMENT_AMOUNT_MISMATCH");
        await Problem(await Ipn(owner, Notification(form, amount: "1000000.50")), HttpStatusCode.BadRequest, "INVALID_SEPAY_IPN");
        foreach (var payload in new[] { Notification(form, currency: "USD"), Notification(form, method: "CARD") }) await Problem(await Ipn(owner, payload), HttpStatusCode.BadRequest, "INVALID_SEPAY_IPN");
        using var mutation = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/orders/" + order.Id + "/sepay-checkout"); mutation.Headers.Add("X-Secret-Key", "synthetic-ipn-key"); await Problem(await owner.SendAsync(mutation), HttpStatusCode.Forbidden, "CSRF_INVALID");
        Assert.Equal(0, (await List(owner, order.Id)).GetProperty("summary").GetProperty("confirmed").GetDecimal());
    }
    [Fact]
    public async Task Transaction_reference_collision_across_orders_confirms_exactly_one()
    {
        using var host = new Host(sql.ConnectionString); using var owner = host.Browser(); var user = await Login(host.Server, owner); var first = await Place(owner, user.Email); var second = await Place(owner, user.Email);
        var a = await Ok(await Send(owner, "me/orders/" + first.Id + "/sepay-checkout")); var b = await Ok(await Send(owner, "me/orders/" + second.Id + "/sepay-checkout")); var reference = Guid.NewGuid().ToString("N");
        var results = await Task.WhenAll(Ipn(owner, Notification(a, reference)), Ipn(owner, Notification(b, reference))); Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); await Problem(results.Single(r => r.StatusCode != HttpStatusCode.OK), HttpStatusCode.Conflict, "PAYMENT_REFERENCE_CONFLICT");
        await using var db = sql.CreateContext(); Assert.Equal(1, await db.Payments.CountAsync(p => p.Reference == "SePaySandbox:" + reference));
    }
    [Fact]
    public async Task Guest_checkout_cookie_can_pay_without_email_grant_but_foreign_browser_cannot()
    {
        using var host = new Host(sql.ConnectionString); using var guest = host.Browser(); using var other = host.Browser(); var order = await Place(guest, "synthetic-guest@example.invalid");
        var route = "checkout/sessions/" + order.Key + "/sepay-checkout"; var form = await Ok(await Send(guest, route)); Assert.Equal("PSB-" + form.GetProperty("paymentId").GetString(), form.GetProperty("invoice").GetString());
        await Problem(await Send(other, route), HttpStatusCode.NotFound, "CHECKOUT_NOT_FOUND");
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/v1/me/orders/" + order.Id + "/payments")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync("/api/v1/checkout/sessions/" + order.Key + "/payments")).StatusCode);
        host.Clock.Current += TimeSpan.FromHours(25); await Problem(await Send(guest, route), HttpStatusCode.NotFound, "CHECKOUT_NOT_FOUND");
    }
    [Fact]
    public async Task Email_view_grant_payment_access_is_limited_and_revoked_when_order_is_claimed()
    {
        using var host = new Host(sql.ConnectionString); using var guest = host.Browser(); using var viewer = host.Browser(); using var owner = host.Browser(); var order = await Place(guest, "synthetic-view@example.invalid");
        await using var db = sql.CreateContext(); var token = new OrderAccessToken { OrderId = long.Parse(order.Id), Purpose = OrderAccessTokenPurpose.ViewOrder, TokenHash = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), CreatedAt = host.Clock.Current.UtcDateTime, ExpiresAt = host.Clock.Current.AddHours(1).UtcDateTime, UsedAt = host.Clock.Current.UtcDateTime }; db.OrderAccessTokens.Add(token); await db.SaveChangesAsync();
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext(); host.Server.Services.GetRequiredService<PhoneStore.Api.Services.GuestOrders.GuestOrderSession>().Write(http, new(long.Parse(order.Id), token.Id, OrderAccessTokenPurpose.ViewOrder, host.Clock.Current.AddMinutes(30).UtcDateTime)); viewer.DefaultRequestHeaders.Add("Cookie", http.Response.Headers.SetCookie.ToString().Split(';')[0]);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/guest/order/payments")).StatusCode); await Ok(await Send(viewer, "guest/order/sepay-checkout")); await Problem(await Send(guest, "guest/order/sepay-checkout"), HttpStatusCode.Unauthorized, "GUEST_ACCESS_REQUIRED");
        var customer = await Login(host.Server, owner); var o = await db.Orders.SingleAsync(o => o.Id == long.Parse(order.Id)); o.UserId = customer.Id; await db.SaveChangesAsync(); await Problem(await Send(viewer, "guest/order/sepay-checkout"), HttpStatusCode.Unauthorized, "GUEST_ACCESS_REQUIRED");
    }
    [Fact]
    public async Task Claimed_order_revokes_guest_checkout_payment_access_and_new_owner_can_resume()
    {
        using var host = new Host(sql.ConnectionString); using var guest = host.Browser(); using var owner = host.Browser(); var order = await Place(guest, "synthetic-claim@example.invalid");
        var form = await Ok(await Send(guest, "checkout/sessions/" + order.Key + "/sepay-checkout")); var customer = await Login(host.Server, owner);
        // Fixture represents the committed P3 claim; payment authorization must inspect current owner.
        await using (var db = sql.CreateContext()) { var o = await db.Orders.SingleAsync(o => o.Id == long.Parse(order.Id)); o.UserId = customer.Id; await db.SaveChangesAsync(); }
        await Problem(await Send(guest, "checkout/sessions/" + order.Key + "/sepay-checkout"), HttpStatusCode.NotFound, "CHECKOUT_NOT_FOUND");
        var resumed = await Ok(await Send(owner, "me/orders/" + order.Id + "/sepay-checkout")); Assert.Equal(form.GetProperty("invoice").GetString(), resumed.GetProperty("invoice").GetString());
    }
    [Fact]
    public async Task Missing_configuration_fails_closed_after_authorization_and_COD_remains_available()
    {
        using var host = new Host(sql.ConnectionString, false); using var owner = host.Browser(); using var other = host.Browser(); using var admin = host.Browser(); var user = await Login(host.Server, owner); await Login(host.Server, other); await Login(host.Server, admin, true);
        var order = await Place(owner, user.Email); await Problem(await Send(owner, "me/orders/" + order.Id + "/sepay-checkout"), HttpStatusCode.ServiceUnavailable, "SEPAY_UNAVAILABLE"); await Problem(await Send(other, "me/orders/" + order.Id + "/sepay-checkout"), HttpStatusCode.NotFound, "NOT_FOUND");
        var cod = await Place(owner, user.Email, "COD"); await Ok(await Send(admin, "admin/orders/" + cod.Id + "/cod-receipts", new { amount = 1000000, note = "Synthetic verified COD", operationKey = Guid.NewGuid() }));
    }
    [Fact]
    public async Task COD_replay_concurrency_permissions_and_remaining_balance_are_enforced()
    {
        using var host = new Host(sql.ConnectionString); using var owner = host.Browser(); using var admin = host.Browser(); var user = await Login(host.Server, owner); var actor = await Login(host.Server, admin, true); var order = await Place(owner, user.Email, "COD"); var route = "admin/orders/" + order.Id + "/cod-receipts";
        var key = Guid.NewGuid(); var payload = new { amount = 400000, note = "Synthetic collected cash", operationKey = key };
        await Problem(await Send(owner, route, payload), HttpStatusCode.Forbidden, "FORBIDDEN");
        var results = await Task.WhenAll(Send(admin, route, payload), Send(admin, route, payload)); Assert.Equal((await Ok(results[0])).ToString(), (await Ok(results[1])).ToString());
        await Problem(await Send(admin, route, new { amount = 400001, payload.note, operationKey = key }), HttpStatusCode.Conflict, "OPERATION_KEY_MISMATCH");
        results = await Task.WhenAll(Send(admin, route, new { amount = 600000, payload.note, operationKey = Guid.NewGuid() }), Send(admin, route, new { amount = 600000, payload.note, operationKey = Guid.NewGuid() })); Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); await Problem(results.Single(r => r.StatusCode != HttpStatusCode.OK), HttpStatusCode.Conflict, "PAYMENT_AMOUNT_EXCEEDED");
        await using var db = sql.CreateContext(); var id = long.Parse(order.Id); Assert.Equal(1000000, await db.Payments.Where(p => p.OrderId == id).SumAsync(p => p.Amount)); Assert.Equal(OrderStatus.Placed, (await db.Orders.SingleAsync(o => o.Id == id)).Status); Assert.All(await db.Payments.Where(p => p.OrderId == id).ToListAsync(), p => Assert.Equal(actor.Id, p.ConfirmedByUserId));
    }
    [Fact]
    public async Task Late_payment_on_cancelled_order_creates_one_system_refund_without_reopening()
    {
        using var host = new Host(sql.ConnectionString); using var owner = host.Browser(); var user = await Login(host.Server, owner); var order = await Place(owner, user.Email, province: "Synthetic province"); var form = await Ok(await Send(owner, "me/orders/" + order.Id + "/sepay-checkout"));
        await using (var db = sql.CreateContext()) { var o = await db.Orders.SingleAsync(o => o.Id == long.Parse(order.Id)); o.Status = OrderStatus.Cancelled; await db.SaveChangesAsync(); }
        var payload = Notification(form); await Ok(await Ipn(owner, payload)); await Ok(await Ipn(owner, payload));
        await using var check = sql.CreateContext(); var refund = await check.Refunds.Include(r => r.Payment).SingleAsync(r => r.Payment.OrderId == long.Parse(order.Id)); Assert.Null(refund.CreatedByUserId); Assert.Equal(1000000, refund.MerchandiseAmount); Assert.Equal(30000, refund.ShippingAmount); Assert.Equal(RefundStatus.Pending, refund.Status); Assert.Equal(OrderStatus.Cancelled, (await check.Orders.SingleAsync(o => o.Id == long.Parse(order.Id))).Status); Assert.Equal("RefundPending", (await List(owner, order.Id)).GetProperty("summary").GetProperty("status").GetString());
    }
    [Fact]
    public async Task Outbox_failure_rolls_back_received_money_audit_and_hold_then_retry_succeeds()
    {
        using var host = new Host(sql.ConnectionString); using var owner = host.Browser(); var user = await Login(host.Server, owner); var order = await Place(owner, user.Email); var form = await Ok(await Send(owner, "me/orders/" + order.Id + "/sepay-checkout")); var payload = Notification(form); var trigger = "SyntheticPayment_" + Guid.NewGuid().ToString("N");
        // SQL identifiers are generated GUIDs; the event ID is a validated bigint from our test API.
        var paymentId = long.Parse(form.GetProperty("paymentId").GetString()!);
        var createTrigger = $"CREATE TRIGGER [{trigger}] ON [OutboxMessages] AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE [EventKey]='PaymentConfirmed:{paymentId}') THROW 51090,'Synthetic outbox failure',1; END";
        var dropTrigger = $"DROP TRIGGER [{trigger}]";
        await using var db = sql.CreateContext(); await db.Database.ExecuteSqlRawAsync(createTrigger);
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await Ipn(owner, payload)).StatusCode); var p = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == long.Parse(form.GetProperty("paymentId").GetString()!)); Assert.Equal(PaymentStatus.Pending, p.Status); Assert.Null(p.Reference); Assert.NotNull((await db.Orders.AsNoTracking().SingleAsync(o => o.Id == long.Parse(order.Id))).PaymentDueAt); }
        finally { await db.Database.ExecuteSqlRawAsync(dropTrigger); }
        await Ok(await Ipn(owner, payload));
    }
}
