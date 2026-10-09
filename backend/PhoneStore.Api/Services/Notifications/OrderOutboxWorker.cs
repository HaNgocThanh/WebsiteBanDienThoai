using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;

namespace PhoneStore.Api.Services.Notifications;

public sealed class OrderOutboxWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<OrderOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Outbox:Enabled", true)) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (!await db.Database.CanConnectAsync(stoppingToken) || (await db.Database.GetPendingMigrationsAsync(stoppingToken)).Any()) continue;
                for (var i = 0; i < 20; i++)
                {
                    using var work = scopes.CreateScope();
                    if (!await work.ServiceProvider.GetRequiredService<OrderOutboxProcessor>().ProcessOneAsync(stoppingToken)) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Order worker unavailable. Code ORDER_WORKER_UNAVAILABLE"); }
        }
    }
}
