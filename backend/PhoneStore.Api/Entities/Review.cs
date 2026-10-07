namespace PhoneStore.Api.Entities;

public class Review
{
    public long Id { get; set; }
    public long OrderItemId { get; set; }
    public Guid UserId { get; set; }
    public byte Rating { get; set; }
    public string Content { get; set; } = string.Empty;
    public ReviewStatus Status { get; set; }
    public string? ModerationReason { get; set; }
    public Guid? ModeratedByUserId { get; set; }
    public DateTime? ModeratedAt { get; set; }
    public string? AdminReply { get; set; }
    public Guid? RepliedByUserId { get; set; }
    public DateTime? RepliedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public byte[] Version { get; set; } = [];

    public OrderItem OrderItem { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
    public ApplicationUser? ModeratedByUser { get; set; }
    public ApplicationUser? RepliedByUser { get; set; }
}

