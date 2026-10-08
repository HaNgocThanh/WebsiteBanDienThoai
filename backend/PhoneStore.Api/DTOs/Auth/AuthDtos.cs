using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PhoneStore.Api.DTOs.Auth;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(12), MaxLength(128)] string Password,
    [Required, MaxLength(150)] string FullName,
    [MaxLength(30)] string? Phone);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LoginRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(128)] string Password);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmailRequest([Required, EmailAddress, MaxLength(256)] string Email);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VerifyEmailRequest(Guid UserId, [Required, MaxLength(4096)] string Token);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResetPasswordRequest(Guid UserId,
    [Required, MaxLength(4096)] string Token,
    [Required, MinLength(12), MaxLength(128)] string NewPassword);

public sealed record SessionDto(Guid UserId, string Email, string FullName, string? Phone,
    bool EmailConfirmed, IReadOnlyList<string> Roles);
