using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Entities;

namespace PhoneStore.IntegrationTests;

[Collection("SQL")]
public sealed class ProfileTests(SqlFixture sql)
{
    private static object Address(string name, bool isDefault = true) => new { recipientName = name, phone = "0000000000", addressLine = "Synthetic street", locality = "Synthetic ward", province = "Synthetic province", countryCode = "vn", isDefault };
    private static async Task<HttpResponseMessage> Send(HttpClient browser, HttpMethod method, string route, object? body = null)
    {
        using var request = new HttpRequestMessage(method, "/api/v1/" + route) { Content = body is null ? null : JsonContent.Create(body) };
        var token = (await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await browser.SendAsync(request);
    }
    private static async Task<(HttpClient Browser, Guid Id)> Customer(AuthFactory factory)
    {
        var email = Guid.NewGuid().ToString("N") + "@example.invalid";
        var browser = factory.Browser();
        Assert.Equal(HttpStatusCode.Accepted, (await Send(browser, HttpMethod.Post, "auth/register", new { email, password = "Synthetic!Password123", fullName = "Test profile" })).StatusCode);
        var mail = factory.Mailbox.Messages.Single(x => x.Email == email);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(browser, HttpMethod.Post, "auth/verify-email", new { mail.UserId, mail.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(browser, HttpMethod.Post, "auth/login", new { email, password = "Synthetic!Password123" })).StatusCode);
        return (browser, mail.UserId);
    }
    private static async Task<string> Create(HttpClient browser, string name)
    {
        var result = await Send(browser, HttpMethod.Post, "me/addresses", Address(name));
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        var dto = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VN", dto.GetProperty("countryCode").GetString());
        return dto.GetProperty("id").GetString()!;
    }
    [Fact]
    public async Task ProfileEmailIsImmutableAndOwnerComesOnlyFromSession()
    {
        using var factory = new AuthFactory(sql.ConnectionString);
        var (browser, id) = await Customer(factory); using var owner = browser;
        var before = await owner.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Equal(id.ToString(), before.GetProperty("userId").GetString());
        Assert.Equal("Bronze", before.GetProperty("tierCode").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, HttpMethod.Patch, "me", new { fullName = "Changed", phone = "123", email = "other@example.invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(owner, HttpMethod.Patch, "me", new { fullName = "Changed name", phone = "123" })).StatusCode);
        var after = await owner.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Equal(before.GetProperty("email").GetString(), after.GetProperty("email").GetString());
        Assert.Equal("Changed name", after.GetProperty("fullName").GetString());
        using var anonymous = factory.Browser(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PatchAsJsonAsync("/api/v1/me", new { fullName = "No CSRF" })).StatusCode);
    }
    [Fact]
    public async Task CrossOwnerUpdateDeleteAndPayloadOwnerTamperAreRejected()
    {
        using var factory = new AuthFactory(sql.ConnectionString); var (a, aId) = await Customer(factory); var (b, bId) = await Customer(factory);
        using var first = a; using var second = b;
        var firstId = await Create(first, "Owner A"); var secondId = await Create(second, "Owner B");
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync("/api/v1/me/addresses/" + firstId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/v1/me/addresses/" + firstId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(second, HttpMethod.Put, "me/addresses/" + firstId, Address("Tamper"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(second, HttpMethod.Delete, "me/addresses/" + firstId)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(second, HttpMethod.Post, "me/addresses", new { recipientName = "Tamper", phone = "0", addressLine = "x", locality = "x", province = "x", countryCode = "VN", isDefault = true, userId = aId })).StatusCode);
        await using var db = sql.CreateContext();
        Assert.Equal("Owner A", (await db.Addresses.SingleAsync(x => x.Id == long.Parse(firstId))).RecipientName);
        Assert.True((await db.Addresses.SingleAsync(x => x.Id == long.Parse(secondId) && x.UserId == bId)).IsDefault);
        var list = await second.GetFromJsonAsync<JsonElement[]>("/api/v1/me/addresses"); Assert.Single(list!); Assert.Equal(secondId, list![0].GetProperty("id").GetString());
    }
    [Fact]
    public async Task ConcurrentDefaultCreatesAndUpdatesKeepExactlyOneDefault()
    {
        using var factory = new AuthFactory(sql.ConnectionString); var (browser, id) = await Customer(factory); using var owner = browser;
        // Concurrent requests share an already authenticated browser session.
        var results = await Task.WhenAll(Create(owner, "First"), Create(owner, "Second"));
        await using var db = sql.CreateContext(); Assert.Equal(1, await db.Addresses.CountAsync(x => x.UserId == id && x.IsDefault));
        var responses = await Task.WhenAll(results.Select(addressId => Send(owner, HttpMethod.Put, "me/addresses/" + addressId, Address("Updated " + addressId))));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(1, await db.Addresses.CountAsync(x => x.UserId == id && x.IsDefault));
        var defaultId = await db.Addresses.Where(x => x.UserId == id && x.IsDefault).Select(x => x.Id).SingleAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await Send(owner, HttpMethod.Delete, "me/addresses/" + defaultId)).StatusCode);
        Assert.Equal(0, await db.Addresses.CountAsync(x => x.UserId == id && x.IsDefault));
    }
    [Fact]
    public async Task UpdatingAndDeletingAddressDoesNotRewriteOrderSnapshot()
    {
        using var factory = new AuthFactory(sql.ConnectionString); var (browser, id) = await Customer(factory); using var owner = browser;
        var addressId = await Create(owner, "Original recipient");
        await using var db = sql.CreateContext();
        var order = new Order { UserId = id, OrderNumber = Guid.NewGuid().ToString("N")[..30], CustomerEmail = "snapshot@example.invalid", NormalizedCustomerEmail = "SNAPSHOT@EXAMPLE.INVALID", RecipientName = "Original recipient", Phone = "0000000000", AddressLine = "Original shipping", Province = "Original province", CountryCode = "VN", CreatedAt = DateTime.UtcNow };
        db.Orders.Add(order); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Put, "me/addresses/" + addressId, Address("Changed recipient"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(owner, HttpMethod.Delete, "me/addresses/" + addressId)).StatusCode);
        await db.Entry(order).ReloadAsync(); Assert.Equal("Original recipient", order.RecipientName); Assert.Equal("Original shipping", order.AddressLine);
    }
}
