namespace PhoneStore.Api.Entities;

public class CustomerTier
{
    public long Id { get; set; }
    public CustomerTierCode Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal MinimumSpend { get; set; }
    public int Rank { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public byte[] Version { get; set; } = [];

    public ICollection<CustomerProfile> CustomerProfiles { get; set; } = new List<CustomerProfile>();
    public ICollection<PromotionCustomerTier> Promotions { get; set; } = new List<PromotionCustomerTier>();
}

