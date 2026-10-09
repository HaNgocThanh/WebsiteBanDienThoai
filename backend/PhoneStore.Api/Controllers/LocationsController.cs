using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhoneStore.Api.Services;

namespace PhoneStore.Api.Controllers;

[ApiController, AllowAnonymous, Route("api/v1/locations")]
public sealed class LocationsController(AdministrativeLocations locations) : ControllerBase
{
    [HttpGet("provinces")]
    public IActionResult Provinces() => Ok(new { locations.Snapshot.AsOf, items = locations.Provinces });
    [HttpGet("provinces/{code}/wards")]
    public IActionResult Wards(string code)
    {
        if (!locations.Snapshot.Provinces.Any(p => p.Code == code)) return NotFound(new ProblemDetails { Status = 404, Title = "Không tìm thấy tỉnh/thành phố.", Extensions = { ["code"] = "NOT_FOUND", ["traceId"] = HttpContext.TraceIdentifier } });
        return Ok(new { locations.Snapshot.AsOf, items = locations.Wards(code) });
    }
}
