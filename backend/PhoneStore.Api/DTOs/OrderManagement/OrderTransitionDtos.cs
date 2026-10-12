using System.ComponentModel.DataAnnotations;
namespace PhoneStore.Api.DTOs.OrderManagement;
public sealed class OrderTransitionRequest
{
    [Required, StringLength(20)] public string TargetStatus { get; init; } = "";
    [Required, StringLength(300, MinimumLength = 1)] public string Reason { get; init; } = "";
    public Guid OperationKey { get; init; }
}
public sealed record OrderTransitionDto(string OrderId, string Status, string Version, string HistoryId, DateTime OccurredAt);
