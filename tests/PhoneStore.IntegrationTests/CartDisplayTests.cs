using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PhoneStore.Api.Entities;

namespace PhoneStore.IntegrationTests;
[Collection("SQL")]
public sealed class CartDisplayTests(SqlFixture sql)
{
    [Fact]
    public async Task Public_batch_has_names_selected_images_general_fallback_and_hides_inactive_catalog()
    {
        await using var db = sql.CreateContext(); var tag = Guid.NewGuid().ToString("N");
        var product = new Product { Name = "Display " + tag, Slug = "display-" + tag, Brand = new Brand { Name = tag, Slug = tag }, Category = new Category { Name = tag, Slug = tag } };
        var first = new ProductVariant { Product = product, Sku = "A-" + tag, Color = "Black", StorageGb = 128, RamGb = 8, Price = 1000000 };
        var second = new ProductVariant { Product = product, Sku = "B-" + tag, Color = "Blue", StorageGb = 256, RamGb = 12, Price = 2000000 };
        db.ProductVariants.AddRange(first, second); await db.SaveChangesAsync();
        var general = new ProductImage { Product = product, ImageUrl = "/api/v1/catalog-images/" + Guid.NewGuid().ToString("N") + ".png", AltText = "General", SortOrder = 0 };
        var specific = new ProductImage { Product = product, Variant = first, ImageUrl = "/api/v1/catalog-images/" + Guid.NewGuid().ToString("N") + ".png", AltText = "Black", SortOrder = 100 };
        db.ProductImages.AddRange(general, specific); await db.SaveChangesAsync();
        using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser();
        var url = $"/api/v1/catalog/variants?variantIds={first.Id}&variantIds={second.Id}&variantIds={first.Id}&variantIds=9223372036854775807";
        var result = await browser.GetFromJsonAsync<JsonElement>(url); Assert.Equal(2, result.GetArrayLength());
        var a = result.EnumerateArray().Single(x => x.GetProperty("variantId").GetString() == first.Id.ToString());
        var b = result.EnumerateArray().Single(x => x.GetProperty("variantId").GetString() == second.Id.ToString());
        Assert.Equal(product.Name, a.GetProperty("productName").GetString()); Assert.Equal("Black", a.GetProperty("color").GetString());
        Assert.Equal(specific.ImageUrl, a.GetProperty("imageUrl").GetString()); Assert.Equal(general.ImageUrl, b.GetProperty("imageUrl").GetString());
        Assert.False(a.TryGetProperty("price", out _)); Assert.False(a.TryGetProperty("version", out _));
        foreach (var target in new[] { "variant", "product", "brand", "category" })
        {
            first.IsActive = target != "variant"; product.IsActive = target != "product"; product.Brand.IsActive = target != "brand"; product.Category.IsActive = target != "category";
            await db.SaveChangesAsync(); var hidden = await browser.GetFromJsonAsync<JsonElement>($"/api/v1/catalog/variants?variantIds={first.Id}"); Assert.Equal(0, hidden.GetArrayLength());
        }
        first.IsActive = product.IsActive = product.Brand.IsActive = product.Category.IsActive = true;
        db.ProductImages.RemoveRange(general, specific); await db.SaveChangesAsync();
        var noImage = await browser.GetFromJsonAsync<JsonElement>($"/api/v1/catalog/variants?variantIds={first.Id}"); Assert.Equal(JsonValueKind.Null, noImage[0].GetProperty("imageUrl").ValueKind);
    }
    [Fact]
    public async Task Public_batch_rejects_invalid_ids_empty_or_over_limit_requests()
    {
        using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser();
        foreach (var value in new[] { "0", "01", "-1", "9223372036854775808", "oops" }) Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync("/api/v1/catalog/variants?variantIds=" + value)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync("/api/v1/catalog/variants")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync("/api/v1/catalog/variants?" + string.Join("&", Enumerable.Repeat("variantIds=1", 101)))).StatusCode);
    }
}
