using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.DTOs.Catalog;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.Entities;

namespace PhoneStore.Api.Services.Catalog;

public sealed partial class CatalogService(AppDbContext db)
{
    private static CatalogException Missing() => new(404, "NOT_FOUND", "Không tìm thấy dữ liệu.");
    private static CatalogException Inactive() => new(409, "INACTIVE_PARENT", "Hãng, danh mục hoặc sản phẩm đã bị ẩn.");
    private async Task LockAsync(CancellationToken ct) => await db.Database.ExecuteSqlRawAsync("""
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource=N'PhoneStore.Catalog.Write', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
        IF @result < 0 THROW 51002, 'Catalog write lock unavailable.', 1;
        """, ct);
    public Task<List<LookupDto>> BrandsAsync(bool admin, CancellationToken ct) => db.Brands.AsNoTracking().Where(x => admin || x.IsActive)
        .OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new LookupDto(x.Id.ToString(), x.Name, x.Slug, x.IsActive)).ToListAsync(ct);
    public Task<List<LookupDto>> CategoriesAsync(bool admin, CancellationToken ct) => db.Categories.AsNoTracking().Where(x => admin || x.IsActive)
        .OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new LookupDto(x.Id.ToString(), x.Name, x.Slug, x.IsActive)).ToListAsync(ct);
    public async Task<LookupDto> WriteLookupAsync(bool brand, long? id, LookupRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct); await LockAsync(ct);
        LookupDto result;
        if (brand)
        {
            var value = id is null ? new Brand() : await db.Brands.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
            value.Name = request.Name.Trim().Normalize(NormalizationForm.FormC); value.Slug = request.Slug; value.IsActive = request.IsActive;
            if (id is null) db.Brands.Add(value); await db.SaveChangesAsync(ct);
            result = new(ApiContract.Id(value.Id), value.Name, value.Slug, value.IsActive);
        }
        else
        {
            var value = id is null ? new Category() : await db.Categories.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
            value.Name = request.Name.Trim().Normalize(NormalizationForm.FormC); value.Slug = request.Slug; value.IsActive = request.IsActive;
            if (id is null) db.Categories.Add(value); await db.SaveChangesAsync(ct);
            result = new(ApiContract.Id(value.Id), value.Name, value.Slug, value.IsActive);
        }
        await transaction.CommitAsync(ct); return result;
    }
    private IQueryable<Product> VisibleProducts() => db.Products.AsNoTracking().Where(x => x.IsActive && x.Brand.IsActive && x.Category.IsActive && x.Variants.Any(v => v.IsActive));
    public async Task<IReadOnlyList<CartVariantDto>> CartVariantsAsync(string[] variantIds, CancellationToken ct)
    {
        if (variantIds.Length is < 1 or > 100 || variantIds.Any(id => !ApiContract.TryParseId(id, out _)))
            throw new CatalogException(400, "VALIDATION_ERROR", "Cần từ 1 đến 100 mã phiên bản hợp lệ.");
        var ids = variantIds.Select(id => long.Parse(id, System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToArray();
        var rows = await db.ProductVariants.AsNoTracking().Where(v => ids.Contains(v.Id) && v.IsActive && v.Product.IsActive && v.Product.Brand.IsActive && v.Product.Category.IsActive)
            .OrderBy(v => v.Id).Select(v => new {
                v.Id, ProductName = v.Product.Name, ProductSlug = v.Product.Slug, v.Sku, v.Color, v.StorageGb, v.RamGb,
                Image = v.Product.Images.Where(i => i.VariantId == v.Id || i.VariantId == null)
                    .OrderBy(i => i.VariantId == v.Id ? 0 : 1).ThenBy(i => i.SortOrder).ThenBy(i => i.Id)
                    .Select(i => new { i.ImageUrl, i.AltText }).FirstOrDefault()
            }).ToListAsync(ct);
        return rows.Select(v => new CartVariantDto(ApiContract.Id(v.Id), v.ProductName, v.ProductSlug, v.Sku, v.Color, v.StorageGb, v.RamGb, v.Image?.ImageUrl, v.Image?.AltText)).ToArray();
    }
    private IQueryable<ProductVariant> MatchingVariants(CatalogQuery query, bool admin) => db.ProductVariants.AsNoTracking().Where(v => (admin || v.IsActive)
        && (query.MinPrice == null || v.Price >= query.MinPrice) && (query.MaxPrice == null || v.Price <= query.MaxPrice)
        && (query.StorageGb == null || v.StorageGb == query.StorageGb) && (query.RamGb == null || v.RamGb == query.RamGb)
        && (query.InStock == null || (v.Inventory != null && v.Inventory.OnHand > v.Inventory.Reserved) == query.InStock));
    private IQueryable<Product> Filter(CatalogQuery query, bool admin)
    {
        var products = admin ? db.Products.AsNoTracking().AsQueryable() : VisibleProducts();
        if (admin && query.IsActive is { } active) products = products.Where(x => x.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Search)) { var search = query.Search.Trim(); products = products.Where(x => x.Name.Contains(search)); }
        if (query.BrandId is { } brand) { var id = long.Parse(brand); products = products.Where(x => x.BrandId == id); }
        if (query.CategoryId is { } category) { var id = long.Parse(category); products = products.Where(x => x.CategoryId == id); }
        if (!admin || query.MinPrice is not null || query.MaxPrice is not null || query.StorageGb is not null || query.RamGb is not null || query.InStock is not null)
        {
            var matching = MatchingVariants(query, admin);
            products = products.Where(x => matching.Any(v => v.ProductId == x.Id));
        }
        return products;
    }
    private IOrderedQueryable<Product> Sort(IQueryable<Product> products, CatalogQuery query, bool admin)
    {
        var matching = MatchingVariants(query, admin);
        return query.Sort switch
        {
        "name" => products.OrderBy(x => x.Name).ThenBy(x => x.Id),
        "priceAsc" => products.OrderBy(x => matching.Where(v => v.ProductId == x.Id).Min(v => (decimal?)v.Price)).ThenBy(x => x.Id),
        "priceDesc" => products.OrderByDescending(x => matching.Where(v => v.ProductId == x.Id).Min(v => (decimal?)v.Price)).ThenBy(x => x.Id),
        _ => products.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        };
    }
    public async Task<PagedResponse<ProductSummaryDto>> ProductsAsync(CatalogQuery query, CancellationToken ct)
    {
        var filtered = Filter(query, false); var count = await filtered.LongCountAsync(ct); var matching = MatchingVariants(query, false);
        var rows = await Sort(filtered, query, false).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new ProductSummaryDto(x.Id.ToString(), x.Name, x.Slug, x.BrandId.ToString(), x.CategoryId.ToString(),
                matching.Where(v => v.ProductId == x.Id).Min(v => (decimal?)v.Price),
                x.Images.Where(i => i.VariantId == null || i.Variant!.IsActive).OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => i.ImageUrl).FirstOrDefault())).ToListAsync(ct);
        foreach (var row in rows) if (row.MinPrice is { } price) ApiContract.Money(price);
        return new(rows, query.Page, query.PageSize, count);
    }
    public async Task<PagedResponse<AdminProductSummaryDto>> AdminProductsAsync(CatalogQuery query, CancellationToken ct)
    {
        var filtered = Filter(query, true); var count = await filtered.LongCountAsync(ct);
        var rows = await Sort(filtered, query, true).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new { x.Id, x.Name, x.Slug, x.BrandId, x.CategoryId, x.IsActive, x.Version }).ToListAsync(ct);
        return new(rows.Select(x => new AdminProductSummaryDto(ApiContract.Id(x.Id), x.Name, x.Slug, ApiContract.Id(x.BrandId), ApiContract.Id(x.CategoryId), x.IsActive, Convert.ToBase64String(x.Version))).ToList(), query.Page, query.PageSize, count);
    }
    public async Task<PublicProductDto> ProductAsync(string slug, CancellationToken ct)
    {
        var value = await VisibleProducts().Where(x => x.Slug == slug).Select(x => new { x.Id, x.Name, x.Slug, x.Description, x.SpecificationsJson,
            Brand = new LookupDto(x.BrandId.ToString(), x.Brand.Name, x.Brand.Slug, x.Brand.IsActive),
            Category = new LookupDto(x.CategoryId.ToString(), x.Category.Name, x.Category.Slug, x.Category.IsActive) }).SingleOrDefaultAsync(ct) ?? throw Missing();
        var variants = await db.ProductVariants.AsNoTracking().Where(x => x.ProductId == value.Id && x.IsActive).OrderBy(x => x.Id)
            .Select(x => new PublicVariantDto(x.Id.ToString(), x.Sku, x.Color, x.StorageGb, x.RamGb, x.Price, x.Inventory == null ? 0 : x.Inventory.OnHand - x.Inventory.Reserved)).ToListAsync(ct);
        foreach (var variant in variants) ApiContract.Money(variant.Price);
        return new(ApiContract.Id(value.Id), value.Name, value.Slug, value.Description, value.SpecificationsJson, value.Brand, value.Category, variants, await ImagesAsync(value.Id, false, ct));
    }
    private Task<List<ImageDto>> ImagesAsync(long id, bool admin, CancellationToken ct) => db.ProductImages.AsNoTracking().Where(x => x.ProductId == id && (admin || x.VariantId == null || x.Variant!.IsActive))
        .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => new ImageDto(x.Id.ToString(), x.VariantId == null ? null : x.VariantId.Value.ToString(), x.ImageUrl, x.AltText, x.SortOrder)).ToListAsync(ct);
    public async Task<AdminProductDto> AdminProductAsync(long id, CancellationToken ct)
    {
        var x = await db.Products.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Id, x.BrandId, x.CategoryId, x.Name, x.Slug, x.Description, x.SpecificationsJson, x.IsActive, x.Version }).SingleOrDefaultAsync(ct) ?? throw Missing();
        var variants = await db.ProductVariants.AsNoTracking().Where(v => v.ProductId == id).OrderBy(v => v.Id).Select(v => new { v.Id, v.ProductId, v.Sku, v.Color, v.StorageGb, v.RamGb, v.Price, v.IsActive, v.Version }).ToListAsync(ct);
        return new(ApiContract.Id(x.Id), ApiContract.Id(x.BrandId), ApiContract.Id(x.CategoryId), x.Name, x.Slug, x.Description, x.SpecificationsJson, x.IsActive, Convert.ToBase64String(x.Version),
            variants.Select(v => new AdminVariantDto(ApiContract.Id(v.Id), ApiContract.Id(v.ProductId), v.Sku, v.Color, v.StorageGb, v.RamGb, ApiContract.Money(v.Price), v.IsActive, Convert.ToBase64String(v.Version))).ToList(), await ImagesAsync(id, true, ct));
    }
    public async Task<AdminVariantDto> AdminVariantAsync(long id, CancellationToken ct)
    {
        var v = await db.ProductVariants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        return new(ApiContract.Id(v.Id), ApiContract.Id(v.ProductId), v.Sku, v.Color, v.StorageGb, v.RamGb, ApiContract.Money(v.Price), v.IsActive, Convert.ToBase64String(v.Version));
    }
    private static byte[] Match(string? header, byte[] actual)
    {
        if (string.IsNullOrEmpty(header)) throw new CatalogException(428, "PRECONDITION_REQUIRED", "Cần If-Match từ phiên bản hiện tại.");
        if (!ApiContract.TryParseETag(header, out var version)) throw new CatalogException(400, "INVALID_ETAG", "If-Match không hợp lệ.");
        if (!version.SequenceEqual(actual)) throw new CatalogException(412, "VERSION_MISMATCH", "Dữ liệu đã thay đổi. Vui lòng tải lại.");
        return version;
    }
    public async Task<AdminProductDto> WriteProductAsync(long? id, ProductRequest request, string? etag, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct); await LockAsync(ct);
        var product = id is null ? new Product() : await db.Products.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        if (id is not null) db.Entry(product).Property(x => x.Version).OriginalValue = Match(etag, product.Version);
        var brandId = long.Parse(request.BrandId); var categoryId = long.Parse(request.CategoryId);
        var brand = await db.Brands.SingleOrDefaultAsync(x => x.Id == brandId, ct) ?? throw new CatalogException(400, "INVALID_REFERENCE", "Hãng không tồn tại.");
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == categoryId, ct) ?? throw new CatalogException(400, "INVALID_REFERENCE", "Danh mục không tồn tại.");
        if (request.IsActive && (!brand.IsActive || !category.IsActive)) throw Inactive();
        product.BrandId = brandId; product.CategoryId = categoryId; product.Name = request.Name.Trim(); product.Slug = request.Slug;
        product.Description = request.Description ?? ""; product.SpecificationsJson = request.SpecificationsJson; product.IsActive = request.IsActive;
        if (id is null) db.Products.Add(product);
        await db.SaveChangesAsync(ct); var result = await AdminProductAsync(product.Id, ct); await transaction.CommitAsync(ct); return result;
    }
    public async Task<AdminVariantDto> WriteVariantAsync(long? id, long? productId, VariantRequest request, string? etag, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct); await LockAsync(ct);
        var variant = id is null ? new ProductVariant { ProductId = productId!.Value } : await db.ProductVariants.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        if (id is not null) db.Entry(variant).Property(x => x.Version).OriginalValue = Match(etag, variant.Version);
        var parent = await db.Products.Where(x => x.Id == variant.ProductId).Select(x => new { x.IsActive, BrandActive = x.Brand.IsActive, CategoryActive = x.Category.IsActive }).SingleOrDefaultAsync(ct) ?? throw Missing();
        if (request.IsActive && (!parent.IsActive || !parent.BrandActive || !parent.CategoryActive)) throw Inactive();
        variant.Sku = request.Sku.ToUpperInvariant(); variant.Color = Regex.Replace(request.Color.Trim().Normalize(NormalizationForm.FormC), @"\s+", " ");
        variant.StorageGb = request.StorageGb; variant.RamGb = request.RamGb; variant.Price = ApiContract.Money(request.Price); variant.IsActive = request.IsActive;
        if (id is null) { variant.Inventory = new Inventory(); db.ProductVariants.Add(variant); }
        await db.SaveChangesAsync(ct); var result = await AdminVariantAsync(variant.Id, ct); await transaction.CommitAsync(ct); return result;
    }
}
