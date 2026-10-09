using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhoneStore.Api.DTOs.OrderManagement;
using PhoneStore.Api.Services.OrderManagement;
using PhoneStore.Api.Services.Checkout;
namespace PhoneStore.Api.Controllers;
[ApiController, Route("api/v1/me/notifications"), Authorize(Roles = "Customer,Admin"), ServiceFilter(typeof(CheckoutExceptionFilter))]
public sealed class NotificationsController(OrderReadService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet] public async Task<IActionResult> List([FromQuery] NotificationQuery query, CancellationToken ct) => Ok(await service.NotificationsAsync(Actor, query, false, ct));
    [HttpPost("{id}/read")] public async Task<IActionResult> Read(string id, CancellationToken ct) { await service.ReadNotificationAsync(Actor, OrderReadService.Id(id), false, ct); return NoContent(); }
}
