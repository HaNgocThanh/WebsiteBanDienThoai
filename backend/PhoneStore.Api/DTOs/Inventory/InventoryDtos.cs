using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PhoneStore.Api.DTOs.Common;

namespace PhoneStore.Api.DTOs.Inventory;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReceiptRequest([Range(1, int.MaxValue)] int Quantity, [Required, MaxLength(300)] string Reason,
    [Required, RegularExpression("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")] string OperationKey);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdjustmentRequest(int QuantityDelta, [Required, MaxLength(300)] string Reason,
    [Required, RegularExpression("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")] string OperationKey) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (QuantityDelta == 0) yield return new("Số lượng điều chỉnh phải khác 0.", [nameof(QuantityDelta)]);
    }
}
public sealed class InventoryQuery : IValidatableObject
{
    [MaxLength(200)] public string? Search { get; init; }
    public string? ProductId { get; init; }
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (ProductId is not null && !ApiContract.TryParseId(ProductId, out _)) yield return new("ProductId không hợp lệ.", [nameof(ProductId)]);
        if (((long)Page - 1) * PageSize > int.MaxValue) yield return new("Trang vượt giới hạn.", [nameof(Page)]);
    }
}
public sealed record InventoryDto(string VariantId, string ProductId, string ProductName, string Sku, string Color, int StorageGb, int RamGb,
    bool IsActive, int OnHand, int Reserved, int Available, string Version);
public sealed record MovementDto(string Id, string VariantId, string Kind, int OnHandDelta, int ReservedDelta,
    string Reason, string? ActorUserId, DateTime CreatedAt, string? OperationKey);
public sealed record InventoryCommandResult(InventoryDto Inventory, MovementDto Movement, bool IsReplay);
