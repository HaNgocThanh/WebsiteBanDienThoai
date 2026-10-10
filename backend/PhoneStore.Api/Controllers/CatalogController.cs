using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using System.Security.Cryptography;
using PhoneStore.Api.DTOs.Catalog;
using PhoneStore.Api.Services.Catalog;

namespace PhoneStore.Api.Controllers;

[ApiController, AllowAnonymous, Route("api/v1"), ServiceFilter(typeof(CatalogExceptionFilter))]
public sealed class CatalogController(CatalogService catalog, ICatalogImageStore images) : ControllerBase
{
    [HttpGet("brands")] public async Task<IActionResult> Brands(CancellationToken ct) => Ok(await catalog.BrandsAsync(false, ct));
    [HttpGet("categories")] public async Task<IActionResult> Categories(CancellationToken ct) => Ok(await catalog.CategoriesAsync(false, ct));
    [HttpGet("products")] public async Task<IActionResult> Products([FromQuery] CatalogQuery query, CancellationToken ct) => Ok(await catalog.ProductsAsync(query, ct));
    [HttpGet("products/{slug}")] public async Task<IActionResult> Product(string slug, CancellationToken ct) => Ok(await catalog.ProductAsync(slug, ct));
    [HttpGet("catalog/variants")] public async Task<IActionResult> CartVariants([FromQuery] string[] variantIds, CancellationToken ct) => Ok(await catalog.CartVariantsAsync(variantIds, ct));
    [HttpGet("catalog-images/{name}")]
    public async Task<IActionResult> Image(string name, [FromQuery] bool download, CancellationToken ct, [FromQuery] string? size = null)
    {
        var preset = size switch { null or "original" => CatalogImageSize.Original, "thumbnail" => CatalogImageSize.Thumbnail, "card" => CatalogImageSize.Card, "display" => CatalogImageSize.Display, _ => throw new CatalogException(400, "INVALID_IMAGE_SIZE", "Kích thước ảnh không hợp lệ.") };
        var content = await catalog.ImageAsync(name, User.IsInRole("Admin"), images, ct, download ? CatalogImageSize.Original : preset);
        Response.Headers.XContentTypeOptions = "nosniff";
        // Revalidate authorization/visibility even when the browser already has the bytes.
        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers.Vary = "Cookie";
        var tag = new EntityTagHeaderValue('"' + Convert.ToHexString(SHA256.HashData(content.Bytes)) + '"');
        return File(content.Bytes, content.ContentType, download ? name : null, lastModified: null, entityTag: tag);
    }
}
