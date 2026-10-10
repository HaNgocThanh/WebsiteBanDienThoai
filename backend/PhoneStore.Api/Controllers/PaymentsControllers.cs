using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.Payments;
using PhoneStore.Api.Services.Checkout;
using PhoneStore.Api.Services.GuestOrders;
using PhoneStore.Api.Services.OrderManagement;
using PhoneStore.Api.Services.Payments;
namespace PhoneStore.Api.Controllers;
[ApiController, Authorize(Policy = "Customer"), Route("api/v1/me/orders/{orderId}"), ServiceFilter(typeof(CheckoutExceptionFilter)), EnableRateLimiting("payment")]
public sealed class MePaymentsController(PaymentService payments) : ControllerBase
{
    private PaymentAccess Access => new(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
    [HttpGet("payments")] public async Task<IActionResult> List(string orderId, [FromQuery] PageQuery query, CancellationToken ct) => Ok(await payments.ListAsync(OrderReadService.Id(orderId), Access, query, ct));
    [HttpPost("sepay-checkout")] public async Task<IActionResult> Checkout(string orderId, CancellationToken ct) => Ok(await payments.CheckoutAsync(OrderReadService.Id(orderId), Access, ct));
}
[ApiController, AllowAnonymous, Route("api/v1/guest/order"), ServiceFilter(typeof(CheckoutExceptionFilter)), EnableRateLimiting("payment")]
public sealed class GuestPaymentsController(PaymentService payments, GuestOrderSession session) : ControllerBase
{
    private PaymentAccess Access => new(null, Guest: session.Read(HttpContext));
    [HttpGet("payments")] public async Task<IActionResult> List([FromQuery] PageQuery query, CancellationToken ct) { var access = Access; return Ok(await payments.ListAsync(access.Guest!.OrderId, access, query, ct)); }
    [HttpPost("sepay-checkout")] public async Task<IActionResult> Checkout(CancellationToken ct) { var access = Access; return Ok(await payments.CheckoutAsync(access.Guest!.OrderId, access, ct)); }
}
[ApiController, Authorize(Policy = "Admin"), Route("api/v1/admin"), ServiceFilter(typeof(CheckoutExceptionFilter))]
public sealed class AdminPaymentsController(PaymentService payments) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("orders/{orderId}/payments")] public async Task<IActionResult> List(string orderId, [FromQuery] PageQuery query, CancellationToken ct) => Ok(await payments.ListAsync(OrderReadService.Id(orderId), new(Actor, true), query, ct));
    [HttpPost("orders/{orderId}/cod-receipts")] public async Task<IActionResult> Receipt(string orderId, CodReceiptRequest request, CancellationToken ct) => Ok(await payments.CodAsync(OrderReadService.Id(orderId), Actor, request, ct));
}
[ApiController, AllowAnonymous, Route("api/v1/payments/sepay"), ServiceFilter(typeof(CheckoutExceptionFilter)), EnableRateLimiting("payment")]
public sealed class SePayIpnController(PaymentService payments) : ControllerBase
{
    [HttpPost("ipn"), SePayIpn, RequestSizeLimit(16384)]
    public async Task<IActionResult> Ipn(SePayIpnRequest request, CancellationToken ct) { await payments.IpnAsync(request, ct); return Ok(new { success = true }); }
}

[ApiController, AllowAnonymous, Route("api/v1/checkout/sessions/{key}"), ServiceFilter(typeof(CheckoutExceptionFilter)), EnableRateLimiting("payment")]
public sealed class CheckoutPaymentsController(CheckoutService checkout, CheckoutIdentity identity, PaymentService payments) : ControllerBase
{
    private async Task<(long OrderId, PaymentAccess Access)> AccessAsync(string key, CancellationToken ct)
    {
        if (!Guid.TryParseExact(key, "D", out var id) || id == Guid.Empty || key != id.ToString("D")) throw new CheckoutException(400, "INVALID_CHECKOUT_KEY", "Mã phiên checkout không hợp lệ.");
        var owner = identity.Resolve(HttpContext, false); var result = await checkout.ResultAsync(owner, id, ct);
        if (result.Order is null) throw new CheckoutException(409, "CHECKOUT_NOT_COMPLETED", "Đơn chưa được ghi nhận.");
        return (OrderReadService.Id(result.Order.Id), new(owner.UserId, Checkout: owner, CheckoutKey: id));
    }
    [HttpGet("payments")] public async Task<IActionResult> List(string key, [FromQuery] PageQuery query, CancellationToken ct) { var access = await AccessAsync(key, ct); return Ok(await payments.ListAsync(access.OrderId, access.Access, query, ct)); }
    [HttpPost("sepay-checkout")] public async Task<IActionResult> Pay(string key, CancellationToken ct) { var access = await AccessAsync(key, ct); return Ok(await payments.CheckoutAsync(access.OrderId, access.Access, ct)); }
}
