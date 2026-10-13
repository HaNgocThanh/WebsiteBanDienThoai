using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhoneStore.Api.DTOs.Promotions;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.Services.Promotions;
using PhoneStore.Api.Services.Checkout;
namespace PhoneStore.Api.Controllers;
[ApiController, Authorize(Policy = "Admin"), Route("api/v1/admin/promotions"), ServiceFilter(typeof(CheckoutExceptionFilter)), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PromotionsController(PromotionService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet] public async Task<IActionResult> List([FromQuery] PageQuery page, string? status, string? search, CancellationToken ct) => Ok(await service.ListAsync(page, status, search, ct));
    [HttpGet("{id}")] public async Task<IActionResult> Read(string id, CancellationToken ct) => ApiContract.TryParseId(id, out var value) ? Ok(await service.ReadAsync(value, ct)) : NotFound();
    [HttpPost] public async Task<IActionResult> Create(PromotionRequest input, CancellationToken ct) { var result = await service.WriteAsync(null, Actor, null, input, "Create", input.OperationKey, ct); return Created("/api/v1/admin/promotions/" + result.Id, result); }
    [HttpPatch("{id}")] public async Task<IActionResult> Edit(string id, PromotionRequest input, CancellationToken ct) => Ok(await service.WriteAsync(Parse(id), Actor, Request.Headers.IfMatch.FirstOrDefault(), input, "Edit", input.OperationKey, ct));
    [HttpPost("{id}/publish")] public Task<IActionResult> Publish(string id, PromotionAction input, CancellationToken ct) => Action(id, input, "Publish", ct);
    [HttpPost("{id}/disable")] public Task<IActionResult> Disable(string id, PromotionAction input, CancellationToken ct) => Action(id, input, "Disable", ct);
    [HttpDelete("{id}")] public Task<IActionResult> Delete(string id, PromotionAction input, CancellationToken ct) => Action(id, input, "Delete", ct);
    private async Task<IActionResult> Action(string id, PromotionAction input, string action, CancellationToken ct) => Ok(await service.WriteAsync(Parse(id), Actor, Request.Headers.IfMatch.FirstOrDefault(), null, action, input.OperationKey, ct));
    private static long Parse(string id) => ApiContract.TryParseId(id, out var value) ? value : throw new CheckoutException(404, "NOT_FOUND", "Không tìm thấy chương trình.");
}
[ApiController, AllowAnonymous, Route("api/v1/catalog/promotions"), ServiceFilter(typeof(CheckoutExceptionFilter)), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PromotionOffersController(PromotionService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Read([FromQuery] string[] variantIds, CancellationToken ct) => Ok(await service.OffersAsync(variantIds, User.Identity?.IsAuthenticated == true && User.IsInRole("Customer") ? Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!) : null, ct));
}
