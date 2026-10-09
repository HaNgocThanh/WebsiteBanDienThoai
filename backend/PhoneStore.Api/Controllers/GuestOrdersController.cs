using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PhoneStore.Api.DTOs.GuestOrders;
using PhoneStore.Api.Services.Checkout;
using PhoneStore.Api.Services.GuestOrders;

namespace PhoneStore.Api.Controllers;
[ApiController, AllowAnonymous, Route("api/v1/guest"), ServiceFilter(typeof(CheckoutExceptionFilter)), EnableRateLimiting("guest")]
public sealed class GuestOrdersController(GuestOrderService orders, GuestOrderSession session) : ControllerBase
{
    [HttpPost("order-access-requests")]
    public async Task<IActionResult> RequestAccess(OrderAccessRequest request, CancellationToken ct)
    { await orders.RequestAsync(request, ct); return Accepted(); }
    [HttpPost("order-access/exchange")]
    public async Task<IActionResult> Exchange(OrderAccessExchange request, CancellationToken ct)
    {
        var grant = await orders.ExchangeAsync(request, ct); session.Write(HttpContext, grant);
        return Ok(new GuestSessionDto(grant.Purpose.ToString(), grant.ExpiresAt));
    }
    [HttpGet("order")]
    public async Task<IActionResult> View(CancellationToken ct) => Ok(await orders.ViewAsync(session.Read(HttpContext), ct));
    [HttpPost("account-setup-requests")]
    public async Task<IActionResult> Setup(CancellationToken ct)
    { await orders.SetupAsync(session.Read(HttpContext), ct); return Accepted(); }
}
