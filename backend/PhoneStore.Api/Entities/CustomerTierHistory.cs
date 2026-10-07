namespace PhoneStore.Api.Entities;

public class CustomerTierHistory
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public long? PreviousTierId { get; set; }
    public long NewTierId { get; set; }
    public decimal EligibleSpend { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public ApplicationUser User { get; set; } = null!;
    public CustomerTier? PreviousTier { get; set; }
    public CustomerTier NewTier { get; set; } = null!;
}

