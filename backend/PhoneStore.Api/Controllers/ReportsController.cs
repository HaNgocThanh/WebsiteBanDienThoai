using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhoneStore.Api.DTOs.Reporting;
using PhoneStore.Api.Services.Checkout;
using PhoneStore.Api.Services.Reporting;

namespace PhoneStore.Api.Controllers;

[ApiController, Route("api/v1/admin/reports"), Authorize(Policy = "Admin"), ServiceFilter(typeof(CheckoutExceptionFilter))]
public sealed class ReportsController(ReportService reports) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] ReportQuery query, CancellationToken ct) => Ok(await reports.OverviewAsync(query, ct));
}
