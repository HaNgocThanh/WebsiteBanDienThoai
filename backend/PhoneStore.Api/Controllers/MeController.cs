using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.Profile;
using PhoneStore.Api.Services;

namespace PhoneStore.Api.Controllers;

[ApiController, Authorize(Policy = "Customer"), Route("api/v1/me")]
public sealed class MeController(ProfileService profiles) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet] public async Task<IActionResult> Read(CancellationToken cancellationToken) => Ok(await profiles.ReadAsync(UserId, cancellationToken));
    [HttpPatch] public async Task<IActionResult> Update(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await profiles.UpdateAsync(UserId, request, cancellationToken);
        if (result.Succeeded) return NoContent();
        var conflict = result.Errors.Any(x => x.Code == "ConcurrencyFailure");
        return Problem(statusCode: conflict ? 409 : 400, title: conflict ? "Thông tin đã thay đổi. Vui lòng thử lại." : "Vui lòng kiểm tra họ tên/số điện thoại.", extensions: new Dictionary<string, object?> { ["code"] = conflict ? "CONFLICT" : "VALIDATION_ERROR" });
    }
    [HttpGet("addresses")] public async Task<IActionResult> Addresses(CancellationToken cancellationToken) => Ok(await profiles.AddressesAsync(UserId, cancellationToken));
    [HttpGet("addresses/{id}")]
    public async Task<IActionResult> Address(string id, CancellationToken cancellationToken)
    {
        if (!ApiContract.TryParseId(id, out var parsed)) return NotFound();
        return await profiles.AddressAsync(UserId, parsed, cancellationToken) is { } address ? Ok(address) : NotFound();
    }
    [HttpPost("addresses")]
    public async Task<IActionResult> Create(AddressRequest request, CancellationToken cancellationToken)
    {
        var address = await profiles.WriteAddressAsync(UserId, null, request, cancellationToken);
        return Created("/api/v1/me/addresses/" + address!.Id, address);
    }
    [HttpPut("addresses/{id}")]
    public async Task<IActionResult> UpdateAddress(string id, AddressRequest request, CancellationToken cancellationToken)
    {
        if (!ApiContract.TryParseId(id, out var parsed)) return NotFound();
        return await profiles.WriteAddressAsync(UserId, parsed, request, cancellationToken) is { } address ? Ok(address) : NotFound();
    }
    [HttpDelete("addresses/{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken) => ApiContract.TryParseId(id, out var parsed) && await profiles.DeleteAddressAsync(UserId, parsed, cancellationToken) ? NoContent() : NotFound();
}
