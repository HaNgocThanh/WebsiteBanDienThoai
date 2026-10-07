namespace PhoneStore.Api.Entities;

public class OrderCancellationRequest
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public CancellationRequestStatus Status { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public byte[] Version { get; set; } = [];

    public Order Order { get; set; } = null!;
    public ApplicationUser? RequestedByUser { get; set; }
    public ApplicationUser? ReviewedByUser { get; set; }
}

