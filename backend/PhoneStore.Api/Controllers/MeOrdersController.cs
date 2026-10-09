using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhoneStore.Api.DTOs.OrderManagement;
using PhoneStore.Api.Services.OrderManagement;
using PhoneStore.Api.Services.Checkout;
namespace PhoneStore.Api.Controllers;
[ApiController, Route("api/v1/me/orders"), Authorize(Policy = "Customer"), ServiceFilter(typeof(CheckoutExceptionFilter))]
public sealed class MeOrdersController(OrderReadService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet] public async Task<IActionResult> List([FromQuery] OrderListQuery query, CancellationToken ct) => Ok(await service.ListAsync(query, Actor, false, ct));
    [HttpGet("{id}")] public async Task<IActionResult> Detail(string id, CancellationToken ct) => Ok(await service.DetailAsync(OrderReadService.Id(id), Actor, false, ct));
}
