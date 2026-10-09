using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PhoneStore.Api.DTOs.GuestOrders;
using PhoneStore.Api.Services.Checkout;
using PhoneStore.Api.Services.GuestOrders;

namespace PhoneStore.Api.Controllers;
[ApiController, Authorize(Policy = "Customer"), Route("api/v1/me/order-claims"), ServiceFilter(typeof(CheckoutExceptionFilter)), EnableRateLimiting("guest")]
public sealed class OrderClaimsController(GuestOrderService orders) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Claim(OrderClaimRequest request, CancellationToken ct) =>
        Ok(await orders.ClaimAsync(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), request.Token, ct));
}
