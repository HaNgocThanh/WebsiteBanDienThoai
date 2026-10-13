using System.ComponentModel.DataAnnotations;
namespace PhoneStore.Api.DTOs.Membership;

public sealed record TierDto(string Code, string Name, decimal MinimumSpend, string? Version, string? Id = null);
public sealed record CustomerMembershipDto(Guid UserId, string FullName, string Email, string? TierCode, decimal EligibleSpend, string? Phone, bool IsActive, bool EmailConfirmed);
public sealed record TierConfigurationDto(bool Enabled, IReadOnlyList<TierDto> Tiers);
public sealed class TierInput
{
    [Required] public string Code { get; init; } = "";
    public decimal MinimumSpend { get; init; }
    public string? Version { get; init; }
}
public sealed class TierConfigurationRequest
{
    [Required, MinLength(4), MaxLength(4)] public TierInput[] Tiers { get; init; } = [];
    public Guid OperationKey { get; init; }
    [Required, StringLength(300, MinimumLength = 1)] public string Reason { get; init; } = "";
}
public sealed record MembershipDto(bool Enabled, string? TierCode, decimal EligibleSpend, TierDto? NextTier, decimal RemainingSpend, IReadOnlyList<TierDto> Tiers);
public sealed record SpendDto(string Id, string OrderId, string? RefundId, string Kind, decimal Amount, DateTime CreatedAt);
public sealed record TierHistoryDto(string Id, string? PreviousTierCode, string? NewTierCode, decimal EligibleSpend, string Reason, DateTime CreatedAt);
