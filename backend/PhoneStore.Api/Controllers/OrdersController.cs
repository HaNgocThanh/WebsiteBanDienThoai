using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PhoneStore.Api.DTOs.Checkout;
using PhoneStore.Api.Services.Checkout;
using PhoneStore.Api.Services.Pricing;

namespace PhoneStore.Api.Controllers;
[ApiController, AllowAnonymous, Route("api/v1/orders"), ServiceFilter(typeof(CheckoutExceptionFilter)), ServiceFilter(typeof(QuoteExceptionFilter))]
public sealed class OrdersController(CheckoutService checkout, CheckoutIdentity identity) : ControllerBase
{
    [HttpPost, EnableRateLimiting("checkout")]
    public async Task<IActionResult> Place(PlaceOrderRequest request, CancellationToken ct)
    {
        var values = Request.Headers["Idempotency-Key"];
        if (values.Count != 1 || !Guid.TryParseExact(values[0], "D", out var key) || key == Guid.Empty || values[0] != key.ToString("D"))
            throw new CheckoutException(400, "INVALID_CHECKOUT_KEY", "Cần Idempotency-Key là mã phiên checkout hợp lệ.");
        var result = await checkout.PlaceAsync(identity.Resolve(HttpContext, false), key, request, ct);
        return StatusCode(result.Replayed ? 200 : 201, result.Order);
    }
}
