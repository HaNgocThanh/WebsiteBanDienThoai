using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    public async Task<IActionResult> Image(string name, CancellationToken ct)
    {
        var bytes = await catalog.ImageAsync(name, User.IsInRole("Admin"), images, ct);
        Response.Headers.XContentTypeOptions = "nosniff"; return File(bytes, "image/png");
    }
}
