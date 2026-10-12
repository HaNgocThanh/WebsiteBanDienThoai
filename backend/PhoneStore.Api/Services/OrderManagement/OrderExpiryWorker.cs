using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PhoneStore.Api.Data;
namespace PhoneStore.Api.Services.OrderManagement;
public sealed class OrderExpiryWorker(IServiceScopeFactory scopes, IOptions<OrderExpiryOptions> options, ILogger<OrderExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;
        long cursor = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try {
                await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), stoppingToken);
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (!await db.Database.CanConnectAsync(stoppingToken) || (await db.Database.GetPendingMigrationsAsync(stoppingToken)).Any()) continue;
                var result = await scope.ServiceProvider.GetRequiredService<OrderExpiryProcessor>().ProcessBatchAsync(cursor, stoppingToken);
                cursor = result.Scanned == settings.BatchSize ? result.LastId : 0;
                if (result.Expired > 0) logger.LogInformation("Expired {Count} payment holds. Code ORDER_EXPIRY_COMPLETED", result.Expired);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Order expiry worker unavailable. Code ORDER_EXPIRY_UNAVAILABLE"); }
        }
    }
}
