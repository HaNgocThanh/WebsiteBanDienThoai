using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.Inventory;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.InventoryManagement;

namespace PhoneStore.Api.Controllers;

[ApiController, Route("api/v1/admin/inventory"), Authorize(Policy = "Admin"), ServiceFilter(typeof(InventoryExceptionFilter))]
public sealed class AdminInventoryController(InventoryService inventory) : ControllerBase
{
    private static long Id(string value) => ApiContract.TryParseId(value, out var id) ? id : throw new InventoryException(400, "INVALID_ID", "ID không hợp lệ.");
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new InventoryException(401, "UNAUTHENTICATED", "Cần đăng nhập.");
    [HttpGet] public async Task<IActionResult> List([FromQuery] InventoryQuery query, CancellationToken ct) => Ok(await inventory.ListAsync(query, ct));
    [HttpGet("{variantId}")] public async Task<IActionResult> Detail(string variantId, CancellationToken ct) => Ok(await inventory.DetailAsync(Id(variantId), ct));
    [HttpGet("{variantId}/movements")] public async Task<IActionResult> Movements(string variantId, [FromQuery] InventoryQuery query, CancellationToken ct) => Ok(await inventory.MovementsAsync(Id(variantId), query, ct));
    private IActionResult Result(InventoryCommandResult result) => result.IsReplay ? Ok(result) : Created($"/api/v1/admin/inventory/{result.Inventory.VariantId}/movements", result);
    [HttpPost("{variantId}/receipts")]
    public async Task<IActionResult> Receive(string variantId, ReceiptRequest request, CancellationToken ct) => Result(await inventory.WriteAsync(Id(variantId), Actor, InventoryMovementKind.Receive, request.Quantity, request.Reason, request.OperationKey, ct));
    [HttpPost("{variantId}/adjustments")]
    public async Task<IActionResult> Adjust(string variantId, AdjustmentRequest request, CancellationToken ct) => Result(await inventory.WriteAsync(Id(variantId), Actor, InventoryMovementKind.Adjust, request.QuantityDelta, request.Reason, request.OperationKey, ct));
}
