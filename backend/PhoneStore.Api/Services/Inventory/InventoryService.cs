using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.Inventory;
using PhoneStore.Api.Entities;

namespace PhoneStore.Api.Services.InventoryManagement;

public sealed class InventoryService(AppDbContext db, TimeProvider clock)
{
    private const string ManualPrefix = "Inventory.Manual:";
    private static InventoryException Missing() => new(404, "NOT_FOUND", "Không tìm thấy tồn kho phiên bản.");
    private static IQueryable<InventoryDto> Project(IQueryable<Entities.Inventory> rows) => rows.Select(x => new InventoryDto(
        x.VariantId.ToString(), x.Variant.ProductId.ToString(), x.Variant.Product.Name, x.Variant.Sku, x.Variant.Color,
        x.Variant.StorageGb, x.Variant.RamGb, x.Variant.IsActive && x.Variant.Product.IsActive && x.Variant.Product.Brand.IsActive && x.Variant.Product.Category.IsActive,
        x.OnHand, x.Reserved, x.OnHand - x.Reserved, Convert.ToBase64String(x.Version)));
    private static MovementDto Movement(InventoryMovement x) => new(ApiContract.Id(x.Id), ApiContract.Id(x.VariantId), x.Kind.ToString(), x.OnHandDelta,
        x.ReservedDelta, x.Reason, x.ActorUserId?.ToString(), x.CreatedAt, x.EventKey.StartsWith(ManualPrefix, StringComparison.Ordinal) ? x.EventKey[ManualPrefix.Length..] : null);
    public async Task<InventoryDto> DetailAsync(long id, CancellationToken ct) => await Project(db.Inventory.AsNoTracking().Where(x => x.VariantId == id)).SingleOrDefaultAsync(ct) ?? throw Missing();
    public async Task<PagedResponse<InventoryDto>> ListAsync(InventoryQuery query, CancellationToken ct)
    {
        var rows = db.Inventory.AsNoTracking().AsQueryable();
        if (query.ProductId is not null) { var id = long.Parse(query.ProductId); rows = rows.Where(x => x.Variant.ProductId == id); }
        if (!string.IsNullOrWhiteSpace(query.Search)) { var text = query.Search.Trim(); rows = rows.Where(x => x.Variant.Sku.Contains(text) || x.Variant.Product.Name.Contains(text)); }
        var count = await rows.LongCountAsync(ct);
        var items = await Project(rows.OrderBy(x => x.Variant.Sku).ThenBy(x => x.VariantId).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)).ToListAsync(ct);
        return new(items, query.Page, query.PageSize, count);
    }
    public async Task<PagedResponse<MovementDto>> MovementsAsync(long id, InventoryQuery query, CancellationToken ct)
    {
        if (!await db.Inventory.AnyAsync(x => x.VariantId == id, ct)) throw Missing();
        var rows = db.InventoryMovements.AsNoTracking().Where(x => x.VariantId == id);
        var count = await rows.LongCountAsync(ct);
        var items = await rows.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(items.Select(Movement).ToList(), query.Page, query.PageSize, count);
    }
    public async Task<InventoryCommandResult> WriteAsync(long variantId, Guid actor, InventoryMovementKind kind, int delta, string reason, string operationKey, CancellationToken ct)
    {
        if (kind is not (InventoryMovementKind.Receive or InventoryMovementKind.Adjust) || delta == 0 || (kind == InventoryMovementKind.Receive && delta < 0)
            || !Guid.TryParseExact(operationKey, "D", out var key) || key == Guid.Empty || operationKey != key.ToString("D"))
            throw new InventoryException(400, "VALIDATION_ERROR", "Thao tác kho không hợp lệ.");
        var normalizedReason = reason.Trim().Normalize(NormalizationForm.FormC);
        if (normalizedReason.Length is 0 or > 300) throw new InventoryException(400, "VALIDATION_ERROR", "Cần lý do tối đa 300 ký tự.");
        var eventKey = ManualPrefix + operationKey;
        var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { domain = "inventory-manual-v1", actor, variantId, kind = kind.ToString(), delta, reason = normalizedReason }));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Same operation key across different variants/admins serializes before checking its payload.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={eventKey}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51003, 'Inventory operation lock unavailable.', 1;
            """, ct);
        var prior = await db.InventoryMovements.AsNoTracking().SingleOrDefaultAsync(x => x.EventKey == eventKey, ct);
        if (prior is not null && (prior.RequestHash is null || !CryptographicOperations.FixedTimeEquals(prior.RequestHash, hash)))
            throw new InventoryException(409, "OPERATION_KEY_CONFLICT", "Khóa thao tác đã dùng với nội dung khác.");
        // Row lock also protects arithmetic against future reservation/dispatch writers using this row.
        var balance = await db.Inventory.FromSqlInterpolated($"SELECT * FROM [Inventory] WITH (UPDLOCK, HOLDLOCK) WHERE [VariantId]={variantId}").SingleOrDefaultAsync(ct) ?? throw Missing();
        if (prior is not null)
        {
            var replay = new InventoryCommandResult(await DetailAsync(variantId, ct), Movement(prior), true);
            await transaction.CommitAsync(ct); return replay;
        }
        var next = (long)balance.OnHand + delta;
        if (next < balance.Reserved) throw new InventoryException(409, "INSUFFICIENT_AVAILABLE", "Không thể giảm kho quá lượng khả dụng.");
        if (next > int.MaxValue) throw new InventoryException(409, "INVENTORY_LIMIT", "Số lượng kho vượt giới hạn.");
        balance.OnHand = (int)next;
        var movement = new InventoryMovement { VariantId = variantId, ActorUserId = actor, Kind = kind, OnHandDelta = delta,
            ReservedDelta = 0, Reason = normalizedReason, EventKey = eventKey, RequestHash = hash, CreatedAt = clock.GetUtcNow().UtcDateTime };
        db.InventoryMovements.Add(movement);
        await db.SaveChangesAsync(ct);
        var result = new InventoryCommandResult(await DetailAsync(variantId, ct), Movement(movement), false);
        await transaction.CommitAsync(ct); return result;
    }
}
