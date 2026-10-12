using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PhoneStore.Api.Data;
using PhoneStore.Api.Entities;
namespace PhoneStore.Api.Services.OrderManagement;
public sealed record OrderExpiryBatch(int Scanned, int Expired, long LastId);
public sealed class OrderExpiryProcessor(AppDbContext db, IServiceScopeFactory scopes, TimeProvider clock, IOptions<OrderExpiryOptions> options, ILogger<OrderExpiryProcessor> logger)
{
    public async Task<OrderExpiryBatch> ProcessBatchAsync(long afterId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = await db.Orders.AsNoTracking().Where(o => o.Id > afterId && o.PaymentMethod == PaymentMethod.BankTransfer && o.PaymentDueAt != null && o.PaymentDueAt <= now
            && (o.Status == OrderStatus.Placed || o.Status == OrderStatus.Reviewing || o.Status == OrderStatus.Approved)
            && (db.Payments.Where(p => p.OrderId == o.Id && p.Status == PaymentStatus.Confirmed).Sum(p => (decimal?)p.Amount) ?? 0) < o.GrandTotal)
            .OrderBy(o => o.Id).Select(o => o.Id).Take(options.Value.BatchSize).ToListAsync(ct);
        var expired = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<AfterSaleService>().ExpireAsync(id, ct)) expired++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) {
                var failure = error is PhoneStore.Api.Services.Checkout.CheckoutException domain ? domain.Code : error.InnerException is Microsoft.Data.SqlClient.SqlException sql ? sql.Number.ToString() : error.GetType().Name;
                logger.LogWarning("Order expiry needs retry. Code ORDER_EXPIRY_RETRY OrderId {OrderId} Failure {Failure}", id, failure);
            }
        }
        return new(ids.Count, expired, ids.Count == 0 ? afterId : ids[^1]);
    }
}
