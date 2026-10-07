namespace PhoneStore.Api.Entities;

public class OrderAccountConsent
{
    public long OrderId { get; set; }
    public bool Accepted { get; set; }
    public string ConsentTextVersion { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; }

    public Order Order { get; set; } = null!;
}

