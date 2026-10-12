using System.ComponentModel.DataAnnotations;
namespace PhoneStore.Api.DTOs.OrderManagement;
public sealed class AfterSaleRequest
{
    [Required, StringLength(300)] public string Reason { get; init; } = "";
    public Guid OperationKey { get; init; }
    public string? Decision { get; init; }
    public bool GoodsRecovered { get; init; }
    [StringLength(150)] public string? Reference { get; init; }
    public decimal MerchandiseAmount { get; init; }
    public decimal ShippingAmount { get; init; }
}
public sealed record AfterSaleReceipt(string OrderId, string Status, string Version, string? ResourceId, string? ResourceVersion);
public sealed record CancellationDto(string Id, string Status, string Reason, string Version, string? ReviewNote, DateTime CreatedAt);
public sealed record RefundDetailDto(string Id, string PaymentId, string Status, decimal MerchandiseAmount, decimal ShippingAmount, string Version, string? Reason, string? Reference);
public sealed record AfterSaleDto(string OrderId, string Status, string Version, bool CanCancel, bool CanRequest, IReadOnlyList<CancellationDto> Requests, IReadOnlyList<RefundDetailDto> Refunds);
