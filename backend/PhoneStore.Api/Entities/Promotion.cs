namespace PhoneStore.Api.Entities;

public class Promotion
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PromotionStatus Status { get; set; }
    public PromotionProductScope ProductScope { get; set; }
    public PromotionAudienceScope AudienceScope { get; set; }
    public DiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public decimal? MaxDiscountPerUnit { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public byte[] Version { get; set; } = [];

    public ICollection<PromotionProduct> Products { get; set; } = new List<PromotionProduct>();
    public ICollection<PromotionCustomerTier> CustomerTiers { get; set; } = new List<PromotionCustomerTier>();
}

