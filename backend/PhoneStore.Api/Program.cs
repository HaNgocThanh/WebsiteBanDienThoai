using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.Entities;

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
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

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

app.Run();

// Entry point for the in-process API test host.
public partial class Program { }



