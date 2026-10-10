using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhoneStore.Api.DTOs.Catalog;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.Services.Catalog;

namespace PhoneStore.Api.Controllers;

public sealed class CatalogImageRequest
{
    [Required] public IFormFile File { get; init; } = null!;
    public Guid OperationKey { get; init; }
    public string? VariantId { get; init; }
    [Required, MaxLength(200)] public string AltText { get; init; } = "";
    [Range(0, int.MaxValue)] public int SortOrder { get; init; }
}
[ApiController, Authorize(Policy = "Admin"), Route("api/v1/admin"), ServiceFilter(typeof(CatalogExceptionFilter))]
public sealed class AdminCatalogController(CatalogService catalog, ICatalogImageStore images, ILogger<AdminCatalogController> logger) : ControllerBase
{
    private static long Id(string value) => ApiContract.TryParseId(value, out var id) ? id : throw new CatalogException(404, "NOT_FOUND", "Không tìm thấy dữ liệu.");
    private string? Match => Request.Headers.IfMatch.Count == 0 ? null : Request.Headers.IfMatch.ToString();
    private IActionResult Versioned(AdminProductDto value, bool created = false)
    {
        Response.Headers.ETag = ApiContract.ETag(Convert.FromBase64String(value.Version));
        return created ? Created("/api/v1/admin/products/" + value.Id, value) : Ok(value);
    }
    private IActionResult Versioned(AdminVariantDto value, bool created = false)
    {
        Response.Headers.ETag = ApiContract.ETag(Convert.FromBase64String(value.Version));
        return created ? Created("/api/v1/admin/variants/" + value.Id, value) : Ok(value);
    }
    [HttpGet("brands")] public async Task<IActionResult> Brands(CancellationToken ct) => Ok(await catalog.BrandsAsync(true, ct));
    [HttpGet("categories")] public async Task<IActionResult> Categories(CancellationToken ct) => Ok(await catalog.CategoriesAsync(true, ct));
    [HttpPost("brands")] public async Task<IActionResult> Brand(LookupRequest request, CancellationToken ct) => StatusCode(201, await catalog.WriteLookupAsync(true, null, request, ct));
    [HttpPatch("brands/{id}")] public async Task<IActionResult> Brand(string id, LookupRequest request, CancellationToken ct) => Ok(await catalog.WriteLookupAsync(true, Id(id), request, ct));
    [HttpPost("categories")] public async Task<IActionResult> Category(LookupRequest request, CancellationToken ct) => StatusCode(201, await catalog.WriteLookupAsync(false, null, request, ct));
    [HttpPatch("categories/{id}")] public async Task<IActionResult> Category(string id, LookupRequest request, CancellationToken ct) => Ok(await catalog.WriteLookupAsync(false, Id(id), request, ct));
    [HttpGet("products")] public async Task<IActionResult> Products([FromQuery] CatalogQuery query, CancellationToken ct) => Ok(await catalog.AdminProductsAsync(query, ct));
    [HttpGet("products/{id}")] public async Task<IActionResult> Product(string id, CancellationToken ct) => Versioned(await catalog.AdminProductAsync(Id(id), ct));
    [HttpPost("products")] public async Task<IActionResult> Product(ProductRequest request, CancellationToken ct) => Versioned(await catalog.WriteProductAsync(null, request, null, ct), true);
    [HttpPatch("products/{id}")] public async Task<IActionResult> Product(string id, ProductRequest request, CancellationToken ct) => Versioned(await catalog.WriteProductAsync(Id(id), request, Match, ct));
    [HttpGet("variants/{id}")] public async Task<IActionResult> Variant(string id, CancellationToken ct) => Versioned(await catalog.AdminVariantAsync(Id(id), ct));
    [HttpPost("products/{id}/variants")] public async Task<IActionResult> CreateVariant(string id, VariantRequest request, CancellationToken ct) => Versioned(await catalog.WriteVariantAsync(null, Id(id), request, null, ct), true);
    [HttpPatch("variants/{id}")] public async Task<IActionResult> UpdateVariant(string id, VariantRequest request, CancellationToken ct) => Versioned(await catalog.WriteVariantAsync(Id(id), null, request, Match, ct));
    [HttpPost("products/{id}/images"), RequestSizeLimit(CatalogImageInput.MaxBytes + 65536)]
    public async Task<IActionResult> Upload(string id, [FromForm] CatalogImageRequest request, CancellationToken ct)
    {
        var productId = Id(id);
        if (request.OperationKey == Guid.Empty) throw new CatalogException(400, "VALIDATION_ERROR", "Cần mã yêu cầu tải ảnh hợp lệ.");
        if (Request.Form.Keys.Any(key => !new[] { "variantId", "altText", "sortOrder", "operationKey" }.Contains(key, StringComparer.OrdinalIgnoreCase)) || Request.Form.Files.Count != 1)
            throw new CatalogException(400, "VALIDATION_ERROR", "Chỉ nhận file, variantId, altText và sortOrder.");
        long? variantId = request.VariantId is null ? null : ApiContract.TryParseId(request.VariantId, out var parsed) ? parsed : throw new CatalogException(400, "INVALID_VARIANT", "VariantId không hợp lệ.");
        if (request.File.Length > (images.UsesCloudinary ? CatalogImageInput.MaxBytes : CatalogPng.MaxBytes)) throw new CatalogException(413, "IMAGE_TOO_LARGE", "Ảnh vượt dung lượng cho phép.");
        if (!images.UsesCloudinary && (!string.Equals(request.File.ContentType, "image/png", StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetExtension(request.File.FileName), ".png", StringComparison.OrdinalIgnoreCase)))
            throw new CatalogException(400, "INVALID_IMAGE", "Chỉ nhận file PNG.");
        await using var buffer = new MemoryStream(); await request.File.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { productId, variantId, altText = request.AltText.Trim(), request.SortOrder, contentType = request.File.ContentType.ToLowerInvariant(), fileHash = Convert.ToHexString(SHA256.HashData(bytes)) }));
        if (images.UsesCloudinary) CatalogImageInput.Validate(request.File.FileName, request.File.ContentType, bytes);
        else bytes = CatalogPng.Validate(bytes);
        var image = await catalog.UploadAsync(productId, variantId, request.AltText, request.SortOrder, bytes, images, logger, Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), request.OperationKey, hash, ct);
        return Created(image.ImageUrl, image);
    }
    [HttpGet("products/{id}/image-uploads/{operationKey:guid}")]
    public async Task<IActionResult> UploadResult(string id, Guid operationKey, CancellationToken ct)
        => Ok(await catalog.ImageUploadResultAsync(Id(id), Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), operationKey, ct));
    [HttpDelete("product-images/{id}")]
    public async Task<IActionResult> DeleteImage(string id, CancellationToken ct) { await catalog.DeleteImageAsync(Id(id), images, logger, ct); return NoContent(); }
}
