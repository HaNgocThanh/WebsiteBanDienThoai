using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.Reviews;
using PhoneStore.Api.Services.Checkout;
using PhoneStore.Api.Services.Reviews;
namespace PhoneStore.Api.Controllers;
[ApiController,Authorize(Policy="Customer"),Route("api/v1/me"),ServiceFilter(typeof(CheckoutExceptionFilter)),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class MyReviewsController(ReviewService service):ControllerBase {
 private Guid UserId=>Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 private static long Id(string id)=>ApiContract.TryParseId(id,out var value)?value:throw new CheckoutException(404,"NOT_FOUND","Không tìm thấy đánh giá.");
 [HttpGet("orders/{id}/reviews")]public async Task<IActionResult> Mine(string id,CancellationToken ct)=>Ok(await service.Mine(Id(id),UserId,ct));
 [HttpPost("order-items/{id}/review")]public async Task<IActionResult> Create(string id,ReviewInput input,CancellationToken ct){var result=await service.Create(Id(id),UserId,input,ct);return Created("/api/v1/me/reviews/"+result.Id,result);}
 [HttpPatch("reviews/{id}")]public async Task<IActionResult> Edit(string id,ReviewInput input,CancellationToken ct)=>Ok(await service.Edit(Id(id),UserId,input,Request.Headers.IfMatch.FirstOrDefault(),ct));
}
[ApiController,Authorize(Policy="Admin"),Route("api/v1/admin/reviews"),ServiceFilter(typeof(CheckoutExceptionFilter)),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class AdminReviewsController(ReviewService service):ControllerBase {
 private Guid Actor=>Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 private static long Id(string id)=>ApiContract.TryParseId(id,out var value)?value:throw new CheckoutException(404,"NOT_FOUND","Không tìm thấy đánh giá.");
 [HttpGet]public async Task<IActionResult> List([FromQuery]PageQuery pagination,string? status,CancellationToken ct)=>Ok(await service.Admin(pagination,status,ct));
 [HttpPost("{id}/moderation")]public async Task<IActionResult> Moderate(string id,ReviewModeration input,CancellationToken ct)=>Ok(await service.Moderate(Id(id),Actor,Request.Headers.IfMatch.FirstOrDefault(),input,null,ct));
 [HttpPost("{id}/reply")]public async Task<IActionResult> Reply(string id,ReviewReply input,CancellationToken ct)=>Ok(await service.Moderate(Id(id),Actor,Request.Headers.IfMatch.FirstOrDefault(),null,input,ct));
}
[ApiController,AllowAnonymous,Route("api/v1/products/{id}/reviews"),ServiceFilter(typeof(CheckoutExceptionFilter)),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class PublicReviewsController(ReviewService service):ControllerBase {
 [HttpGet]public async Task<IActionResult> List(string id,[FromQuery]PageQuery pagination,CancellationToken ct)=>ApiContract.TryParseId(id,out var product)?Ok(await service.Public(product,pagination,ct)):NotFound();
}
