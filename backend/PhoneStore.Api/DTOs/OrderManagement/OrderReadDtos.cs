using System.ComponentModel.DataAnnotations;
using PhoneStore.Api.DTOs.GuestOrders;
namespace PhoneStore.Api.DTOs.OrderManagement;
public sealed class OrderListQuery
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    public string? Status { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? CustomerId { get; init; }
    [StringLength(256)] public string? Email { get; init; }
}
public sealed record OrderListDto(string Id, string OrderNumber, string Status, string PaymentMethod, decimal GrandTotal, string Currency, DateTime CreatedAt, string RecipientName);
public sealed record AdminOrderListDto(string Id, string OrderNumber, string Status, string PaymentMethod, decimal GrandTotal, string Currency, DateTime CreatedAt, string RecipientName, Guid? CustomerId, string CustomerEmail);
public sealed record PaymentSummaryDto(decimal Confirmed, decimal RefundPending, decimal Refunded, decimal Remaining);
public sealed record OrderReadDto(GuestOrderDto Snapshot, string Version, PaymentSummaryDto PaymentSummary, string[] AllowedActions);
public sealed record InternalNoteDto(string Id, Guid ActorUserId, string Text, DateTime CreatedAt);
public sealed record AdminHistoryDto(string Id, string? FromStatus, string ToStatus, Guid? ActorUserId, string ActorType, string? Reason, DateTime CreatedAt);
public sealed record OrderPaymentDto(string Id, string Method, string Status, decimal Amount, DateTime CreatedAt);
public sealed record OrderRefundDto(string Id, string PaymentId, string Status, decimal MerchandiseAmount, decimal ShippingAmount, DateTime CreatedAt);
public sealed record OrderStockDto(string OrderItemId, string VariantId, int Quantity, string Status, DateTime? ExpiresAt);
public sealed record AdminOrderReadDto(GuestOrderDto Snapshot, string Version, PaymentSummaryDto PaymentSummary, string[] AllowedActions, Guid? CustomerId, string CustomerEmail, IReadOnlyList<InternalNoteDto> Notes, IReadOnlyList<AdminHistoryDto> History, IReadOnlyList<OrderPaymentDto> Payments, IReadOnlyList<OrderRefundDto> Refunds, IReadOnlyList<OrderStockDto> Reservations);
public sealed class InternalNoteRequest
{
    [Required, StringLength(500, MinimumLength = 1)] public string Text { get; init; } = "";
    public Guid OperationKey { get; init; }
}
public sealed class NotificationQuery
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    public bool UnreadOnly { get; init; }
}
public sealed record NotificationDto(string Id, string? OrderId, string Type, string Title, string Body, DateTime CreatedAt, DateTime? ReadAt);
