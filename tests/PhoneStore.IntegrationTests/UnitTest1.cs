using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using PhoneStore.Api.DTOs.Common;

namespace PhoneStore.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=PhoneStore_Test_Host;Integrated Security=True;Encrypt=True"
        }));
        builder.ConfigureServices(services => services.AddControllers().AddApplicationPart(typeof(ContractProbeController).Assembly));
    }
}

// Test host only: not part of the production API assembly.
[ApiController]
[Route("/__test/contract")]
public sealed class ContractProbeController : ControllerBase
{
    [HttpGet] public IActionResult Page([FromQuery] PageQuery query) => Ok(query);
    [HttpGet("throw")] public IActionResult Fail() => throw new InvalidOperationException("private-exception-marker");
}

public class ApiSmokeTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient client;
    public ApiSmokeTests(ApiFactory factory) => client = factory.CreateClient();

    [Fact]
    public async Task HealthReturnsExpectedDto()
    {
        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("/__test/contract?pageSize=101", 400, "VALIDATION_ERROR")]
    [InlineData("/__test/contract?page=0", 400, "VALIDATION_ERROR")]
    [InlineData("/missing", 404, "NOT_FOUND")]
    [InlineData("/__test/contract/throw", 500, "INTERNAL_ERROR")]
    public async Task ErrorsUseSafeProblemDetails(string path, int status, string code)
    {
        var response = await client.GetAsync(path);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
        var json = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(code, json.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("private-exception-marker", raw);
        Assert.DoesNotContain("StackTrace", raw);
    }
}
