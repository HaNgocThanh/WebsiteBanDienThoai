namespace PhoneStore.Api.Entities;

public class Address
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string? Locality { get; set; }
    public string Province { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "VN";
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ApplicationUser User { get; set; } = null!;
}

