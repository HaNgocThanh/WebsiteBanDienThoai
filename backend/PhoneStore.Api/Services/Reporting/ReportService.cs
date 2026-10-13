using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.Reporting;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Checkout;

namespace PhoneStore.Api.Services.Reporting;

public sealed class ReportService(AppDbContext db, TimeProvider clock)
{
    private static CheckoutException Invalid() => new(400, "INVALID_REPORT_RANGE", "Chọn khoảng ngày hợp lệ, tối đa 366 ngày.");
    private static DateOnly Parse(string value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw Invalid();
    private static decimal Money(decimal value)
    {
        if (decimal.Truncate(value) != value || Math.Abs(value) > ApiContract.MaxSafeMoney)
            throw new CheckoutException(400, "REPORT_LIMIT", "Tổng tiền vượt giới hạn báo cáo. Hãy chọn khoảng ngày ngắn hơn.");
        return value;
    }
    public async Task<OverviewReport> OverviewAsync(ReportQuery query, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now.AddHours(7));
        if ((query.From is null) != (query.To is null)) throw Invalid();
        var from = query.From is null ? today.AddDays(-29) : Parse(query.From);
        var to = query.To is null ? today : Parse(query.To);
        if (from > to || to.DayNumber - from.DayNumber >= 366 || from == DateOnly.MinValue || to == DateOnly.MaxValue) throw Invalid();
        var start = ApiContract.VietnamDay(from).From; var end = ApiContract.VietnamDay(to).To;
        // Orders -> payments -> refunds, as in business writes. Inventory is read separately after commit,
        // avoiding an Orders/Inventory lock inversion with checkout and dispatch.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var allStates = await db.Orders.AsNoTracking().GroupBy(o => o.Status).Select(g => new { Status = g.Key, Count = g.LongCount() }).ToListAsync(ct);
        var cohort = db.Orders.AsNoTracking().Where(o => o.CreatedAt >= start && o.CreatedAt < end);
        var statuses = await cohort.GroupBy(o => o.Status).Select(g => new { Status = g.Key, Count = g.LongCount() }).ToListAsync(ct);
        var placed = await cohort.GroupBy(o => o.CreatedAt.AddHours(7).Date).Select(g => new { Date = g.Key, Count = g.LongCount(), Amount = g.Sum(o => o.GrandTotal) }).ToListAsync(ct);
        var best = await db.OrderItems.AsNoTracking().Where(i => i.Order.Status == OrderStatus.Completed && i.Order.CompletedAt >= start && i.Order.CompletedAt < end)
            .GroupBy(i => i.VariantId).Select(g => new { Id = g.Key, Quantity = g.Sum(i => (long)i.Quantity), Orders = g.Select(i => i.OrderId).Distinct().LongCount() })
            .OrderByDescending(g => g.Quantity).ThenBy(g => g.Id).Take(10).ToListAsync(ct);
        var bestIds = best.Select(b => b.Id).ToArray();
        var names = await db.OrderItems.AsNoTracking().Where(i => bestIds.Contains(i.VariantId)
                && i.Order.Status == OrderStatus.Completed && i.Order.CompletedAt >= start && i.Order.CompletedAt < end)
            .GroupBy(i => i.VariantId).Select(g => new { Id = g.Key, ItemId = g.Max(i => i.Id) }).ToListAsync(ct);
        var itemIds = names.Select(n => n.ItemId).ToArray();
        var snapshots = await db.OrderItems.AsNoTracking().Where(i => itemIds.Contains(i.Id))
            .Select(i => new { i.VariantId, i.ProductNameSnapshot, i.VariantSnapshot }).ToListAsync(ct);
        var incoming = await db.Payments.AsNoTracking().Where(p => p.Status == PaymentStatus.Confirmed && p.ConfirmedAt >= start && p.ConfirmedAt < end)
            .GroupBy(p => p.ConfirmedAt!.Value.AddHours(7).Date).Select(g => new { Date = g.Key, Amount = g.Sum(p => p.Amount) }).ToListAsync(ct);
        var outgoing = await db.Refunds.AsNoTracking().Where(r => r.Status == RefundStatus.Completed && r.CompletedAt >= start && r.CompletedAt < end)
            .GroupBy(r => r.CompletedAt!.Value.AddHours(7).Date).Select(g => new { Date = g.Key, Amount = g.Sum(r => r.Amount) }).ToListAsync(ct);
        var pendingRefunds = await db.Refunds.LongCountAsync(r => r.Status == RefundStatus.Pending, ct);
        await tx.CommitAsync(ct);
        var stock = db.Inventory.AsNoTracking().Where(i => i.Variant.IsActive && i.Variant.Product.IsActive
            && i.Variant.Product.Brand.IsActive && i.Variant.Product.Category.IsActive);
        var activeVariants = await stock.LongCountAsync(ct);
        var lowStockCount = await stock.LongCountAsync(i => i.OnHand - i.Reserved <= 5, ct);
        var lowStock = await stock.Where(i => i.OnHand - i.Reserved <= 5).OrderBy(i => i.OnHand - i.Reserved).ThenBy(i => i.VariantId).Take(10)
            .Select(i => new ReportLowStock(i.VariantId.ToString(), i.Variant.Product.Name, i.Variant.Sku, i.OnHand - i.Reserved)).ToListAsync(ct);
        var days = new List<ReportDay>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var key = date.ToDateTime(TimeOnly.MinValue); var p = placed.SingleOrDefault(d => d.Date == key);
            var received = incoming.SingleOrDefault(d => d.Date == key)?.Amount ?? 0;
            var refunded = outgoing.SingleOrDefault(d => d.Date == key)?.Amount ?? 0;
            days.Add(new(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), p?.Count ?? 0, Money(p?.Amount ?? 0), Money(received), Money(refunded), Money(received - refunded)));
        }
        long State(OrderStatus status) => allStates.SingleOrDefault(s => s.Status == status)?.Count ?? 0;
        return new(from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "Asia/Ho_Chi_Minh", now,
            days.Sum(d => d.Orders), Money(days.Sum(d => d.OrderValue)), Money(days.Sum(d => d.Received)), Money(days.Sum(d => d.Refunded)), Money(days.Sum(d => d.NetReceived)), days,
            Enum.GetValues<OrderStatus>().Select(s => new ReportStatus(s.ToString(), statuses.SingleOrDefault(r => r.Status == s)?.Count ?? 0)).ToArray(),
            best.Select(b => { var name = snapshots.Single(s => s.VariantId == b.Id); return new ReportBestSeller(ApiContract.Id(b.Id), name.ProductNameSnapshot, name.VariantSnapshot, b.Quantity, b.Orders); }).ToArray(),
            new(State(OrderStatus.Reviewing), State(OrderStatus.Shipping), State(OrderStatus.Delivered), pendingRefunds), new(activeVariants, lowStockCount, lowStock));
    }
}
