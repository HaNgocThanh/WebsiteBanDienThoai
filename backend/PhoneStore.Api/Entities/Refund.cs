namespace PhoneStore.Api.Entities;

public class Refund
{
    public long Id { get; set; }
    public long PaymentId { get; set; }
    public RefundStatus Status { get; set; }
    public decimal MerchandiseAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal Amount { get; private set; }
    public string Reason { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public byte[] RequestHash { get; set; } = [];
    public Guid? CreatedByUserId { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public byte[] Version { get; set; } = [];

    public Payment Payment { get; set; } = null!;
    public ApplicationUser? CreatedByUser { get; set; }
    public ApplicationUser? CompletedByUser { get; set; }
}

