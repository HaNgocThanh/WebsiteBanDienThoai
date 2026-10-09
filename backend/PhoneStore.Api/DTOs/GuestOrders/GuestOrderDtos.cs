using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PhoneStore.Api.DTOs.GuestOrders;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrderAccessRequest(
    [Required, MaxLength(30)] string OrderNumber,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, RegularExpression("^(ViewOrder|CancelOrder|ClaimOrder)$")] string Purpose);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrderAccessExchange(
    [Required, RegularExpression("^[0-9a-f]{64}$")] string Token,
    [Required, RegularExpression("^(ViewOrder|CancelOrder)$")] string Purpose);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrderClaimRequest([Required, RegularExpression("^[0-9a-f]{64}$")] string Token);
public sealed record GuestOrderItemDto(string Id, string VariantId, string ProductName, string Sku,
    string Variant, int Quantity, decimal UnitPrice, decimal UnitDiscount, decimal LineTotal);
public sealed record GuestOrderHistoryDto(string? FromStatus, string ToStatus, DateTime CreatedAt);
public sealed record GuestOrderDto(string Id, string OrderNumber, string Status, string PaymentMethod,
    string Currency, decimal Subtotal, decimal DiscountTotal, decimal ShippingFee, decimal GrandTotal,
    string RecipientName, string Phone, string AddressLine, string? Locality, string Province,
    string CountryCode, string? Note, DateTime CreatedAt, DateTime? PaymentDueAt,
    bool CreateAccountConsent, IReadOnlyList<GuestOrderItemDto> Items, IReadOnlyList<GuestOrderHistoryDto> Timeline);
public sealed record GuestSessionDto(string Purpose, DateTime ExpiresAt);
public sealed record OrderClaimDto(string Id, string OrderNumber);
