using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PhoneStore.Api.Data;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Auth;

namespace PhoneStore.IntegrationTests;

public sealed class TestAuthMailbox : IAuthEmailSender
{
    public bool IsConfigured { get; set; } = true;
    public bool FailDelivery { get; set; }
    public ConcurrentQueue<AuthEmail> Messages { get; } = new();
    public Task SendAsync(AuthEmail message, CancellationToken cancellationToken)
    {
        if (FailDelivery) throw new IOException("private-mailbox-error-marker");
        Messages.Enqueue(message);
        return Task.CompletedTask;
    }
}

public sealed class AuthFactory(string connection, string environment = "Testing", int limit = 1000, bool expired = false) : WebApplicationFactory<Program>
{
    public TestAuthMailbox Mailbox { get; } = new();
    public ConcurrentQueue<string> Diagnostics { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connection,
            ["Outbox:Enabled"] = "false",
            ["Auth:RateLimit:auth"] = limit.ToString(), ["Auth:RateLimit:csrf"] = "1000"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuthEmailSender>();
            services.AddSingleton<ILoggerProvider>(new SafeAuthTestLogger(Diagnostics));
            services.AddSingleton<IAuthEmailSender>(Mailbox);
            services.AddControllers().AddApplicationPart(typeof(AuthProbeController).Assembly);
            if (expired) services.PostConfigure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromTicks(-1));
        });
    }
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
}

// Exception types/frames only; never include exception messages, SQL credentials or mail tokens.
public sealed class SafeAuthTestLogger(ConcurrentQueue<string> diagnostics) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Logger(diagnostics);
    public void Dispose() { }
    private sealed class Logger(ConcurrentQueue<string> diagnostics) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Error;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null) diagnostics.Enqueue(exception.GetType().Name + ": " + exception.StackTrace + "; inner=" + exception.InnerException?.GetType().Name);
        }
    }
}

// Test assembly only. Exercises real production policies without inventing catalog endpoints.
[ApiController, Route("api/v1/__test/auth")]
public sealed class AuthProbeController : ControllerBase
{
    [HttpGet("customer"), Authorize(Policy = "Customer")]
    public IActionResult Customer() => NoContent();
    [HttpGet("admin"), Authorize(Policy = "Admin")]
    public IActionResult Admin() => NoContent();
    [HttpPost("mutation"), Authorize(Policy = "Customer")]
    public IActionResult Mutation() => NoContent();
}

[Collection("SQL")]
public sealed class AuthTests(SqlFixture sql)
{
    private const string Password = "Synthetic!Password123";
    private const string ChangedPassword = "Changed!Password456";
    private static string Email() => Guid.NewGuid().ToString("N") + "@example.invalid";
    private static object Registration(string email) => new { email, password = Password, fullName = "Synthetic customer", phone = "0000000000" };

    private static async Task<string> Csrf(HttpClient browser)
    {
        var response = await browser.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }
    private static async Task<HttpResponseMessage> Post(HttpClient browser, string endpoint, object body, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/" + endpoint) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token ?? await Csrf(browser));
        return await browser.SendAsync(request);
    }
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
        Assert.True(response.Headers.CacheControl!.NoStore);
    }
    private static async Task<AuthEmail> Register(AuthFactory factory, HttpClient browser, string address)
    {
        var response = await Post(browser, "auth/register", Registration(address));
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, "Status=" + (int)response.StatusCode + " " + string.Join("\n", factory.Diagnostics));
        return factory.Mailbox.Messages.Single(x => x.Email == address && x.Purpose == "verify-email");
    }
    private static async Task Verify(HttpClient browser, AuthEmail message) => Assert.Equal(HttpStatusCode.NoContent,
        (await Post(browser, "auth/verify-email", new { message.UserId, message.Token })).StatusCode);
    private static async Task<HttpResponseMessage> Login(HttpClient browser, string email, string password = Password) => await Post(browser, "auth/login", new { email, password });

    [Fact]
    public async Task RegisterVerifyLoginLogoutUsesRealCookieAndRevokesReplay()
    {
        using var factory = new AuthFactory(sql.ConnectionString);
        using var browser = factory.Browser();
        var email = Email();
        var message = await Register(factory, browser, email);
        await Problem(await browser.GetAsync("/api/v1/auth/session"), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await Problem(await Login(browser, email), HttpStatusCode.Forbidden, "EMAIL_NOT_VERIFIED");
        await Problem(await Post(browser, "auth/verify-email", new { message.UserId, token = "wrong" }), HttpStatusCode.BadRequest, "INVALID_TOKEN");
        await Verify(browser, message);
        await Problem(await Post(browser, "auth/verify-email", new { message.UserId, message.Token }), HttpStatusCode.BadRequest, "INVALID_TOKEN");
        var anonymousCsrf = await Csrf(browser);
        var login = await Login(browser, email);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.Equal("", await login.Content.ReadAsStringAsync());
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("PhoneStore.Auth="));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("secure", cookie.ToLowerInvariant());
        Assert.DoesNotContain("domain=", cookie.ToLowerInvariant());
        var cookieOptions = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var ticket = cookieOptions.TicketDataFormat.Unprotect(cookie.Split(';')[0]["PhoneStore.Auth=".Length..]);
        Assert.NotNull(ticket);
        Assert.Equal(TimeSpan.FromHours(8), ticket.Properties.ExpiresUtc - ticket.Properties.IssuedUtc);
        var sessionResponse = await browser.GetAsync("/api/v1/auth/session");
        Assert.False(sessionResponse.Headers.TryGetValues("Set-Cookie", out var renewed) && renewed.Any(x => x.StartsWith("PhoneStore.Auth=")));
        var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, session.GetProperty("email").GetString());
        Assert.Equal("Customer", session.GetProperty("roles")[0].GetString());
        Assert.False(session.TryGetProperty("passwordHash", out _));
        Assert.Equal(HttpStatusCode.NoContent, (await browser.GetAsync("/api/v1/__test/auth/customer")).StatusCode);
        await Problem(await browser.GetAsync("/api/v1/__test/auth/admin"), HttpStatusCode.Forbidden, "FORBIDDEN");
        await Problem(await Post(browser, "__test/auth/mutation", new { }, anonymousCsrf), HttpStatusCode.Forbidden, "CSRF_INVALID");
        Assert.Equal(HttpStatusCode.NoContent, (await Post(browser, "__test/auth/mutation", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(browser, "auth/logout", new { })).StatusCode);
        using var replay = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false, AllowAutoRedirect = false });
        replay.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        await Problem(await replay.GetAsync("/api/v1/auth/session"), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await Problem(await browser.GetAsync("/api/v1/auth/session"), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task ConcurrentNormalizedEmailCreatesOneCustomerProfileAndNoAdmin()
    {
        using var factory = new AuthFactory(sql.ConnectionString);
        var email = Email();
        using var first = factory.Browser(); using var second = factory.Browser();
        var results = await Task.WhenAll(Post(first, "auth/register", Registration(email)), Post(second, "auth/register", Registration(email.ToUpperInvariant())));
        Assert.All(results, result => Assert.Equal(HttpStatusCode.Accepted, result.StatusCode));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await db.Users.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant());
        Assert.False(user.EmailConfirmed);
        Assert.Equal(new[] { "Customer" }, await users.GetRolesAsync(user));
        var profile = await db.CustomerProfiles.Include(x => x.Tier).SingleAsync(x => x.UserId == user.Id);
        Assert.Equal(0, profile.EligibleSpend); Assert.Equal(CustomerTierCode.Bronze, profile.Tier.Code); Assert.Equal(0, profile.Tier.MinimumSpend);
        Assert.Single(factory.Mailbox.Messages);
        var tamper = await Post(first, "auth/register", new { email = Email(), password = Password, fullName = "Test", role = "Admin", tierId = "1" });
        await Problem(tamper, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task PasswordResetIsNeutralPurposeBoundOneUseAndRevokesCookies()
    {
        using var factory = new AuthFactory(sql.ConnectionString);
        using var browser = factory.Browser(); var email = Email();
        var confirmation = await Register(factory, browser, email); await Verify(browser, confirmation);
        Assert.Equal(HttpStatusCode.NoContent, (await Login(browser, email)).StatusCode);
        var known = await Post(browser, "auth/forgot-password", new { email });
        var unknown = await Post(browser, "auth/forgot-password", new { email = Email() });
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode); Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        var reset = factory.Mailbox.Messages.Single(x => x.Purpose == "reset-password");
        await Problem(await Post(browser, "auth/reset-password", new { reset.UserId, token = confirmation.Token, newPassword = ChangedPassword }), HttpStatusCode.BadRequest, "INVALID_TOKEN");
        await Problem(await Post(browser, "auth/reset-password", new { userId = Guid.NewGuid(), reset.Token, newPassword = ChangedPassword }), HttpStatusCode.BadRequest, "INVALID_TOKEN");
        Assert.Equal(HttpStatusCode.NoContent, (await Post(browser, "auth/reset-password", new { reset.UserId, reset.Token, newPassword = ChangedPassword })).StatusCode);
        await Problem(await browser.GetAsync("/api/v1/auth/session"), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await Problem(await Post(browser, "auth/reset-password", new { reset.UserId, reset.Token, newPassword = ChangedPassword }), HttpStatusCode.BadRequest, "INVALID_TOKEN");
        await Problem(await Login(browser, email), HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
        Assert.Equal(HttpStatusCode.NoContent, (await Login(browser, email, ChangedPassword)).StatusCode);
    }

    [Fact]
    public async Task ExpiredProviderTokensCannotVerifyOrReset()
    {
        using var factory = new AuthFactory(sql.ConnectionString, expired: true);
        using var browser = factory.Browser(); var email = Email();
        var message = await Register(factory, browser, email);
        await Problem(await Post(browser, "auth/verify-email", new { message.UserId, message.Token }), HttpStatusCode.BadRequest, "INVALID_TOKEN");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            user.EmailConfirmed = true; Assert.True((await users.UpdateAsync(user)).Succeeded); // Test-only fixture for reset expiry.
        }
        Assert.Equal(HttpStatusCode.Accepted, (await Post(browser, "auth/forgot-password", new { email })).StatusCode);
        var reset = factory.Mailbox.Messages.Single(x => x.Purpose == "reset-password");
        await Problem(await Post(browser, "auth/reset-password", new { reset.UserId, reset.Token, newPassword = ChangedPassword }), HttpStatusCode.BadRequest, "INVALID_TOKEN");
    }

    [Fact]
    public async Task MissingWrongAndOtherBrowsersCsrfCannotMutate()
    {
        using var factory = new AuthFactory(sql.ConnectionString);
        using var browser = factory.Browser(); using var other = factory.Browser();
        var email = Email();
        await Problem(await browser.PostAsJsonAsync("/api/v1/auth/register", Registration(email)), HttpStatusCode.Forbidden, "CSRF_INVALID");
        await Problem(await Post(browser, "auth/register", Registration(email), "wrong"), HttpStatusCode.Forbidden, "CSRF_INVALID");
        await Csrf(browser);
        await Problem(await Post(browser, "auth/register", Registration(email), await Csrf(other)), HttpStatusCode.Forbidden, "CSRF_INVALID");
        await using var db = sql.CreateContext(); Assert.False(await db.Users.AnyAsync(x => x.Email == email));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task BadPasswordsLockOutVerifiedAndUnverifiedAccounts(bool confirmed)
    {
        using var factory = new AuthFactory(sql.ConnectionString); using var browser = factory.Browser(); var email = Email();
        var message = await Register(factory, browser, email); if (confirmed) await Verify(browser, message);
        for (var index = 0; index < 5; index++) await Problem(await Login(browser, email, "wrong"), HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
        await Problem(await Login(browser, email), HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
        await using var db = sql.CreateContext(); var user = await db.Users.SingleAsync(x => x.Email == email);
        Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ExplicitBootstrapGrantsOnlyExistingVerifiedAdminAndIsIdempotent()
    {
        using var factory = new AuthFactory(sql.ConnectionString); using var browser = factory.Browser(); var email = Email();
        var message = await Register(factory, browser, email);
        using (var scope = factory.Services.CreateScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<AuthBootstrap>().RunAsync(email, default));
        await Verify(browser, message);
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AuthBootstrap>().RunAsync(email, default);
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AuthBootstrap>().RunAsync(email, default);
        Assert.Equal(HttpStatusCode.NoContent, (await Login(browser, email)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.GetAsync("/api/v1/__test/auth/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.GetAsync("/api/v1/__test/auth/customer")).StatusCode);
    }

    [Fact]
    public async Task DeliveryFailureRetainsUnverifiedAccountAndResendRecovers()
    {
        using var factory = new AuthFactory(sql.ConnectionString); using var browser = factory.Browser(); var email = Email();
        factory.Mailbox.FailDelivery = true;
        var response = await Post(browser, "auth/register", Registration(email));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain("private-mailbox-error-marker", await response.Content.ReadAsStringAsync());
        await using (var db = sql.CreateContext()) Assert.False((await db.Users.SingleAsync(x => x.Email == email)).EmailConfirmed);
        factory.Mailbox.FailDelivery = false;
        Assert.Equal(HttpStatusCode.Accepted, (await Post(browser, "auth/resend-verification", new { email })).StatusCode);
        await Verify(browser, factory.Mailbox.Messages.Single());
        Assert.Equal(HttpStatusCode.NoContent, (await Login(browser, email)).StatusCode);
        factory.Mailbox.FailDelivery = true;
        Assert.Equal(HttpStatusCode.Accepted, (await Post(browser, "auth/forgot-password", new { email })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Post(browser, "auth/forgot-password", new { email = Email() })).StatusCode);
    }

    [Fact]
    public async Task DisabledAccountCookieIsRejectedImmediately()
    {
        using var factory = new AuthFactory(sql.ConnectionString); using var browser = factory.Browser(); var email = Email();
        await Verify(browser, await Register(factory, browser, email)); await Login(browser, email);
        await using (var db = sql.CreateContext()) { var user = await db.Users.SingleAsync(x => x.Email == email); user.IsActive = false; await db.SaveChangesAsync(); }
        await Problem(await browser.GetAsync("/api/v1/auth/session"), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await Problem(await Login(browser, email), HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task RateLimitReturnsProblemAndRetryAfter()
    {
        using var factory = new AuthFactory(sql.ConnectionString, limit: 2); using var browser = factory.Browser();
        for (var index = 0; index < 2; index++) Assert.Equal(HttpStatusCode.Accepted, (await Post(browser, "auth/forgot-password", new { email = Email() })).StatusCode);
        var response = await Post(browser, "auth/forgot-password", new { email = Email() });
        await Problem(response, HttpStatusCode.TooManyRequests, "RATE_LIMITED"); Assert.NotNull(response.Headers.RetryAfter);
    }

    [Theory]
    [InlineData("Development", false)] [InlineData("Production", true)]
    public async Task CookieSecurePolicyUsesEnvironmentWithoutWeakeningProduction(string environment, bool secure)
    {
        using var factory = new AuthFactory(sql.ConnectionString, environment);
        var cookie = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme).Cookie;
        var csrf = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>>().Value.Cookie;
        Assert.True(cookie.HttpOnly); Assert.True(csrf.HttpOnly);
        Assert.Equal(secure ? Microsoft.AspNetCore.Http.CookieSecurePolicy.Always : Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest, cookie.SecurePolicy);
        Assert.Equal(cookie.SecurePolicy, csrf.SecurePolicy);
    }

    [Fact]
    public async Task InvalidPasswordRollsBackUserProfileAndConfiguredEmailIsRequired()
    {
        using var factory = new AuthFactory(sql.ConnectionString); using var browser = factory.Browser(); var email = Email();
        await Problem(await Post(browser, "auth/register", new { email, password = "alllowercasepassword", fullName = "Test" }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await using var db = sql.CreateContext(); Assert.False(await db.Users.AnyAsync(x => x.Email == email));
        Assert.Empty(factory.Mailbox.Messages);
        var existing = Email(); await Register(factory, browser, existing);
        await Problem(await Post(browser, "auth/register", new { email = existing, password = "alllowercasepassword", fullName = "Test" }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        factory.Mailbox.IsConfigured = false;
        await Problem(await Post(browser, "auth/register", Registration(email)), HttpStatusCode.ServiceUnavailable, "EMAIL_UNAVAILABLE");
        Assert.False(await db.Users.AnyAsync(x => x.Email == email));
        await Problem(await Post(browser, "auth/forgot-password", new { email }), HttpStatusCode.ServiceUnavailable, "EMAIL_UNAVAILABLE");
    }

    [Fact]
    public async Task ConcurrentResetConsumesOneTokenAndKeepsOnePassword()
    {
        using var factory = new AuthFactory(sql.ConnectionString); using var first = factory.Browser(); using var second = factory.Browser(); var email = Email();
        await Verify(first, await Register(factory, first, email));
        await Post(first, "auth/forgot-password", new { email });
        var reset = factory.Mailbox.Messages.Single(x => x.Purpose == "reset-password");
        var results = await Task.WhenAll(Post(first, "auth/reset-password", new { reset.UserId, reset.Token, newPassword = ChangedPassword }),
            Post(second, "auth/reset-password", new { reset.UserId, reset.Token, newPassword = "Other!Password789" }));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.NoContent);
        await Problem(results.Single(x => x.StatusCode != HttpStatusCode.NoContent), HttpStatusCode.BadRequest, "INVALID_TOKEN");
    }

    [Fact]
    public async Task ConcurrentFailedLoginsDoNotLoseLockoutCounter()
    {
        using var factory = new AuthFactory(sql.ConnectionString); using var browser = factory.Browser(); var email = Email();
        await Verify(browser, await Register(factory, browser, email));
        var browsers = Enumerable.Range(0, 5).Select(_ => factory.Browser()).ToArray();
        try
        {
            var failures = await Task.WhenAll(browsers.Select(client => Login(client, email, "wrong")));
            foreach (var failure in failures) await Problem(failure, HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
            await Problem(await Login(browser, email), HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
            await using var db = sql.CreateContext(); Assert.True((await db.Users.SingleAsync(x => x.Email == email)).LockoutEnd > DateTimeOffset.UtcNow);
        }
        finally { foreach (var client in browsers) client.Dispose(); }
    }

    [Fact]
    public async Task LocalMailboxWritesSensitiveEnvelopeOnlyInDevelopment()
    {
        using var factory = new AuthFactory(sql.ConnectionString, "Development");
        var directory = Path.Combine(Path.GetTempPath(), "PhoneStore_Test_Mail_" + Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:MailboxPath"] = directory }).Build();
        var sender = new LocalAuthEmailSender(factory.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>(), config);
        try
        {
            var message = new AuthEmail("verify-email", Email(), Guid.NewGuid(), "synthetic-token-test-only", DateTime.UtcNow.AddHours(1));
            await sender.SendAsync(message, default);
            var file = Assert.Single(Directory.GetFiles(directory)); Assert.EndsWith(".json", file);
            Assert.Equal(message, JsonSerializer.Deserialize<AuthEmail>(await File.ReadAllTextAsync(file)));
            using var production = new AuthFactory(sql.ConnectionString, "Production");
            var disabled = new LocalAuthEmailSender(production.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>(), config);
            Assert.False(disabled.IsConfigured);
            await Assert.ThrowsAsync<InvalidOperationException>(() => disabled.SendAsync(message, default));
        }
        finally
        {
            // Only delete the exact GUID directory created by this test.
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
