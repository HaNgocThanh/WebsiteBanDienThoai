namespace PhoneStore.Api.Entities;

public class OrderAccessToken
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public OrderAccessTokenPurpose Purpose { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public byte[] Version { get; set; } = [];

    public Order Order { get; set; } = null!;
}

