namespace PhoneStore.Api.DTOs.Reporting;

public sealed class ReportQuery
{
    public string? From { get; init; }
    public string? To { get; init; }
}
public sealed record ReportDay(string Date, long Orders, decimal OrderValue, decimal Received, decimal Refunded, decimal NetReceived);
public sealed record ReportStatus(string Status, long Count);
public sealed record ReportBestSeller(string VariantId, string ProductName, string Variant, long Quantity, long Orders);
public sealed record ReportLowStock(string VariantId, string ProductName, string Sku, int Available);
public sealed record ReportQueue(long Reviewing, long Shipping, long Delivered, long PendingRefunds);
public sealed record ReportStock(long ActiveVariants, long LowStockVariants, IReadOnlyList<ReportLowStock> Items);
public sealed record OverviewReport(string From, string To, string Timezone, DateTime GeneratedAt,
    long Orders, decimal OrderValue, decimal Received, decimal Refunded, decimal NetReceived,
    IReadOnlyList<ReportDay> Daily, IReadOnlyList<ReportStatus> Statuses, IReadOnlyList<ReportBestSeller> BestSellers,
    ReportQueue Queue, ReportStock Stock);
