using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.Services.Checkout;
using PhoneStore.Api.Services.Vouchers;
namespace PhoneStore.Api.Controllers;
[ApiController,Authorize(Policy="Admin"),Route("api/v1/admin/vouchers"),ServiceFilter(typeof(CheckoutExceptionFilter)),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class VouchersController(VoucherService service):ControllerBase {
 private Guid Actor=>Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 [HttpGet]public async Task<IActionResult> List([FromQuery]PageQuery page,CancellationToken ct)=>Ok(await service.List(page,ct));
 [HttpPost]public async Task<IActionResult> Create(VoucherInput input,CancellationToken ct){var v=await service.Create(input,Actor,ct);return Created("/api/v1/admin/vouchers/"+v.Id,v);}
 [HttpPost("{id}/publish")]public Task<IActionResult> Publish(string id,CancellationToken ct)=>Action(id,"Published",ct);
 [HttpPost("{id}/disable")]public Task<IActionResult> Disable(string id,CancellationToken ct)=>Action(id,"Disabled",ct);
 private async Task<IActionResult> Action(string id,string target,CancellationToken ct)=>ApiContract.TryParseId(id,out var parsed)?Ok(await service.Action(parsed,target,Request.Headers.IfMatch.FirstOrDefault(),Actor,ct)):NotFound();
}
