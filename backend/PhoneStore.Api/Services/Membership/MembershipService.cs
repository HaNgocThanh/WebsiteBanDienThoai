using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.Membership;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Auth;
using PhoneStore.Api.Services.Checkout;

namespace PhoneStore.Api.Services.Membership;

public sealed class MembershipService(AppDbContext db, TimeProvider clock, AuthBootstrap userLocks)
{
    private static readonly CustomerTierCode[] Codes = [CustomerTierCode.Bronze, CustomerTierCode.Silver, CustomerTierCode.Gold, CustomerTierCode.Diamond];
    private static readonly string[] Names = ["Đồng", "Bạc", "Vàng", "Kim cương"];
    private static readonly decimal[] Defaults = [1_000_000, 10_000_000, 50_000_000, 100_000_000];
    private DateTime Now => new(clock.GetUtcNow().UtcTicks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    private static CheckoutException Error(int status, string code) => new(status, code, "Cấu hình hạng không hợp lệ hoặc đã thay đổi. Vui lòng tải lại và kiểm tra ngưỡng.");
    // Global policy gate always precedes user and order locks. Exclusive configuration
    // blocks accrual/claim/refunds until all backfill and recalculation commit together.
    public static async Task LockAsync(AppDbContext db, bool exclusive, CancellationToken ct)
    {
        var mode = exclusive ? "Exclusive" : "Shared";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'PhoneStore.Membership.Policy',
                @LockMode={mode}, @LockOwner=N'Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51004, 'Membership policy busy.', 1;
            """, ct);
    }
    private static TierDto Dto(CustomerTier t) => new(t.Code.ToString(), t.Name, ApiContract.Money(t.MinimumSpend), Convert.ToBase64String(t.Version), t.Id.ToString());
    private static bool Enabled(IReadOnlyList<CustomerTier> tiers) => tiers.Count == 4 && tiers.Select(t => t.Code).SequenceEqual(Codes)
        && tiers[0].MinimumSpend > 0 && tiers.All(t => t.MinimumSpend <= ApiContract.MaxSafeMoney)
        && tiers.Zip(tiers.Skip(1)).All(pair => pair.First.MinimumSpend < pair.Second.MinimumSpend);
    private Task<List<CustomerTier>> TiersAsync(CancellationToken ct) => db.CustomerTiers.AsNoTracking().OrderBy(t => t.Rank).ToListAsync(ct);
    public async Task<TierConfigurationDto> ConfigurationAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(db, false, ct);
        var tiers = await TiersAsync(ct); var result = new TierConfigurationDto(Enabled(tiers), tiers.Select(Dto).ToArray());
        await tx.CommitAsync(ct); return result;
    }
    public async Task<MembershipDto> ReadAsync(Guid user, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(db, false, ct);
        var tiers = await TiersAsync(ct); var profile = await db.CustomerProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == user, ct) ?? throw Error(404, "NOT_FOUND");
        var enabled = Enabled(tiers); var tier = enabled ? tiers.SingleOrDefault(t => t.Id == profile.TierId) : null;
        var next = enabled ? tiers.FirstOrDefault(t => t.MinimumSpend > profile.EligibleSpend) : null;
        var result = new MembershipDto(enabled, tier?.Code.ToString(), ApiContract.Money(profile.EligibleSpend), next is null ? null : Dto(next), next is null ? 0 : next.MinimumSpend - profile.EligibleSpend, tiers.Select(Dto).ToArray());
        await tx.CommitAsync(ct); return result;
    }
    // Caller holds policy/user/order locks and owns transaction. Canonical keys unify
    // completion, old-order backfill and guest claim; purchase snapshots are immutable.
    public async Task SyncOrderAsync(Order order, CancellationToken ct)
    {
        if (order.Status != OrderStatus.Completed || order.UserId is not { } user) return;
        var key = $"order:{order.Id}:completed";
        if (await db.CustomerSpendEntries.AnyAsync(e => e.OrderId == order.Id && e.Kind == SpendEntryKind.OrderCompleted && e.EventKey != key, ct)) throw Error(409, "SPEND_LEDGER_CONFLICT");
        var prior = await db.CustomerSpendEntries.AsNoTracking().SingleOrDefaultAsync(e => e.EventKey == key, ct);
        var amount = order.Subtotal - order.DiscountTotal;
        if (prior is not null && (prior.UserId != user || prior.OrderId != order.Id || prior.Amount != amount || prior.Kind != SpendEntryKind.OrderCompleted)) throw Error(409, "SPEND_LEDGER_CONFLICT");
        if (prior is null) db.CustomerSpendEntries.Add(new() { UserId = user, OrderId = order.Id, Kind = SpendEntryKind.OrderCompleted, Amount = amount, EventKey = key, CreatedAt = order.CompletedAt ?? Now });
        var refunds = await db.Refunds.AsNoTracking().Where(r => r.Payment.OrderId == order.Id && r.Status == RefundStatus.Completed && r.MerchandiseAmount > 0).ToListAsync(ct);
        foreach (var refund in refunds) {
            var refundKey = $"refund:{refund.Id}";
            if (await db.CustomerSpendEntries.AnyAsync(e => e.RefundId == refund.Id && e.Kind == SpendEntryKind.Refund && e.EventKey != refundKey, ct)) throw Error(409, "SPEND_LEDGER_CONFLICT");
            var existing = await db.CustomerSpendEntries.AsNoTracking().SingleOrDefaultAsync(e => e.EventKey == refundKey, ct);
            if (existing is not null && (existing.UserId != user || existing.OrderId != order.Id || existing.RefundId != refund.Id || existing.Amount != -refund.MerchandiseAmount || existing.Kind != SpendEntryKind.Refund)) throw Error(409, "SPEND_LEDGER_CONFLICT");
            if (existing is null) db.CustomerSpendEntries.Add(new() { UserId = user, OrderId = order.Id, RefundId = refund.Id, Kind = SpendEntryKind.Refund, Amount = -refund.MerchandiseAmount, EventKey = refundKey, CreatedAt = refund.CompletedAt ?? Now });
        }
        await db.SaveChangesAsync(ct);
    }
    public async Task RecalculateAsync(Guid user, string reason, CancellationToken ct)
    {
        var profile = await db.CustomerProfiles.SingleAsync(p => p.UserId == user, ct);
        var amount = await db.CustomerSpendEntries.Where(e => e.UserId == user).SumAsync(e => e.Amount, ct);
        if (amount < 0 || amount > ApiContract.MaxSafeMoney) throw Error(409, "SPEND_LEDGER_CONFLICT");
        var tiers = await TiersAsync(ct);
        var next = Enabled(tiers) ? tiers.LastOrDefault(t => t.MinimumSpend <= amount)?.Id : null;
        if (profile.TierId != next) db.CustomerTierHistories.Add(new() { UserId = user, PreviousTierId = profile.TierId, NewTierId = next, EligibleSpend = amount, Reason = reason, CreatedAt = Now });
        profile.TierId = next; profile.EligibleSpend = amount; profile.TierCalculatedAt = Now;
        await db.SaveChangesAsync(ct);
    }
    private async Task BackfillAsync(string reason, CancellationToken ct)
    {
        // Keyset batches avoid loading the entire order/ledger history into memory.
        long last = 0;
        while (true) {
            var batch = await db.Orders.AsNoTracking().Where(o => o.Id > last && o.UserId != null && o.Status == OrderStatus.Completed).OrderBy(o => o.Id).Take(100).ToListAsync(ct);
            if (batch.Count == 0) break;
            foreach (var order in batch) await SyncOrderAsync(order, ct);
            last = batch[^1].Id;
        }
        var users = await db.CustomerProfiles.Select(p => p.UserId).OrderBy(id => id).ToListAsync(ct);
        foreach (var user in users) { await userLocks.LockUserAsync(user, ct); await RecalculateAsync(user, reason, ct); }
    }
    public async Task<TierConfigurationDto> ConfigureAsync(TierConfigurationRequest request, Guid? actor, CancellationToken ct)
    {
        if (request.OperationKey == Guid.Empty || request.Reason.Trim().Length is < 1 or > 300 || request.Tiers.Length != 4 || request.Tiers.Any(t => t is null)) throw Error(400, "VALIDATION_ERROR");
        if (request.Tiers.Select(t => t.Code).Distinct().Count() != 4) throw Error(400, "VALIDATION_ERROR");
        var inputs = Codes.Select(code => request.Tiers.SingleOrDefault(t => t.Code == code.ToString())).ToArray();
        if (inputs.Any(t => t is null)) throw Error(400, "VALIDATION_ERROR");
        for (var i = 0; i < 4; i++) if (inputs[i]!.MinimumSpend <= 0 || inputs[i]!.MinimumSpend > ApiContract.MaxSafeMoney || decimal.Truncate(inputs[i]!.MinimumSpend) != inputs[i]!.MinimumSpend || i > 0 && inputs[i]!.MinimumSpend <= inputs[i - 1]!.MinimumSpend) throw Error(400, "VALIDATION_ERROR");
        var key = "Membership.Configuration:" + request.OperationKey.ToString("D");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { actor, tiers = inputs, reason = request.Reason.Trim() })));
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(db, true, ct);
        var receipt = await db.OutboxMessages.SingleOrDefaultAsync(m => m.EventKey == key, ct);
        if (receipt is not null) {
            using var json = JsonDocument.Parse(receipt.PayloadJson);
            if (json.RootElement.GetProperty("hash").GetString() != hash) throw Error(409, "OPERATION_KEY_MISMATCH");
            var replay = json.RootElement.GetProperty("receipt").Deserialize<TierConfigurationDto>()!; await tx.CommitAsync(ct); return replay;
        }
        var existing = await db.CustomerTiers.OrderBy(t => t.Rank).ToListAsync(ct);
        foreach (var tier in existing) await db.Entry(tier).ReloadAsync(ct);
        if (existing.Any(t => t.Rank != Array.IndexOf(Codes, t.Code) + 1)) throw Error(409, "TIER_CONFIGURATION_CONFLICT");
        for (var i = 0; i < 4; i++) {
            var tier = existing.SingleOrDefault(t => t.Code == Codes[i]); var input = inputs[i]!;
            if (tier is null ? input.Version is not null : input.Version != Convert.ToBase64String(tier.Version)) throw Error(412, "VERSION_CONFLICT");
        }
        // Separate temporary values prevent collisions in the unique threshold index
        // when one tier takes another tier's former value. Never visible outside tx.
        for (var i = 0; i < existing.Count; i++) existing[i].MinimumSpend = 999_999_999_999_999_990m + i;
        await db.SaveChangesAsync(ct);
        for (var i = 0; i < 4; i++) {
            var tier = existing.SingleOrDefault(t => t.Code == Codes[i]);
            if (tier is null) { tier = new() { Code = Codes[i], Rank = i + 1 }; db.CustomerTiers.Add(tier); existing.Add(tier); }
            tier.Name = Names[i]; tier.MinimumSpend = inputs[i]!.MinimumSpend; tier.UpdatedAt = Now;
        }
        await db.SaveChangesAsync(ct); await BackfillAsync(request.Reason.Trim(), ct);
        var result = new TierConfigurationDto(true, existing.OrderBy(t => t.Rank).Select(Dto).ToArray());
        db.AuditLogs.Add(new() { ActorUserId = actor, Action = "MembershipConfigured", EntityType = "CustomerTier", EntityId = "all", Summary = "sha256:" + hash, CorrelationId = key, CreatedAt = Now });
        // Processed durable receipt, no email side effect for this operation.
        db.OutboxMessages.Add(new() { Type = "MembershipConfigured", EventKey = key, PayloadJson = JsonSerializer.Serialize(new { hash, receipt = result }), CreatedAt = Now, NextAttemptAt = Now, ProcessedAt = Now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
    }
    public async Task BootstrapAsync(CancellationToken ct)
    {
        var config = await ConfigurationAsync(ct);
        if (!config.Enabled || !config.Tiers.Select(t => t.MinimumSpend).SequenceEqual(Defaults)) {
            await ConfigureAsync(new() { OperationKey = Guid.NewGuid(), Reason = "Khởi tạo chính sách D-35 và backfill đơn hoàn tất", Tiers = Codes.Select((code, i) => new TierInput { Code = code.ToString(), MinimumSpend = Defaults[i], Version = config.Tiers.SingleOrDefault(t => t.Code == code.ToString())?.Version }).ToArray() }, null, ct);
        } else {
            await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(db, true, ct); await BackfillAsync("Tính lại sổ chi tiêu", ct); await tx.CommitAsync(ct);
        }
    }
    public async Task<PagedResponse<SpendDto>> SpendAsync(Guid user, PageQuery page, CancellationToken ct)
    {
        var query = db.CustomerSpendEntries.AsNoTracking().Where(e => e.UserId == user);
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Skip((int)Math.Min(page.Offset, int.MaxValue)).Take(page.PageSize).ToListAsync(ct);
        return new(rows.Select(e => new SpendDto(e.Id.ToString(), e.OrderId.ToString(), e.RefundId?.ToString(), e.Kind.ToString(), e.Amount, e.CreatedAt)).ToArray(), page.Page, page.PageSize, total);
    }
    public async Task<PagedResponse<TierHistoryDto>> HistoryAsync(Guid user, PageQuery page, CancellationToken ct)
    {
        var query = db.CustomerTierHistories.AsNoTracking().Where(e => e.UserId == user);
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Skip((int)Math.Min(page.Offset, int.MaxValue)).Take(page.PageSize)
            .Select(e => new TierHistoryDto(e.Id.ToString(), e.PreviousTier == null ? null : e.PreviousTier.Code.ToString(), e.NewTier == null ? null : e.NewTier.Code.ToString(), e.EligibleSpend, e.Reason, e.CreatedAt)).ToListAsync(ct);
        return new(rows, page.Page, page.PageSize, total);
    }
    public async Task<PagedResponse<CustomerMembershipDto>> CustomersAsync(PageQuery page, string? search, string? tierId, bool? isActive, CancellationToken ct)
    {
        var query = db.CustomerProfiles.AsNoTracking().AsQueryable();
        if (isActive is { } active) query = query.Where(p => p.User.IsActive == active);
        if (search is { Length: > 150 }) throw Error(400, "VALIDATION_ERROR");
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(p => p.User.FullName.Contains(term) || p.User.Email!.Contains(term)); }
        if (tierId == "none") query = query.Where(p => p.TierId == null);
        else if (tierId is not null) {
            if (!ApiContract.TryParseId(tierId, out var id)) throw Error(400, "VALIDATION_ERROR");
            query = query.Where(p => p.TierId == id);
        }
        var count = await query.LongCountAsync(ct);
        var rows = await query.OrderBy(p => p.UserId).Skip((int)Math.Min(page.Offset, int.MaxValue)).Take(page.PageSize)
            .Select(p => new CustomerMembershipDto(p.UserId, p.User.FullName, p.User.Email!, p.Tier == null ? null : p.Tier.Code.ToString(), p.EligibleSpend, p.User.PhoneNumber, p.User.IsActive, p.User.EmailConfirmed)).ToListAsync(ct);
        return new(rows, page.Page, page.PageSize, count);
    }
}
