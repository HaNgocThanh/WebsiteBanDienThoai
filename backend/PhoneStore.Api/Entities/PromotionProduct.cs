namespace PhoneStore.Api.Entities;

public class PromotionProduct
{
    public long PromotionId { get; set; }
    public long ProductId { get; set; }

    public Promotion Promotion { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

