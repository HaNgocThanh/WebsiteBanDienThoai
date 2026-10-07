namespace PhoneStore.Api.Entities;

public class OrderItem
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public long VariantId { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string SkuSnapshot { get; set; } = string.Empty;
    public string VariantSnapshot { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitDiscount { get; set; }
    public decimal LineTotal { get; private set; }
    public long? PromotionId { get; set; }
    public string? PromotionNameSnapshot { get; set; }
    public string? PromotionRuleSnapshot { get; set; }

    public Order Order { get; set; } = null!;
    public ProductVariant Variant { get; set; } = null!;
    public Promotion? Promotion { get; set; }
    public StockReservation? StockReservation { get; set; }
    public ICollection<InventoryMovement> InventoryMovements { get; set; } = new List<InventoryMovement>();
    public Review? Review { get; set; }
}

