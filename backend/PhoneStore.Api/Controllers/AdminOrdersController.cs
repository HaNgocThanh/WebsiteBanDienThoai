using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhoneStore.Api.DTOs.OrderManagement;
using PhoneStore.Api.Services.OrderManagement;
using PhoneStore.Api.Services.Checkout;
namespace PhoneStore.Api.Controllers;
[ApiController, Route("api/v1/admin/orders"), Authorize(Policy = "Admin"), ServiceFilter(typeof(CheckoutExceptionFilter))]
public sealed class AdminOrdersController(OrderReadService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet] public async Task<IActionResult> List([FromQuery] OrderListQuery query, CancellationToken ct) => Ok(await service.ListAsync(query, null, true, ct));
    [HttpGet("{id}")] public async Task<IActionResult> Detail(string id, CancellationToken ct) => Ok(await service.DetailAsync(OrderReadService.Id(id), null, true, ct));
    [HttpPost("{id}/notes")] public async Task<IActionResult> Note(string id, InternalNoteRequest request, CancellationToken ct) => Ok(await service.NoteAsync(OrderReadService.Id(id), Actor, request, ct));
}
