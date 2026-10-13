using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.OrderManagement;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Checkout;
using PhoneStore.Api.Services.GuestOrders;
namespace PhoneStore.Api.Services.OrderManagement;
public sealed record AfterSaleAccess(Guid? UserId, bool Admin = false, GuestOrderGrant? Guest = null);
public sealed class AfterSaleService(AppDbContext db, TimeProvider clock, PhoneStore.Api.Services.Auth.AuthBootstrap userLocks, PhoneStore.Api.Services.Membership.MembershipService membership)
{
    private DateTime Now => new(clock.GetUtcNow().UtcTicks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    private static CheckoutException Error(int status, string code) => new(status, code, "Thao tác hủy/hoàn tiền không hợp lệ. Kiểm tra trạng thái và tải lại thông tin.");
    private static bool Early(OrderStatus status) => status is OrderStatus.Placed or OrderStatus.Reviewing or OrderStatus.Approved;
    public async Task<bool> ExpireAsync(long id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleOrDefaultAsync(ct);
        if (order is null || !Early(order.Status) || order.PaymentMethod != PaymentMethod.BankTransfer || order.PaymentDueAt is null || order.PaymentDueAt > Now)
            return false;
        var received = await db.Payments.Where(p => p.OrderId == id && p.Status == PaymentStatus.Confirmed).SumAsync(p => p.Amount, ct);
        if (received >= order.GrandTotal) return false;
        var dueAt = order.PaymentDueAt;
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { domain = "payment-hold-expiry-v1", id, dueAt })));
        const string reason = "Hết thời hạn thanh toán chuyển khoản, chưa nhận đủ tiền";
        var previous = order.Status;
        await CancelAsync(order, new(null), reason, hash, false, ct);
        order.UpdatedAt = Now;
        db.AuditLogs.Add(new() { ActorUserId = null, Action = "OrderPaymentExpired", EntityType = "Order", EntityId = id.ToString(), Summary = "sha256:" + hash, CorrelationId = "OrderPaymentExpired:" + id, CreatedAt = Now });
        db.OutboxMessages.Add(new() { EventKey = "OrderPaymentExpired:" + id, Type = "OrderPaymentExpired", PayloadJson = JsonSerializer.Serialize(new { version = 1, orderId = id.ToString(), fromStatus = previous.ToString(), toStatus = "Cancelled", reasonCode = "PAYMENT_HOLD_EXPIRED", dueAt }), CreatedAt = Now, NextAttemptAt = Now });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return true;
    }
    private async Task AuthorizeAsync(Order order, AfterSaleAccess access, bool mutate, CancellationToken ct)
    {
        if (access.Admin && access.UserId is not null) return;
        if (access.Guest is { } grant)
        {
            if (order.UserId is not null || grant.OrderId != order.Id || grant.ExpiresAt <= Now || mutate && grant.Purpose != OrderAccessTokenPurpose.CancelOrder
                || !await db.OrderAccessTokens.AnyAsync(t => t.Id == grant.TokenId && t.OrderId == order.Id && t.Purpose == grant.Purpose && t.UsedAt != null && t.ExpiresAt > Now, ct))
                throw Error(401, "GUEST_ACCESS_REQUIRED");
            return;
        }
        if (access.UserId is null || order.UserId != access.UserId) throw Error(404, "NOT_FOUND");
    }
    public async Task<AfterSaleDto> ReadAsync(long id, AfterSaleAccess access, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw Error(404, "NOT_FOUND");
        await AuthorizeAsync(order, access, false, ct);
        var requests = await db.OrderCancellationRequests.AsNoTracking().Where(r => r.OrderId == id).OrderByDescending(r => r.Id).ToListAsync(ct);
        var refunds = await db.Refunds.AsNoTracking().Where(r => r.Payment.OrderId == id).OrderByDescending(r => r.Id).ToListAsync(ct);
        var canAct = access.Guest is null || access.Guest.Purpose == OrderAccessTokenPurpose.CancelOrder;
        return new(id.ToString(), order.Status.ToString(), Convert.ToBase64String(order.Version), canAct && Early(order.Status), canAct && order.Status == OrderStatus.Shipping && !requests.Any(r => r.Status == CancellationRequestStatus.Pending),
            requests.Select(r => new CancellationDto(r.Id.ToString(), r.Status.ToString(), access.Admin || r.RequestedByUserId == order.UserId ? r.Reason : "Cửa hàng đang xử lý yêu cầu thu hồi.", Convert.ToBase64String(r.Version), access.Admin ? r.ReviewNote : null, r.CreatedAt)).ToList(),
            refunds.Select(r => new RefundDetailDto(r.Id.ToString(), r.PaymentId.ToString(), r.Status.ToString(), r.MerchandiseAmount, r.ShippingAmount, Convert.ToBase64String(r.Version), access.Admin ? r.Reason : null, access.Admin ? r.Reference : null)).ToList());
    }
    private static void Match(byte[] expected, byte[] actual)
    { if (!CryptographicOperations.FixedTimeEquals(expected, actual)) throw Error(412, "VERSION_CONFLICT"); }
    public async Task<AfterSaleReceipt> MutateAsync(long id, AfterSaleAccess access, string? etag, AfterSaleRequest input, string action, long? resourceId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(etag)) throw Error(428, "PRECONDITION_REQUIRED");
        if (!ApiContract.TryParseETag(etag, out var version)) throw Error(400, "INVALID_ETAG");
        var reason = input.Reason.Trim().Normalize(); var reference = input.Reference?.Trim();
        if (action == "Complete") reference = reference?.ToUpperInvariant();
        if (input.OperationKey == Guid.Empty || reason.Length is < 1 or > 300 || reference?.Length > 150) throw Error(400, "VALIDATION_ERROR");
        var key = "Order.AfterSale:" + input.OperationKey.ToString("D");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { id, actor = access.UserId, access.Admin, guest = access.Guest?.TokenId, action, resourceId, etag, reason, reference, input.Decision, input.GoodsRecovered, input.MerchandiseAmount, input.ShippingAmount })));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await PhoneStore.Api.Services.Membership.MembershipService.LockAsync(db, false, ct);
        var spendOwner = action == "Complete" ? await db.Orders.AsNoTracking().Where(o => o.Id == id).Select(o => o.UserId).SingleOrDefaultAsync(ct) : null;
        if (spendOwner is { } spendUser) await userLocks.LockUserAsync(spendUser, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource={key}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51004,'After sale lock unavailable.',1;
            """, ct);
        var order = await db.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleOrDefaultAsync(ct) ?? throw Error(404, "NOT_FOUND");
        await AuthorizeAsync(order, access, true, ct);
        if (action == "Complete" && order.UserId != spendOwner) throw Error(412, "VERSION_CONFLICT");
        if (action is "Review" or "Refund" or "Complete" or "Fail" && !access.Admin) throw Error(403, "FORBIDDEN");
        var prior = await db.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(m => m.EventKey == key, ct);
        if (prior is not null) {
            using var json = JsonDocument.Parse(prior.PayloadJson);
            if (json.RootElement.GetProperty("requestHash").GetString() != hash) throw Error(409, "OPERATION_KEY_MISMATCH");
            var receipt = json.RootElement.GetProperty("receipt").Deserialize<AfterSaleReceipt>()!; await transaction.CommitAsync(ct); return receipt;
        }
        OrderCancellationRequest? cancellation = null; Refund? refund = null;
        var eventType = "OrderStateChanged";
        if (action == "Cancel") {
            Match(version, order.Version); if (!Early(order.Status)) throw Error(409, "CANCELLATION_NOT_ALLOWED");
            await CancelAsync(order, access, reason, hash, false, ct);
        }
        else if (action == "Request") {
            Match(version, order.Version); if (order.Status != OrderStatus.Shipping) throw Error(409, "CANCELLATION_NOT_ALLOWED");
            if (await db.OrderCancellationRequests.AnyAsync(r => r.OrderId == id && r.Status == CancellationRequestStatus.Pending, ct)) throw Error(409, "CANCELLATION_PENDING");
            cancellation = new() { OrderId = id, RequestedByUserId = access.UserId, Status = CancellationRequestStatus.Pending, Reason = reason, CreatedAt = Now }; db.OrderCancellationRequests.Add(cancellation); eventType = "CancellationRequested";
        }
        else if (action == "Review") {
            cancellation = await db.OrderCancellationRequests.SingleOrDefaultAsync(r => r.Id == resourceId && r.OrderId == id, ct) ?? throw Error(404, "NOT_FOUND");
            Match(version, cancellation.Version);
            if (cancellation.Status != CancellationRequestStatus.Pending) throw Error(409, "CANCELLATION_NOT_ALLOWED");
            if (input.Decision == "Approve") {
                if (order.Status != OrderStatus.Shipping) throw Error(409, "CANCELLATION_NOT_ALLOWED");
                if (!input.GoodsRecovered || string.IsNullOrWhiteSpace(reference)) throw Error(409, "GOODS_RECOVERY_REQUIRED");
                await CancelAsync(order, access, reason, hash, true, ct); cancellation.Status = CancellationRequestStatus.Approved;
            }
            else if (input.Decision == "Reject") cancellation.Status = CancellationRequestStatus.Rejected;
            else throw Error(400, "VALIDATION_ERROR");
            cancellation.ReviewedByUserId = access.UserId; cancellation.ReviewedAt = Now; cancellation.ReviewNote = reason + (reference is null ? "" : " — " + reference); eventType = "CancellationReviewed";
        }
        else if (action == "Refund") {
            Match(version, order.Version);
            if (order.Status is not (OrderStatus.Cancelled or OrderStatus.Completed)) throw Error(409, "REFUND_NOT_ALLOWED");
            var payment = await db.Payments.FromSqlInterpolated($"SELECT * FROM [Payments] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={resourceId} AND [OrderId]={id}").SingleOrDefaultAsync(ct) ?? throw Error(404, "NOT_FOUND");
            if (payment.Status != PaymentStatus.Confirmed) throw Error(409, "REFUND_NOT_ALLOWED");
            await CapacityAsync(order, payment, input.MerchandiseAmount, input.ShippingAmount, ct);
            refund = NewRefund(payment.Id, input.MerchandiseAmount, input.ShippingAmount, reason, key, hash, access.UserId); db.Refunds.Add(refund); eventType = "RefundCreated";
        }
        else if (action is "Complete" or "Fail") {
            refund = await db.Refunds.Include(r => r.Payment).SingleOrDefaultAsync(r => r.Id == resourceId && r.Payment.OrderId == id, ct) ?? throw Error(404, "NOT_FOUND");
            Match(version, refund.Version); if (refund.Status != RefundStatus.Pending) throw Error(409, "REFUND_NOT_ALLOWED");
            if (action == "Complete") {
                if (string.IsNullOrWhiteSpace(reference) || !System.Text.RegularExpressions.Regex.IsMatch(reference, "^[A-Za-z0-9._:/-]{1,150}$")) throw Error(400, "REFUND_REFERENCE_REQUIRED");
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource={"Refund.Reference:" + reference}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
                    IF @result < 0 THROW 51004,'Refund reference busy.',1;
                    """, ct);
                if (await db.Refunds.AnyAsync(r => r.Id != refund.Id && r.Reference == reference && r.Status == RefundStatus.Completed, ct)) throw Error(409, "REFUND_REFERENCE_CONFLICT");
                refund.Status = RefundStatus.Completed; refund.Reference = reference; refund.CompletedByUserId = access.UserId; refund.CompletedAt = Now; eventType = "RefundCompleted";
            } else { refund.Status = RefundStatus.Failed; eventType = "RefundFailed"; }
        } else throw Error(400, "VALIDATION_ERROR");
        order.UpdatedAt = Now; db.Entry(order).Property(o => o.UpdatedAt).IsModified = true;
        if (access.Admin && action is "Complete" or "Fail")
            db.OrderInternalNotes.Add(new() { OrderId = id, ActorUserId = access.UserId!.Value, OperationKey = input.OperationKey, Text = action + ": " + reason + (reference is null ? "" : " — " + reference), CreatedAt = Now });
        db.AuditLogs.Add(new() { ActorUserId = access.UserId, Action = "AfterSale." + action, EntityType = "Order", EntityId = id.ToString(), Summary = "sha256:" + hash, CorrelationId = key, CreatedAt = Now });
        await db.SaveChangesAsync(ct);
        if (action == "Complete" && spendOwner is { } profileUser && order.Status == OrderStatus.Completed) {
            await membership.SyncOrderAsync(order, ct);
            await membership.RecalculateAsync(profileUser, "Hoàn tiền hàng đã hoàn tất", ct);
        }
        var result = new AfterSaleReceipt(id.ToString(), order.Status.ToString(), Convert.ToBase64String(order.Version), cancellation?.Id.ToString() ?? refund?.Id.ToString(), cancellation is not null ? Convert.ToBase64String(cancellation.Version) : refund is not null ? Convert.ToBase64String(refund.Version) : null);
        db.OutboxMessages.Add(new() { EventKey = key, Type = eventType, PayloadJson = JsonSerializer.Serialize(new { version = 1, orderId = id.ToString(), requestHash = hash, receipt = result, refundId = refund?.Id.ToString(), merchandiseAmount = refund?.MerchandiseAmount, shippingAmount = refund?.ShippingAmount, spendEventKey = refund is null ? null : "refund:" + refund.Id }), CreatedAt = Now, NextAttemptAt = Now });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return result;
    }
    private Refund NewRefund(long payment, decimal merchandise, decimal shipping, string reason, string key, string hash, Guid? actor) => new() { PaymentId = payment, Status = RefundStatus.Pending, MerchandiseAmount = merchandise, ShippingAmount = shipping, Reason = reason, EventKey = key, RequestHash = Convert.FromHexString(hash), CreatedByUserId = actor, CreatedAt = Now };
    private async Task CapacityAsync(Order order, Payment payment, decimal merchandise, decimal shipping, CancellationToken ct)
    {
        if (merchandise < 0 || shipping < 0 || decimal.Truncate(merchandise) != merchandise || decimal.Truncate(shipping) != shipping || merchandise + shipping <= 0 || merchandise + shipping > ApiContract.MaxSafeMoney) throw Error(400, "VALIDATION_ERROR");
        var active = db.Refunds.Where(r => r.Payment.OrderId == order.Id && r.Status != RefundStatus.Failed);
        var used = await active.Where(r => r.PaymentId == payment.Id).SumAsync(r => r.Amount, ct);
        if (used + merchandise + shipping > payment.Amount || await active.SumAsync(r => r.MerchandiseAmount, ct) + merchandise > order.Subtotal - order.DiscountTotal || await active.SumAsync(r => r.ShippingAmount, ct) + shipping > order.ShippingFee) throw Error(409, "REFUND_LIMIT");
    }
    private async Task CancelAsync(Order order, AfterSaleAccess access, string reason, string hash, bool returned, CancellationToken ct)
    {
        var items = await db.OrderItems.Where(i => i.OrderId == order.Id).OrderBy(i => i.VariantId).ToListAsync(ct);
        foreach (var item in items) {
            var stock = await db.Inventory.FromSqlInterpolated($"SELECT * FROM [Inventory] WITH (UPDLOCK,HOLDLOCK) WHERE [VariantId]={item.VariantId}").SingleAsync(ct);
            var reservation = await db.StockReservations.FromSqlInterpolated($"SELECT * FROM [StockReservations] WITH (UPDLOCK,HOLDLOCK) WHERE [OrderItemId]={item.Id}").SingleAsync(ct);
            if (reservation.Quantity != item.Quantity || reservation.Status != (returned ? StockReservationStatus.Consumed : StockReservationStatus.Active) || !returned && stock.Reserved < item.Quantity) throw Error(409, "RESERVATION_INVALID");
            if (returned) { if ((long)stock.OnHand + item.Quantity > int.MaxValue) throw Error(409, "INVENTORY_OVERFLOW"); stock.OnHand += item.Quantity; }
            else { stock.Reserved -= item.Quantity; reservation.Status = StockReservationStatus.Released; reservation.ClosedAt = Now; reservation.ExpiresAt = null; }
            db.InventoryMovements.Add(new() { VariantId = item.VariantId, OrderItemId = item.Id, ActorUserId = access.UserId, Kind = returned ? InventoryMovementKind.CancelReturn : InventoryMovementKind.Release, OnHandDelta = returned ? item.Quantity : 0, ReservedDelta = returned ? 0 : -item.Quantity, EventKey = (returned ? "CancelReturn:" : "Release:") + item.Id, Reason = reason, RequestHash = Convert.FromHexString(hash), CreatedAt = Now });
        }
        var previous = order.Status; order.Status = OrderStatus.Cancelled; order.CancelledAt = Now; order.PaymentDueAt = null;
        db.OrderStatusHistories.Add(new() { OrderId = order.Id, FromStatus = previous, ToStatus = OrderStatus.Cancelled, ActorUserId = access.UserId, ActorType = access.Admin ? OrderActorType.Admin : access.UserId is null ? OrderActorType.System : OrderActorType.Customer, Reason = reason, CreatedAt = Now });
        var payments = await db.Payments.Where(p => p.OrderId == order.Id && p.Status == PaymentStatus.Confirmed).OrderBy(p => p.Id).ToListAsync(ct);
        if (!returned && payments.Count == 0) await PhoneStore.Api.Services.Vouchers.VoucherService.Release(db, order.Id, ct);
        var active = await db.Refunds.Where(r => r.Payment.OrderId == order.Id && r.Status != RefundStatus.Failed).ToListAsync(ct);
        var merchandiseCapacity = order.Subtotal - order.DiscountTotal - active.Sum(r => r.MerchandiseAmount); var shippingCapacity = order.ShippingFee - active.Sum(r => r.ShippingAmount);
        foreach (var payment in payments) {
            var amount = payment.Amount - active.Where(r => r.PaymentId == payment.Id).Sum(r => r.Amount); if (amount <= 0) continue;
            var merchandise = Math.Min(amount, merchandiseCapacity); var shipping = amount - merchandise;
            if (merchandise < 0 || shipping > shippingCapacity) throw Error(409, "REFUND_LIMIT");
            db.Refunds.Add(NewRefund(payment.Id, merchandise, shipping, reason, "CancelRefund:" + payment.Id, hash, access.UserId)); merchandiseCapacity -= merchandise; shippingCapacity -= shipping;
        }
    }
}
