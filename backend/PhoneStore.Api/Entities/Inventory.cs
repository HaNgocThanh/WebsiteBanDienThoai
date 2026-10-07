namespace PhoneStore.Api.Entities;

public class Inventory
{
    public long VariantId { get; set; }
    public int OnHand { get; set; }
    public int Reserved { get; set; }
    public byte[] Version { get; set; } = [];

    // Derived from the single inventory balance; never stored separately.
    public int Available => OnHand - Reserved;

    public ProductVariant Variant { get; set; } = null!;
}

