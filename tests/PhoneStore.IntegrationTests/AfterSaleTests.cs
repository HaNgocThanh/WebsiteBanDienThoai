using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.GuestOrders;
namespace PhoneStore.IntegrationTests;
public sealed partial class PaymentTests
{
    private static async Task<HttpResponseMessage> After(HttpClient client, string path, string? version, object body, bool csrf = true) {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/" + path) { Content = JsonContent.Create(body) };
        if (version is not null) request.Headers.TryAddWithoutValidation("If-Match", "\"" + version + "\"");
        if (csrf) request.Headers.Add("X-CSRF-TOKEN", (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString()); return await client.SendAsync(request);
    }
    private static Task<JsonElement> AfterRead(HttpClient client, string id) => client.GetFromJsonAsync<JsonElement>("/api/v1/admin/orders/" + id + "/after-sale");
    [Fact]
    public async Task Delivery_winning_before_recall_review_prevents_return_but_allows_rejecting_pending_request() {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var order = await Place(admin, actor.Email, "COD"); await Advance(admin, order.Id, "Approved"); await Advance(admin, order.Id, "Shipping");
        var requested = await Ok(await After(admin, "me/orders/" + order.Id + "/cancellation-requests", await OrderVersion(admin, order.Id), new { reason = "Synthetic stop delivery", operationKey = Guid.NewGuid() })); await Advance(admin, order.Id, "Delivered"); var path = "admin/orders/" + order.Id + "/cancellation-requests/" + requested.GetProperty("resourceId").GetString() + "/review"; var rv = requested.GetProperty("resourceVersion").GetString();
        await Problem(await After(admin, path, rv, new { reason = "Synthetic recovered claim after delivery", decision = "Approve", goodsRecovered = true, reference = "Synthetic proof", operationKey = Guid.NewGuid() }), HttpStatusCode.Conflict, "CANCELLATION_NOT_ALLOWED");
        await Ok(await After(admin, path, rv, new { reason = "Synthetic already delivered, reject recall", decision = "Reject", operationKey = Guid.NewGuid() }));
        await using var db = sql.CreateContext(); Assert.Equal(OrderStatus.Delivered, (await db.Orders.SingleAsync(o => o.Id == long.Parse(order.Id))).Status); Assert.False(await db.InventoryMovements.AnyAsync(m => m.OrderItem!.OrderId == long.Parse(order.Id) && m.Kind == InventoryMovementKind.CancelReturn));
    }
    [Fact]
    public async Task Refund_reference_cannot_complete_two_different_orders_even_concurrently() {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); using var customer = host.Browser(); var a = await Login(host.Server, admin, true); var b = await Login(host.Server, customer);
        var first = await Place(admin, a.Email); var second = await Place(customer, b.Email);
        async Task<JsonElement> PaidAndCancelled(HttpClient owner, string id) {
            var form = await Ok(await Send(owner, "me/orders/" + id + "/sepay-checkout")); await Ok(await Ipn(owner, Notification(form)));
            await Ok(await After(admin, "admin/orders/" + id + "/cancel", await OrderVersion(admin, id), new { reason = "Synthetic cancelled paid", operationKey = Guid.NewGuid() }));
            return (await AfterRead(admin, id)).GetProperty("refunds")[0];
        }
        var ra = await PaidAndCancelled(admin, first.Id); var rb = await PaidAndCancelled(customer, second.Id); var reference = "ref-" + Guid.NewGuid().ToString("N");
        var calls = await Task.WhenAll(After(admin, "admin/refunds/" + ra.GetProperty("id").GetString() + "/complete", ra.GetProperty("version").GetString(), new { reason = "Synthetic verified", reference, operationKey = Guid.NewGuid() }), After(admin, "admin/refunds/" + rb.GetProperty("id").GetString() + "/complete", rb.GetProperty("version").GetString(), new { reason = "Synthetic verified", reference = reference.ToUpperInvariant(), operationKey = Guid.NewGuid() }));
        Assert.Single(calls, r => r.StatusCode == HttpStatusCode.OK); await Problem(calls.Single(r => r.StatusCode != HttpStatusCode.OK), HttpStatusCode.Conflict, "REFUND_REFERENCE_CONFLICT");
        await using var db = sql.CreateContext(); Assert.Equal(1, await db.Refunds.CountAsync(r => r.Reference == reference && r.Status == RefundStatus.Completed));
    }
    [Fact]
    public async Task Paid_cancel_releases_once_creates_pending_refund_and_verified_refund_completes_once() {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var order = await Place(admin, actor.Email);
        var form = await Ok(await Send(admin, "me/orders/" + order.Id + "/sepay-checkout")); await Ok(await Ipn(admin, Notification(form)));
        var version = await OrderVersion(admin, order.Id); var body = new { reason = "Synthetic owner cancellation", operationKey = Guid.NewGuid() }; var path = "me/orders/" + order.Id + "/cancel";
        var receipt = await Ok(await After(admin, path, version, body)); Assert.Equal(receipt.ToString(), (await Ok(await After(admin, path, version, body))).ToString());
        await using var db = sql.CreateContext(); var id = long.Parse(order.Id); var reservation = await db.StockReservations.Include(r => r.OrderItem).SingleAsync(r => r.OrderItem.OrderId == id); Assert.Equal(StockReservationStatus.Released, reservation.Status);
        var stock = await db.Inventory.SingleAsync(i => i.VariantId == reservation.OrderItem.VariantId); Assert.Equal(10, stock.OnHand); Assert.Equal(0, stock.Reserved); Assert.Equal(1, await db.InventoryMovements.CountAsync(m => m.OrderItem!.OrderId == id && m.Kind == InventoryMovementKind.Release));
        var refund = await db.Refunds.SingleAsync(r => r.Payment.OrderId == id); Assert.Equal(RefundStatus.Pending, refund.Status); Assert.Equal(1000000, refund.Amount); Assert.Equal(OrderStatus.Cancelled, (await db.Orders.SingleAsync(o => o.Id == id)).Status);
        var refundVersion = Convert.ToBase64String(refund.Version); var complete = new { reason = "Synthetic verified transfer back", reference = Guid.NewGuid().ToString("N"), operationKey = Guid.NewGuid() };
        var completed = await Ok(await After(admin, "admin/refunds/" + refund.Id + "/complete", refundVersion, complete)); Assert.Equal(completed.ToString(), (await Ok(await After(admin, "admin/refunds/" + refund.Id + "/complete", refundVersion, complete))).ToString());
        await db.Entry(refund).ReloadAsync(); Assert.Equal(RefundStatus.Completed, refund.Status); Assert.Equal(0, await db.CustomerSpendEntries.CountAsync(e => e.OrderId == id));
    }
    [Fact]
    public async Task Shipping_request_does_not_restock_until_recovered_approval_and_rejection_allows_new_request() {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var order = await Place(admin, actor.Email, "COD"); await Advance(admin, order.Id, "Approved"); await Advance(admin, order.Id, "Shipping");
        var version = await OrderVersion(admin, order.Id); await Problem(await After(admin, "me/orders/" + order.Id + "/cancel", version, new { reason = "Synthetic", operationKey = Guid.NewGuid() }), HttpStatusCode.Conflict, "CANCELLATION_NOT_ALLOWED");
        var requestBody = new { reason = "Synthetic stop delivery", operationKey = Guid.NewGuid() }; var request = await Ok(await After(admin, "me/orders/" + order.Id + "/cancellation-requests", version, requestBody)); await Ok(await After(admin, "me/orders/" + order.Id + "/cancellation-requests", version, requestBody));
        var reviewPath = "admin/orders/" + order.Id + "/cancellation-requests/" + request.GetProperty("resourceId").GetString() + "/review"; var rv = request.GetProperty("resourceVersion").GetString();
        await Problem(await After(admin, reviewPath, rv, new { reason = "Synthetic", decision = "Approve", goodsRecovered = false, reference = "Synthetic proof", operationKey = Guid.NewGuid() }), HttpStatusCode.Conflict, "GOODS_RECOVERY_REQUIRED");
        await using var db = sql.CreateContext(); var stock = await db.Inventory.SingleAsync(i => i.VariantId == db.OrderItems.Where(i => i.OrderId == long.Parse(order.Id)).Select(i => i.VariantId).Single()); Assert.Equal(9, stock.OnHand);
        await Ok(await After(admin, reviewPath, rv, new { reason = "Synthetic rejected", decision = "Reject", operationKey = Guid.NewGuid() }));
        request = await Ok(await After(admin, "me/orders/" + order.Id + "/cancellation-requests", await OrderVersion(admin, order.Id), new { reason = "Synthetic retry recall", operationKey = Guid.NewGuid() }));
        reviewPath = "admin/orders/" + order.Id + "/cancellation-requests/" + request.GetProperty("resourceId").GetString() + "/review"; rv = request.GetProperty("resourceVersion").GetString(); var approve = new { reason = "Synthetic all goods checked", decision = "Approve", goodsRecovered = true, reference = "Synthetic recovered proof", operationKey = Guid.NewGuid() };
        await Ok(await After(admin, reviewPath, rv, approve)); await Ok(await After(admin, reviewPath, rv, approve)); await db.Entry(stock).ReloadAsync(); Assert.Equal(10, stock.OnHand); Assert.Equal(0, stock.Reserved);
        Assert.Equal(1, await db.InventoryMovements.CountAsync(m => m.OrderItem!.OrderId == long.Parse(order.Id) && m.Kind == InventoryMovementKind.CancelReturn)); Assert.Equal(StockReservationStatus.Consumed, (await db.StockReservations.SingleAsync(r => r.OrderItem.OrderId == long.Parse(order.Id))).Status);
    }
    [Fact]
    public async Task Cancel_shipping_race_has_exactly_one_branch_with_consistent_stock() {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var order = await Place(admin, actor.Email, "COD"); await Advance(admin, order.Id, "Approved"); var version = await OrderVersion(admin, order.Id);
        var calls = await Task.WhenAll(Transition(admin, order.Id, "Shipping", version), After(admin, "admin/orders/" + order.Id + "/cancel", version, new { reason = "Synthetic concurrent cancellation", operationKey = Guid.NewGuid() })); Assert.Single(calls, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(calls, r => r.StatusCode == HttpStatusCode.PreconditionFailed);
        await using var db = sql.CreateContext(); var orderRow = await db.Orders.SingleAsync(o => o.Id == long.Parse(order.Id)); var item = await db.OrderItems.SingleAsync(i => i.OrderId == orderRow.Id); var stock = await db.Inventory.SingleAsync(i => i.VariantId == item.VariantId); Assert.Equal(0, stock.Reserved); Assert.Equal(orderRow.Status == OrderStatus.Shipping ? 9 : 10, stock.OnHand);
    }
    [Fact]
    public async Task Cancellation_before_IPN_late_money_stays_cancelled_with_one_pending_refund() {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var order = await Place(admin, actor.Email); var form = await Ok(await Send(admin, "me/orders/" + order.Id + "/sepay-checkout"));
        await Ok(await After(admin, "me/orders/" + order.Id + "/cancel", await OrderVersion(admin, order.Id), new { reason = "Synthetic unpaid cancellation", operationKey = Guid.NewGuid() })); var notification = Notification(form); await Ok(await Ipn(admin, notification)); await Ok(await Ipn(admin, notification));
        await using var db = sql.CreateContext(); Assert.Equal(OrderStatus.Cancelled, (await db.Orders.SingleAsync(o => o.Id == long.Parse(order.Id))).Status); Assert.Equal(1, await db.Refunds.CountAsync(r => r.Payment.OrderId == long.Parse(order.Id) && r.Status == RefundStatus.Pending));
    }
    [Fact]
    public async Task Refund_failure_releases_capacity_new_requests_are_bounded_and_spend_is_reduced_once_only_after_completion() {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var order = await Place(admin, actor.Email, "COD"); await Advance(admin, order.Id, "Approved"); await Advance(admin, order.Id, "Shipping"); await Advance(admin, order.Id, "Delivered"); await Ok(await Send(admin, "admin/orders/" + order.Id + "/cod-receipts", new { amount = 1000000, note = "Synthetic collected", operationKey = Guid.NewGuid() })); await Advance(admin, order.Id, "Completed");
        await using var db = sql.CreateContext(); var id = long.Parse(order.Id); db.CustomerSpendEntries.Add(new() { UserId = actor.Id, OrderId = id, Kind = SpendEntryKind.OrderCompleted, Amount = 1000000, EventKey = "order:" + id + ":completed" }); var profile = await db.CustomerProfiles.SingleAsync(p => p.UserId == actor.Id); profile.EligibleSpend = 1000000; await db.SaveChangesAsync();
        var payment = await db.Payments.SingleAsync(p => p.OrderId == id); var path = "admin/payments/" + payment.Id + "/refunds";
        await Problem(await After(admin, path, await OrderVersion(admin, order.Id), new { reason = "Synthetic invalid shipping refund", merchandiseAmount = 0, shippingAmount = 1, operationKey = Guid.NewGuid() }), HttpStatusCode.Conflict, "REFUND_LIMIT");
        var pending = await Ok(await After(admin, path, await OrderVersion(admin, order.Id), new { reason = "Synthetic first refund", merchandiseAmount = 600000, shippingAmount = 0, operationKey = Guid.NewGuid() }));
        await Problem(await After(admin, path, await OrderVersion(admin, order.Id), new { reason = "Synthetic over refund", merchandiseAmount = 500000, shippingAmount = 0, operationKey = Guid.NewGuid() }), HttpStatusCode.Conflict, "REFUND_LIMIT");
        await Ok(await After(admin, "admin/refunds/" + pending.GetProperty("resourceId").GetString() + "/fail", pending.GetProperty("resourceVersion").GetString(), new { reason = "Synthetic failed transfer", operationKey = Guid.NewGuid() })); await db.Entry(profile).ReloadAsync(); Assert.Equal(1000000, profile.EligibleSpend);
        pending = await Ok(await After(admin, path, await OrderVersion(admin, order.Id), new { reason = "Synthetic retry new key", merchandiseAmount = 600000, shippingAmount = 0, operationKey = Guid.NewGuid() })); var complete = new { reason = "Synthetic verified refund", reference = Guid.NewGuid().ToString("N"), operationKey = Guid.NewGuid() }; var completePath = "admin/refunds/" + pending.GetProperty("resourceId").GetString() + "/complete"; var rv = pending.GetProperty("resourceVersion").GetString();
        var results = await Task.WhenAll(After(admin, completePath, rv, complete), After(admin, completePath, rv, complete)); foreach (var result in results) await Ok(result); await db.Entry(profile).ReloadAsync(); Assert.Equal(400000, profile.EligibleSpend); Assert.Equal(1, await db.CustomerSpendEntries.CountAsync(e => e.OrderId == id && e.Kind == SpendEntryKind.Refund)); Assert.Equal(OrderStatus.Completed, (await db.Orders.SingleAsync(o => o.Id == id)).Status);
    }
    [Fact]
    public async Task Cancellation_owner_guest_scope_roles_CSRF_and_versions_are_enforced() {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); using var other = host.Browser(); using var guest = host.Browser(); var actor = await Login(host.Server, admin, true); await Login(host.Server, other); var order = await Place(admin, actor.Email, "COD"); var rv = await OrderVersion(admin, order.Id); var body = new { reason = "Synthetic", operationKey = Guid.NewGuid() };
        await Problem(await After(other, "me/orders/" + order.Id + "/cancel", rv, body), HttpStatusCode.NotFound, "NOT_FOUND"); Assert.Equal(HttpStatusCode.Forbidden, (await After(other, "admin/orders/" + order.Id + "/cancel", rv, body)).StatusCode); await Problem(await After(admin, "me/orders/" + order.Id + "/cancel", rv, body, false), HttpStatusCode.Forbidden, "CSRF_INVALID"); await Problem(await After(admin, "me/orders/" + order.Id + "/cancel", null, body), (HttpStatusCode)428, "PRECONDITION_REQUIRED");
        var guestOrder = await Place(guest, "synthetic-cancel@example.invalid", "COD");
        await using var db = sql.CreateContext(); var token = new OrderAccessToken { OrderId = long.Parse(guestOrder.Id), Purpose = OrderAccessTokenPurpose.ViewOrder, TokenHash = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), CreatedAt = host.Clock.Current.UtcDateTime, ExpiresAt = host.Clock.Current.AddHours(1).UtcDateTime, UsedAt = host.Clock.Current.UtcDateTime }; db.OrderAccessTokens.Add(token); await db.SaveChangesAsync();
        void Grant(OrderAccessTokenPurpose purpose) { var http = new Microsoft.AspNetCore.Http.DefaultHttpContext(); host.Server.Services.GetRequiredService<GuestOrderSession>().Write(http, new(token.OrderId, token.Id, purpose, host.Clock.Current.AddMinutes(30).UtcDateTime)); guest.DefaultRequestHeaders.Remove("Cookie"); guest.DefaultRequestHeaders.Add("Cookie", http.Response.Headers.SetCookie.ToString().Split(';')[0]); }
        Grant(OrderAccessTokenPurpose.ViewOrder); var data = await guest.GetFromJsonAsync<JsonElement>("/api/v1/guest/order/after-sale"); rv = data.GetProperty("version").GetString(); Assert.False(data.GetProperty("canCancel").GetBoolean()); await Problem(await After(guest, "guest/order/cancel", rv, body), HttpStatusCode.Unauthorized, "GUEST_ACCESS_REQUIRED");
        token.Purpose = OrderAccessTokenPurpose.CancelOrder; await db.SaveChangesAsync(); Grant(OrderAccessTokenPurpose.CancelOrder); await Ok(await After(guest, "guest/order/cancel", rv, body));
    }
    [Fact]
    public async Task Cancellation_outbox_failure_rolls_back_inventory_history_refunds_and_same_key_retries() {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var order = await Place(admin, actor.Email); var form = await Ok(await Send(admin, "me/orders/" + order.Id + "/sepay-checkout")); await Ok(await Ipn(admin, Notification(form))); var rv = await OrderVersion(admin, order.Id); var op = Guid.NewGuid(); var body = new { reason = "Synthetic rollback cancellation", operationKey = op }; var trigger = "SyntheticAfterSale_" + Guid.NewGuid().ToString("N");
        await using var db = sql.CreateContext(); var create = $"CREATE TRIGGER [{trigger}] ON [OutboxMessages] AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE [EventKey]='Order.AfterSale:{op:D}') THROW 51090,'Synthetic after sale failure',1; END"; var drop = $"DROP TRIGGER [{trigger}]"; await db.Database.ExecuteSqlRawAsync(create);
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await After(admin, "admin/orders/" + order.Id + "/cancel", rv, body)).StatusCode); Assert.Equal(rv, await OrderVersion(admin, order.Id)); Assert.False(await db.Refunds.AnyAsync(r => r.Payment.OrderId == long.Parse(order.Id))); Assert.Equal(StockReservationStatus.Active, (await db.StockReservations.SingleAsync(r => r.OrderItem.OrderId == long.Parse(order.Id))).Status); }
        finally { await db.Database.ExecuteSqlRawAsync(drop); }
        await Ok(await After(admin, "admin/orders/" + order.Id + "/cancel", rv, body));
        var refund = await db.Refunds.AsNoTracking().SingleAsync(r => r.Payment.OrderId == long.Parse(order.Id)); var completeOp = Guid.NewGuid(); var complete = new { reason = "Synthetic completed rollback", reference = Guid.NewGuid().ToString("N"), operationKey = completeOp };
        var completeTrigger = "SyntheticRefund_" + Guid.NewGuid().ToString("N"); var completeDdl = $"CREATE TRIGGER [{completeTrigger}] ON [OutboxMessages] AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE [EventKey]='Order.AfterSale:{completeOp:D}') THROW 51090,'Synthetic complete failure',1; END"; var completeDrop = $"DROP TRIGGER [{completeTrigger}]";
        await db.Database.ExecuteSqlRawAsync(completeDdl);
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await After(admin, "admin/refunds/" + refund.Id + "/complete", Convert.ToBase64String(refund.Version), complete)).StatusCode); var unchanged = await db.Refunds.AsNoTracking().SingleAsync(r => r.Id == refund.Id); Assert.Equal(RefundStatus.Pending, unchanged.Status); Assert.Null(unchanged.Reference); }
        finally { await db.Database.ExecuteSqlRawAsync(completeDrop); }
        await Ok(await After(admin, "admin/refunds/" + refund.Id + "/complete", Convert.ToBase64String(refund.Version), complete));
    }
}
