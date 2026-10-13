using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.Entities;
using PhoneStore.Api.DTOs.Membership;
using PhoneStore.Api.Services.Auth;
using PhoneStore.Api.Services.Checkout;
namespace PhoneStore.Api.Services.Membership;

public sealed class AdminCustomerService(AppDbContext db, UserManager<ApplicationUser> users, AuthBootstrap locks, TimeProvider clock)
{
    private static CheckoutException Error(int status, string code) => new(status, code, "Không thể cập nhật khách hàng. Hãy kiểm tra thông tin và tải lại hồ sơ.");
    private async Task<ApplicationUser> CustomerAsync(Guid id, CancellationToken ct) => await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.CustomerProfile != null, ct) ?? throw Error(404, "NOT_FOUND");
    public async Task<AdminCustomerProfileDto> ReadAsync(Guid id, Guid actor, CancellationToken ct)
    {
        var user = await CustomerAsync(id, ct);
        return new(user.Id, user.Email!, user.FullName, user.PhoneNumber, user.IsActive, user.EmailConfirmed,
            user.Id != actor && !await users.IsInRoleAsync(user, "Admin"), user.CreatedAt, user.ConcurrencyStamp!);
    }
    public async Task<CustomerUpdateReceipt> UpdateAsync(Guid id, Guid actor, AdminCustomerUpdate input, CancellationToken ct)
    {
        var name = input.FullName.Trim(); var phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim();
        if (name.Length is < 1 or > 150 || input.OperationKey == Guid.Empty || string.IsNullOrWhiteSpace(input.Version)) throw Error(400, "VALIDATION_ERROR");
        var key = "Customer.Update:" + input.OperationKey.ToString("D");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { id, actor, name, phone, input.IsActive, input.Version })));
        await using var tx = await db.Database.BeginTransactionAsync(ct); await locks.LockUserAsync(id, ct);
        var user = await CustomerAsync(id, ct); await db.Entry(user).ReloadAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource={key}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51004, 'Customer operation busy.',1;
            """, ct);
        var prior = await db.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(m => m.EventKey == key, ct);
        if (prior is not null) {
            using var json = JsonDocument.Parse(prior.PayloadJson);
            if (json.RootElement.GetProperty("hash").GetString() != hash) throw Error(409, "OPERATION_KEY_MISMATCH");
            var replay = json.RootElement.GetProperty("receipt").Deserialize<CustomerUpdateReceipt>()!; await tx.CommitAsync(ct); return replay;
        }
        if (user.ConcurrencyStamp != input.Version) throw Error(412, "VERSION_CONFLICT");
        var statusChanged = user.IsActive != input.IsActive;
        if (statusChanged && (id == actor || await users.IsInRoleAsync(user, "Admin"))) throw Error(409, "ADMIN_ACCOUNT_PROTECTED");
        user.FullName = name; user.PhoneNumber = phone; user.IsActive = input.IsActive; user.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        // Revocation also persists through disable/enable cycles; do not touch email verification.
        if (statusChanged) user.SecurityStamp = Guid.NewGuid().ToString("N");
        var saved = await users.UpdateAsync(user); if (!saved.Succeeded) throw Error(409, "VERSION_CONFLICT");
        var result = new CustomerUpdateReceipt(user.ConcurrencyStamp!); var now = clock.GetUtcNow().UtcDateTime;
        db.AuditLogs.Add(new() { ActorUserId = actor, Action = "CustomerUpdated", EntityType = "ApplicationUser", EntityId = id.ToString(), Summary = "sha256:" + hash, CorrelationId = key, CreatedAt = now });
        db.OutboxMessages.Add(new() { EventKey = key, Type = "CustomerUpdated", PayloadJson = JsonSerializer.Serialize(new { hash, receipt = result }), CreatedAt = now, NextAttemptAt = now, ProcessedAt = now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
    }
}
