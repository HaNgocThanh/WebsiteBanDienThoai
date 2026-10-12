using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.OrderManagement;
using PhoneStore.Api.Services.Notifications;
namespace PhoneStore.IntegrationTests;
public sealed partial class PaymentTests
{
    private static async Task<bool> Expire(Host host, string id) { using var scope = host.Server.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<AfterSaleService>().ExpireAsync(long.Parse(id), default); }
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> ExpiryDiagnostics = new();
    private sealed class ExpiryTestLogger : Microsoft.Extensions.Logging.ILogger<OrderExpiryProcessor> {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => ExpiryDiagnostics.Enqueue(formatter(state, null));
    }
    private static async Task<OrderExpiryBatch> ExpiryBatch(Host host, long afterId) { using var scope = host.Server.Services.CreateScope(); return await new OrderExpiryProcessor(scope.ServiceProvider.GetRequiredService<PhoneStore.Api.Data.AppDbContext>(), scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), host.Clock, scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OrderExpiryOptions>>(), new ExpiryTestLogger()).ProcessBatchAsync(afterId, default); }
    [Fact]
    public async Task Expiry_exact_deadline_system_actor_releases_once_with_durable_event_and_no_money_loss() {
        using var host = new Host(sql.ConnectionString); using var owner = host.Browser(); var actor = await Login(host.Server, owner); var order = await Place(owner, actor.Email);
        await using var db = sql.CreateContext(); var id = long.Parse(order.Id); var row = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == id); Assert.Equal(TimeSpan.FromHours(24), row.PaymentDueAt - row.CreatedAt);
        host.Clock.Current = new DateTimeOffset(row.PaymentDueAt!.Value.AddMilliseconds(-1)); Assert.False(await Expire(host, order.Id)); host.Clock.Current = new DateTimeOffset(row.PaymentDueAt.Value);
        Assert.True(await Expire(host, order.Id)); Assert.False(await Expire(host, order.Id)); var reservation = await db.StockReservations.Include(r => r.OrderItem).SingleAsync(r => r.OrderItem.OrderId == id); var stock = await db.Inventory.SingleAsync(i => i.VariantId == reservation.OrderItem.VariantId);
        Assert.Equal(StockReservationStatus.Released, reservation.Status); Assert.Equal(10, stock.OnHand); Assert.Equal(0, stock.Reserved); Assert.Equal(1, await db.InventoryMovements.CountAsync(m => m.OrderItem!.OrderId == id && m.Kind == InventoryMovementKind.Release));
        var cancelled = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == id); Assert.Equal(OrderStatus.Cancelled, cancelled.Status); Assert.Equal(row.PaymentDueAt.Value, cancelled.CancelledAt); Assert.Null(cancelled.PaymentDueAt);
        var history = await db.OrderStatusHistories.SingleAsync(h => h.OrderId == id && h.ToStatus == OrderStatus.Cancelled); Assert.Equal(OrderActorType.System, history.ActorType); Assert.Null(history.ActorUserId);
        var audit = await db.AuditLogs.SingleAsync(a => a.Action == "OrderPaymentExpired" && a.EntityId == order.Id); Assert.Null(audit.ActorUserId); Assert.DoesNotContain(actor.Email, audit.Summary);
        var message = await db.OutboxMessages.SingleAsync(m => m.EventKey == "OrderPaymentExpired:" + id); Assert.Equal("OrderPaymentExpired", message.Type); Assert.DoesNotContain(actor.Email, message.PayloadJson); Assert.False(await db.Refunds.AnyAsync(r => r.Payment.OrderId == id));
    }
    [Fact]
    public async Task Parallel_expiry_workers_emit_one_release_and_one_cancellation() {
        using var host = new Host(sql.ConnectionString); using var owner = host.Browser(); var user = await Login(host.Server, owner); var order = await Place(owner, user.Email); host.Clock.Current += TimeSpan.FromHours(24);
        var results = await Task.WhenAll(Expire(host, order.Id), Expire(host, order.Id)); Assert.Single(results, r => r); await using var db = sql.CreateContext(); var id = long.Parse(order.Id);
        Assert.Equal(1, await db.InventoryMovements.CountAsync(m => m.OrderItem!.OrderId == id && m.Kind == InventoryMovementKind.Release)); Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.EventKey == "OrderPaymentExpired:" + id));
    }
    [Fact]
    public async Task Expiry_racing_manual_cancellation_releases_and_creates_pending_partial_refund_once() {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var order = await Place(admin, actor.Email); var id = long.Parse(order.Id);
        await using var db = sql.CreateContext(); db.Payments.Add(new() { OrderId = id, Method = PaymentMethod.BankTransfer, Status = PaymentStatus.Confirmed, Amount = 400000, Reference = "SyntheticCancelRace:" + Guid.NewGuid(), CreatedAt = host.Clock.Current.UtcDateTime, ConfirmedAt = host.Clock.Current.UtcDateTime }); await db.SaveChangesAsync();
        host.Clock.Current += TimeSpan.FromHours(24); await Send(admin, "auth/login", new { email = actor.Email, password = "Synthetic!Password123" }); var version = await OrderVersion(admin, order.Id);
        var expiry = Expire(host, order.Id); var cancel = After(admin, "admin/orders/" + order.Id + "/cancel", version, new { reason = "Synthetic manual cancellation at deadline", operationKey = Guid.NewGuid() }); await Task.WhenAll(expiry, cancel);
        Assert.Equal(await expiry ? HttpStatusCode.PreconditionFailed : HttpStatusCode.OK, (await cancel).StatusCode);
        Assert.Equal(OrderStatus.Cancelled, (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == id)).Status); Assert.Equal(1, await db.InventoryMovements.CountAsync(m => m.OrderItem!.OrderId == id && m.Kind == InventoryMovementKind.Release)); Assert.Equal(400000, (await db.Refunds.SingleAsync(r => r.Payment.OrderId == id)).Amount);
    }
    [Fact]
    public async Task Expiry_racing_authenticated_payment_preserves_money_and_exactly_one_inventory_branch() {
        using var host = new Host(sql.ConnectionString); using var owner = host.Browser(); var user = await Login(host.Server, owner); var order = await Place(owner, user.Email); var form = await Ok(await Send(owner, "me/orders/" + order.Id + "/sepay-checkout")); var notification = Notification(form); host.Clock.Current += TimeSpan.FromHours(24);
        var expiry = Expire(host, order.Id); var payment = Ipn(owner, notification); await Task.WhenAll(expiry, payment); await Ok(await payment); await Ok(await Ipn(owner, notification));
        await using var db = sql.CreateContext(); var id = long.Parse(order.Id); var row = await db.Orders.SingleAsync(o => o.Id == id); var reservation = await db.StockReservations.Include(r => r.OrderItem).SingleAsync(r => r.OrderItem.OrderId == id); var stock = await db.Inventory.SingleAsync(i => i.VariantId == reservation.OrderItem.VariantId);
        Assert.Equal(1000000, await db.Payments.Where(p => p.OrderId == id && p.Status == PaymentStatus.Confirmed).SumAsync(p => p.Amount)); Assert.Equal(10, stock.OnHand); Assert.Null(row.PaymentDueAt);
        if (await expiry) { Assert.Equal(OrderStatus.Cancelled, row.Status); Assert.Equal(0, stock.Reserved); Assert.Equal(StockReservationStatus.Released, reservation.Status); Assert.Equal(1000000, (await db.Refunds.SingleAsync(r => r.Payment.OrderId == id)).Amount); }
        else { Assert.Equal(OrderStatus.Approved, row.Status); Assert.Equal(1, stock.Reserved); Assert.Equal(StockReservationStatus.Active, reservation.Status); Assert.Null(reservation.ExpiresAt); Assert.False(await db.Refunds.AnyAsync(r => r.Payment.OrderId == id)); }
    }
    [Fact]
    public async Task Confirmed_bank_order_and_COD_are_not_expired_and_bank_can_complete_after_original_deadline() {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var bank = await Place(admin, actor.Email); var cod = await Place(admin, actor.Email, "COD");
        var form = await Ok(await Send(admin, "me/orders/" + bank.Id + "/sepay-checkout")); await Ok(await Ipn(admin, Notification(form))); host.Clock.Current += TimeSpan.FromHours(25);
        await Send(admin, "auth/login", new { email = actor.Email, password = "Synthetic!Password123" });
        Assert.False(await Expire(host, bank.Id)); Assert.False(await Expire(host, cod.Id)); await Advance(admin, bank.Id, "Shipping"); await Advance(admin, bank.Id, "Delivered"); await Advance(admin, bank.Id, "Completed"); Assert.False(await Expire(host, bank.Id));
        await using var db = sql.CreateContext(); Assert.Equal(OrderStatus.Completed, (await db.Orders.SingleAsync(o => o.Id == long.Parse(bank.Id))).Status); Assert.Equal(OrderStatus.Reviewing, (await db.Orders.SingleAsync(o => o.Id == long.Parse(cod.Id))).Status);
    }
    [Fact]
    public async Task Expired_unpaid_order_with_late_IPN_refunds_once_then_Admin_records_real_refund() {
        using var host = new Host(sql.ConnectionString); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var order = await Place(admin, actor.Email, province: "Hà Nội"); var form = await Ok(await Send(admin, "me/orders/" + order.Id + "/sepay-checkout")); host.Clock.Current += TimeSpan.FromHours(24); Assert.True(await Expire(host, order.Id));
        var notification = Notification(form); await Ok(await Ipn(admin, notification)); await Ok(await Ipn(admin, notification)); await using var db = sql.CreateContext(); var refund = await db.Refunds.AsNoTracking().SingleAsync(r => r.Payment.OrderId == long.Parse(order.Id)); Assert.Equal(1000000, refund.MerchandiseAmount); Assert.Equal(30000, refund.ShippingAmount); Assert.Equal(RefundStatus.Pending, refund.Status);
        var body = new { reason = "Synthetic verified return of late funds", reference = Guid.NewGuid().ToString("N"), operationKey = Guid.NewGuid() }; var path = "admin/refunds/" + refund.Id + "/complete";
        await Send(admin, "auth/login", new { email = actor.Email, password = "Synthetic!Password123" });
        await Ok(await After(admin, path, Convert.ToBase64String(refund.Version), body)); await Ok(await After(admin, path, Convert.ToBase64String(refund.Version), body)); Assert.Equal(OrderStatus.Cancelled, (await db.Orders.SingleAsync(o => o.Id == long.Parse(order.Id))).Status); Assert.Equal(RefundStatus.Completed, (await db.Refunds.AsNoTracking().SingleAsync(r => r.Id == refund.Id)).Status);
    }
    [Fact]
    public async Task Partial_legacy_receipt_is_refunded_on_expiry_and_late_remaining_money_fits_capacities() {
        using var host = new Host(sql.ConnectionString); using var owner = host.Browser(); var actor = await Login(host.Server, owner); var order = await Place(owner, actor.Email); await using var db = sql.CreateContext(); var id = long.Parse(order.Id);
        db.Payments.Add(new() { OrderId = id, Method = PaymentMethod.BankTransfer, Status = PaymentStatus.Confirmed, Amount = 400000, Reference = "SyntheticPartial:" + Guid.NewGuid(), CreatedAt = host.Clock.Current.UtcDateTime, ConfirmedAt = host.Clock.Current.UtcDateTime }); await db.SaveChangesAsync();
        var form = await Ok(await Send(owner, "me/orders/" + order.Id + "/sepay-checkout")); Assert.Equal(600000, form.GetProperty("amount").GetDecimal()); host.Clock.Current += TimeSpan.FromHours(24); Assert.True(await Expire(host, order.Id)); Assert.Equal(400000, await db.Refunds.Where(r => r.Payment.OrderId == id).SumAsync(r => r.Amount));
        await Ok(await Ipn(owner, Notification(form))); Assert.Equal(1000000, await db.Refunds.Where(r => r.Payment.OrderId == id && r.Status == RefundStatus.Pending).SumAsync(r => r.Amount)); Assert.Equal(0, await db.Refunds.Where(r => r.Payment.OrderId == id).SumAsync(r => r.ShippingAmount));
    }
    [Fact]
    public async Task Expiry_outbox_error_rolls_back_all_stock_money_history_and_is_retryable() {
        using var host = new Host(sql.ConnectionString); using var owner = host.Browser(); var user = await Login(host.Server, owner); var order = await Place(owner, user.Email); host.Clock.Current += TimeSpan.FromHours(24); await Send(owner, "auth/login", new { email = user.Email, password = "Synthetic!Password123" }); var version = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/me/orders/" + order.Id)).GetProperty("version").GetString(); var trigger = "SyntheticExpiry_" + Guid.NewGuid().ToString("N"); var id = long.Parse(order.Id);
        var create = $"CREATE TRIGGER [{trigger}] ON [OutboxMessages] AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE [EventKey]='OrderPaymentExpired:{id}') THROW 51090,'Synthetic expiry rollback',1; END"; var drop = $"DROP TRIGGER [{trigger}]"; await using var db = sql.CreateContext(); await db.Database.ExecuteSqlRawAsync(create);
        try { await Assert.ThrowsAsync<DbUpdateException>(() => Expire(host, order.Id)); Assert.Equal(version, (await owner.GetFromJsonAsync<JsonElement>("/api/v1/me/orders/" + order.Id)).GetProperty("version").GetString()); Assert.Equal(StockReservationStatus.Active, (await db.StockReservations.SingleAsync(r => r.OrderItem.OrderId == id)).Status); Assert.False(await db.InventoryMovements.AnyAsync(m => m.OrderItem!.OrderId == id && m.Kind == InventoryMovementKind.Release)); }
        finally { await db.Database.ExecuteSqlRawAsync(drop); }
        Assert.True(await Expire(host, order.Id));
    }
    [Fact]
    public async Task Batch_error_isolated_to_one_order_cursor_advances_and_recovery_retries_it() {
        using var host = new Host(sql.ConnectionString); using var owner = host.Browser(); var user = await Login(host.Server, owner); var first = await Place(owner, user.Email); var second = await Place(owner, user.Email); host.Clock.Current += TimeSpan.FromHours(24); var id = long.Parse(first.Id);
        await using var db = sql.CreateContext(); var invalid = await db.StockReservations.SingleAsync(r => r.OrderItem.OrderId == id); var originalQuantity = invalid.Quantity; invalid.Quantity++; await db.SaveChangesAsync();
        try { var result = await ExpiryBatch(host, id - 1); Assert.Equal(2, result.Scanned); Assert.True(result.Expired == 1, string.Join("; ", ExpiryDiagnostics)); Assert.Equal(long.Parse(second.Id), result.LastId); Assert.Equal(OrderStatus.Reviewing, (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == id)).Status); Assert.Equal(OrderStatus.Cancelled, (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == long.Parse(second.Id))).Status); }
        finally { invalid.Quantity = originalQuantity; await db.SaveChangesAsync(); }
        Assert.Equal(1, (await ExpiryBatch(host, id - 1)).Expired);
    }
    [Fact]
    public async Task Hosted_worker_expires_due_order_and_outbox_delivers_exactly_one_notification() {
        using var host = new Host(sql.ConnectionString, expiry: true); using var admin = host.Browser(); var actor = await Login(host.Server, admin, true); var order = await Place(admin, actor.Email); host.Clock.Current += TimeSpan.FromHours(24); var id = long.Parse(order.Id);
        var deadline = DateTime.UtcNow.AddSeconds(15); bool expired = false;
        while (DateTime.UtcNow < deadline) { await using var check = sql.CreateContext(); expired = await check.Orders.AnyAsync(o => o.Id == id && o.Status == OrderStatus.Cancelled); if (expired) break; await Task.Delay(100); } Assert.True(expired);
        await using var db = sql.CreateContext(); var message = await db.OutboxMessages.SingleAsync(m => m.EventKey == "OrderPaymentExpired:" + id); message.NextAttemptAt = host.Clock.Current.AddYears(-1).UtcDateTime; await db.SaveChangesAsync();
        using (var scope = host.Server.Services.CreateScope()) Assert.True(await scope.ServiceProvider.GetRequiredService<OrderOutboxProcessor>().ProcessOneAsync(default));
        var notices = await db.Notifications.Where(n => n.OrderId == id && n.Type == "OrderPaymentExpired" && n.UserId == actor.Id).ToListAsync(); Assert.Single(notices); Assert.Equal(actor.Id, notices[0].UserId); Assert.NotNull((await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.EventKey == message.EventKey)).ProcessedAt); Assert.False(await Expire(host, order.Id));
    }
}
