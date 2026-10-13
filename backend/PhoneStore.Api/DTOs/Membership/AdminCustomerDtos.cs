using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace PhoneStore.Api.DTOs.Membership;
public sealed record AdminCustomerProfileDto(Guid UserId, string Email, string FullName, string? Phone, bool IsActive, bool EmailConfirmed, bool CanChangeStatus, DateTime CreatedAt, string Version);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminCustomerUpdate
{
    [Required, StringLength(150)] public string FullName { get; init; } = "";
    [StringLength(30)] public string? Phone { get; init; }
    [JsonRequired] public bool IsActive { get; init; }
    [Required, StringLength(100)] public string Version { get; init; } = "";
    public Guid OperationKey { get; init; }
}
public sealed record CustomerUpdateReceipt(string Version);
