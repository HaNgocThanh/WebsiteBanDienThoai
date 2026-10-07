namespace PhoneStore.Api.Entities;

public class PromotionCustomerTier
{
    public long PromotionId { get; set; }
    public long TierId { get; set; }

    public Promotion Promotion { get; set; } = null!;
    public CustomerTier Tier { get; set; } = null!;
}

