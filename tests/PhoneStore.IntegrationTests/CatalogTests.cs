using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Auth;
using PhoneStore.Api.Services.Catalog;

namespace PhoneStore.IntegrationTests;

[Collection("SQL")]
public sealed class CatalogTests(SqlFixture sql)
{
    private sealed class Host : IDisposable
    {
        public AuthFactory Auth { get; }
        public WebApplicationFactory<Program> Server { get; }
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "PhoneStore_Test_Images_" + Guid.NewGuid().ToString("N"));
        public Host(string connection, string environment = "Development", bool failImageSave = false)
        {
            Auth = new AuthFactory(connection, environment);
            Server = Auth.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Catalog:ImagePath"] = DirectoryPath, ["Logging:LogLevel:Default"] = "Warning" }))
                .ConfigureTestServices(services => { if (failImageSave) services.AddSingleton<ICatalogImageStore>(provider => new FailingStore(new LocalCatalogImageStore(provider.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>(), provider.GetRequiredService<IConfiguration>()))); }));
        }
        public HttpClient Browser() => Server.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false });
        public void Dispose()
        {
            Server.Dispose(); Auth.Dispose();
            var expected = Path.GetFullPath(DirectoryPath);
            if (Path.GetDirectoryName(expected) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) || !Path.GetFileName(expected).StartsWith("PhoneStore_Test_Images_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test cleanup path.");
            if (Directory.Exists(expected)) Directory.Delete(expected, true);
        }
    }
    private sealed class FailingStore(ICatalogImageStore inner) : ICatalogImageStore
    {
        public bool IsConfigured => true;
        public async Task SaveAsync(string name, byte[] bytes, CancellationToken ct) { await inner.SaveAsync(name, bytes, ct); throw new IOException("synthetic failure"); }
        public Task<byte[]?> ReadAsync(string name, CancellationToken ct) => inner.ReadAsync(name, ct);
        public Task DeleteAsync(string name) => inner.DeleteAsync(name);
    }
    private static async Task<HttpResponseMessage> Command(HttpClient browser, HttpMethod method, string route, object? body = null, string? etag = null)
    {
        var token = (await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
        using var request = new HttpRequestMessage(method, "/api/v1/" + route) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token); if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await browser.SendAsync(request);
    }
    private static async Task<HttpClient> Account(Host host, bool admin = true)
    {
        var browser = host.Browser(); var email = Guid.NewGuid().ToString("N") + "@example.invalid";
        Assert.Equal(HttpStatusCode.Accepted, (await Command(browser, HttpMethod.Post, "auth/register", new { email, fullName = "Catalog test", password = "Synthetic!Password123" })).StatusCode);
        var mail = host.Auth.Mailbox.Messages.Single(x => x.Email == email);
        Assert.Equal(HttpStatusCode.NoContent, (await Command(browser, HttpMethod.Post, "auth/verify-email", new { mail.UserId, mail.Token })).StatusCode);
        if (admin) { using var scope = host.Server.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<AuthBootstrap>().RunAsync(email, default); }
        Assert.Equal(HttpStatusCode.NoContent, (await Command(browser, HttpMethod.Post, "auth/login", new { email, password = "Synthetic!Password123" })).StatusCode);
        return browser;
    }
    private static string Id(JsonElement dto) => dto.GetProperty("id").GetString()!;
    private static string Version(JsonElement dto) => '"' + dto.GetProperty("version").GetString()! + '"';
    private static async Task<JsonElement> Created(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static object Product(string brand, string category, string slug, bool active = true, string? name = null) => new { brandId = brand, categoryId = category, name = name ?? "Synthetic " + slug, slug, description = "Text <script> remains data", specificationsJson = "{\"screen\":\"synthetic\"}", isActive = active };
    private static object Variant(string sku, string color = "Black", int storage = 128, decimal price = 100, bool active = true) => new { sku, color, storageGb = storage, ramGb = 8, price, isActive = active };
    private static async Task<(JsonElement Product, string Brand, string Category, string Slug)> Catalog(HttpClient admin, string? brand = null, string? category = null)
    {
        var slug = "test-" + Guid.NewGuid().ToString("N");
        brand ??= Id(await Created(await Command(admin, HttpMethod.Post, "admin/brands", new { name = slug, slug })));
        category ??= Id(await Created(await Command(admin, HttpMethod.Post, "admin/categories", new { name = slug, slug })));
        return (await Created(await Command(admin, HttpMethod.Post, "admin/products", Product(brand, category, slug))), brand, category, slug);
    }
    private static async Task<JsonElement> AddVariant(HttpClient admin, JsonElement product, string? sku = null, string color = "Black", int storage = 128, decimal price = 100) =>
        await Created(await Command(admin, HttpMethod.Post, "admin/products/" + Id(product) + "/variants", Variant(sku ?? Guid.NewGuid().ToString("N"), color, storage, price)));
    private static async Task<HttpResponseMessage> Upload(HttpClient browser, string productId, byte[] bytes, string? variantId = null, string filename = "image.png", string contentType = "image/png")
    {
        var token = (await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
        using var form = new MultipartFormDataContent(); var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", filename); form.Add(new StringContent("Synthetic alt"), "altText"); form.Add(new StringContent("0"), "sortOrder");
        if (variantId is not null) form.Add(new StringContent(variantId), "variantId");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/products/" + productId + "/images") { Content = form };
        request.Headers.Add("X-CSRF-TOKEN", token); return await browser.SendAsync(request);
    }
    [Fact]
    public async Task AnonymousAndCustomerCannotReadAdminOrWriteCatalogAndCsrfIsRequired()
    {
        using var host = new Host(sql.ConnectionString); using var admin = await Account(host); using var customer = await Account(host, false); using var anonymous = host.Browser();
        foreach (var browser in new[] { anonymous, customer })
        {
            var status = browser == anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            Assert.Equal(status, (await browser.GetAsync("/api/v1/admin/products")).StatusCode);
            foreach (var route in new[] { "admin/brands", "admin/categories", "admin/products", "admin/products/1/variants", "admin/products/1/images" })
                Assert.Equal(status, (await Command(browser, HttpMethod.Post, route, new { })).StatusCode);
            Assert.Equal(status, (await Command(browser, HttpMethod.Delete, "admin/product-images/1")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/v1/admin/brands", new { name = "No CSRF", slug = "no-csrf" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/v1/brands")).StatusCode);
    }
    [Fact]
    public async Task DuplicateSkuAndNormalizedVariantAreRejectedIncludingConcurrentCreates()
    {
        using var host = new Host(sql.ConnectionString); using var admin = await Account(host);
        var first = await Catalog(admin); var second = await Catalog(admin); var sku = Guid.NewGuid().ToString("N");
        var responses = await Task.WhenAll(Command(admin, HttpMethod.Post, "admin/products/" + Id(first.Product) + "/variants", Variant(sku.ToLowerInvariant())), Command(admin, HttpMethod.Post, "admin/products/" + Id(second.Product) + "/variants", Variant(sku.ToUpperInvariant())));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var db = sql.CreateContext(); var saved = await db.ProductVariants.SingleAsync(x => x.Sku == sku.ToUpperInvariant());
        Assert.Equal(0, (await db.Inventory.SingleAsync(x => x.VariantId == saved.Id)).OnHand);
        Assert.Equal(HttpStatusCode.Conflict, (await Command(admin, HttpMethod.Post, "admin/products/" + saved.ProductId + "/variants", Variant(Guid.NewGuid().ToString("N"), "  black  "))).StatusCode);
        Assert.Equal(1, await db.ProductVariants.CountAsync(x => x.ProductId == saved.ProductId));
    }
    [Fact]
    public async Task PublicFiltersUseOneVariantAndPagingDoesNotExposeInternalInventory()
    {
        using var host = new Host(sql.ConnectionString); using var admin = await Account(host); using var publicClient = host.Browser();
        var catalog = await Catalog(admin); await AddVariant(admin, catalog.Product); var red = await AddVariant(admin, catalog.Product, color: "Red", storage: 256, price: 200);
        await using (var db = sql.CreateContext()) { var balance = await db.Inventory.SingleAsync(x => x.VariantId == long.Parse(Id(red))); balance.OnHand = 5; balance.Reserved = 2; await db.SaveChangesAsync(); }
        var route = "/api/v1/products?brandId=" + catalog.Brand;
        var mismatch = await publicClient.GetFromJsonAsync<JsonElement>(route + "&maxPrice=150&storageGb=256"); Assert.Equal(0, mismatch.GetProperty("totalCount").GetInt32());
        var match = await publicClient.GetFromJsonAsync<JsonElement>(route + "&minPrice=150&storageGb=256&ramGb=8&inStock=true&pageSize=1"); Assert.Equal(1, match.GetProperty("totalCount").GetInt32()); Assert.Single(match.GetProperty("items").EnumerateArray());
        Assert.Equal(200, match.GetProperty("items")[0].GetProperty("minPrice").GetDecimal());
        var second = await Catalog(admin, catalog.Brand, catalog.Category); await AddVariant(admin, second.Product, price: 20);
        var page1 = await publicClient.GetFromJsonAsync<JsonElement>(route + "&pageSize=1&sort=priceAsc"); var page2 = await publicClient.GetFromJsonAsync<JsonElement>(route + "&pageSize=1&sort=priceAsc&page=2");
        Assert.Equal(2, page1.GetProperty("totalCount").GetInt32()); Assert.Equal(Id(second.Product), Id(page1.GetProperty("items")[0])); Assert.Equal(Id(catalog.Product), Id(page2.GetProperty("items")[0]));
        var detail = await publicClient.GetFromJsonAsync<JsonElement>("/api/v1/products/" + catalog.Slug); Assert.Equal(3, detail.GetProperty("variants")[1].GetProperty("available").GetInt32());
        Assert.DoesNotContain("onHand", detail.GetRawText()); Assert.DoesNotContain("reserved", detail.GetRawText()); Assert.DoesNotContain("version", detail.GetRawText());
        foreach (var invalid in new[] { "page=0", "pageSize=101", "page=2147483647&pageSize=100", "brandId=01", "minPrice=10&maxPrice=5", "minPrice=0.5", "sort=sql-injection" }) Assert.Equal(HttpStatusCode.BadRequest, (await publicClient.GetAsync("/api/v1/products?" + invalid)).StatusCode);
    }
    [Fact]
    public async Task RowversionPreventsLostUpdatesOnProductAndVariant()
    {
        using var host = new Host(sql.ConnectionString); using var admin = await Account(host); var catalog = await Catalog(admin); var variant = await AddVariant(admin, catalog.Product);
        var route = "admin/variants/" + Id(variant); var sku = variant.GetProperty("sku").GetString()!;
        Assert.Equal((HttpStatusCode)428, (await Command(admin, HttpMethod.Patch, route, Variant(sku))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Command(admin, HttpMethod.Patch, route, Variant(sku), "*")).StatusCode);
        var writes = await Task.WhenAll(Command(admin, HttpMethod.Patch, route, Variant(sku, price: 200), Version(variant)), Command(admin, HttpMethod.Patch, route, Variant(sku, price: 300), Version(variant)));
        Assert.Single(writes, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(writes, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        var current = await admin.GetAsync("/api/v1/" + route); Assert.NotEqual(Version(variant), current.Headers.ETag!.ToString());
        var productRoute = "admin/products/" + Id(catalog.Product);
        var updates = await Task.WhenAll(Command(admin, HttpMethod.Patch, productRoute, Product(catalog.Brand, catalog.Category, catalog.Slug, name: "First change"), Version(catalog.Product)), Command(admin, HttpMethod.Patch, productRoute, Product(catalog.Brand, catalog.Category, catalog.Slug, name: "Second change"), Version(catalog.Product)));
        Assert.Single(updates, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(updates, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
    }
    [Fact]
    public async Task ParentVisibilityAndInputConstraintsApplyToAllPublicReads()
    {
        using var host = new Host(sql.ConnectionString); using var admin = await Account(host); using var publicClient = host.Browser(); var catalog = await Catalog(admin); await AddVariant(admin, catalog.Product);
        Assert.Equal(HttpStatusCode.BadRequest, (await Command(admin, HttpMethod.Post, "admin/products", new { brandId = catalog.Brand, categoryId = catalog.Category, name = "bad", slug = "invalid", specificationsJson = "<script>" })).StatusCode);
        foreach (var price in new[] { 1.2m, -1m, 9007199254740992m }) Assert.Equal(HttpStatusCode.BadRequest, (await Command(admin, HttpMethod.Post, "admin/products/" + Id(catalog.Product) + "/variants", Variant("INVALIDPRICE", price: price))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Command(admin, HttpMethod.Post, "admin/products/" + Id(catalog.Product) + "/variants", new { sku = "NO-STOCK-PAYLOAD", color = "Blue", storageGb = 512, ramGb = 8, price = 0, onHand = 999, reserved = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Command(admin, HttpMethod.Patch, "admin/brands/" + catalog.Brand, new { name = catalog.Slug, slug = catalog.Slug, isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync("/api/v1/products/" + catalog.Slug)).StatusCode);
        var list = await publicClient.GetFromJsonAsync<JsonElement>("/api/v1/products?brandId=" + catalog.Brand); Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await Command(admin, HttpMethod.Post, "admin/products", Product(catalog.Brand, catalog.Category, "another-" + Guid.NewGuid().ToString("N")))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Command(admin, HttpMethod.Post, "admin/products/" + Id(catalog.Product) + "/variants", Variant("HIDDEN-" + Guid.NewGuid().ToString("N"), "Blue"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/admin/products/" + Id(catalog.Product))).StatusCode);
        var publicBrands = await publicClient.GetFromJsonAsync<JsonElement[]>("/api/v1/brands"); Assert.DoesNotContain(publicBrands!, x => Id(x) == catalog.Brand);
        Assert.Equal(HttpStatusCode.OK, (await Command(admin, HttpMethod.Patch, "admin/brands/" + catalog.Brand, new { name = catalog.Slug, slug = catalog.Slug, isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Command(admin, HttpMethod.Patch, "admin/categories/" + catalog.Category, new { name = catalog.Slug, slug = catalog.Slug, isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync("/api/v1/products/" + catalog.Slug)).StatusCode);
    }
    [Fact]
    public async Task UpdatingAndHidingCatalogPreservesOrderSnapshotsAndHasNoHardDelete()
    {
        using var host = new Host(sql.ConnectionString); using var admin = await Account(host); var catalog = await Catalog(admin); var variant = await AddVariant(admin, catalog.Product);
        await using var db = sql.CreateContext(); var order = new Order { OrderNumber = Guid.NewGuid().ToString("N")[..30], CustomerEmail = "snapshot@example.invalid", NormalizedCustomerEmail = "SNAPSHOT@EXAMPLE.INVALID", RecipientName = "Test", Phone = "0", AddressLine = "Synthetic", Province = "Test" };
        var item = new OrderItem { Order = order, VariantId = long.Parse(Id(variant)), ProductNameSnapshot = "Historical name", SkuSnapshot = "HISTORICAL-SKU", VariantSnapshot = "Historical variant", Quantity = 1, UnitPrice = 100 };
        db.OrderItems.Add(item); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK, (await Command(admin, HttpMethod.Patch, "admin/variants/" + Id(variant), Variant("NEW-" + Guid.NewGuid().ToString("N"), price: 900, active: false), Version(variant))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Command(admin, HttpMethod.Patch, "admin/products/" + Id(catalog.Product), Product(catalog.Brand, catalog.Category, catalog.Slug, false, "New catalog name"), Version(catalog.Product))).StatusCode);
        await db.Entry(item).ReloadAsync(); Assert.Equal("Historical name", item.ProductNameSnapshot); Assert.Equal("HISTORICAL-SKU", item.SkuSnapshot); Assert.Equal(100, item.UnitPrice);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Command(admin, HttpMethod.Delete, "admin/products/" + Id(catalog.Product))).StatusCode);
        Assert.True(await db.ProductVariants.AnyAsync(x => x.Id == item.VariantId));
    }
    [Fact]
    public async Task ManagedImagesAreValidatedBelongToVariantAndRespectVisibilityAndDeletion()
    {
        using var host = new Host(sql.ConnectionString); using var admin = await Account(host); using var publicClient = host.Browser();
        var catalog = await Catalog(admin); var variant = await AddVariant(admin, catalog.Product); var other = await Catalog(admin); var otherVariant = await AddVariant(admin, other.Product);
        await AddVariant(admin, catalog.Product, color: "Blue", storage: 256);
        var png = Png();
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(admin, Id(catalog.Product), png, Id(otherVariant))).StatusCode);
        Assert.False(Directory.Exists(host.DirectoryPath));
        foreach (var bad in new[] { "<svg onload='alert(1)'/>"u8.ToArray(), png.Concat("<script>"u8.ToArray()).ToArray(), png[..^1], new byte[] { 137, 80, 78, 71 } }) Assert.Equal(HttpStatusCode.BadRequest, (await Upload(admin, Id(catalog.Product), bad)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(admin, Id(catalog.Product), png, filename: "script.svg")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(admin, Id(catalog.Product), png, contentType: "image/jpeg")).StatusCode);
        Assert.Equal((HttpStatusCode)413, (await Upload(admin, Id(catalog.Product), new byte[CatalogPng.MaxBytes + 1])).StatusCode);
        var image = await Created(await Upload(admin, Id(catalog.Product), png, Id(variant), "../../evil.png"));
        var url = image.GetProperty("imageUrl").GetString()!; Assert.DoesNotContain("evil", url); Assert.Single(Directory.GetFiles(host.DirectoryPath));
        var response = await publicClient.GetAsync(url); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType); Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
        Assert.Equal(HttpStatusCode.OK, (await Command(admin, HttpMethod.Patch, "admin/variants/" + Id(variant), Variant(variant.GetProperty("sku").GetString()!, active: false), Version(variant))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync(url)).StatusCode); Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
        var visible = await publicClient.GetFromJsonAsync<JsonElement>("/api/v1/products/" + catalog.Slug); Assert.Single(visible.GetProperty("variants").EnumerateArray()); Assert.Empty(visible.GetProperty("images").EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await Command(admin, HttpMethod.Delete, "admin/product-images/" + Id(image))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(url)).StatusCode); Assert.Empty(Directory.GetFiles(host.DirectoryPath));
        var common = await Created(await Upload(admin, Id(catalog.Product), png)); var commonUrl = common.GetProperty("imageUrl").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync(commonUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Command(admin, HttpMethod.Patch, "admin/products/" + Id(catalog.Product), Product(catalog.Brand, catalog.Category, catalog.Slug, false), Version(catalog.Product))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync(commonUrl)).StatusCode);
    }
    [Fact]
    public async Task FailedStorageWriteRollsBackMetadataAndRemovesPartialFile()
    {
        using var host = new Host(sql.ConnectionString, failImageSave: true); using var admin = await Account(host); var catalog = await Catalog(admin);
        var result = await Upload(admin, Id(catalog.Product), Png()); Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        await using var db = sql.CreateContext(); Assert.False(await db.ProductImages.AnyAsync(x => x.ProductId == long.Parse(Id(catalog.Product))));
        Assert.Empty(Directory.GetFiles(host.DirectoryPath));
    }
    [Fact]
    public async Task PngDecoderRejectsCorruptionBombsAndStripsAncillaryPayloads()
    {
        var withMetadata = Png(metadata: true); Assert.True(CatalogPng.Validate(withMetadata).Length < withMetadata.Length);
        var damaged = Png(); damaged[damaged.Length - 1] ^= 1; Assert.Throws<CatalogException>(() => CatalogPng.Validate(damaged));
        Assert.Throws<CatalogException>(() => CatalogPng.Validate(Png(width: 4096)));
        Assert.Throws<CatalogException>(() => CatalogPng.Validate(Png(extraRaster: true)));
        Assert.Throws<CatalogException>(() => CatalogPng.Validate(Png(filter: 5)));
        using var host = new Host(sql.ConnectionString, "Testing"); using var admin = await Account(host); var catalog = await Catalog(admin);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Upload(admin, Id(catalog.Product), Png())).StatusCode);
    }
    private static byte[] Png(int width = 1, bool metadata = false, bool extraRaster = false, byte filter = 0)
    {
        using var output = new MemoryStream(); output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), 1); header[8] = 8; header[9] = 6;
        Chunk(output, "IHDR"u8.ToArray(), header);
        if (metadata) Chunk(output, "tEXt"u8.ToArray(), "Comment\0<script>synthetic</script>"u8.ToArray());
        using var compressed = new MemoryStream(); using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true)) zlib.Write(new byte[] { filter, 1, 2, 3, 255 }.Concat(extraRaster ? new byte[100] : []).ToArray());
        Chunk(output, "IDAT"u8.ToArray(), compressed.ToArray()); Chunk(output, "IEND"u8.ToArray(), []); return output.ToArray();
    }
    private static void Chunk(Stream stream, byte[] type, byte[] data)
    {
        var length = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length); stream.Write(length); stream.Write(type); stream.Write(data);
        var crc = uint.MaxValue; foreach (var value in type.Concat(data)) { crc ^= value; for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u); }
        BinaryPrimitives.WriteUInt32BigEndian(length, ~crc); stream.Write(length);
    }
}
