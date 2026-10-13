using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.GuestOrders;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Auth;
using PhoneStore.Api.Services.Checkout;

namespace PhoneStore.Api.Services.GuestOrders;

public sealed class GuestOrderService(AppDbContext db, UserManager<ApplicationUser> users,
    AuthBootstrap authLocks, OrderAccessMailQueue mail, TimeProvider clock, PhoneStore.Api.Services.Membership.MembershipService membership)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static CheckoutException Invalid() => new(400, "INVALID_ORDER_TOKEN", "Liên kết không hợp lệ, đã sử dụng hoặc đã hết hạn.");
    public async Task RequestAsync(OrderAccessRequest request, CancellationToken ct)
    {
        var normalized = users.NormalizeEmail(request.Email.Trim());
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK, HOLDLOCK) WHERE [OrderNumber]={request.OrderNumber.Trim()}").SingleOrDefaultAsync(ct);
        if (order is not null && order.UserId is null && order.NormalizedCustomerEmail == normalized)
        {
            if (await db.OrderAccessTokens.CountAsync(t => t.OrderId == order.Id && t.CreatedAt > Now.AddHours(-1), ct) < 5)
                await mail.QueueAccessAsync(order, Enum.Parse<OrderAccessTokenPurpose>(request.Purpose), ct);
        }
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    private async Task<OrderAccessToken> TokenAsync(string secret, OrderAccessTokenPurpose purpose, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(secret, "^[0-9a-f]{64}$")) throw Invalid();
        var hash = SHA256.HashData(Convert.FromHexString(secret));
        var token = await db.OrderAccessTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, ct);
        return token ?? throw Invalid();
    }
    private async Task<Order> LockOrderAsync(long id, CancellationToken ct) =>
        await db.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={id}").SingleAsync(ct);
    public async Task<GuestOrderGrant> ExchangeAsync(OrderAccessExchange request, CancellationToken ct)
    {
        var purpose = Enum.Parse<OrderAccessTokenPurpose>(request.Purpose);
        var found = await TokenAsync(request.Token, purpose, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await LockOrderAsync(found.OrderId, ct);
        var token = await db.OrderAccessTokens.SingleAsync(t => t.Id == found.Id, ct);
        if (order.UserId is not null || token.UsedAt is not null || token.ExpiresAt <= Now) throw Invalid();
        token.UsedAt = Now; await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(order.Id, token.Id, purpose, Now.AddMinutes(30));
    }
    public async Task<GuestOrderDto> ViewAsync(GuestOrderGrant grant, CancellationToken ct)
    {
        var order = await AuthorizedAsync(grant, ct);
        var items = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == order.Id).OrderBy(i => i.Id)
            .Select(i => new GuestOrderItemDto(ApiContract.Id(i.Id), ApiContract.Id(i.VariantId), i.ProductNameSnapshot,
                i.SkuSnapshot, i.VariantSnapshot, i.Quantity, i.UnitPrice, i.UnitDiscount, i.LineTotal)).ToListAsync(ct);
        var history = await db.OrderStatusHistories.AsNoTracking().Where(h => h.OrderId == order.Id).OrderBy(h => h.CreatedAt).ThenBy(h => h.Id)
            .Select(h => new GuestOrderHistoryDto(h.FromStatus == null ? null : h.FromStatus.ToString(), h.ToStatus.ToString(), h.CreatedAt)).ToListAsync(ct);
        var consent = await db.OrderAccountConsents.AsNoTracking().AnyAsync(c => c.OrderId == order.Id && c.Accepted, ct);
        return new(ApiContract.Id(order.Id), order.OrderNumber, order.Status.ToString(), order.PaymentMethod.ToString(), order.Currency,
            ApiContract.Money(order.Subtotal), ApiContract.Money(order.DiscountTotal), ApiContract.Money(order.ShippingFee), ApiContract.Money(order.GrandTotal),
            order.RecipientName, order.Phone, order.AddressLine, order.Locality, order.Province, order.CountryCode,
            order.CustomerNote, order.CreatedAt, order.PaymentDueAt, consent, items, history);
    }
    private async Task<Order> AuthorizedAsync(GuestOrderGrant grant, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == grant.OrderId && o.UserId == null
            && o.AccessTokens.Any(t => t.Id == grant.TokenId && t.Purpose == grant.Purpose && t.UsedAt != null), ct);
        return order ?? throw new CheckoutException(401, "GUEST_ACCESS_REQUIRED", "Cần mở liên kết truy cập đơn hợp lệ từ email.");
    }
    public async Task SetupAsync(GuestOrderGrant grant, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await LockOrderAsync(grant.OrderId, ct);
        await AuthorizedAsync(grant, ct);
        if (!await db.OrderAccountConsents.AnyAsync(c => c.OrderId == order.Id && c.Accepted, ct))
            throw new CheckoutException(403, "CONSENT_REQUIRED", "Đơn này chưa đồng ý tạo tài khoản.");
        if (await db.OrderAccessTokens.CountAsync(t => t.OrderId == order.Id && t.CreatedAt > Now.AddHours(-1), ct) < 5)
            await mail.QueueAccessAsync(order, OrderAccessTokenPurpose.ClaimOrder, ct, setup: true);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    public async Task<OrderClaimDto> ClaimAsync(Guid userId, string secret, CancellationToken ct)
    {
        var found = await TokenAsync(secret, OrderAccessTokenPurpose.ClaimOrder, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await PhoneStore.Api.Services.Membership.MembershipService.LockAsync(db, false, ct);
        await authLocks.LockUserAsync(userId, ct);
        var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive && u.EmailConfirmed, ct);
        if (account is null) throw new CheckoutException(401, "UNAUTHENTICATED", "Cần tài khoản đã xác minh.");
        var order = await LockOrderAsync(found.OrderId, ct);
        var token = await db.OrderAccessTokens.SingleAsync(t => t.Id == found.Id, ct);
        if (order.NormalizedCustomerEmail != account.NormalizedEmail || order.UserId is { } owner && owner != userId) throw Invalid();
        if (token.UsedAt is not null)
        {
            if (order.UserId != userId) throw Invalid();
            await transaction.CommitAsync(ct); return new(ApiContract.Id(order.Id), order.OrderNumber);
        }
        if (token.ExpiresAt <= Now || order.UserId is not null) throw Invalid();
        order.UserId = userId; token.UsedAt = Now;
        if (order.Status == OrderStatus.Completed)
        {
            await membership.SyncOrderAsync(order, ct);
            await membership.RecalculateAsync(userId, "Nhận đơn khách vãng lai đã hoàn tất", ct);
        }
        db.OutboxMessages.Add(new OutboxMessage { Type = "OrderClaimed", EventKey = "OrderClaimed:" + order.Id,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { version = 1, orderId = ApiContract.Id(order.Id) }),
            CreatedAt = Now, NextAttemptAt = Now });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(ApiContract.Id(order.Id), order.OrderNumber);
    }
}
