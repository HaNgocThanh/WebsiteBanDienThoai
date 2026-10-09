using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.DTOs.Checkout;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.Pricing;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Pricing;

namespace PhoneStore.Api.Services.Checkout;
public sealed class CheckoutService(AppDbContext db, QuoteService pricing, UserManager<ApplicationUser> users, TimeProvider clock, IConfiguration configuration)
{
    public const string ConsentVersion = "account-create-v1";
    private DateTime Now() { var now = clock.GetUtcNow().UtcDateTime; return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc); }
    private async Task LockAsync(string resource, string mode, CancellationToken ct) => await db.Database.ExecuteSqlInterpolatedAsync($"""
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode={mode}, @LockOwner=N'Transaction', @LockTimeout=10000;
        IF @result < 0 THROW 51004, 'Checkout lock unavailable.', 1;
        """, ct);
    private static bool Owned(IdempotencyRequest key, CheckoutOwner owner) => owner.UserId is { } id
        ? key.OwnerUserId == id && key.GuestSessionHash is null
        : key.OwnerUserId is null && key.GuestSessionHash is { } hash && owner.GuestHash is { } guest && CryptographicOperations.FixedTimeEquals(hash, guest);
    public async Task<CheckoutSessionDto> IssueAsync(CheckoutOwner owner, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync("PhoneStore.Checkout.Issue:" + owner.LockKey, "Exclusive", ct);
        if (owner.UserId is { } userId) await AccountAsync(userId, ct);
        var now = Now();
        var open = owner.UserId is { } id ? db.IdempotencyRequests.Where(k => k.OwnerUserId == id)
            : db.IdempotencyRequests.Where(k => k.OwnerUserId == null && k.GuestSessionHash == owner.GuestHash);
        if (await open.CountAsync(k => k.Status == IdempotencyRequestStatus.Issued && k.ExpiresAt > now, ct) >= 5)
            throw new CheckoutException(429, "CHECKOUT_SESSION_LIMIT", "Bạn đang có 5 phiên checkout chưa dùng. Hãy dùng lại phiên hoặc chờ hết hạn.");
        var key = new IdempotencyRequest { Id = Guid.NewGuid(), OwnerUserId = owner.UserId, GuestSessionHash = owner.GuestHash,
            Status = IdempotencyRequestStatus.Issued, CreatedAt = now, ExpiresAt = now.AddMinutes(30) };
        db.IdempotencyRequests.Add(key); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(key.Id.ToString("D"), key.ExpiresAt);
    }
    private async Task<ApplicationUser> AccountAsync(Guid id, CancellationToken ct)
    {
        var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (account is null || !account.IsActive || !account.EmailConfirmed) throw new CheckoutException(401, "UNAUTHENTICATED", "Cần đăng nhập vào tài khoản đã xác minh.");
        return account;
    }
    public async Task<CheckoutResultDto> ResultAsync(CheckoutOwner owner, Guid keyId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync("PhoneStore.Checkout.Key:" + keyId.ToString("N"), "Exclusive", ct);
        var key = await db.IdempotencyRequests.AsNoTracking().SingleOrDefaultAsync(k => k.Id == keyId, ct);
        if (key is null || !Owned(key, owner)) throw new CheckoutException(404, "CHECKOUT_NOT_FOUND", "Không tìm thấy phiên checkout của bạn.");
        if (owner.UserId is { } userId) await AccountAsync(userId, ct);
        var order = key.Status == IdempotencyRequestStatus.Completed
            ? Response(await db.Orders.AsNoTracking().SingleAsync(o => o.Id == key.OrderId, ct)) : null;
        var state = order is not null ? "Completed" : key.ExpiresAt <= Now() ? "Expired" : "Issued";
        await transaction.CommitAsync(ct); return new(state, key.ExpiresAt, order);
    }
    private static string Text(string value) => value.Trim().Normalize(NormalizationForm.FormC);
    public static List<QuoteItemRequest> CanonicalItems(List<QuoteItemRequest> items)
    {
        if (items is not { Count: > 0 and <= 100 } || items.Any(x => x is null || !ApiContract.TryParseId(x.VariantId, out _) || x.Quantity <= 0)) throw new CheckoutException(400, "VALIDATION_ERROR", "Giỏ hàng không hợp lệ.");
        var result = new SortedDictionary<long, int>();
        foreach (var item in items)
        {
            var id = long.Parse(item.VariantId, CultureInfo.InvariantCulture); var qty = (long)result.GetValueOrDefault(id) + item.Quantity;
            if (qty > int.MaxValue) throw new CheckoutException(400, "VALIDATION_ERROR", "Số lượng gộp vượt giới hạn.");
            result[id] = (int)qty;
        }
        return result.Select(x => new QuoteItemRequest(ApiContract.Id(x.Key), x.Value)).ToList();
    }
    public async Task<PlaceOrderResult> PlaceAsync(CheckoutOwner owner, Guid keyId, PlaceOrderRequest request, CancellationToken ct)
    {
        var items = CanonicalItems(request.Items);
        if (owner.UserId is null && (string.IsNullOrWhiteSpace(request.Email) || request.CreateAccountConsent is null || request.ConsentTextVersion != ConsentVersion))
            throw new CheckoutException(400, "VALIDATION_ERROR", "Khách vãng lai cần email, lựa chọn tạo tài khoản và phiên bản nội dung đồng ý hợp lệ.");
        if (owner.UserId is not null && request.CreateAccountConsent == true) throw new CheckoutException(400, "VALIDATION_ERROR", "Tài khoản đăng nhập không cần tạo tài khoản mới.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync("PhoneStore.Checkout.Key:" + keyId.ToString("N"), "Exclusive", ct);
        var key = await db.IdempotencyRequests.SingleOrDefaultAsync(k => k.Id == keyId, ct);
        if (key is null || !Owned(key, owner)) throw new CheckoutException(404, "CHECKOUT_NOT_FOUND", "Không tìm thấy phiên checkout của bạn.");
        var account = owner.UserId is { } userId ? await AccountAsync(userId, ct) : null;
        if (account is not null && request.Email is not null && users.NormalizeEmail(Text(request.Email)) != account.NormalizedEmail)
            throw new CheckoutException(400, "VALIDATION_ERROR", "Email đặt hàng phải là email tài khoản đăng nhập.");
        var email = account?.Email ?? Text(request.Email!); var normalizedEmail = users.NormalizeEmail(email)!;
        var address = new QuoteAddress(Text(request.Province), request.CountryCode, AddressLine: Text(request.AddressLine),
            ProvinceCode: request.ProvinceCode, WardCode: request.WardCode, Locality: request.Locality is null ? null : Text(request.Locality));
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : Text(request.Note);
        var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { version = "checkout-v1", items,
            email = normalizedEmail, recipientName = Text(request.RecipientName), phone = Text(request.Phone), address,
            request.PaymentMethod, createAccountConsent = request.CreateAccountConsent ?? false,
            consentTextVersion = owner.UserId is null ? request.ConsentTextVersion : null, request.QuoteHash, note }));
        if (key.Status == IdempotencyRequestStatus.Completed)
        {
            if (key.RequestHash is null || !CryptographicOperations.FixedTimeEquals(key.RequestHash, hash)) throw new CheckoutException(409, "IDEMPOTENCY_PAYLOAD_MISMATCH", "Phiên checkout đã dùng với nội dung khác.");
            var existing = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == key.OrderId, ct);
            await transaction.CommitAsync(ct); return new(Response(existing), true);
        }
        var now = Now();
        if (key.ExpiresAt <= now) throw new CheckoutException(409, "CHECKOUT_EXPIRED", "Phiên checkout đã hết hạn. Cần tạo phiên mới.");
        DateTime? dueAt = null;
        if (request.PaymentMethod == "BankTransfer")
        {
            if (!int.TryParse(configuration["Checkout:BankTransferHoldHours"], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) || hours is < 1 or > 168)
                throw new CheckoutException(503, "CHECKOUT_UNAVAILABLE", "Chưa cấu hình thời hạn giữ hàng chuyển khoản hợp lệ.");
            dueAt = now.AddHours(hours);
        }
        // Coordinate public catalog snapshots with its existing exclusive write lock.
        await LockAsync("PhoneStore.Catalog.Write", "Shared", ct);
        var balances = new List<Inventory>();
        foreach (var item in items)
        {
            var id = long.Parse(item.VariantId, CultureInfo.InvariantCulture);
            var balance = await db.Inventory.FromSqlInterpolated($"SELECT * FROM [Inventory] WITH (UPDLOCK, HOLDLOCK) WHERE [VariantId]={id}").SingleOrDefaultAsync(ct);
            if (balance is null) throw new CheckoutException(409, "OUT_OF_STOCK", "Một phiên bản chưa có tồn kho khả dụng.");
            balances.Add(balance);
        }
        var quote = await pricing.QuoteAsync(new(items, address, request.QuoteHash), ct);
        var membership = account is null ? null : await db.CustomerProfiles.AsNoTracking().Where(p => p.UserId == account.Id).Select(p => new { p.TierId, p.Tier.Code }).SingleOrDefaultAsync(ct);
        var order = new Order { OrderNumber = "PS" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(14)), UserId = owner.UserId,
            CustomerEmail = email, NormalizedCustomerEmail = normalizedEmail, RecipientName = Text(request.RecipientName), Phone = Text(request.Phone),
            AddressLine = address.AddressLine!, Locality = address.Locality, Province = address.Province, CountryCode = address.CountryCode,
            PaymentMethod = Enum.Parse<PaymentMethod>(request.PaymentMethod), Status = OrderStatus.Placed, Currency = "VND",
            Subtotal = quote.Subtotal, DiscountTotal = quote.DiscountTotal, ShippingFee = quote.ShippingFee, GrandTotal = quote.GrandTotal,
            TierIdAtPurchase = membership?.TierId, TierCodeSnapshot = membership?.Code, CustomerNote = note, PaymentDueAt = dueAt, CreatedAt = now };
        order.AccountConsent = new() { Accepted = request.CreateAccountConsent ?? false, ConsentTextVersion = owner.UserId is null ? ConsentVersion : "authenticated-v1", RecordedAt = now };
        order.StatusHistories.Add(new() { ToStatus = OrderStatus.Placed, ActorUserId = owner.UserId, ActorType = account is null ? OrderActorType.System : OrderActorType.Customer, Reason = "Checkout", CreatedAt = now });
        for (var i = 0; i < quote.Items.Count; i++)
        {
            var line = quote.Items[i]; var id = long.Parse(line.VariantId, CultureInfo.InvariantCulture);
            var item = new OrderItem { VariantId = id, ProductNameSnapshot = line.ProductName, SkuSnapshot = line.Sku,
                VariantSnapshot = $"{line.Color} · {line.StorageGb} GB · RAM {line.RamGb} GB", Quantity = line.Quantity, UnitPrice = line.UnitPrice,
                UnitDiscount = line.UnitDiscount, PromotionNameSnapshot = line.PromotionName };
            item.StockReservation = new() { Quantity = line.Quantity, Status = StockReservationStatus.Active, ExpiresAt = dueAt, CreatedAt = now };
            item.InventoryMovements.Add(new() { VariantId = id, ActorUserId = owner.UserId, Kind = InventoryMovementKind.Reserve,
                OnHandDelta = 0, ReservedDelta = line.Quantity, Reason = "Checkout", EventKey = $"Order.Reserve:{keyId:D}:{id}", RequestHash = hash, CreatedAt = now });
            balances[i].Reserved = checked(balances[i].Reserved + line.Quantity); order.Items.Add(item);
        }
        db.Orders.Add(order); await db.SaveChangesAsync(ct);
        db.OutboxMessages.Add(new() { EventKey = "OrderPlaced:" + ApiContract.Id(order.Id), Type = "OrderPlaced",
            PayloadJson = JsonSerializer.Serialize(new { version = 1, orderId = ApiContract.Id(order.Id) }), CreatedAt = now, NextAttemptAt = now });
        key.Status = IdempotencyRequestStatus.Completed; key.RequestHash = hash; key.OrderId = order.Id;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return new(Response(order), false);
    }
    private static PlacedOrderDto Response(Order order) => new(ApiContract.Id(order.Id), order.OrderNumber, order.Status.ToString(),
        ApiContract.Money(order.GrandTotal), order.Currency, order.PaymentMethod.ToString(), order.PaymentDueAt);
}
