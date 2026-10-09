using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace PhoneStore.Api.Services.Checkout;
public sealed record CheckoutOwner(Guid? UserId, byte[]? GuestHash)
{
    public string LockKey => UserId is { } id ? "user:" + id.ToString("N") : "guest:" + Convert.ToHexStringLower(GuestHash!);
}
public sealed record GuestCheckoutCookie(string Nonce, DateTime ExpiresAt);
public sealed class CheckoutIdentity(IDataProtectionProvider protection, TimeProvider clock, IHostEnvironment environment)
{
    public const string CookieName = "PhoneStore.CheckoutGuest";
    private readonly IDataProtector protector = protection.CreateProtector("PhoneStore.CheckoutGuest.v1");
    public CheckoutOwner Resolve(HttpContext context, bool createGuest)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            if (!context.User.IsInRole("Customer")) throw new CheckoutException(403, "FORBIDDEN", "Tài khoản không có quyền mua hàng.");
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) throw new CheckoutException(401, "UNAUTHENTICATED", "Cần đăng nhập lại.");
            return new(id, null);
        }
        GuestCheckoutCookie? cookie = null;
        if (context.Request.Cookies.TryGetValue(CookieName, out var protectedValue) && protectedValue.Length <= 4096)
        {
            try { cookie = JsonSerializer.Deserialize<GuestCheckoutCookie>(protector.Unprotect(protectedValue)); }
            catch (Exception e) when (e is CryptographicException or JsonException or FormatException) { /* Untrusted cookie; never log its contents. */ }
        }
        if (cookie is null || cookie.ExpiresAt <= clock.GetUtcNow().UtcDateTime || cookie.Nonce is null || !System.Text.RegularExpressions.Regex.IsMatch(cookie.Nonce, "^[0-9a-f]{64}$"))
        {
            if (!createGuest) throw new CheckoutException(404, "CHECKOUT_NOT_FOUND", "Không tìm thấy phiên checkout của bạn.");
            cookie = new(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), clock.GetUtcNow().AddHours(24).UtcDateTime);
            context.Response.Cookies.Append(CookieName, protector.Protect(JsonSerializer.Serialize(cookie)), new CookieOptions {
                HttpOnly = true, Secure = !environment.IsDevelopment() || context.Request.IsHttps, SameSite = SameSiteMode.Strict,
                Path = "/api/v1", Expires = new DateTimeOffset(cookie.ExpiresAt), IsEssential = true
            });
        }
        return new(null, SHA256.HashData(Convert.FromHexString(cookie.Nonce)));
    }
}
