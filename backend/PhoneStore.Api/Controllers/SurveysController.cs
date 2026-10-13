using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.Surveys;
using PhoneStore.Api.Services.Checkout;
using PhoneStore.Api.Services.Surveys;
namespace PhoneStore.Api.Controllers;
[ApiController,Authorize(Policy="Customer"),Route("api/v1/surveys"),ServiceFilter(typeof(CheckoutExceptionFilter)),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class SurveysController(SurveyService service):ControllerBase {
 private Guid Actor=>Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 private static long Id(string id)=>ApiContract.TryParseId(id,out var value)?value:throw new CheckoutException(404,"NOT_FOUND","Không tìm thấy khảo sát.");
 [HttpGet]public async Task<IActionResult> List([FromQuery]PageQuery pagination,CancellationToken ct)=>Ok(await service.List(Actor,false,pagination,ct));
 [HttpGet("{id}")]public async Task<IActionResult> Read(string id,CancellationToken ct)=>Ok(await service.Read(Id(id),Actor,false,ct));
 [HttpGet("{id}/my-response")]public async Task<IActionResult> Status(string id,CancellationToken ct)=>Ok(await service.Status(Id(id),Actor,ct));
 [HttpPost("{id}/responses")]public async Task<IActionResult> Submit(string id,SurveySubmit input,CancellationToken ct)=>Ok(await service.Submit(Id(id),Actor,input,ct));
}
[ApiController,Authorize(Policy="Admin"),Route("api/v1/admin/surveys"),ServiceFilter(typeof(CheckoutExceptionFilter)),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class AdminSurveysController(SurveyService service):ControllerBase {
 private Guid Actor=>Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 private static long Id(string id)=>ApiContract.TryParseId(id,out var value)?value:throw new CheckoutException(404,"NOT_FOUND","Không tìm thấy khảo sát.");
 [HttpGet]public async Task<IActionResult> List([FromQuery]PageQuery pagination,CancellationToken ct)=>Ok(await service.List(Actor,true,pagination,ct));
 [HttpGet("{id}")]public async Task<IActionResult> Read(string id,CancellationToken ct)=>Ok(await service.Read(Id(id),Actor,true,ct));
 [HttpPost]public async Task<IActionResult> Create(SurveyInput input,CancellationToken ct){var result=await service.Write(null,Actor,null,input,"Create",input.OperationKey,ct);return Created("/api/v1/admin/surveys/"+result.Id,result);}
 [HttpPatch("{id}")]public async Task<IActionResult> Edit(string id,SurveyInput input,CancellationToken ct)=>Ok(await service.Write(Id(id),Actor,Request.Headers.IfMatch.FirstOrDefault(),input,"Edit",input.OperationKey,ct));
 [HttpPost("{id}/publish")]public async Task<IActionResult> Publish(string id,SurveyAction input,CancellationToken ct)=>Ok(await service.Write(Id(id),Actor,Request.Headers.IfMatch.FirstOrDefault(),null,"Publish",input.OperationKey,ct));
 [HttpPost("{id}/close")]public async Task<IActionResult> Close(string id,SurveyAction input,CancellationToken ct)=>Ok(await service.Write(Id(id),Actor,Request.Headers.IfMatch.FirstOrDefault(),null,"Close",input.OperationKey,ct));
 [HttpGet("{id}/results")]public async Task<IActionResult> Results(string id,CancellationToken ct)=>Ok(await service.Results(Id(id),Actor,ct));
 [HttpGet("{id}/results/text")]public async Task<IActionResult> Text(string id,string questionId,[FromQuery]PageQuery pagination,CancellationToken ct)=>Ok(await service.TextResults(Id(id),Id(questionId),Actor,pagination,ct));
}
