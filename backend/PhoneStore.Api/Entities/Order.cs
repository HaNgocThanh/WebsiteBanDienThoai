namespace PhoneStore.Api.Entities;

public class Order
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string CustomerEmail { get; set; } = string.Empty;
    public string NormalizedCustomerEmail { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string? Locality { get; set; }
    public string Province { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "VN";
    public OrderStatus Status { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string Currency { get; set; } = "VND";
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal GrandTotal { get; set; }
    public long? TierIdAtPurchase { get; set; }
    public CustomerTierCode? TierCodeSnapshot { get; set; }
    public string? CustomerNote { get; set; }
    public DateTime? PaymentDueAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public byte[] Version { get; set; } = [];

    public ApplicationUser? User { get; set; }
    public CustomerTier? TierAtPurchase { get; set; }
    public ICollection<CustomerSpendEntry> SpendEntries { get; set; } = new List<CustomerSpendEntry>();
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<OrderStatusHistory> StatusHistories { get; set; } = new List<OrderStatusHistory>();
    public ICollection<OrderCancellationRequest> CancellationRequests { get; set; } = new List<OrderCancellationRequest>();
    public OrderAccountConsent? AccountConsent { get; set; }
    public ICollection<OrderAccessToken> AccessTokens { get; set; } = new List<OrderAccessToken>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public IdempotencyRequest? IdempotencyRequest { get; set; }
}

