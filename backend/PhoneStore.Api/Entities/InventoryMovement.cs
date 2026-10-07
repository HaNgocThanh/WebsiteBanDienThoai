namespace PhoneStore.Api.Entities;

public class InventoryMovement
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public long? OrderItemId { get; set; }
    public Guid? ActorUserId { get; set; }
    public InventoryMovementKind Kind { get; set; }
    public int OnHandDelta { get; set; }
    public int ReservedDelta { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string EventKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public byte[]? RequestHash { get; set; }

    public ProductVariant Variant { get; set; } = null!;
    public OrderItem? OrderItem { get; set; }
    public ApplicationUser? ActorUser { get; set; }
}

