using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.Entities;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.Promotions;
using PhoneStore.Api.Services.Checkout;
using PhoneStore.Api.Services.Auth;
using PhoneStore.Api.Services.Membership;
namespace PhoneStore.Api.Services.Promotions;

public sealed class PromotionService(AppDbContext db, TimeProvider clock, AuthBootstrap users)
{
    private DateTime Now => new(clock.GetUtcNow().UtcTicks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    public DateTime CurrentTime => Now;
    private static CheckoutException Error(int status, string code) => new(status, code, "Chương trình không hợp lệ hoặc đã thay đổi. Hãy kiểm tra dữ liệu và tải lại.");
    public static async Task LockAsync(AppDbContext db, bool exclusive, CancellationToken ct)
    {
        var mode = exclusive ? "Exclusive" : "Shared";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'PhoneStore.Promotions.Policy', @LockMode={mode}, @LockOwner=N'Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51004,'Promotion policy busy.',1;
            """, ct);
    }
    public async Task LockPricingAsync(Guid? userId, CancellationToken ct)
    {
        await MembershipService.LockAsync(db, false, ct);
        if (userId is { } user) await users.LockUserAsync(user, ct);
        await LockAsync(db, false, ct);
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'PhoneStore.Catalog.Write', @LockMode=N'Shared', @LockOwner=N'Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51004,'Catalog busy.',1;
            """, ct);
    }
    public static decimal Discount(Promotion p, decimal price) {
        var amount = p.DiscountType == DiscountType.Percent ? decimal.Floor(price * p.DiscountValue / 100) : p.DiscountValue;
        if (p.DiscountType == DiscountType.Percent && p.MaxDiscountPerUnit is { } cap) amount = Math.Min(amount, cap);
        return Math.Min(price, Math.Max(0, amount));
    }
    public static Promotion? Best(IEnumerable<Promotion> candidates, long productId, long? tierId, decimal price, DateTime now) => candidates
        .Where(p => p.Status == PromotionStatus.Published && p.StartsAt <= now && now < p.EndsAt
            && (p.ProductScope == PromotionProductScope.All || p.Products.Any(x => x.ProductId == productId))
            && (p.AudienceScope == PromotionAudienceScope.All || tierId is { } tier && p.CustomerTiers.Any(x => x.TierId == tier)))
        .Select(p => new { Promotion = p, Amount = Discount(p, price) }).Where(x => x.Amount > 0).OrderByDescending(x => x.Amount).ThenBy(x => x.Promotion.Id).Select(x => x.Promotion).FirstOrDefault();
    public async Task<(long? TierId, List<Promotion> Promotions, DateTime Now)> EligibleAsync(Guid? user, CancellationToken ct)
    {
        long? tier = null;
        if (user is { } id) tier = await db.CustomerProfiles.AsNoTracking().Where(p => p.UserId == id && p.User.IsActive && p.User.EmailConfirmed).Select(p => p.TierId).SingleOrDefaultAsync(ct);
        var now = Now;
        var promotions = await db.Promotions.AsNoTracking().Where(p => p.Status == PromotionStatus.Published && p.StartsAt <= now && p.EndsAt > now)
            .Include(p => p.Products).Include(p => p.CustomerTiers).AsSplitQuery().ToListAsync(ct);
        return (tier, promotions, now);
    }
    public static string Snapshot(Promotion p) => JsonSerializer.Serialize(new { version = 1, id = p.Id.ToString(), p.Name, type = p.DiscountType.ToString(), p.DiscountValue, p.MaxDiscountPerUnit, p.StartsAt, p.EndsAt, productScope = p.ProductScope.ToString(), audienceScope = p.AudienceScope.ToString(), productIds = p.Products.Select(x => x.ProductId.ToString()).Order().ToArray(), tierIds = p.CustomerTiers.Select(x => x.TierId.ToString()).Order().ToArray() });
    private static PromotionDto Dto(Promotion p) => new(p.Id.ToString(), p.Name, p.Description, p.Status.ToString(), p.ProductScope.ToString(), p.Products.Select(x => x.ProductId.ToString()).ToArray(), p.AudienceScope.ToString(), p.CustomerTiers.Select(x => x.TierId.ToString()).ToArray(), p.DiscountType.ToString(), p.DiscountValue, p.MaxDiscountPerUnit, p.StartsAt, p.EndsAt, Convert.ToBase64String(p.Version), p.Products.Select(x => new PromotionTargetDto(x.ProductId.ToString(), x.Product.Name)).ToArray());
    private IQueryable<Promotion> Query() => db.Promotions.Include(p => p.Products).ThenInclude(x => x.Product).Include(p => p.CustomerTiers).AsSplitQuery();
    public async Task<PromotionDto> ReadAsync(long id, CancellationToken ct) {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(db, false, ct);
        var result = Dto(await Query().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw Error(404, "NOT_FOUND")); await tx.CommitAsync(ct); return result;
    }
    public async Task<PagedResponse<PromotionDto>> ListAsync(PageQuery page, string? status, string? search, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(db, false, ct);
        var query = Query().AsNoTracking();
        if (status is not null) { if (!Enum.TryParse<PromotionStatus>(status, out var s) || s.ToString() != status) throw Error(400, "VALIDATION_ERROR"); query = query.Where(p => p.Status == s); }
        if (search is { Length: > 200 }) throw Error(400, "VALIDATION_ERROR");
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(p => p.Name.Contains(search.Trim()));
        var total = await query.LongCountAsync(ct); var rows = await query.OrderByDescending(p => p.Id).Skip((int)Math.Min(page.Offset, int.MaxValue)).Take(page.PageSize).ToListAsync(ct);
        var result = new PagedResponse<PromotionDto>(rows.Select(Dto).ToArray(), page.Page, page.PageSize, total); await tx.CommitAsync(ct); return result;
    }
    private static long[] Ids(string[] values) {
        if (values.Length > 1000 || values.Any(id => !ApiContract.TryParseId(id, out _)) || values.Distinct().Count() != values.Length) throw Error(400, "VALIDATION_ERROR");
        return values.Select(long.Parse).Order().ToArray();
    }
    public async Task<PromotionReceipt> WriteAsync(long? id, Guid actor, string? match, PromotionRequest? input, string action, Guid operation, CancellationToken ct)
    {
        if (operation == Guid.Empty) throw Error(400, "VALIDATION_ERROR");
        byte[] version = [];
        if (id is not null) { if (match is null) throw Error(428, "PRECONDITION_REQUIRED"); if (!ApiContract.TryParseETag(match, out version)) throw Error(400, "INVALID_ETAG"); }
        var productIds = input is null ? [] : Ids(input.ProductIds); var tierIds = input is null ? [] : Ids(input.TierIds);
        if (input is not null && (input.Name.Trim().Length is < 1 or > 200 || !Enum.TryParse<PromotionProductScope>(input.ProductScope, out var ps) || ps.ToString() != input.ProductScope
            || !Enum.TryParse<PromotionAudienceScope>(input.AudienceScope, out var audience) || audience.ToString() != input.AudienceScope || !Enum.TryParse<DiscountType>(input.DiscountType, out var type) || type.ToString() != input.DiscountType
            || input.DiscountValue <= 0 || input.DiscountValue > ApiContract.MaxSafeMoney || decimal.Round(input.DiscountValue, 2) != input.DiscountValue
            || type == DiscountType.Percent && input.DiscountValue > 100 || type == DiscountType.Fixed && (decimal.Truncate(input.DiscountValue) != input.DiscountValue || input.MaxDiscountPerUnit is not null)
            || input.MaxDiscountPerUnit is { } cap && (cap <= 0 || cap > ApiContract.MaxSafeMoney || decimal.Truncate(cap) != cap)
            || input.StartsAt.Kind != DateTimeKind.Utc || input.EndsAt.Kind != DateTimeKind.Utc || input.StartsAt.Ticks / 10000 >= input.EndsAt.Ticks / 10000
            || ps == PromotionProductScope.All && productIds.Length != 0 || ps == PromotionProductScope.Selected && productIds.Length == 0
            || audience == PromotionAudienceScope.All && tierIds.Length != 0 || audience == PromotionAudienceScope.SelectedTiers && tierIds.Length == 0)) throw Error(400, "VALIDATION_ERROR");
        var key = "Promotion.Operation:" + operation.ToString("D"); var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { id, actor, match, input, action })));
        await using var tx = await db.Database.BeginTransactionAsync(ct); await MembershipService.LockAsync(db, false, ct); await LockAsync(db, true, ct);
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'PhoneStore.Catalog.Write', @LockMode=N'Shared', @LockOwner=N'Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51004,'Catalog busy.',1;
            """, ct);
        var prior = await db.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(m => m.EventKey == key, ct);
        if (prior is not null) { using var json = JsonDocument.Parse(prior.PayloadJson); if (json.RootElement.GetProperty("hash").GetString() != hash) throw Error(409, "OPERATION_KEY_MISMATCH"); var receipt = json.RootElement.GetProperty("receipt").Deserialize<PromotionReceipt>()!; await tx.CommitAsync(ct); return receipt; }
        var p = id is null ? new Promotion { CreatedAt = Now } : await Query().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw Error(404, "NOT_FOUND");
        if (id is not null && !version.SequenceEqual(p.Version)) throw Error(412, "VERSION_CONFLICT");
        if (input is not null) {
            if (p.Status != PromotionStatus.Draft) throw Error(409, "PROMOTION_FROZEN");
            if (await db.Products.CountAsync(x => productIds.Contains(x.Id) && x.IsActive && x.Brand.IsActive && x.Category.IsActive, ct) != productIds.Length || await db.CustomerTiers.CountAsync(x => tierIds.Contains(x.Id), ct) != tierIds.Length) throw Error(400, "VALIDATION_ERROR");
            p.Name = input.Name.Trim(); p.Description = input.Description?.Trim(); p.ProductScope = Enum.Parse<PromotionProductScope>(input.ProductScope); p.AudienceScope = Enum.Parse<PromotionAudienceScope>(input.AudienceScope); p.DiscountType = Enum.Parse<DiscountType>(input.DiscountType); p.DiscountValue = input.DiscountValue; p.MaxDiscountPerUnit = input.MaxDiscountPerUnit;
            p.StartsAt = new DateTime(input.StartsAt.Ticks / 10000 * 10000, DateTimeKind.Utc); p.EndsAt = new DateTime(input.EndsAt.Ticks / 10000 * 10000, DateTimeKind.Utc);
            if (id is null) db.Promotions.Add(p);
            else { db.PromotionProducts.RemoveRange(p.Products); db.PromotionCustomerTiers.RemoveRange(p.CustomerTiers); await db.SaveChangesAsync(ct); p.Products.Clear(); p.CustomerTiers.Clear(); }
            foreach (var product in productIds) p.Products.Add(new() { ProductId = product }); foreach (var tier in tierIds) p.CustomerTiers.Add(new() { TierId = tier });
        } else if (action == "Publish") {
            if (p.Status != PromotionStatus.Draft || p.EndsAt <= Now || p.ProductScope == PromotionProductScope.Selected && !p.Products.Any() || p.AudienceScope == PromotionAudienceScope.SelectedTiers && !p.CustomerTiers.Any()) throw Error(409, "PROMOTION_INVALID");
            if (await db.Products.CountAsync(x => p.Products.Select(y => y.ProductId).Contains(x.Id) && x.IsActive && x.Brand.IsActive && x.Category.IsActive, ct) != p.Products.Count) throw Error(409, "PROMOTION_INVALID");
            p.Status = PromotionStatus.Published;
        } else if (action == "Disable") { if (p.Status != PromotionStatus.Published) throw Error(409, "PROMOTION_INVALID"); p.Status = PromotionStatus.Disabled; }
        else if (action == "Delete") { if (p.Status != PromotionStatus.Draft) throw Error(409, "PROMOTION_FROZEN"); db.PromotionProducts.RemoveRange(p.Products); db.PromotionCustomerTiers.RemoveRange(p.CustomerTiers); db.Promotions.Remove(p); }
        else throw Error(400, "VALIDATION_ERROR");
        p.UpdatedAt = Now; await db.SaveChangesAsync(ct);
        var result = new PromotionReceipt(p.Id.ToString(), action == "Delete" ? "Deleted" : p.Status.ToString(), Convert.ToBase64String(p.Version));
        db.AuditLogs.Add(new() { ActorUserId = actor, Action = "Promotion." + action, EntityType = "Promotion", EntityId = p.Id.ToString(), Summary = "sha256:" + hash, CorrelationId = key, CreatedAt = Now });
        db.OutboxMessages.Add(new() { Type = "PromotionOperation", EventKey = key, PayloadJson = JsonSerializer.Serialize(new { hash, receipt = result }), CreatedAt = Now, NextAttemptAt = Now, ProcessedAt = Now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
    }
    public async Task<IReadOnlyList<PromotionOfferDto>> OffersAsync(string[] ids, Guid? user, CancellationToken ct) {
        var variants = Ids(ids); if (variants.Length is < 1 or > 100) throw Error(400, "VALIDATION_ERROR");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockPricingAsync(user, ct); var eligible = await EligibleAsync(user, ct);
        var rows = await db.ProductVariants.AsNoTracking().Where(v => variants.Contains(v.Id) && v.IsActive && v.Product.IsActive && v.Product.Brand.IsActive && v.Product.Category.IsActive).Select(v => new { v.Id, v.ProductId, v.Price }).ToListAsync(ct);
        var result = rows.Select(v => { var p = Best(eligible.Promotions, v.ProductId, eligible.TierId, v.Price, eligible.Now); var discount = p is null ? 0 : Discount(p, v.Price); return new PromotionOfferDto(v.Id.ToString(), v.Price, discount, v.Price - discount, p?.Name, p?.EndsAt); }).ToArray(); await tx.CommitAsync(ct); return result;
    }
}
