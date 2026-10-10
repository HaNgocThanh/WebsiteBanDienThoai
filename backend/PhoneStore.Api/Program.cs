using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "Configure ConnectionStrings:DefaultConnection before using the database.")));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;
        options.Password.RequiredLength = 12;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
}).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "PhoneStore.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    options.Events.OnValidatePrincipal = async context =>
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        context.ShouldRenew = false; // Per-request stamp validation must not extend the absolute 8-hour ticket.
        if (context.Principal?.Identity?.IsAuthenticated == true)
        {
            var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.GetUserAsync(context.Principal);
            if (user is null || !user.IsActive || !user.EmailConfirmed)
            {
                context.RejectPrincipal();
                await context.HttpContext.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>().SignOutAsync();
            }
        }
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Customer", policy => policy.RequireAuthenticatedUser().RequireRole("Customer"));
    options.AddPolicy("Admin", policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "PhoneStore.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddScoped<PhoneStore.Api.Services.OrderManagement.OrderReadService>();
builder.Services.AddScoped<AuthBootstrap>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PhoneStore.Api.Services.ProfileService>();
builder.Services.AddScoped<PhoneStore.Api.Services.Pricing.QuoteService>();
builder.Services.AddScoped<PhoneStore.Api.Services.Checkout.CheckoutService>();
builder.Services.AddScoped<PhoneStore.Api.Services.Checkout.CheckoutIdentity>();
builder.Services.AddScoped<PhoneStore.Api.Services.Checkout.CheckoutExceptionFilter>();
builder.Services.AddScoped<PhoneStore.Api.Services.GuestOrders.GuestOrderService>();
builder.Services.AddScoped<PhoneStore.Api.Services.GuestOrders.GuestOrderSession>();
builder.Services.AddScoped<PhoneStore.Api.Services.GuestOrders.OrderAccessMailQueue>();
builder.Services.AddScoped<PhoneStore.Api.Services.Notifications.OrderOutboxProcessor>();
builder.Services.AddSingleton<PhoneStore.Api.Services.Notifications.IOrderEmailSender, PhoneStore.Api.Services.Notifications.LocalOrderEmailSender>();
builder.Services.AddHostedService<PhoneStore.Api.Services.Notifications.OrderOutboxWorker>();
builder.Services.AddSingleton<PhoneStore.Api.Services.AdministrativeLocations>();
builder.Services.AddScoped<PhoneStore.Api.Services.Pricing.QuoteExceptionFilter>();
builder.Services.AddScoped<PhoneStore.Api.Services.Catalog.CatalogService>();
builder.Services.AddScoped<PhoneStore.Api.Services.InventoryManagement.InventoryService>();
builder.Services.AddScoped<PhoneStore.Api.Services.InventoryManagement.InventoryExceptionFilter>();
builder.Services.AddScoped<PhoneStore.Api.Services.Catalog.CatalogExceptionFilter>();
builder.Services.AddSingleton<PhoneStore.Api.Services.Catalog.ICatalogImageStore>(services =>
    new PhoneStore.Api.Services.Catalog.CachedCatalogImageStore(string.Equals(builder.Configuration["Catalog:ImageProvider"], "Cloudinary", StringComparison.OrdinalIgnoreCase)
        ? new PhoneStore.Api.Services.Catalog.CloudinaryCatalogImageStore(services.GetRequiredService<IHostEnvironment>(), builder.Configuration)
        : new PhoneStore.Api.Services.Catalog.LocalCatalogImageStore(services.GetRequiredService<IHostEnvironment>(), builder.Configuration)));
builder.Services.AddSingleton<IAuthEmailSender, LocalAuthEmailSender>();
builder.Services.AddScoped<PhoneStore.Api.Services.Payments.PaymentService>();
builder.Services.AddSingleton<PhoneStore.Api.Services.Payments.SePaySandbox>();
builder.Services.AddRateLimiter(options =>
{
    foreach (var (name, defaultLimit) in new[] { ("auth", 20), ("csrf", 60), ("quote", 60), ("checkout", 20), ("guest", 20), ("payment", 60) })
        options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Clamp(builder.Configuration.GetValue<int?>($"Auth:RateLimit:{name}") ?? defaultLimit, 1, 1000),
                Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
            }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await Results.Problem(statusCode: 429, title: "Quá nhiều yêu cầu. Vui lòng thử lại sau.",
            extensions: new Dictionary<string, object?> { ["code"] = "RATE_LIMITED", ["traceId"] = context.HttpContext.TraceIdentifier }).ExecuteAsync(context.HttpContext);
    };
});

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Extensions.TryAdd("code", context.ProblemDetails.Status switch
    {
        400 => "VALIDATION_ERROR", 401 => "UNAUTHENTICATED", 403 => "FORBIDDEN",
        404 => "NOT_FOUND", 409 => "CONFLICT", 412 => "VERSION_MISMATCH",
        428 => "PRECONDITION_REQUIRED", 429 => "RATE_LIMITED", _ => "INTERNAL_ERROR"
    });
});
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new PhoneStore.Api.DTOs.Common.UtcDateTimeJsonConverter()))
    .ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new Microsoft.AspNetCore.Mvc.ValidationProblemDetails(context.ModelState)
        {
            Status = 400, Title = "Dữ liệu không hợp lệ", Type = "about:blank"
        };
        problem.Extensions["code"] = "VALIDATION_ERROR";
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(problem);
    };
});
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRouting();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1")) context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseRateLimiter();
// Sensitive API responses are never cached. Validate every versioned mutation,
// including anonymous login/register, before controller/model processing.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1"))
    {
        if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method)
            || HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method))
        {
            try {
                if (context.GetEndpoint()?.Metadata.GetMetadata<PhoneStore.Api.Services.Payments.SePayIpnAttribute>() is not null) {
                    var values = context.Request.Headers["X-Secret-Key"];
                    context.RequestServices.GetRequiredService<PhoneStore.Api.Services.Payments.SePaySandbox>().CheckIpn(values.Count == 1 ? values[0] : null);
                } else await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            }
            catch (PhoneStore.Api.Services.Checkout.CheckoutException failure) {
                await Results.Problem(statusCode: failure.Status, title: failure.Message, extensions: new Dictionary<string, object?> { ["code"] = failure.Code, ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
                return;
            }
            catch (AntiforgeryValidationException)
            {
                await Results.Problem(statusCode: 403, title: "Phiên làm việc đã thay đổi. Vui lòng tải lại trang.",
                    extensions: new Dictionary<string, object?> { ["code"] = "CSRF_INVALID", ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
                return;
            }
        }
    }
    await next(context);
});
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    message = "PhoneStore API đang hoạt động"
}));

app.MapGet("/api/ready", async (AppDbContext db, HttpContext http, CancellationToken cancellationToken) =>
{
    http.Response.Headers.CacheControl = "no-store";
    try
    {
        if (await db.Database.CanConnectAsync(cancellationToken)
            && !(await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            return Results.Ok(new { status = "ready" });
    }
    catch (Exception exception) when (exception is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
    {
        // Public readiness never exposes connection details or migration diagnostics.
    }
    return Results.Problem(statusCode: 503, title: "Database/schema is not ready",
        extensions: new Dictionary<string, object?> { ["code"] = "NOT_READY", ["traceId"] = http.TraceIdentifier });
});

app.MapControllers();

if (args.Contains("--bootstrap-auth", StringComparer.Ordinal))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AuthBootstrap>().RunAsync(builder.Configuration["Auth:BootstrapAdminEmail"], CancellationToken.None);
    return;
}

app.Run();

// Entry point for the in-process API test host.
public partial class Program { }
