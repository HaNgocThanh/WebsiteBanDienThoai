namespace PhoneStore.Api.Entities;

public class Payment
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public string? ProofUrl { get; set; }
    public string? Note { get; set; }
    public Guid? ConfirmedByUserId { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public byte[] Version { get; set; } = [];
    public string? OperationKey { get; set; }
    public byte[]? RequestHash { get; set; }

    public Order Order { get; set; } = null!;
    public ApplicationUser? ConfirmedByUser { get; set; }
    public ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}

