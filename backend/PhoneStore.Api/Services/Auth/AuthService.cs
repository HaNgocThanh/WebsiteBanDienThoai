using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.DTOs.Auth;
using PhoneStore.Api.Entities;

namespace PhoneStore.Api.Services.Auth;

public sealed record AuthFailure(int Status, string Code, string Message, Dictionary<string, string[]>? Errors = null);

public sealed class AuthService(AppDbContext db, UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn, AuthBootstrap bootstrap, IAuthEmailSender email,
    TimeProvider clock, Microsoft.Extensions.Options.IOptions<DataProtectionTokenProviderOptions> tokenOptions, ILogger<AuthService> logger)
{
    private static readonly AuthFailure InvalidToken = new(400, "INVALID_TOKEN", "Liên kết không hợp lệ hoặc đã hết hạn.");
    private static readonly AuthFailure DeliveryUnavailable = new(503, "EMAIL_UNAVAILABLE", "Chưa thể gửi email. Vui lòng thử lại sau.");

    public async Task<AuthFailure?> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (!email.IsConfigured) return DeliveryUnavailable;
        if (string.IsNullOrWhiteSpace(request.FullName)) return new(400, "VALIDATION_ERROR", "Dữ liệu không hợp lệ.", new() { ["fullName"] = ["Vui lòng nhập họ tên."] });
        var candidate = new ApplicationUser { Id = Guid.NewGuid(), Email = request.Email.Trim(), UserName = request.Email.Trim(), FullName = request.FullName.Trim(), PhoneNumber = request.Phone?.Trim(), CreatedAt = clock.GetUtcNow().UtcDateTime };
        // Password validation must be identical for existing/new emails, otherwise
        // a weak-password request becomes an account enumeration oracle.
        var passwordErrors = new List<IdentityError>();
        foreach (var validator in users.PasswordValidators)
            passwordErrors.AddRange((await validator.ValidateAsync(users, candidate, request.Password)).Errors);
        if (passwordErrors.Count > 0) return PasswordFailure(IdentityResult.Failed(passwordErrors.ToArray()));
        ApplicationUser? created = null;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await bootstrap.LockAsync(cancellationToken);
            var tier = await bootstrap.EnsureAsync(cancellationToken);
            var existing = await users.FindByEmailAsync(request.Email.Trim());
            if (existing is null)
            {
                created = candidate;
                var result = await users.CreateAsync(created, request.Password);
                if (!result.Succeeded) return PasswordFailure(result);
                AuthBootstrap.Require(await users.AddToRoleAsync(created, "Customer"));
                db.CustomerProfiles.Add(new CustomerProfile { UserId = created.Id, TierId = tier.Id, EligibleSpend = 0, TierCalculatedAt = clock.GetUtcNow().UtcDateTime });
                await db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        // Delivery is outside the transaction. A failed delivery retains the account;
        // resend-verification is the recovery path, never silently confirming email.
        if (created is not null) await SendAsync(created, "verify-email", cancellationToken);
        return null; // Neutral 202 is acceptance, not a claim that an email was delivered.
    }

    public async Task<AuthFailure?> VerifyAsync(VerifyEmailRequest request)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is null || !user.IsActive || user.EmailConfirmed) return InvalidToken;
        var result = await users.ConfirmEmailAsync(user, request.Token);
        return result.Succeeded ? null : InvalidToken;
    }

    public async Task<AuthFailure?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null) return new(401, "INVALID_CREDENTIALS", "Email hoặc mật khẩu không hợp lệ.");
        AuthFailure? failure;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await bootstrap.LockUserAsync(user.Id, cancellationToken);
            await db.Entry(user).ReloadAsync(cancellationToken);
            failure = await CheckLoginAsync(user, request.Password);
            await transaction.CommitAsync(cancellationToken); // Persist failed-attempt/lockout counters as well.
        }
        if (failure is not null) return failure;
        await signIn.SignInAsync(user, isPersistent: false);
        return null;
    }

    private async Task<AuthFailure?> CheckLoginAsync(ApplicationUser user, string password)
    {
        if (!user.IsActive) return new(401, "INVALID_CREDENTIALS", "Email hoặc mật khẩu không hợp lệ.");
        // Unconfirmed accounts must also accrue failures and obey lockout. Identity's
        // CheckPasswordSignInAsync exits before checking a password when confirmation is required.
        if (!user.EmailConfirmed)
        {
            if (await users.IsLockedOutAsync(user)) return new(401, "INVALID_CREDENTIALS", "Email hoặc mật khẩu không hợp lệ.");
            if (!await users.CheckPasswordAsync(user, password))
            {
                AuthBootstrap.Require(await users.AccessFailedAsync(user));
                return new(401, "INVALID_CREDENTIALS", "Email hoặc mật khẩu không hợp lệ.");
            }
            return new(403, "EMAIL_NOT_VERIFIED", "Vui lòng xác minh email trước khi đăng nhập.");
        }
        var check = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!check.Succeeded) return new(401, "INVALID_CREDENTIALS", "Email hoặc mật khẩu không hợp lệ.");
        return null;
    }

    public async Task<AuthFailure?> SendForEmailAsync(EmailRequest request, bool reset, CancellationToken cancellationToken)
    {
        if (!email.IsConfigured) return DeliveryUnavailable;
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive || user.EmailConfirmed != reset) return null;
        await SendAsync(user, reset ? "reset-password" : "verify-email", cancellationToken);
        return null; // Neutral even when delivery fails: no account enumeration through adapter errors.
    }

    private async Task<AuthFailure?> SendAsync(ApplicationUser user, string purpose, CancellationToken cancellationToken)
    {
        var token = purpose == "verify-email" ? await users.GenerateEmailConfirmationTokenAsync(user) : await users.GeneratePasswordResetTokenAsync(user);
        try
        {
            await email.SendAsync(new AuthEmail(purpose, user.Email!, user.Id, token,
                clock.GetUtcNow().UtcDateTime.Add(tokenOptions.Value.TokenLifespan)), cancellationToken);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Do not log the adapter exception: it may contain recipient/token/path details.
            logger.LogWarning("Auth email delivery unavailable; use resend after restoring the adapter.");
            return DeliveryUnavailable;
        }
    }

    public async Task<AuthFailure?> ResetAsync(ResetPasswordRequest request)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is null || !user.IsActive || !user.EmailConfirmed) return InvalidToken;
        var result = await users.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (result.Succeeded) return null; // Identity updates security stamp: old tokens/cookies are rejected.
        return result.Errors.Any(x => x.Code == "InvalidToken" || x.Code == "ConcurrencyFailure") ? InvalidToken : PasswordFailure(result, "newPassword");
    }

    public async Task LogoutAsync(System.Security.Claims.ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal);
        if (user is not null) AuthBootstrap.Require(await users.UpdateSecurityStampAsync(user));
        await signIn.SignOutAsync(); // All sessions revoked by the new stamp, not only browser cookie deletion.
    }

    public async Task<SessionDto?> SessionAsync(System.Security.Claims.ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal);
        return user is null ? null : new(user.Id, user.Email!, user.FullName, user.PhoneNumber, user.EmailConfirmed, (await users.GetRolesAsync(user)).ToArray());
    }

    private static AuthFailure PasswordFailure(IdentityResult result, string field = "password") => new(400, "VALIDATION_ERROR", "Dữ liệu không hợp lệ.",
        new() { [field] = result.Errors.Select(x => x.Code.StartsWith("Password", StringComparison.Ordinal) ? "Mật khẩu cần ít nhất 12 ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt." : "Không thể tạo hoặc cập nhật tài khoản.").Distinct().ToArray() });
}
