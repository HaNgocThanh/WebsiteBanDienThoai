using System.ComponentModel.DataAnnotations;
namespace PhoneStore.Api.Services.OrderManagement;
public sealed class OrderExpiryOptions
{
    public bool Enabled { get; set; } = true;
    [Range(1, 300)] public int PollIntervalSeconds { get; set; } = 30;
    [Range(1, 500)] public int BatchSize { get; set; } = 100;
}
