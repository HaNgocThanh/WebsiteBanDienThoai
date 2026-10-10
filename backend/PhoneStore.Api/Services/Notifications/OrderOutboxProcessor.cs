using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.GuestOrders;

namespace PhoneStore.Api.Services.Notifications;

// One scoped processor per iteration; never retain EF tracking across retries/work items.
public sealed class OrderOutboxProcessor(AppDbContext db, OrderAccessMailQueue mail, IOrderEmailSender sender,
    TimeProvider clock, ILogger<OrderOutboxProcessor> logger)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        var owner = Guid.NewGuid().ToString("N");
        var now = Now;
        await db.Database.OpenConnectionAsync(ct);
        long? id;
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = """
                ;WITH candidate AS (
                    SELECT TOP(1) * FROM [OutboxMessages] WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE [ProcessedAt] IS NULL AND [NextAttemptAt] <= @now
                      AND ([LockedUntil] IS NULL OR [LockedUntil] <= @now)
                      AND [Type] IN ('OrderPlaced', 'OrderMail', 'OrderClaimed', 'PaymentInitiated', 'PaymentConfirmed')
                    ORDER BY [NextAttemptAt], [Id]
                )
                UPDATE candidate SET [LockOwner]=@owner, [LockedUntil]=@until, [Attempts]=[Attempts]+1
                OUTPUT INSERTED.[Id];
                """;
            foreach (var (name, value, type) in new[] { ("@now", (object)now, DbType.DateTime2), ("@until", (object)now.AddMinutes(1), DbType.DateTime2), ("@owner", (object)owner, DbType.AnsiString) })
            { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; parameter.DbType = type; command.Parameters.Add(parameter); }
            var result = await command.ExecuteScalarAsync(ct); id = result is null ? null : Convert.ToInt64(result);
        }
        await db.Database.CloseConnectionAsync();
        if (id is null) return false;
        try
        {
            var message = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id, ct);
            if (message.Type == "OrderMail")
            {
                // Mail timeout is shorter than the lease. Remote adapters must honor cancellation and use MessageKey.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
                await DeliverAsync(message, timeout.Token);
                await CompleteAsync(message.Id, owner, ct);
            }
            else await NotifyAsync(message, owner, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            var failed = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id, ct);
            var retryAt = Now.AddSeconds(Math.Min(3600, 5 * Math.Pow(2, Math.Min(failed.Attempts - 1, 10))));
            await db.OutboxMessages.Where(m => m.Id == id && m.LockOwner == owner && m.ProcessedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.NextAttemptAt, retryAt).SetProperty(m => m.LockOwner, (string?)null)
                    .SetProperty(m => m.LockedUntil, (DateTime?)null).SetProperty(m => m.LastError, "ORDER_DELIVERY_FAILED"), ct);
            logger.LogWarning("Order outbox {MessageId} failed; retry scheduled. Code ORDER_DELIVERY_FAILED", id);
        }
        return true;
    }
    private async Task<OutboxMessage> OwnedAsync(long id, string owner, CancellationToken ct)
    {
        var message = await db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM [OutboxMessages] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={id}").SingleAsync(ct);
        if (message.LockOwner != owner || message.ProcessedAt is not null || message.LockedUntil is null || message.LockedUntil <= Now)
            throw new InvalidOperationException("Outbox lease no longer owned.");
        return message;
    }
    private void Mark(OutboxMessage message) { message.ProcessedAt = Now; message.LockOwner = null; message.LockedUntil = null; message.LastError = null; }
    private async Task CompleteAsync(long id, string owner, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var message = await OwnedAsync(id, owner, ct); Mark(message);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    private async Task NotifyAsync(OutboxMessage source, string owner, CancellationToken ct)
    {
        using var payload = JsonDocument.Parse(source.PayloadJson);
        if (payload.RootElement.GetProperty("version").GetInt32() != 1 || !ApiContract.TryParseId(payload.RootElement.GetProperty("orderId").GetString(), out var orderId))
            throw new InvalidOperationException("Unsupported order event.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var message = await OwnedAsync(source.Id, owner, ct);
        var order = await db.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={orderId}").SingleAsync(ct);
        var recipients = await (from userRole in db.UserRoles join role in db.Roles on userRole.RoleId equals role.Id
            join user in db.Users on userRole.UserId equals user.Id
            where role.Name == "Admin" && user.IsActive && user.EmailConfirmed select user.Id).ToListAsync(ct);
        if (order.UserId is { } userId) recipients.Add(userId);
        foreach (var recipient in recipients.Distinct())
        {
            if (!await db.Notifications.AnyAsync(n => n.UserId == recipient && n.EventKey == source.EventKey, ct))
                db.Notifications.Add(new Notification { UserId = recipient, OrderId = order.Id, Type = source.Type,
                    Title = source.Type switch { "OrderPlaced" => "Đơn hàng mới", "OrderClaimed" => "Đơn hàng đã được nhận quyền", "PaymentInitiated" => "Đang chờ thanh toán SePay", "PaymentConfirmed" => "Đã ghi nhận thanh toán", _ => "Đơn hàng có cập nhật" },
                    Body = "Đơn " + order.OrderNumber, EventKey = source.EventKey, CreatedAt = Now });
        }
        if (source.Type == "OrderPlaced")
        {
            var deliveryKey = "OrderPlacedMail:" + order.Id;
            if (!await db.OutboxMessages.AnyAsync(m => m.EventKey == deliveryKey, ct))
            {
                if (order.UserId is null) await mail.QueueAccessAsync(order, OrderAccessTokenPurpose.ViewOrder, ct, eventKey: deliveryKey);
                else mail.Queue(new(1, order.Id, null, null, "OrderPlaced"), deliveryKey);
            }
        }
        Mark(message); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    private async Task DeliverAsync(OutboxMessage source, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<OrderMailPayload>(source.PayloadJson) ?? throw new InvalidOperationException("Invalid order mail.");
        if (payload.Version != 1) throw new InvalidOperationException("Unsupported order mail.");
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == payload.OrderId, ct);
        string? path = null; DateTime? expires = null;
        if (payload.TokenId is { } tokenId)
        {
            var token = await db.OrderAccessTokens.AsNoTracking().SingleAsync(t => t.Id == tokenId && t.OrderId == order.Id, ct);
            // Drop obsolete capabilities rather than emailing stale/claimed order access.
            if (token.UsedAt is not null || token.ExpiresAt <= Now || order.UserId is not null) return;
            var secret = mail.Unprotect(payload.ProtectedToken!);
            var hash = System.Security.Cryptography.SHA256.HashData(Convert.FromHexString(secret));
            if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(hash, token.TokenHash) || payload.Purpose != token.Purpose.ToString())
                throw new InvalidOperationException("Invalid mail capability.");
            path = "/guest/access#token=" + secret + "&purpose=" + payload.Purpose; expires = token.ExpiresAt;
        }
        if (!sender.IsConfigured) throw new InvalidOperationException("Order email unavailable.");
        await sender.SendAsync(new(source.EventKey, order.CustomerEmail, order.OrderNumber, payload.Purpose, path, expires, payload.AccountSetup), ct);
    }
}
