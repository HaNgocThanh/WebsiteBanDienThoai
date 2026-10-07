namespace PhoneStore.Api.Entities;

public class IdempotencyRequest
{
    public Guid Id { get; set; }
    public Guid? OwnerUserId { get; set; }
    public byte[]? GuestSessionHash { get; set; }
    public byte[]? RequestHash { get; set; }
    public IdempotencyRequestStatus Status { get; set; }
    public long? OrderId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public byte[] Version { get; set; } = [];

    public ApplicationUser? OwnerUser { get; set; }
    public Order? Order { get; set; }
}

