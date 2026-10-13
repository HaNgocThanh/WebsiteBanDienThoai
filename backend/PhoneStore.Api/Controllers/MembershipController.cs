using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.Membership;
using PhoneStore.Api.Services.Membership;
using PhoneStore.Api.Services.Checkout;
namespace PhoneStore.Api.Controllers;

[ApiController, ServiceFilter(typeof(CheckoutExceptionFilter)), Route("api/v1")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class MembershipController(MembershipService service, AdminCustomerService customers, PhoneStore.Api.Services.ProfileService profiles) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("membership/tiers"), AllowAnonymous]
    public async Task<IActionResult> PublicTiers(CancellationToken ct) => Ok(await service.ConfigurationAsync(ct));
    [HttpGet("me/membership"), Authorize(Policy = "Customer")]
    public async Task<IActionResult> Membership(CancellationToken ct) => Ok(await service.ReadAsync(UserId, ct));
    [HttpGet("me/membership/spend"), Authorize(Policy = "Customer")]
    public async Task<IActionResult> Spend([FromQuery] PageQuery page, CancellationToken ct) => Ok(await service.SpendAsync(UserId, page, ct));
    [HttpGet("me/membership/history"), Authorize(Policy = "Customer")]
    public async Task<IActionResult> History([FromQuery] PageQuery page, CancellationToken ct) => Ok(await service.HistoryAsync(UserId, page, ct));
    [HttpGet("admin/customer-tiers"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Configuration(CancellationToken ct) => Ok(await service.ConfigurationAsync(ct));
    [HttpGet("admin/customers"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Customers([FromQuery] PageQuery page, string? search, string? tierId, bool? isActive, CancellationToken ct) => Ok(await service.CustomersAsync(page, search, tierId, isActive, ct));
    [HttpGet("admin/customers/{id:guid}"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Customer(Guid id, CancellationToken ct) => Ok(await service.ReadAsync(id, ct));
    [HttpGet("admin/customers/{id:guid}/profile"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Profile(Guid id, CancellationToken ct) => Ok(await customers.ReadAsync(id, UserId, ct));
    [HttpPut("admin/customers/{id:guid}/profile"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> UpdateProfile(Guid id, AdminCustomerUpdate input, CancellationToken ct) => Ok(await customers.UpdateAsync(id, UserId, input, ct));
    [HttpGet("admin/customers/{id:guid}/addresses"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> Addresses(Guid id, CancellationToken ct) { await customers.ReadAsync(id, UserId, ct); return Ok(await profiles.AddressesAsync(id, ct)); }
    [HttpGet("admin/customers/{id:guid}/spend"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> CustomerSpend(Guid id, [FromQuery] PageQuery page, CancellationToken ct) => Ok(await service.SpendAsync(id, page, ct));
    [HttpGet("admin/customers/{id:guid}/history"), Authorize(Policy = "Admin")]
    public async Task<IActionResult> CustomerHistory(Guid id, [FromQuery] PageQuery page, CancellationToken ct) => Ok(await service.HistoryAsync(id, page, ct));
}
