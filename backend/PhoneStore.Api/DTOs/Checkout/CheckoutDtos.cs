using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PhoneStore.Api.DTOs.Pricing;

namespace PhoneStore.Api.DTOs.Checkout;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PlaceOrderRequest(
    [Required, MinLength(1), MaxLength(100)] List<QuoteItemRequest> Items,
    [Required, MaxLength(150)] string RecipientName,
    [Required, MaxLength(30)] string Phone,
    [Required, MaxLength(300)] string AddressLine,
    [Required, MaxLength(100)] string Province,
    [Required, RegularExpression("^VN$")] string CountryCode,
    [Required, RegularExpression("^(COD|BankTransfer)$")] string PaymentMethod,
    [Required, RegularExpression("^[0-9a-f]{64}$")] string QuoteHash,
    [EmailAddress, MaxLength(256)] string? Email = null,
    bool? CreateAccountConsent = null,
    [MaxLength(30)] string? ConsentTextVersion = null,
    [MaxLength(1000)] string? Note = null,
    [MaxLength(150)] string? Locality = null,
    [RegularExpression("^[0-9]{2}$")] string? ProvinceCode = null,
    [RegularExpression("^[0-9]{5}$")] string? WardCode = null);
public sealed record CheckoutSessionDto(string CheckoutKey, DateTime ExpiresAt);
public sealed record CheckoutResultDto(string State, DateTime ExpiresAt, PlacedOrderDto? Order);
public sealed record PlacedOrderDto(string Id, string OrderNumber, string Status, decimal GrandTotal,
    string Currency, string PaymentMethod, DateTime? PaymentDueAt);
public sealed record PlaceOrderResult(PlacedOrderDto Order, bool Replayed);
