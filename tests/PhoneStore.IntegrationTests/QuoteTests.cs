using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PhoneStore.Api.Entities;

namespace PhoneStore.IntegrationTests;
[Collection("SQL")]
public sealed class QuoteTests(SqlFixture sql)
{
    private async Task<long> Variant(decimal price = 1000000, int onHand = 10, int reserved = 2)
    {
        await using var db = sql.CreateContext(); var tag = Guid.NewGuid().ToString("N");
        var variant = new ProductVariant { Sku = "QUOTE-" + tag, Color = "Black", StorageGb = 128, RamGb = 8, Price = price,
            Product = new Product { Name = "Quote " + tag, Slug = "quote-" + tag, Brand = new Brand { Name = tag, Slug = tag }, Category = new Category { Name = tag, Slug = tag } }, Inventory = new Inventory { OnHand = onHand, Reserved = reserved } };
        db.ProductVariants.Add(variant); await db.SaveChangesAsync(); return variant.Id;
    }
    private static object Body(long id, string province = "TP. Hồ Chí Minh", int quantity = 2, string? hash = null) => new { items = new[] { new { variantId = id.ToString(), quantity } }, shippingAddress = new { province, countryCode = "VN" }, quoteHash = hash };
    [Fact]
    public async Task Public_locations_are_complete_and_coded_quote_rejects_mismatched_hierarchy_or_names()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser();
        var catalog = await browser.GetFromJsonAsync<JsonElement>("/api/v1/locations/provinces"); Assert.Equal("2026-10-09", catalog.GetProperty("asOf").GetString()); Assert.Equal(34, catalog.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/api/v1/locations/provinces/00/wards")).StatusCode);
        var total = 0;
        foreach (var p in catalog.GetProperty("items").EnumerateArray())
        {
            var code = p.GetProperty("code").GetString(); var list = await browser.GetFromJsonAsync<JsonElement>($"/api/v1/locations/provinces/{code}/wards");
            var wards = list.GetProperty("items"); Assert.True(wards.GetArrayLength() > 0); total += wards.GetArrayLength();
            foreach (var w in wards.EnumerateArray()) Assert.Equal(code, w.GetProperty("provinceCode").GetString());
        }
        Assert.Equal(3321, total);
        var hcm = await browser.GetFromJsonAsync<JsonElement>("/api/v1/locations/provinces/79/wards"); var ward = hcm.GetProperty("items")[0];
        object Payload(string code, string name, string? line = "Synthetic street", string? wardCode = null) => new { items = new[] { new { variantId = id.ToString(), quantity = 1 } }, shippingAddress = new { provinceCode = code, province = name, wardCode = wardCode ?? ward.GetProperty("code").GetString(), locality = ward.GetProperty("name").GetString(), addressLine = line, countryCode = "VN" } };
        var valid = await Send(browser, Payload("79", "Thành phố Hồ Chí Minh")); Assert.Equal(HttpStatusCode.OK, valid.StatusCode); Assert.Equal(0, (await valid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("shippingFee").GetInt32());
        foreach (var body in new[] { Payload("48", "Thành phố Đà Nẵng"), Payload("79", "HCM"), Payload("79", "Thành phố Hồ Chí Minh", " "), Payload("79", "Thành phố Hồ Chí Minh", wardCode: "99999"), Payload("", "Thành phố Hồ Chí Minh") })
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(browser, body)).StatusCode);
        await using var db = sql.CreateContext(); Assert.False(await db.InventoryMovements.AnyAsync(x => x.VariantId == id));
    }
    private static async Task<HttpResponseMessage> Send(HttpClient browser, object body)
    {
        var token = (await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/quote") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token); return await browser.SendAsync(request);
    }
    private static async Task<HttpResponseMessage> Auth(HttpClient browser, string route, object body)
    {
        var token = (await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/" + route) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", token); return await browser.SendAsync(request);
    }
    [Fact]
    public async Task Guest_quote_has_server_prices_fees_canonical_merge_and_never_reserves()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser();
        var response = await Send(browser, Body(id)); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        var quote = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(2000000m, quote.GetProperty("grandTotal").GetDecimal()); Assert.Equal(0, quote.GetProperty("discountTotal").GetDecimal());
        Assert.Equal(8, quote.GetProperty("items")[0].GetProperty("available").GetInt32()); Assert.Equal(id.ToString(), quote.GetProperty("items")[0].GetProperty("variantId").GetString());
        var merged = await Send(browser, new { items = new[] { new { variantId = id.ToString(), quantity = 1 }, new { variantId = id.ToString(), quantity = 1 } }, shippingAddress = new { province = "thành phố hồ chí minh", countryCode = "VN" } });
        var mergedQuote = await merged.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(HttpStatusCode.OK, merged.StatusCode); Assert.Equal(quote.GetProperty("quoteHash").GetString(), mergedQuote.GetProperty("quoteHash").GetString());
        var elsewhere = await (await Send(browser, Body(id, "Đà Nẵng"))).Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(30000m, elsewhere.GetProperty("shippingFee").GetDecimal()); Assert.Equal(2030000m, elsewhere.GetProperty("grandTotal").GetDecimal());
        await using var db = sql.CreateContext(); var balance = await db.Inventory.SingleAsync(x => x.VariantId == id); Assert.Equal(10, balance.OnHand); Assert.Equal(2, balance.Reserved); Assert.False(await db.InventoryMovements.AnyAsync(x => x.VariantId == id)); Assert.False(await db.StockReservations.AnyAsync(x => x.OrderItem.VariantId == id));
    }
    [Fact]
    public async Task Payload_price_tier_fee_owner_and_nested_tampering_is_rejected()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser();
        foreach (var field in new[] { "unitPrice", "discount", "tier", "shippingFee", "userId", "grandTotal" })
        {
            var body = new Dictionary<string, object> { ["items"] = new[] { new { variantId = id.ToString(), quantity = 1 } }, ["shippingAddress"] = new { province = "Đà Nẵng", countryCode = "VN" }, [field] = 0 };
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(browser, body)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(browser, new { items = new[] { new { variantId = id.ToString(), quantity = 1, unitPrice = 1 } }, shippingAddress = new { province = "HCM", countryCode = "VN" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsJsonAsync("/api/v1/checkout/quote", Body(id))).StatusCode);
        var valid = await (await Send(browser, Body(id, "Đà Nẵng", 1))).Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(1030000m, valid.GetProperty("grandTotal").GetDecimal());
    }
    [Fact]
    public async Task Invalid_quantity_ids_empty_cart_address_and_aggregate_are_blocked()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser();
        foreach (var qty in new[] { 0, -1 }) Assert.Equal(HttpStatusCode.BadRequest, (await Send(browser, Body(id, quantity: qty))).StatusCode);
        foreach (var variantId in new[] { "01", "0", "-1", "9223372036854775808" }) Assert.Equal(HttpStatusCode.BadRequest, (await Send(browser, new { items = new[] { new { variantId, quantity = 1 } }, shippingAddress = new { province = "HCM", countryCode = "VN" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(browser, new { items = Array.Empty<object>(), shippingAddress = new { province = "HCM", countryCode = "VN" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(browser, new { items = new[] { new { variantId = id.ToString(), quantity = 1 } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(browser, Body(id, hash: ""))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(browser, new { items = new[] { new { variantId = id.ToString(), quantity = int.MaxValue }, new { variantId = id.ToString(), quantity = 1 } }, shippingAddress = new { province = "HCM", countryCode = "VN" } })).StatusCode);
    }
    [Fact]
    public async Task Authenticated_customer_has_same_server_prices_and_quote_rate_limit_is_enforced()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser();
        var email = Guid.NewGuid().ToString("N") + "@example.invalid";
        Assert.Equal(HttpStatusCode.Accepted, (await Auth(browser, "register", new { email, fullName = "Synthetic quote customer", password = "Synthetic!Password123" })).StatusCode);
        var mail = host.Mailbox.Messages.Single(x => x.Email == email);
        Assert.Equal(HttpStatusCode.NoContent, (await Auth(browser, "verify-email", new { mail.UserId, mail.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Auth(browser, "login", new { email, password = "Synthetic!Password123" })).StatusCode);
        var quote = await (await Send(browser, Body(id))).Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(2000000m, quote.GetProperty("grandTotal").GetDecimal()); Assert.Equal(0m, quote.GetProperty("discountTotal").GetDecimal()); Assert.False(quote.TryGetProperty("userId", out _));
        using var limited = host.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:RateLimit:quote"] = "1" })));
        using var guest = limited.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await Send(guest, Body(id))).StatusCode);
        var throttled = await Send(guest, Body(id)); Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode); Assert.Equal("RATE_LIMITED", (await throttled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()); Assert.True(throttled.Headers.Contains("Retry-After"));
    }
    [Fact]
    public async Task Price_or_fee_changes_require_fresh_confirmation_hash_and_same_quote_is_stable()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser();
        var initial = await (await Send(browser, Body(id))).Content.ReadFromJsonAsync<JsonElement>(); var hash = initial.GetProperty("quoteHash").GetString();
        Assert.Equal(HttpStatusCode.OK, (await Send(browser, Body(id, hash: hash))).StatusCode);
        await using (var db = sql.CreateContext()) { var variant = await db.ProductVariants.SingleAsync(x => x.Id == id); variant.Price = 1500000; await db.SaveChangesAsync(); }
        var stale = await Send(browser, Body(id, hash: hash)); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.Equal("PRICE_CHANGED", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var fresh = await (await Send(browser, Body(id))).Content.ReadFromJsonAsync<JsonElement>(); Assert.NotEqual(hash, fresh.GetProperty("quoteHash").GetString()); Assert.Equal(3000000m, fresh.GetProperty("grandTotal").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await Send(browser, Body(id, hash: fresh.GetProperty("quoteHash").GetString()))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(browser, Body(id, "Hà Nội", hash: fresh.GetProperty("quoteHash").GetString()))).StatusCode);
    }
    [Fact]
    public async Task Inactive_catalog_missing_variant_and_insufficient_available_never_quote()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser();
        var insufficient = await Send(browser, Body(id, quantity: 9)); Assert.Equal(HttpStatusCode.Conflict, insufficient.StatusCode); Assert.Equal("OUT_OF_STOCK", (await insufficient.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(browser, Body(long.MaxValue))).StatusCode);
        foreach (var target in new[] { "variant", "product", "brand", "category" })
        {
            await using var db = sql.CreateContext(); var v = await db.ProductVariants.Include(x => x.Product.Brand).Include(x => x.Product.Category).SingleAsync(x => x.Id == id);
            v.IsActive = target != "variant"; v.Product.IsActive = target != "product"; v.Product.Brand.IsActive = target != "brand"; v.Product.Category.IsActive = target != "category"; await db.SaveChangesAsync();
            var hidden = await Send(browser, Body(id)); Assert.Equal(HttpStatusCode.Conflict, hidden.StatusCode); Assert.Equal("VARIANT_UNAVAILABLE", (await hidden.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
    }
    [Fact]
    public async Task Money_limit_invalid_fee_and_concurrent_quotes_are_safe()
    {
        var id = await Variant(); using var host = new AuthFactory(sql.ConnectionString); using var browser = host.Browser();
        var token = (await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ => { using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkout/quote") { Content = JsonContent.Create(Body(id)) }; req.Headers.Add("X-CSRF-TOKEN", token); return await browser.SendAsync(req); }));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode)); foreach (var response in results) response.Dispose();
        await using (var db = sql.CreateContext()) { var v = await db.ProductVariants.SingleAsync(x => x.Id == id); v.Price = 9007199254740991m; await db.SaveChangesAsync(); }
        var excessive = await Send(browser, Body(id)); Assert.Equal(HttpStatusCode.BadRequest, excessive.StatusCode); Assert.Equal("QUOTE_LIMIT", (await excessive.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        using var missing = host.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Pricing:HoChiMinhShippingFeeVnd"] = "-1" })));
        using var invalidClient = missing.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var invalid = await Send(invalidClient, Body(id)); Assert.Equal(HttpStatusCode.ServiceUnavailable, invalid.StatusCode); Assert.Equal("SHIPPING_UNAVAILABLE", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await using var verify = sql.CreateContext(); Assert.Equal(2, (await verify.Inventory.SingleAsync(x => x.VariantId == id)).Reserved); Assert.False(await verify.InventoryMovements.AnyAsync(x => x.VariantId == id));
    }
}
