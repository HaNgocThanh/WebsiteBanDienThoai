using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.GuestOrders;
using PhoneStore.Api.DTOs.OrderManagement;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Checkout;
namespace PhoneStore.Api.Services.OrderManagement;
public sealed class OrderReadService(AppDbContext db, TimeProvider clock)
{
    private static CheckoutException Bad(string code = "INVALID_FILTER") => new(400, code, "Bộ lọc hoặc ID không hợp lệ.");
    private static CheckoutException Missing() => new(404, "NOT_FOUND", "Không tìm thấy đơn hàng.");
    public static long Id(string value) => ApiContract.TryParseId(value, out var id) ? id : throw Bad("INVALID_ID");
    private static DateTime? Boundary(string? value)
    {
        if (value is null) return null;
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(\\.\\d{1,7})?Z$") || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw Bad();
        return date.UtcDateTime;
    }
    private IQueryable<Order> Filter(OrderListQuery q, Guid? owner, bool admin)
    {
        var from = Boundary(q.From); var to = Boundary(q.To);
        if (from >= to || (long)(q.Page - 1) * q.PageSize > int.MaxValue) throw Bad();
        var rows = db.Orders.AsNoTracking();
        if (!admin) { if (q.CustomerId is not null || q.Email is not null) throw Bad(); rows = rows.Where(o => o.UserId == owner); }
        if (q.Status is not null) { if (!Enum.TryParse<OrderStatus>(q.Status, out var status) || status.ToString() != q.Status) throw Bad(); rows = rows.Where(o => o.Status == status); }
        if (q.CustomerId is not null) { if (!Guid.TryParseExact(q.CustomerId, "D", out var customer) || customer == Guid.Empty) throw Bad(); rows = rows.Where(o => o.UserId == customer); }
        if (q.Email is not null) { var email = q.Email.Trim().ToUpperInvariant(); if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email)) throw Bad(); rows = rows.Where(o => o.NormalizedCustomerEmail == email); }
        if (from is not null) rows = rows.Where(o => o.CreatedAt >= from);
        if (to is not null) rows = rows.Where(o => o.CreatedAt < to);
        return rows;
    }
    public async Task<object> ListAsync(OrderListQuery q, Guid? owner, bool admin, CancellationToken ct)
    {
        var rows = Filter(q, owner, admin); var total = await rows.LongCountAsync(ct);
        var page = rows.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize);
        if (admin) return new PagedResponse<AdminOrderListDto>(await page.Select(o => new AdminOrderListDto(o.Id.ToString(), o.OrderNumber, o.Status.ToString(), o.PaymentMethod.ToString(), o.GrandTotal, o.Currency, o.CreatedAt, o.RecipientName, o.UserId, o.CustomerEmail)).ToListAsync(ct), q.Page, q.PageSize, total);
        return new PagedResponse<OrderListDto>(await page.Select(o => new OrderListDto(o.Id.ToString(), o.OrderNumber, o.Status.ToString(), o.PaymentMethod.ToString(), o.GrandTotal, o.Currency, o.CreatedAt, o.RecipientName)).ToListAsync(ct), q.Page, q.PageSize, total);
    }
    public async Task<object> DetailAsync(long id, Guid? owner, bool admin, CancellationToken ct)
    {
        var o = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id && (admin || o.UserId == owner), ct) ?? throw Missing();
        var items = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == id).OrderBy(i => i.Id).Select(i => new GuestOrderItemDto(i.Id.ToString(), i.VariantId.ToString(), i.ProductNameSnapshot, i.SkuSnapshot, i.VariantSnapshot, i.Quantity, i.UnitPrice, i.UnitDiscount, i.LineTotal)).ToListAsync(ct);
        var history = await db.OrderStatusHistories.AsNoTracking().Where(h => h.OrderId == id).OrderBy(h => h.CreatedAt).ThenBy(h => h.Id).Select(h => new GuestOrderHistoryDto(h.FromStatus == null ? null : h.FromStatus.ToString(), h.ToStatus.ToString(), h.CreatedAt)).ToListAsync(ct);
        var snapshot = new GuestOrderDto(ApiContract.Id(o.Id), o.OrderNumber, o.Status.ToString(), o.PaymentMethod.ToString(), o.Currency, ApiContract.Money(o.Subtotal), ApiContract.Money(o.DiscountTotal), ApiContract.Money(o.ShippingFee), ApiContract.Money(o.GrandTotal), o.RecipientName, o.Phone, o.AddressLine, o.Locality, o.Province, o.CountryCode, o.CustomerNote, o.CreatedAt, o.PaymentDueAt, false, items, history);
        var confirmed = await db.Payments.Where(p => p.OrderId == id && p.Status == PaymentStatus.Confirmed).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var refunds = db.Refunds.Where(r => r.Payment.OrderId == id);
        var summary = new PaymentSummaryDto(ApiContract.Money(confirmed), ApiContract.Money(await refunds.Where(r => r.Status == RefundStatus.Pending).SumAsync(r => (decimal?)r.Amount, ct) ?? 0), ApiContract.Money(await refunds.Where(r => r.Status == RefundStatus.Completed).SumAsync(r => (decimal?)r.Amount, ct) ?? 0), ApiContract.Money(Math.Max(0, o.GrandTotal - confirmed)));
        var version = Convert.ToBase64String(o.Version);
        // No payment/state command exists yet (P4-02/03); never advertise an unavailable action.
        if (!admin) return new OrderReadDto(snapshot, version, summary, []);
        var notes = await db.OrderInternalNotes.AsNoTracking().Where(n => n.OrderId == id).OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(50).Select(n => new InternalNoteDto(n.Id.ToString(), n.ActorUserId, n.Text, n.CreatedAt)).ToListAsync(ct);
        var internalHistory = await db.OrderStatusHistories.AsNoTracking().Where(h => h.OrderId == id).OrderBy(h => h.CreatedAt).ThenBy(h => h.Id).Select(h => new AdminHistoryDto(h.Id.ToString(), h.FromStatus == null ? null : h.FromStatus.ToString(), h.ToStatus.ToString(), h.ActorUserId, h.ActorType.ToString(), h.Reason, h.CreatedAt)).ToListAsync(ct);
        var payments = await db.Payments.AsNoTracking().Where(p => p.OrderId == id).OrderBy(p => p.Id).Select(p => new OrderPaymentDto(p.Id.ToString(), p.Method.ToString(), p.Status.ToString(), p.Amount, p.CreatedAt)).ToListAsync(ct);
        var refundRows = await refunds.AsNoTracking().OrderBy(r => r.Id).Select(r => new OrderRefundDto(r.Id.ToString(), r.PaymentId.ToString(), r.Status.ToString(), r.MerchandiseAmount, r.ShippingAmount, r.CreatedAt)).ToListAsync(ct);
        var stock = await db.StockReservations.AsNoTracking().Where(r => r.OrderItem.OrderId == id).OrderBy(r => r.OrderItemId).Select(r => new OrderStockDto(r.OrderItemId.ToString(), r.OrderItem.VariantId.ToString(), r.Quantity, r.Status.ToString(), r.ExpiresAt)).ToListAsync(ct);
        return new AdminOrderReadDto(snapshot, version, summary, [], o.UserId, o.CustomerEmail, notes, internalHistory, payments, refundRows, stock);
    }
    private static InternalNoteDto NoteDto(OrderInternalNote n) => new(ApiContract.Id(n.Id), n.ActorUserId, n.Text, n.CreatedAt);
    public async Task<InternalNoteDto> NoteAsync(long orderId, Guid actor, InternalNoteRequest request, CancellationToken ct)
    {
        var text = request.Text.Trim().Normalize(); if (text.Length is < 1 or > 500 || request.OperationKey == Guid.Empty) throw Bad("VALIDATION_ERROR");
        if (!await db.Orders.AnyAsync(o => o.Id == orderId, ct)) throw Missing();
        var existing = await db.OrderInternalNotes.AsNoTracking().SingleOrDefaultAsync(n => n.OrderId == orderId && n.ActorUserId == actor && n.OperationKey == request.OperationKey, ct);
        if (existing is null)
        {
            var n = new OrderInternalNote { OrderId = orderId, ActorUserId = actor, OperationKey = request.OperationKey, Text = text, CreatedAt = clock.GetUtcNow().UtcDateTime }; db.OrderInternalNotes.Add(n);
            try { await db.SaveChangesAsync(ct); return NoteDto(n); }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { db.Entry(n).State = EntityState.Detached; existing = await db.OrderInternalNotes.AsNoTracking().SingleAsync(n => n.OrderId == orderId && n.ActorUserId == actor && n.OperationKey == request.OperationKey, ct); }
        }
        if (existing.Text != text) throw new CheckoutException(409, "OPERATION_KEY_MISMATCH", "Yêu cầu này đã được dùng cho ghi chú khác.");
        return NoteDto(existing);
    }
    public async Task<object> NotificationsAsync(Guid owner, NotificationQuery q, bool admin, CancellationToken ct)
    {
        if ((long)(q.Page - 1) * q.PageSize > int.MaxValue) throw Bad();
        var all = db.Notifications.AsNoTracking().Where(n => n.UserId == owner && (admin || n.OrderId == null || n.Order!.UserId == owner));
        var unreadCount = await all.LongCountAsync(n => n.ReadAt == null, ct); var rows = q.UnreadOnly ? all.Where(n => n.ReadAt == null) : all;
        var total = await rows.LongCountAsync(ct);
        var items = await rows.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(n => new NotificationDto(n.Id.ToString(), n.OrderId == null ? null : n.OrderId.ToString(), n.Type, n.Title, n.Body, n.CreatedAt, n.ReadAt)).ToListAsync(ct);
        return new { items, q.Page, q.PageSize, totalCount = total, unreadCount };
    }
    public async Task ReadNotificationAsync(Guid owner, long id, bool admin, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Notifications.Where(n => n.Id == id && n.UserId == owner && (admin || n.OrderId == null || n.Order!.UserId == owner) && n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        if (!await db.Notifications.AnyAsync(n => n.Id == id && n.UserId == owner && (admin || n.OrderId == null || n.Order!.UserId == owner), ct)) throw Missing();
    }
}
