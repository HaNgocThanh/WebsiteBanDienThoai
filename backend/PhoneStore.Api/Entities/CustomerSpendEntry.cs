namespace PhoneStore.Api.Entities;

public class CustomerSpendEntry
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public long OrderId { get; set; }
    public long? RefundId { get; set; }
    public SpendEntryKind Kind { get; set; }
    public decimal Amount { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public ApplicationUser User { get; set; } = null!;
    public Order Order { get; set; } = null!;
    public Refund? Refund { get; set; }
}

