using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhoneStore.Api.DTOs.Common;

namespace PhoneStore.Api.DTOs.Catalog;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LookupRequest([Required, MaxLength(100)] string Name,
    [Required, MaxLength(150), RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$")] string Slug, bool IsActive = true);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductRequest([Required] string BrandId, [Required] string CategoryId,
    [Required, MaxLength(200)] string Name,
    [Required, MaxLength(220), RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$")] string Slug,
    [MaxLength(20000)] string Description = "", [MaxLength(20000)] string? SpecificationsJson = null, bool IsActive = true) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!ApiContract.TryParseId(BrandId, out _)) yield return new("BrandId không hợp lệ.", [nameof(BrandId)]);
        if (!ApiContract.TryParseId(CategoryId, out _)) yield return new("CategoryId không hợp lệ.", [nameof(CategoryId)]);
        if (SpecificationsJson is not null && !ValidJson(SpecificationsJson)) yield return new("Thông số phải là JSON object hợp lệ.", [nameof(SpecificationsJson)]);
    }
    private static bool ValidJson(string value)
    {
        try { using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 16 }); return document.RootElement.ValueKind == JsonValueKind.Object; }
        catch (JsonException) { return false; }
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VariantRequest(
    [Required, MaxLength(64), RegularExpression("^[A-Za-z0-9][A-Za-z0-9._-]*$")] string Sku,
    [Required, MaxLength(50)] string Color, [Range(1, 65536)] int StorageGb, [Range(1, 1024)] int RamGb,
    [Range(typeof(decimal), "0", "9007199254740991")] decimal Price, bool IsActive = true) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (decimal.Truncate(Price) != Price) yield return new("Giá VND phải là số nguyên.", [nameof(Price)]);
    }
}
public sealed class CatalogQuery : IValidatableObject
{
    [MaxLength(200)] public string? Search { get; init; }
    public string? BrandId { get; init; }
    public string? CategoryId { get; init; }
    [Range(typeof(decimal), "0", "9007199254740991")] public decimal? MinPrice { get; init; }
    [Range(typeof(decimal), "0", "9007199254740991")] public decimal? MaxPrice { get; init; }
    [Range(1, 65536)] public int? StorageGb { get; init; }
    [Range(1, 1024)] public int? RamGb { get; init; }
    public bool? InStock { get; init; }
    public bool? IsActive { get; init; }
    [RegularExpression("^(newest|name|priceAsc|priceDesc)$")] public string Sort { get; init; } = "newest";
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (BrandId is not null && !ApiContract.TryParseId(BrandId, out _)) yield return new("BrandId không hợp lệ.", [nameof(BrandId)]);
        if (CategoryId is not null && !ApiContract.TryParseId(CategoryId, out _)) yield return new("CategoryId không hợp lệ.", [nameof(CategoryId)]);
        if (MinPrice > MaxPrice || MinPrice is { } min && decimal.Truncate(min) != min || MaxPrice is { } max && decimal.Truncate(max) != max) yield return new("Khoảng giá không hợp lệ.", [nameof(MinPrice), nameof(MaxPrice)]);
        if (((long)Page - 1) * PageSize > int.MaxValue) yield return new("Trang vượt giới hạn.", [nameof(Page)]);
    }
}
public sealed record LookupDto(string Id, string Name, string Slug, bool IsActive);
public sealed record ImageDto(string Id, string? VariantId, string ImageUrl, string AltText, int SortOrder);
public sealed record PublicVariantDto(string Id, string Sku, string Color, int StorageGb, int RamGb, decimal Price, int Available);
public sealed record CartVariantDto(string VariantId, string ProductName, string ProductSlug, string Sku, string Color,
    int StorageGb, int RamGb, string? ImageUrl, string? ImageAltText);
public sealed record AdminVariantDto(string Id, string ProductId, string Sku, string Color, int StorageGb, int RamGb, decimal Price, bool IsActive, string Version);
public sealed record PublicProductDto(string Id, string Name, string Slug, string Description, string? SpecificationsJson,
    LookupDto Brand, LookupDto Category, IReadOnlyList<PublicVariantDto> Variants, IReadOnlyList<ImageDto> Images);
public sealed record AdminProductDto(string Id, string BrandId, string CategoryId, string Name, string Slug, string Description,
    string? SpecificationsJson, bool IsActive, string Version, IReadOnlyList<AdminVariantDto> Variants, IReadOnlyList<ImageDto> Images);
public sealed record ProductSummaryDto(string Id, string Name, string Slug, string BrandId, string CategoryId, decimal? MinPrice, string? ImageUrl);
public sealed record AdminProductSummaryDto(string Id, string Name, string Slug, string BrandId, string CategoryId, bool IsActive, string Version);
