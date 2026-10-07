namespace PhoneStore.Api.Entities;

public class OrderStatusHistory
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public OrderStatus? FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public Guid? ActorUserId { get; set; }
    public OrderActorType ActorType { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }

    public Order Order { get; set; } = null!;
    public ApplicationUser? ActorUser { get; set; }
}

