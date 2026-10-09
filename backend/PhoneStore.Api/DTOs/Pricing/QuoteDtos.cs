using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PhoneStore.Api.DTOs.Common;

namespace PhoneStore.Api.DTOs.Pricing;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteItemRequest([Required] string VariantId, [Range(1, int.MaxValue)] int Quantity) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!ApiContract.TryParseId(VariantId, out _)) yield return new("Mã phiên bản không hợp lệ.", [nameof(VariantId)]);
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteAddress([Required, MaxLength(100)] string Province, [Required, RegularExpression("^VN$")] string CountryCode,
    [MaxLength(150)] string? RecipientName = null, [MaxLength(30)] string? Phone = null, [MaxLength(300)] string? AddressLine = null,
    [RegularExpression("^[0-9]{2}$")] string? ProvinceCode = null, [RegularExpression("^[0-9]{5}$")] string? WardCode = null, [MaxLength(150)] string? Locality = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteRequest([Required, MinLength(1), MaxLength(100)] List<QuoteItemRequest> Items,
    [Required] QuoteAddress ShippingAddress, [RegularExpression("^[0-9a-f]{64}$")] string? QuoteHash = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (QuoteHash is { Length: 0 }) yield return new("QuoteHash không hợp lệ.", [nameof(QuoteHash)]);
    }
}
public sealed record QuoteLine(string VariantId, int Quantity, string ProductName, string ProductSlug, string Sku,
    string Color, int StorageGb, int RamGb, int Available, decimal UnitPrice, decimal UnitDiscount, decimal LineTotal, string? PromotionName);
public sealed record QuoteDto(IReadOnlyList<QuoteLine> Items, decimal Subtotal, decimal DiscountTotal, decimal ShippingFee,
    decimal GrandTotal, string Currency, string QuoteHash);
