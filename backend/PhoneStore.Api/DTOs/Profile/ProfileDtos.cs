using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PhoneStore.Api.DTOs.Profile;

public sealed record ProfileDto(Guid UserId, string Email, string FullName, string? Phone, string? TierCode, decimal EligibleSpend);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateProfileRequest([Required, MaxLength(150)] string FullName, [MaxLength(30)] string? Phone);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddressRequest(
    [Required, MaxLength(150)] string RecipientName,
    [Required, MaxLength(30)] string Phone,
    [Required, MaxLength(300)] string AddressLine,
    [MaxLength(150)] string? Locality,
    [Required, MaxLength(150)] string Province,
    [Required, RegularExpression("^[A-Za-z]{2}$")] string CountryCode,
    bool IsDefault);
public sealed record AddressDto(string Id, string RecipientName, string Phone, string AddressLine, string? Locality, string Province, string CountryCode, bool IsDefault);
