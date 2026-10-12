using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.OrderManagement;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Checkout;
namespace PhoneStore.Api.Services.OrderManagement;

public sealed class OrderTransitionService(AppDbContext db, TimeProvider clock)
{
    public static OrderStatus? Next(OrderStatus status) => status switch {
        OrderStatus.Placed => OrderStatus.Reviewing, OrderStatus.Reviewing => OrderStatus.Approved,
        OrderStatus.Approved => OrderStatus.Shipping, OrderStatus.Shipping => OrderStatus.Delivered,
        OrderStatus.Delivered => OrderStatus.Completed, _ => null };
    public static string[] Allowed(OrderStatus status, PaymentMethod method, decimal netReceived, decimal pendingRefund, decimal total)
    {
        var next = Next(status);
        if (next is null || next == OrderStatus.Completed && (netReceived < total || pendingRefund > 0)
            || next == OrderStatus.Approved && method == PaymentMethod.BankTransfer && (netReceived < total || pendingRefund > 0)
            || next == OrderStatus.Shipping && (pendingRefund > 0 || method == PaymentMethod.BankTransfer && netReceived < total)) return [];
        return [next.Value.ToString()];
    }
    private static CheckoutException Fail(int status, string code, string message) => new(status, code, message);
    public async Task<OrderTransitionDto> TransitionAsync(long id, Guid actor, string? match, OrderTransitionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(match)) throw Fail(428, "PRECONDITION_REQUIRED", "Cần phiên bản đơn hiện tại.");
        if (!ApiContract.TryParseETag(match, out var version)) throw Fail(400, "INVALID_ETAG", "Phiên bản đơn không hợp lệ.");
        var reason = request.Reason.Trim().Normalize();
        if (request.OperationKey == Guid.Empty || reason.Length is < 1 or > 300 || !Enum.TryParse<OrderStatus>(request.TargetStatus, out var target)
            || target.ToString() != request.TargetStatus || target is OrderStatus.Placed or OrderStatus.Cancelled)
            throw Fail(400, "VALIDATION_ERROR", "Cần bước chuyển hợp lệ, lý do và khóa thao tác.");
        var key = "Order.Transition:" + request.OperationKey.ToString("D");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { domain = "order-transition-v1", id, actor, target = target.ToString(), reason, match })));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource={key}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51004, 'Order operation lock unavailable.', 1;
            """, ct);
        var prior = await db.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(m => m.EventKey == key, ct);
        if (prior is not null) {
            using var payload = JsonDocument.Parse(prior.PayloadJson);
            if (payload.RootElement.GetProperty("requestHash").GetString() != hash) throw Fail(409, "OPERATION_KEY_MISMATCH", "Khóa thao tác đã dùng với nội dung khác.");
            var replay = payload.RootElement.GetProperty("receipt").Deserialize<OrderTransitionDto>()!;
            await transaction.CommitAsync(ct); return replay;
        }
        var order = await db.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleOrDefaultAsync(ct)
            ?? throw Fail(404, "NOT_FOUND", "Không tìm thấy đơn hàng.");
        if (!CryptographicOperations.FixedTimeEquals(version, order.Version)) throw Fail(412, "VERSION_CONFLICT", "Đơn đã thay đổi. Hãy tải lại trước khi thao tác.");
        if (Next(order.Status) != target) throw Fail(409, "INVALID_TRANSITION", "Không thể bỏ qua bước hoặc quay lại trạng thái trước.");
        var received = await db.Payments.Where(p => p.OrderId == id && p.Status == PaymentStatus.Confirmed).SumAsync(p => p.Amount, ct);
        var refunds = db.Refunds.Where(r => r.Payment.OrderId == id);
        var net = received - await refunds.Where(r => r.Status == RefundStatus.Completed).SumAsync(r => r.Amount, ct);
        var pending = await refunds.Where(r => r.Status == RefundStatus.Pending).SumAsync(r => r.Amount, ct);
        if (Allowed(order.Status, order.PaymentMethod, net, pending, order.GrandTotal).Length == 0)
            throw Fail(409, "PAYMENT_REQUIRED", "Cần nhận đủ tiền và xử lý khoản hoàn đang chờ trước bước này.");
        var now = new DateTime(clock.GetUtcNow().UtcTicks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        if (target == OrderStatus.Shipping)
        {
            var items = await db.OrderItems.Where(i => i.OrderId == id).OrderBy(i => i.VariantId).ToListAsync(ct);
            if (items.Count == 0) throw Fail(409, "RESERVATION_INVALID", "Đơn không có hàng để xuất.");
            foreach (var item in items)
            {
                var stock = await db.Inventory.FromSqlInterpolated($"SELECT * FROM [Inventory] WITH (UPDLOCK,HOLDLOCK) WHERE [VariantId]={item.VariantId}").SingleOrDefaultAsync(ct);
                var reservation = await db.StockReservations.FromSqlInterpolated($"SELECT * FROM [StockReservations] WITH (UPDLOCK,HOLDLOCK) WHERE [OrderItemId]={item.Id}").SingleOrDefaultAsync(ct);
                if (stock is null || reservation is null || reservation.Status != StockReservationStatus.Active || reservation.Quantity != item.Quantity
                    || reservation.ExpiresAt <= now || stock.Reserved < item.Quantity || stock.OnHand < item.Quantity)
                    throw Fail(409, "RESERVATION_INVALID", "Hàng giữ cho đơn đã thay đổi hoặc hết hạn.");
                stock.OnHand -= item.Quantity; stock.Reserved -= item.Quantity;
                reservation.Status = StockReservationStatus.Consumed; reservation.ClosedAt = now; reservation.ExpiresAt = null;
                db.InventoryMovements.Add(new InventoryMovement { VariantId = item.VariantId, OrderItemId = item.Id, ActorUserId = actor,
                    Kind = InventoryMovementKind.Dispatch, OnHandDelta = -item.Quantity, ReservedDelta = -item.Quantity,
                    Reason = reason, EventKey = "Dispatch:" + item.Id, RequestHash = Convert.FromHexString(hash), CreatedAt = now });
            }
            order.ShippedAt = now;
        }
        if (target == OrderStatus.Delivered) order.DeliveredAt = now;
        if (target == OrderStatus.Completed) order.CompletedAt = now;
        var from = order.Status; order.Status = target; order.UpdatedAt = now;
        var history = new OrderStatusHistory { OrderId = id, FromStatus = from, ToStatus = target, ActorType = OrderActorType.Admin, ActorUserId = actor, Reason = reason, CreatedAt = now };
        db.OrderStatusHistories.Add(history);
        db.AuditLogs.Add(new AuditLog { ActorUserId = actor, Action = "OrderTransition", EntityType = "Order", EntityId = id.ToString(), Summary = "sha256:" + hash, CorrelationId = key, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        var result = new OrderTransitionDto(id.ToString(), target.ToString(), Convert.ToBase64String(order.Version), history.Id.ToString(), now);
        db.OutboxMessages.Add(new OutboxMessage { EventKey = key, Type = target == OrderStatus.Completed ? "OrderCompleted" : "OrderStateChanged",
            PayloadJson = JsonSerializer.Serialize(new { version = 1, orderId = id.ToString(), fromStatus = from.ToString(), toStatus = target.ToString(),
                requestHash = hash, receipt = result, userAtCompletion = target == OrderStatus.Completed ? order.UserId : null,
                eligibleMerchandise = order.Subtotal - order.DiscountTotal, spendEventKey = "OrderCompleted:" + id }), CreatedAt = now, NextAttemptAt = now });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return result;
    }
}
