using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace PhoneStore.Api.DTOs.Payments;
public sealed class CodReceiptRequest
{
    [Range(typeof(decimal), "1", "9007199254740991")] public decimal Amount { get; init; }
    [Required, StringLength(500, MinimumLength = 1)] public string Note { get; init; } = "";
    public Guid OperationKey { get; init; }
}
public sealed record PaymentDto(string Id, string Method, string Status, decimal Amount, string Version, DateTime CreatedAt, DateTime? ConfirmedAt, string? Reference, string? Note);
public sealed record PaymentBalance(decimal Confirmed, decimal RefundPending, decimal Refunded, decimal Remaining, string Status);
public sealed record PaymentPage(IReadOnlyList<PaymentDto> Items, int Page, int PageSize, long TotalCount, PaymentBalance Summary, bool CanPay, bool CanRecordCod, string Method, bool SandboxConfigured);
public sealed record SePayField(string Name, string Value);
public sealed record SePayCheckoutDto(string Action, IReadOnlyList<SePayField> Fields, string Invoice, string PaymentId, decimal Amount);
public sealed class SePayIpnRequest
{
    [JsonPropertyName("notification_type"), Required, StringLength(50)] public string NotificationType { get; init; } = "";
    [JsonPropertyName("order")] public SePayOrder? Order { get; init; }
    [JsonPropertyName("transaction")] public SePayTransaction? Transaction { get; init; }
}
public sealed class SePayOrder
{
    [JsonPropertyName("order_invoice_number"), Required, StringLength(100)] public string Invoice { get; init; } = "";
    [JsonPropertyName("order_status"), Required, StringLength(30)] public string Status { get; init; } = "";
    [JsonPropertyName("order_currency"), Required, StringLength(3)] public string Currency { get; init; } = "";
    [JsonPropertyName("order_amount"), Required, StringLength(30)] public string Amount { get; init; } = "";
}
public sealed class SePayTransaction
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("transaction_id"), Required, StringLength(100)] public string TransactionId { get; init; } = "";
    [JsonPropertyName("payment_method"), Required, StringLength(30)] public string Method { get; init; } = "";
    [JsonPropertyName("transaction_type"), Required, StringLength(30)] public string Type { get; init; } = "";
    [JsonPropertyName("transaction_status"), Required, StringLength(30)] public string Status { get; init; } = "";
    [JsonPropertyName("transaction_currency"), Required, StringLength(3)] public string Currency { get; init; } = "";
    [JsonPropertyName("transaction_amount"), Required, StringLength(30)] public string Amount { get; init; } = "";
}
