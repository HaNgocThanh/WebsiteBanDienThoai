using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using PhoneStore.Api.Data;
using PhoneStore.Api.Entities;

namespace PhoneStore.Api.Services.GuestOrders;

public sealed record OrderMailPayload(int Version, long OrderId, long? TokenId, string? ProtectedToken,
    string Purpose, bool AccountSetup = false);
// Caller owns the transaction. Durable payload contains encrypted capability, never plaintext token or email.
public sealed class OrderAccessMailQueue(AppDbContext db, IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("PhoneStore.OrderMail.v1");
    public string Unprotect(string value) => protector.Unprotect(value);
    public async Task QueueAccessAsync(Order order, OrderAccessTokenPurpose purpose, CancellationToken ct, bool setup = false, string? eventKey = null)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var token = new OrderAccessToken { OrderId = order.Id, TokenHash = SHA256.HashData(Convert.FromHexString(secret)),
            Purpose = purpose, CreatedAt = now, ExpiresAt = now.AddHours(1) };
        db.OrderAccessTokens.Add(token); await db.SaveChangesAsync(ct);
        Queue(new(1, order.Id, token.Id, protector.Protect(secret), purpose.ToString(), setup), eventKey ?? "OrderAccessMail:" + token.Id);
    }
    public void Queue(OrderMailPayload payload, string eventKey) => db.OutboxMessages.Add(new OutboxMessage {
        EventKey = eventKey, Type = "OrderMail", PayloadJson = JsonSerializer.Serialize(payload),
        CreatedAt = clock.GetUtcNow().UtcDateTime, NextAttemptAt = clock.GetUtcNow().UtcDateTime
    });
}
