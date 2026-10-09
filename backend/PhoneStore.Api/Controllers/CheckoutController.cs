using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PhoneStore.Api.DTOs.Pricing;
using PhoneStore.Api.Services.Pricing;
using PhoneStore.Api.Services.Checkout;

namespace PhoneStore.Api.Controllers;
[ApiController, AllowAnonymous, Route("api/v1/checkout"), ServiceFilter(typeof(QuoteExceptionFilter))]
public sealed class CheckoutController(QuoteService quotes, CheckoutService checkout, CheckoutIdentity identity) : ControllerBase
{
    [HttpPost("quote"), EnableRateLimiting("quote")]
    public async Task<IActionResult> Quote(QuoteRequest request, CancellationToken ct) => Ok(await quotes.QuoteAsync(request, ct));
    [HttpPost("sessions"), EnableRateLimiting("checkout"), ServiceFilter(typeof(CheckoutExceptionFilter))]
    public async Task<IActionResult> Session(CancellationToken ct) => Ok(await checkout.IssueAsync(identity.Resolve(HttpContext, true), ct));
    [HttpGet("sessions/{key}/result"), EnableRateLimiting("checkout"), ServiceFilter(typeof(CheckoutExceptionFilter))]
    public async Task<IActionResult> Result(string key, CancellationToken ct)
    {
        if (!Guid.TryParseExact(key, "D", out var id) || id == Guid.Empty || key != id.ToString("D"))
            throw new CheckoutException(400, "INVALID_CHECKOUT_KEY", "Mã phiên checkout không hợp lệ.");
        return Ok(await checkout.ResultAsync(identity.Resolve(HttpContext, false), id, ct));
    }
}
