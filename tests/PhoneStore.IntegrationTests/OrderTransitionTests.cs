using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Entities;
namespace PhoneStore.IntegrationTests;
public sealed partial class PaymentTests
{
    private static async Task<string> OrderVersion(HttpClient admin, string id) => (await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/orders/" + id)).GetProperty("version").GetString()!;
    private static async Task<HttpResponseMessage> Transition(HttpClient client, string id, string target, string? version, Guid? key = null, string reason = "Synthetic verified transition", bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/orders/" + id + "/transitions") { Content = JsonContent.Create(new { targetStatus = target, reason, operationKey = key ?? Guid.NewGuid() }) };
        if (version is not null) request.Headers.TryAddWithoutValidation("If-Match", "\"" + version + "\"");
        if (csrf) request.Headers.Add("X-CSRF-TOKEN", (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
    private static async Task<JsonElement> Advance(HttpClient admin, string id, string target) => await Ok(await Transition(admin, id, target, await OrderVersion(admin, id)));
    [Fact]
    public async Task SePay_auto_approval_wins_safely_against_manual_approval_and_never_regresses_shipping()
    {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var order = await Place(admin, actor.Email);
        var form = await Ok(await Send(admin, "me/orders/" + order.Id + "/sepay-checkout")); var notification = Notification(form); var version = await OrderVersion(admin, order.Id);
        var approve = Transition(admin, order.Id, "Approved", version); var ipn = Ipn(admin, notification);
        var results = await Task.WhenAll(approve, ipn); Assert.Contains(results[0].StatusCode, new[] { HttpStatusCode.Conflict, HttpStatusCode.PreconditionFailed }); await Ok(results[1]);
        await Advance(admin, order.Id, "Shipping"); await Ok(await Ipn(admin, notification));
        await using var db = sql.CreateContext(); var id = long.Parse(order.Id);
        Assert.Equal(OrderStatus.Shipping, (await db.Orders.SingleAsync(o => o.Id == id)).Status);
        Assert.Equal(1, await db.OrderStatusHistories.CountAsync(h => h.OrderId == id && h.ToStatus == OrderStatus.Approved));
        Assert.Equal(1, await db.InventoryMovements.CountAsync(m => m.OrderItem!.OrderId == id && m.Kind == InventoryMovementKind.Dispatch));
    }
    [Fact]
    public async Task COD_forward_edges_dispatch_once_and_completed_requires_receipt_with_event_for_future_spend()
    {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var order = await Place(admin, actor.Email, "COD");
        await Advance(admin, order.Id, "Approved"); await Advance(admin, order.Id, "Shipping"); await Advance(admin, order.Id, "Delivered");
        await Problem(await Transition(admin, order.Id, "Completed", await OrderVersion(admin, order.Id)), HttpStatusCode.Conflict, "PAYMENT_REQUIRED");
        await Ok(await Send(admin, "admin/orders/" + order.Id + "/cod-receipts", new { amount = 1000000, note = "Synthetic collected cash", operationKey = Guid.NewGuid() }));
        await Advance(admin, order.Id, "Completed");
        await using var db = sql.CreateContext(); var id = long.Parse(order.Id); var o = await db.Orders.SingleAsync(o => o.Id == id);
        Assert.Equal(OrderStatus.Completed, o.Status); Assert.NotNull(o.ShippedAt); Assert.NotNull(o.DeliveredAt); Assert.NotNull(o.CompletedAt);
        var reservation = await db.StockReservations.Include(r => r.OrderItem).SingleAsync(r => r.OrderItem.OrderId == id); Assert.Equal(StockReservationStatus.Consumed, reservation.Status);
        var inventory = await db.Inventory.SingleAsync(i => i.VariantId == reservation.OrderItem.VariantId); Assert.Equal(9, inventory.OnHand); Assert.Equal(0, inventory.Reserved);
        Assert.Single(await db.InventoryMovements.Where(m => m.OrderItemId == reservation.OrderItemId && m.Kind == InventoryMovementKind.Dispatch).ToListAsync());
        Assert.Equal(4, await db.OrderStatusHistories.CountAsync(h => h.OrderId == id && h.ActorType == OrderActorType.Admin));
        using var payload = JsonDocument.Parse((await db.OutboxMessages.SingleAsync(m => m.Type == "OrderCompleted" && m.PayloadJson.Contains("\"orderId\":\"" + order.Id + "\""))).PayloadJson);
        Assert.Equal(1000000, payload.RootElement.GetProperty("eligibleMerchandise").GetDecimal()); Assert.Equal("order:" + order.Id + ":completed", payload.RootElement.GetProperty("spendEventKey").GetString());
        Assert.Equal(actor.Id, payload.RootElement.GetProperty("userAtCompletion").GetGuid()); Assert.Equal(1, await db.CustomerSpendEntries.CountAsync(e => e.OrderId == id));
        await Problem(await Transition(admin, order.Id, "Delivered", await OrderVersion(admin, order.Id)), HttpStatusCode.Conflict, "INVALID_TRANSITION");
    }
    [Fact]
    public async Task Transition_RV_roles_CSRF_skip_edges_and_operation_payload_are_enforced()
    {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); using var customer = host.Browser(); using var guest = host.Browser(); var actor = await Login(host.Server, admin, true); await Login(host.Server, customer);
        var order = await Place(admin, actor.Email, "COD"); var version = await OrderVersion(admin, order.Id);
        await Problem(await Transition(admin, order.Id, "Approved", null), (HttpStatusCode)428, "PRECONDITION_REQUIRED");
        await Problem(await Transition(admin, order.Id, "Approved", "bad"), HttpStatusCode.BadRequest, "INVALID_ETAG");
        Assert.Equal(HttpStatusCode.Forbidden, (await Transition(customer, order.Id, "Approved", version)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Transition(guest, order.Id, "Approved", version)).StatusCode);
        await Problem(await Transition(admin, order.Id, "Approved", version, csrf: false), HttpStatusCode.Forbidden, "CSRF_INVALID");
        await Problem(await Transition(admin, order.Id, "Shipping", version), HttpStatusCode.Conflict, "INVALID_TRANSITION");
        var key = Guid.NewGuid(); await Ok(await Transition(admin, order.Id, "Approved", version, key));
        await Problem(await Transition(admin, order.Id, "Approved", version), HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT");
        await Problem(await Transition(admin, order.Id, "Approved", version, key, "Changed reason"), HttpStatusCode.Conflict, "OPERATION_KEY_MISMATCH");
    }
    [Fact]
    public async Task Parallel_dispatch_same_key_and_late_replay_return_one_receipt_without_double_inventory()
    {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var order = await Place(admin, actor.Email, "COD");
        await Advance(admin, order.Id, "Approved"); var version = await OrderVersion(admin, order.Id); var key = Guid.NewGuid();
        var calls = await Task.WhenAll(Transition(admin, order.Id, "Shipping", version, key), Transition(admin, order.Id, "Shipping", version, key));
        var first = await Ok(calls[0]); Assert.Equal(first.ToString(), (await Ok(calls[1])).ToString());
        await Advance(admin, order.Id, "Delivered"); Assert.Equal(first.ToString(), (await Ok(await Transition(admin, order.Id, "Shipping", version, key))).ToString());
        await using var db = sql.CreateContext(); Assert.Equal(1, await db.InventoryMovements.CountAsync(m => m.OrderItem!.OrderId == long.Parse(order.Id) && m.Kind == InventoryMovementKind.Dispatch));
    }
    [Fact]
    public async Task Parallel_different_keys_using_same_version_have_one_winner()
    {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var order = await Place(admin, actor.Email, "COD"); var version = await OrderVersion(admin, order.Id);
        var calls = await Task.WhenAll(Transition(admin, order.Id, "Approved", version), Transition(admin, order.Id, "Approved", version));
        Assert.Single(calls, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(calls, r => r.StatusCode == HttpStatusCode.PreconditionFailed);
    }
    [Fact]
    public async Task Bank_dispatch_requires_confirmed_money_and_refund_pending_blocks_completion()
    {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var order = await Place(admin, actor.Email);
        await Problem(await Transition(admin, order.Id, "Approved", await OrderVersion(admin, order.Id)), HttpStatusCode.Conflict, "PAYMENT_REQUIRED");
        var detail = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/orders/" + order.Id); Assert.Empty(detail.GetProperty("allowedActions").EnumerateArray());
        await Problem(await Transition(admin, order.Id, "Shipping", await OrderVersion(admin, order.Id)), HttpStatusCode.Conflict, "INVALID_TRANSITION");
        var form = await Ok(await Send(admin, "me/orders/" + order.Id + "/sepay-checkout")); await Ok(await Ipn(admin, Notification(form)));
        await Advance(admin, order.Id, "Shipping"); await Advance(admin, order.Id, "Delivered");
        await using var db = sql.CreateContext(); var p = await db.Payments.SingleAsync(p => p.OrderId == long.Parse(order.Id));
        var refund = new Refund { PaymentId = p.Id, MerchandiseAmount = 1, ShippingAmount = 0, Status = RefundStatus.Pending, Reason = "Synthetic capacity boundary", EventKey = "Synthetic:" + Guid.NewGuid(), CreatedByUserId = actor.Id }; db.Refunds.Add(refund); await db.SaveChangesAsync();
        await Problem(await Transition(admin, order.Id, "Completed", await OrderVersion(admin, order.Id)), HttpStatusCode.Conflict, "PAYMENT_REQUIRED");
        refund.Status = RefundStatus.Completed; await db.SaveChangesAsync();
        await Problem(await Transition(admin, order.Id, "Completed", await OrderVersion(admin, order.Id)), HttpStatusCode.Conflict, "PAYMENT_REQUIRED");
    }
    [Fact]
    public async Task Invalid_second_reservation_rolls_back_first_item_and_hidden_catalog_does_not_block_snapshot_dispatch()
    {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true);
        var first = await Variant(); var second = await Variant(); var items = new[] { new { variantId = first.ToString(), quantity = 1 }, new { variantId = second.ToString(), quantity = 1 } };
        var payload = await Payload(admin, first, email: actor.Email); payload["paymentMethod"] = "COD"; payload["items"] = items;
        var quote = await Ok(await Send(admin, "checkout/quote", new { items, shippingAddress = new { province = "Thành phố Hồ Chí Minh", countryCode = "VN" } })); payload["quoteHash"] = quote.GetProperty("quoteHash").GetString();
        var placed = await Send(admin, "orders", payload, await Session(admin)); Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        var id = (await placed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        await Advance(admin, id, "Approved"); var version = await OrderVersion(admin, id);
        await using var db = sql.CreateContext(); var reservation = await db.StockReservations.Include(r => r.OrderItem).SingleAsync(r => r.OrderItem.OrderId == long.Parse(id) && r.OrderItem.VariantId == second);
        reservation.ExpiresAt = host.Clock.Current.AddMinutes(-1).UtcDateTime; await db.SaveChangesAsync();
        await Problem(await Transition(admin, id, "Shipping", version), HttpStatusCode.Conflict, "RESERVATION_INVALID");
        Assert.Equal(version, await OrderVersion(admin, id)); Assert.False(await db.InventoryMovements.AnyAsync(m => m.Kind == InventoryMovementKind.Dispatch && m.OrderItem!.OrderId == long.Parse(id)));
        var stocks = await db.Inventory.AsNoTracking().Where(i => i.VariantId == first || i.VariantId == second).ToListAsync(); Assert.All(stocks, i => { Assert.Equal(10, i.OnHand); Assert.Equal(1, i.Reserved); });
        reservation.ExpiresAt = null; var productId = await db.ProductVariants.Where(v => v.Id == second).Select(v => v.ProductId).SingleAsync(); var product = await db.Products.SingleAsync(p => p.Id == productId); product.IsActive = false; await db.SaveChangesAsync();
        await Advance(admin, id, "Shipping");
    }
    [Fact]
    public async Task Outbox_failure_rolls_back_dispatch_history_inventory_then_same_key_can_retry()
    {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var order = await Place(admin, actor.Email, "COD");
        await Advance(admin, order.Id, "Approved"); var version = await OrderVersion(admin, order.Id); var key = Guid.NewGuid(); var trigger = "SyntheticTransition_" + Guid.NewGuid().ToString("N");
        // Only generated GUID identifiers and a typed Guid operation key enter this fixture DDL.
        var createTrigger = $"CREATE TRIGGER [{trigger}] ON [OutboxMessages] AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE [EventKey]='Order.Transition:{key:D}') THROW 51090,'Synthetic outbox failure',1; END";
        var dropTrigger = $"DROP TRIGGER [{trigger}]";
        await using var db = sql.CreateContext(); await db.Database.ExecuteSqlRawAsync(createTrigger);
        try {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Transition(admin, order.Id, "Shipping", version, key)).StatusCode);
            Assert.Equal(version, await OrderVersion(admin, order.Id));
            var reservation = await db.StockReservations.Include(r => r.OrderItem).SingleAsync(r => r.OrderItem.OrderId == long.Parse(order.Id)); Assert.Equal(StockReservationStatus.Active, reservation.Status);
            var stock = await db.Inventory.SingleAsync(i => i.VariantId == reservation.OrderItem.VariantId); Assert.Equal(10, stock.OnHand); Assert.Equal(1, stock.Reserved);
            Assert.False(await db.InventoryMovements.AnyAsync(m => m.OrderItem!.OrderId == long.Parse(order.Id) && m.Kind == InventoryMovementKind.Dispatch));
        } finally { await db.Database.ExecuteSqlRawAsync(dropTrigger); }
        await Ok(await Transition(admin, order.Id, "Shipping", version, key));
    }
}
