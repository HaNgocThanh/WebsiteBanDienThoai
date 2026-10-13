using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace PhoneStore.Api.DTOs.Promotions;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PromotionRequest
{
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [StringLength(1000)] public string? Description { get; init; }
    [Required] public string ProductScope { get; init; } = "All";
    [Required, MaxLength(1000)] public string[] ProductIds { get; init; } = [];
    [Required] public string AudienceScope { get; init; } = "All";
    [Required, MaxLength(1000)] public string[] TierIds { get; init; } = [];
    [Required] public string DiscountType { get; init; } = "Percent";
    public decimal DiscountValue { get; init; }
    public decimal? MaxDiscountPerUnit { get; init; }
    public DateTime StartsAt { get; init; }
    public DateTime EndsAt { get; init; }
    public Guid OperationKey { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PromotionAction(Guid OperationKey);
public sealed record PromotionDto(string Id, string Name, string? Description, string Status, string ProductScope, IReadOnlyList<string> ProductIds,
    string AudienceScope, IReadOnlyList<string> TierIds, string DiscountType, decimal DiscountValue, decimal? MaxDiscountPerUnit, DateTime StartsAt, DateTime EndsAt, string Version, IReadOnlyList<PromotionTargetDto> Products);
public sealed record PromotionTargetDto(string Id, string Name);
public sealed record PromotionReceipt(string Id, string Status, string Version);
public sealed record PromotionOfferDto(string VariantId, decimal UnitPrice, decimal UnitDiscount, decimal FinalPrice, string? PromotionName, DateTime? EndsAt);
