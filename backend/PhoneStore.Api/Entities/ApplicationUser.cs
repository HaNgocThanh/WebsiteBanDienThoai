using Microsoft.AspNetCore.Identity;

namespace PhoneStore.Api.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Address> Addresses { get; set; } = new List<Address>();
    public CustomerProfile? CustomerProfile { get; set; }
    public ICollection<CustomerSpendEntry> SpendEntries { get; set; } = new List<CustomerSpendEntry>();
    public ICollection<CustomerTierHistory> TierHistories { get; set; } = new List<CustomerTierHistory>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<SurveyResponse> SurveyResponses { get; set; } = new List<SurveyResponse>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}

