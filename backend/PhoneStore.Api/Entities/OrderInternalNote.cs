namespace PhoneStore.Api.Entities;
public class OrderInternalNote
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid OperationKey { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Order Order { get; set; } = null!;
    public ApplicationUser ActorUser { get; set; } = null!;
}
