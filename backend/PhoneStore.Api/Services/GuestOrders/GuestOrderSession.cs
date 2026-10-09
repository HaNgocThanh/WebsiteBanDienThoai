using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Checkout;

namespace PhoneStore.Api.Services.GuestOrders;

public sealed record GuestOrderGrant(long OrderId, long TokenId, OrderAccessTokenPurpose Purpose, DateTime ExpiresAt);
public sealed class GuestOrderSession(IDataProtectionProvider protection, TimeProvider clock, IHostEnvironment environment)
{
    public const string CookieName = "PhoneStore.GuestOrder";
    private readonly IDataProtector protector = protection.CreateProtector("PhoneStore.GuestOrder.v1");
    public GuestOrderGrant Read(HttpContext http)
    {
        GuestOrderGrant? grant = null;
        if (http.Request.Cookies.TryGetValue(CookieName, out var value) && value.Length <= 4096)
        {
            try { grant = JsonSerializer.Deserialize<GuestOrderGrant>(protector.Unprotect(value)); }
            catch (Exception e) when (e is CryptographicException or JsonException or FormatException) { }
        }
        if (grant is null || grant.OrderId <= 0 || grant.TokenId <= 0 || grant.ExpiresAt <= clock.GetUtcNow().UtcDateTime
            || grant.Purpose is not (OrderAccessTokenPurpose.ViewOrder or OrderAccessTokenPurpose.CancelOrder))
            throw new CheckoutException(401, "GUEST_ACCESS_REQUIRED", "Cần mở liên kết truy cập đơn hợp lệ từ email.");
        return grant;
    }
    public void Write(HttpContext http, GuestOrderGrant grant) => http.Response.Cookies.Append(CookieName,
        protector.Protect(JsonSerializer.Serialize(grant)), new CookieOptions {
            HttpOnly = true, Secure = !environment.IsDevelopment() || http.Request.IsHttps, SameSite = SameSiteMode.Strict,
            Path = "/api/v1/guest", Expires = new DateTimeOffset(grant.ExpiresAt), IsEssential = true
        });
}
