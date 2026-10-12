using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.DTOs.OrderManagement;
using PhoneStore.Api.Services.Checkout;
using PhoneStore.Api.Services.GuestOrders;
using PhoneStore.Api.Services.OrderManagement;
namespace PhoneStore.Api.Controllers;
[ApiController, ServiceFilter(typeof(CheckoutExceptionFilter))]
public sealed class AfterSaleController(AfterSaleService service, GuestOrderSession guests, AppDbContext db) : ControllerBase
{
    private AfterSaleAccess Account(bool admin = false) => new(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), admin);
    private string? Version => Request.Headers.IfMatch.Count == 0 ? null : Request.Headers.IfMatch.ToString();
    private Task<AfterSaleReceipt> Act(string id, AfterSaleAccess access, AfterSaleRequest input, string action, string? resource, CancellationToken ct) => service.MutateAsync(OrderReadService.Id(id), access, Version, input, action, resource is null ? null : OrderReadService.Id(resource), ct);
    [Authorize(Policy = "Admin"), HttpGet("api/v1/admin/orders/{id}/after-sale")]
    public async Task<IActionResult> AdminRead(string id, CancellationToken ct) => Ok(await service.ReadAsync(OrderReadService.Id(id), Account(true), ct));
    [Authorize(Policy = "Customer"), HttpGet("api/v1/me/orders/{id}/after-sale")]
    public async Task<IActionResult> MeRead(string id, CancellationToken ct) => Ok(await service.ReadAsync(OrderReadService.Id(id), Account(), ct));
    [AllowAnonymous, HttpGet("api/v1/guest/order/after-sale")]
    public async Task<IActionResult> GuestRead(CancellationToken ct) { var grant = guests.Read(HttpContext); return Ok(await service.ReadAsync(grant.OrderId, new(null, Guest: grant), ct)); }
    [Authorize(Policy = "Admin"), HttpPost("api/v1/admin/orders/{id}/cancel")]
    public async Task<IActionResult> AdminCancel(string id, AfterSaleRequest input, CancellationToken ct) => Ok(await Act(id, Account(true), input, "Cancel", null, ct));
    [Authorize(Policy = "Customer"), HttpPost("api/v1/me/orders/{id}/cancel")]
    public async Task<IActionResult> MeCancel(string id, AfterSaleRequest input, CancellationToken ct) => Ok(await Act(id, Account(), input, "Cancel", null, ct));
    [AllowAnonymous, HttpPost("api/v1/guest/order/cancel")]
    public async Task<IActionResult> GuestCancel(AfterSaleRequest input, CancellationToken ct) { var grant = guests.Read(HttpContext); return Ok(await service.MutateAsync(grant.OrderId, new(null, Guest: grant), Version, input, "Cancel", null, ct)); }
    [Authorize(Policy = "Admin"), HttpPost("api/v1/admin/orders/{id}/cancellation-requests")]
    public async Task<IActionResult> AdminRequest(string id, AfterSaleRequest input, CancellationToken ct) => Ok(await Act(id, Account(true), input, "Request", null, ct));
    [Authorize(Policy = "Customer"), HttpPost("api/v1/me/orders/{id}/cancellation-requests")]
    public async Task<IActionResult> MeRequest(string id, AfterSaleRequest input, CancellationToken ct) => Ok(await Act(id, Account(), input, "Request", null, ct));
    [AllowAnonymous, HttpPost("api/v1/guest/order/cancellation-requests")]
    public async Task<IActionResult> GuestRequest(AfterSaleRequest input, CancellationToken ct) { var grant = guests.Read(HttpContext); return Ok(await service.MutateAsync(grant.OrderId, new(null, Guest: grant), Version, input, "Request", null, ct)); }
    [Authorize(Policy = "Admin"), HttpPost("api/v1/admin/orders/{id}/cancellation-requests/{requestId}/review")]
    public async Task<IActionResult> Review(string id, string requestId, AfterSaleRequest input, CancellationToken ct) => Ok(await Act(id, Account(true), input, "Review", requestId, ct));
    [Authorize(Policy = "Admin"), HttpPost("api/v1/admin/payments/{id}/refunds")]
    public async Task<IActionResult> Refund(string id, AfterSaleRequest input, CancellationToken ct)
    { var order = await db.Payments.Where(p => p.Id == OrderReadService.Id(id)).Select(p => (long?)p.OrderId).SingleOrDefaultAsync(ct) ?? throw new CheckoutException(404, "NOT_FOUND", "Không tìm thấy khoản tiền."); return Ok(await Act(order.ToString(), Account(true), input, "Refund", id, ct)); }
    [Authorize(Policy = "Admin"), HttpPost("api/v1/admin/refunds/{id}/complete")]
    public Task<IActionResult> Complete(string id, AfterSaleRequest input, CancellationToken ct) => RefundAction(id, input, "Complete", ct);
    [Authorize(Policy = "Admin"), HttpPost("api/v1/admin/refunds/{id}/fail")]
    public Task<IActionResult> Fail(string id, AfterSaleRequest input, CancellationToken ct) => RefundAction(id, input, "Fail", ct);
    private async Task<IActionResult> RefundAction(string id, AfterSaleRequest input, string action, CancellationToken ct)
    { var order = await db.Refunds.Where(r => r.Id == OrderReadService.Id(id)).Select(r => (long?)r.Payment.OrderId).SingleOrDefaultAsync(ct) ?? throw new CheckoutException(404, "NOT_FOUND", "Không tìm thấy khoản hoàn."); return Ok(await Act(order.ToString(), Account(true), input, action, id, ct)); }
}
