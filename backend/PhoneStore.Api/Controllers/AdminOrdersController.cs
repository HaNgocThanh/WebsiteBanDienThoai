using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhoneStore.Api.DTOs.OrderManagement;
using PhoneStore.Api.Services.OrderManagement;
using PhoneStore.Api.Services.Checkout;
namespace PhoneStore.Api.Controllers;
[ApiController, Route("api/v1/admin/orders"), Authorize(Policy = "Admin"), ServiceFilter(typeof(CheckoutExceptionFilter))]
public sealed class AdminOrdersController(OrderReadService service, OrderTransitionService transitions) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet] public async Task<IActionResult> List([FromQuery] OrderListQuery query, CancellationToken ct) => Ok(await service.ListAsync(query, null, true, ct));
    [HttpGet("{id}")] public async Task<IActionResult> Detail(string id, CancellationToken ct) => Ok(await service.DetailAsync(OrderReadService.Id(id), null, true, ct));
    [HttpPost("{id}/notes")] public async Task<IActionResult> Note(string id, InternalNoteRequest request, CancellationToken ct) => Ok(await service.NoteAsync(OrderReadService.Id(id), Actor, request, ct));
    [HttpPost("{id}/transitions")] public async Task<IActionResult> Transition(string id, OrderTransitionRequest request, CancellationToken ct)
    {
        var result = await transitions.TransitionAsync(OrderReadService.Id(id), Actor, Request.Headers.IfMatch.Count == 0 ? null : Request.Headers.IfMatch.ToString(), request, ct);
        Response.Headers.ETag = PhoneStore.Api.DTOs.Common.ApiContract.ETag(Convert.FromBase64String(result.Version));
        return Ok(result);
    }
}
