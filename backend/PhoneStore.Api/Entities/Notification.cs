namespace PhoneStore.Api.Entities;

public class Notification
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public long? OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public string EventKey { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;
    public Order? Order { get; set; }
}

