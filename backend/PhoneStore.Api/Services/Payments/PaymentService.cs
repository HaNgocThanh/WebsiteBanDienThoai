using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.Payments;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Checkout;
using PhoneStore.Api.Services.GuestOrders;
namespace PhoneStore.Api.Services.Payments;
public sealed record PaymentAccess(Guid? UserId, bool Admin = false, GuestOrderGrant? Guest = null, CheckoutOwner? Checkout = null, Guid? CheckoutKey = null);
public sealed class PaymentService(AppDbContext db, TimeProvider clock, SePaySandbox gateway)
{
    private DateTime Now => new(clock.GetUtcNow().UtcTicks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    private static CheckoutException Error(int status, string code, string message) => new(status, code, message);
    private static CheckoutException Missing() => Error(404, "NOT_FOUND", "Không tìm thấy đơn hoặc khoản thanh toán.");
    private static CheckoutException Conflict(string code) => Error(409, code, "Yêu cầu thanh toán không khớp hoặc đã được xử lý.");
    private static byte[] Hash(object value) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value));
    private static PaymentDto Dto(Payment p, bool admin) => new(p.Id.ToString(), p.Method.ToString(), p.Status.ToString(), p.Amount, Convert.ToBase64String(p.Version), p.CreatedAt, p.ConfirmedAt, admin ? p.Reference : null, admin ? p.Note : null);
    private async Task LockAsync(string key, CancellationToken ct) => await db.Database.ExecuteSqlInterpolatedAsync($"""
        DECLARE @result int;
        EXEC @result=sys.sp_getapplock @Resource={key}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
        IF @result < 0 THROW 51004, 'Payment lock unavailable.', 1;
        """, ct);
    private async Task<Order> OrderAsync(long id, PaymentAccess access, bool locked, CancellationToken ct)
    {
        var rows = locked ? db.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}") : db.Orders.AsNoTracking().Where(o => o.Id == id);
        var order = await rows.SingleOrDefaultAsync(ct) ?? throw Missing();
        if (access.Admin && access.UserId is not null) return order;
        if (access.Checkout is { } owner && access.CheckoutKey is { } checkoutKey)
        {
            var key = await db.IdempotencyRequests.AsNoTracking().SingleOrDefaultAsync(k => k.Id == checkoutKey && k.OrderId == id && k.Status == IdempotencyRequestStatus.Completed, ct);
            var owned = key is not null && (owner.UserId is not null ? key.OwnerUserId == owner.UserId && order.UserId == owner.UserId
                : order.UserId == null && key.OwnerUserId == null && key.GuestSessionHash is not null && owner.GuestHash is not null && CryptographicOperations.FixedTimeEquals(key.GuestSessionHash, owner.GuestHash));
            if (!owned) throw Error(404, "CHECKOUT_NOT_FOUND", "Không tìm thấy phiên checkout của bạn.");
            return order;
        }
        if (access.Guest is { } grant)
        {
            if (order.Id != grant.OrderId || order.UserId is not null || grant.ExpiresAt <= Now || grant.Purpose is not (OrderAccessTokenPurpose.ViewOrder or OrderAccessTokenPurpose.CancelOrder)
                || !await db.OrderAccessTokens.AnyAsync(t => t.Id == grant.TokenId && t.OrderId == id && t.Purpose == grant.Purpose && t.UsedAt != null, ct))
                throw Error(401, "GUEST_ACCESS_REQUIRED", "Cần mở lại liên kết truy cập đơn từ email.");
            return order;
        }
        if (access.UserId is null || order.UserId != access.UserId) throw Missing();
        return order;
    }
    private async Task<PaymentBalance> BalanceAsync(Order order, CancellationToken ct)
    {
        var received = await db.Payments.Where(p => p.OrderId == order.Id && p.Status == PaymentStatus.Confirmed).SumAsync(p => p.Amount, ct);
        var refunds = db.Refunds.Where(r => r.Payment.OrderId == order.Id);
        var pending = await refunds.Where(r => r.Status == RefundStatus.Pending).SumAsync(r => r.Amount, ct);
        var refunded = await refunds.Where(r => r.Status == RefundStatus.Completed).SumAsync(r => r.Amount, ct);
        var status = pending > 0 ? "RefundPending" : received > 0 && refunded == received ? "Refunded" : refunded > 0 ? "PartiallyRefunded" : received >= order.GrandTotal ? "Paid" : received > 0 ? "PartiallyPaid" : await db.Payments.AnyAsync(p => p.OrderId == order.Id && p.Status == PaymentStatus.Pending, ct) ? "PendingVerification" : "Unpaid";
        return new(received, pending, refunded, Math.Max(0, order.GrandTotal - received), status);
    }
    public async Task<PaymentPage> ListAsync(long id, PaymentAccess access, PageQuery query, CancellationToken ct)
    {
        if (query.Offset > int.MaxValue) throw Error(400, "INVALID_FILTER", "Phân trang vượt giới hạn.");
        var order = await OrderAsync(id, access, false, ct); var balance = await BalanceAsync(order, ct);
        var rows = db.Payments.AsNoTracking().Where(p => p.OrderId == id); var count = await rows.LongCountAsync(ct);
        var items = await rows.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).Skip((int)query.Offset).Take(query.PageSize).ToListAsync(ct);
        return new(items.Select(p => Dto(p, access.Admin)).ToList(), query.Page, query.PageSize, count, balance,
            !access.Admin && order.PaymentMethod == PaymentMethod.BankTransfer && order.Status != OrderStatus.Cancelled && balance.Remaining > 0 && (order.PaymentDueAt == null || order.PaymentDueAt > Now),
            access.Admin && order.PaymentMethod == PaymentMethod.COD && balance.Remaining > 0, order.PaymentMethod.ToString(), gateway.Configured);
    }
    private void Audit(Payment payment, Guid? actor, string action, byte[] hash) => db.AuditLogs.Add(new AuditLog { ActorUserId = actor, Action = action, EntityType = "Payment", EntityId = payment.Id.ToString(), Summary = "sha256:" + Convert.ToHexString(hash), CorrelationId = action + ":" + payment.Id, CreatedAt = Now });
    private void Event(Payment payment, string type) => db.OutboxMessages.Add(new OutboxMessage { Type = type, EventKey = type + ":" + payment.Id, PayloadJson = JsonSerializer.Serialize(new { version = 1, orderId = payment.OrderId.ToString(), paymentId = payment.Id.ToString() }), CreatedAt = Now, NextAttemptAt = Now });
    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 }) { throw Conflict("PAYMENT_REFERENCE_CONFLICT"); }
    }
    public async Task<SePayCheckoutDto> CheckoutAsync(long id, PaymentAccess access, CancellationToken ct)
    {
        // Configuration check never substitutes for owner authorization.
        await using var transaction = await db.Database.BeginTransactionAsync(ct); var order = await OrderAsync(id, access, true, ct);
        if (!gateway.Configured) throw Error(503, "SEPAY_UNAVAILABLE", "SePay Sandbox chưa được cấu hình.");
        var remaining = (await BalanceAsync(order, ct)).Remaining;
        if (access.Admin || order.PaymentMethod != PaymentMethod.BankTransfer || order.Status == OrderStatus.Cancelled || remaining <= 0 || order.PaymentDueAt <= Now) throw Conflict("PAYMENT_NOT_ALLOWED");
        var key = "SePay.Sandbox.Order:" + id;
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.OperationKey == key, ct);
        if (payment is null)
        {
            payment = new Payment { OrderId = id, Method = PaymentMethod.BankTransfer, Status = PaymentStatus.Pending, Amount = remaining, OperationKey = key, RequestHash = Hash(new { domain = "sepay-sandbox-checkout-v1", id, remaining }), CreatedAt = Now };
            db.Payments.Add(payment); await SaveAsync(ct); Audit(payment, access.UserId, "SePayInitiated", payment.RequestHash); Event(payment, "PaymentInitiated"); await SaveAsync(ct);
        }
        if (payment.Status != PaymentStatus.Pending || payment.Amount != remaining) throw Conflict("PAYMENT_NOT_ALLOWED");
        var form = gateway.Form(payment.Id, order.Id, payment.Amount); await transaction.CommitAsync(ct); return form;
    }
    private async Task ReceivedAsync(Order order, Payment payment, Guid? actor, CancellationToken ct)
    {
        if (order.Status == OrderStatus.Cancelled)
        {
            var refunds = db.Refunds.Where(r => r.Payment.OrderId == order.Id && (r.Status == RefundStatus.Pending || r.Status == RefundStatus.Completed));
            var merchCapacity = order.Subtotal - order.DiscountTotal - await refunds.SumAsync(r => r.MerchandiseAmount, ct);
            var shippingCapacity = order.ShippingFee - await refunds.SumAsync(r => r.ShippingAmount, ct);
            var merchandise = Math.Min(payment.Amount, Math.Max(0, merchCapacity)); var shipping = payment.Amount - merchandise;
            if (shipping > shippingCapacity) throw Conflict("REFUND_LIMIT");
            db.Refunds.Add(new Refund { PaymentId = payment.Id, Status = RefundStatus.Pending, MerchandiseAmount = merchandise, ShippingAmount = shipping, Reason = "Tiền nhận sau khi đơn đã hủy", EventKey = "PaymentLateRefund:" + payment.Id, RequestHash = Hash(new { payment.Id, merchandise, shipping }), CreatedByUserId = actor, CreatedAt = Now });
        }
        else if (order.PaymentMethod == PaymentMethod.BankTransfer && (await BalanceAsync(order, ct)).Remaining == 0)
        {
            order.PaymentDueAt = null;
            foreach (var reservation in await db.StockReservations.Where(r => r.OrderItem.OrderId == order.Id && r.Status == StockReservationStatus.Active).ToListAsync(ct)) reservation.ExpiresAt = null;
        }
        db.Entry(order).Property(o => o.UpdatedAt).IsModified = true;
    }
    public async Task<PaymentDto> CodAsync(long id, Guid actor, CodReceiptRequest request, CancellationToken ct)
    {
        var note = request.Note.Trim().Normalize();
        if (request.Amount <= 0 || request.Amount > ApiContract.MaxSafeMoney || decimal.Truncate(request.Amount) != request.Amount || request.OperationKey == Guid.Empty || note.Length is 0 or > 500) throw Error(400, "VALIDATION_ERROR", "Cần số tiền VND nguyên dương, ghi chú và khóa thao tác.");
        var key = "Payment.COD:" + request.OperationKey.ToString("D"); var hash = Hash(new { domain = "cod-receipt-v1", id, actor, request.Amount, note });
        await using var transaction = await db.Database.BeginTransactionAsync(ct); await LockAsync(key, ct);
        var prior = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.OperationKey == key, ct);
        if (prior is not null) { if (prior.RequestHash is null || !CryptographicOperations.FixedTimeEquals(prior.RequestHash, hash)) throw Conflict("OPERATION_KEY_MISMATCH"); await transaction.CommitAsync(ct); return Dto(prior, true); }
        var order = await OrderAsync(id, new(actor, true), true, ct);
        if (order.PaymentMethod != PaymentMethod.COD || request.Amount > (await BalanceAsync(order, ct)).Remaining) throw Conflict("PAYMENT_AMOUNT_EXCEEDED");
        var payment = new Payment { OrderId = id, Method = PaymentMethod.COD, Status = PaymentStatus.Confirmed, Amount = request.Amount, Note = note, ConfirmedByUserId = actor, ConfirmedAt = Now, CreatedAt = Now, OperationKey = key, RequestHash = hash };
        db.Payments.Add(payment); await SaveAsync(ct); await ReceivedAsync(order, payment, actor, ct); Audit(payment, actor, "CodReceived", hash); Event(payment, "PaymentConfirmed"); await SaveAsync(ct); await transaction.CommitAsync(ct); return Dto(payment, true);
    }
    private static decimal ProviderAmount(string value)
    {
        if (!Regex.IsMatch(value, "^[0-9]{1,16}(\\.[0-9]{1,2})?$") || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount <= 0 || amount > ApiContract.MaxSafeMoney || decimal.Truncate(amount) != amount) throw Error(400, "INVALID_SEPAY_IPN", "Số tiền thông báo thanh toán không hợp lệ.");
        return amount;
    }
    public async Task IpnAsync(SePayIpnRequest request, CancellationToken ct)
    {
        // Header authentication is enforced by the narrow endpoint middleware before body/model binding.
        if (request.NotificationType == "TRANSACTION_VOID") return; // Never silently subtract received money or cancel an order; refunds have their own workflow.
        var o = request.Order; var t = request.Transaction;
        if (request.NotificationType != "ORDER_PAID" || o is null || t is null || o.Status != "CAPTURED" || t.Status != "APPROVED" || t.Type != "PAYMENT" || t.Method != "BANK_TRANSFER" || o.Currency != "VND" || t.Currency != "VND" || t.Id == Guid.Empty || !Regex.IsMatch(t.TransactionId, "^[A-Za-z0-9_-]{1,100}$") || !o.Invoice.StartsWith("PSB-", StringComparison.Ordinal) || !ApiContract.TryParseId(o.Invoice[4..], out var id)) throw Error(400, "INVALID_SEPAY_IPN", "Thông báo SePay không khớp giao dịch chuyển khoản hợp lệ.");
        var amount = ProviderAmount(o.Amount); if (amount != ProviderAmount(t.Amount)) throw Conflict("PAYMENT_AMOUNT_MISMATCH");
        var reference = "SePaySandbox:" + t.TransactionId; var hash = Hash(new { domain = "sepay-sandbox-paid-v1", id, amount, t.Id, reference });
        var found = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.OperationKey == "SePay.Sandbox.Order:" + p.OrderId, ct) ?? throw Missing();
        await using var transaction = await db.Database.BeginTransactionAsync(ct); await LockAsync("Payment.Reference:" + reference, ct);
        var order = await db.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={found.OrderId}").SingleAsync(ct);
        var payment = await db.Payments.FromSqlInterpolated($"SELECT * FROM [Payments] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleAsync(ct);
        var prior = await db.AuditLogs.AsNoTracking().SingleOrDefaultAsync(a => a.EntityType == "Payment" && a.EntityId == id.ToString() && a.Action == "SePayReceived", ct);
        if (prior is not null) { if (prior.Summary != "sha256:" + Convert.ToHexString(hash)) throw Conflict("OPERATION_KEY_MISMATCH"); await transaction.CommitAsync(ct); return; }
        if (payment.Status != PaymentStatus.Pending || payment.Method != PaymentMethod.BankTransfer || order.PaymentMethod != PaymentMethod.BankTransfer || payment.Amount != amount || amount > (await BalanceAsync(order, ct)).Remaining) throw Conflict("PAYMENT_AMOUNT_MISMATCH");
        if (await db.Payments.AnyAsync(p => p.Id != id && p.Method == PaymentMethod.BankTransfer && p.Reference == reference, ct)) throw Conflict("PAYMENT_REFERENCE_CONFLICT");
        payment.Status = PaymentStatus.Confirmed; payment.Reference = reference; payment.ConfirmedAt = Now; payment.ConfirmedByUserId = null;
        await SaveAsync(ct); await ReceivedAsync(order, payment, null, ct); Audit(payment, null, "SePayReceived", hash); Event(payment, "PaymentConfirmed"); await SaveAsync(ct); await transaction.CommitAsync(ct);
    }
}
