namespace PhoneStore.Api.Entities;

public class CustomerProfile
{
    public Guid UserId { get; set; }
    public long TierId { get; set; }
    public decimal EligibleSpend { get; set; }
    public DateTime TierCalculatedAt { get; set; }
    public byte[] Version { get; set; } = [];

    public ApplicationUser User { get; set; } = null!;
    public CustomerTier Tier { get; set; } = null!;
}

