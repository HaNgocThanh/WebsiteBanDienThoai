using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.Pricing;

namespace PhoneStore.Api.Services.Pricing;

public sealed class QuoteService(AppDbContext db, IConfiguration configuration, AdministrativeLocations locations)
{
    public async Task<QuoteDto> QuoteAsync(QuoteRequest request, CancellationToken ct)
    {
        if (request.Items is not { Count: > 0 and <= 100 } || request.Items.Any(x => x is null || !ApiContract.TryParseId(x.VariantId, out _) || x.Quantity <= 0))
            throw new QuoteException(400, "VALIDATION_ERROR", "Giỏ hàng không hợp lệ.");
        var quantities = new SortedDictionary<long, int>();
        foreach (var item in request.Items)
        {
            var id = long.Parse(item.VariantId, CultureInfo.InvariantCulture);
            var quantity = (long)quantities.GetValueOrDefault(id) + item.Quantity;
            if (quantity > int.MaxValue) throw new QuoteException(400, "VALIDATION_ERROR", "Số lượng gộp vượt giới hạn.");
            quantities[id] = (int)quantity;
        }
        if (request.ShippingAddress is null || string.IsNullOrWhiteSpace(request.ShippingAddress.Province) || request.ShippingAddress.CountryCode != "VN")
            throw new QuoteException(400, "VALIDATION_ERROR", "Cần tỉnh/thành phố giao hàng tại Việt Nam.");
        var address = request.ShippingAddress;
        var coded = address.ProvinceCode is not null || address.WardCode is not null;
        if (coded && (!locations.Matches(address.ProvinceCode, address.WardCode, address.Province, address.Locality) || string.IsNullOrWhiteSpace(address.AddressLine)))
            throw new QuoteException(400, "VALIDATION_ERROR", "Tỉnh/thành phố và phường/xã phải thuộc danh mục hiện hành; cần số nhà, tên đường.");
        var hoChiMinh = coded ? address.ProvinceCode == "79" : IsHoChiMinh(address.Province);
        var rawFee = configuration[hoChiMinh ? "Pricing:HoChiMinhShippingFeeVnd" : "Pricing:OtherProvinceShippingFeeVnd"];
        if (!decimal.TryParse(rawFee, NumberStyles.None, CultureInfo.InvariantCulture, out var fee) || fee < 0 || fee > ApiContract.MaxSafeMoney)
            throw new QuoteException(503, "SHIPPING_UNAVAILABLE", "Chưa cấu hình phí giao hàng hợp lệ.");
        var ids = quantities.Keys.ToArray();
        // One projected SQL statement reads the public catalog and current availability. No reservation or write.
        var rows = await db.ProductVariants.AsNoTracking().Where(v => ids.Contains(v.Id) && v.IsActive && v.Product.IsActive && v.Product.Brand.IsActive && v.Product.Category.IsActive)
            .Select(v => new { v.Id, ProductName = v.Product.Name, ProductSlug = v.Product.Slug, v.Sku, v.Color, v.StorageGb, v.RamGb, v.Price,
                Available = v.Inventory == null ? 0 : v.Inventory.OnHand - v.Inventory.Reserved }).ToListAsync(ct);
        if (rows.Count != ids.Length)
        {
            var unavailable = ids.Except(rows.Select(x => x.Id)).Select(ApiContract.Id);
            throw new QuoteException(409, "VARIANT_UNAVAILABLE", $"Phiên bản {string.Join(", ", unavailable)} không còn được bán. Xóa các phiên bản này và tính lại giỏ.");
        }
        var lines = new List<QuoteLine>();
        foreach (var row in rows.OrderBy(v => v.Id))
        {
            var quantity = quantities[row.Id];
            if (quantity > row.Available) throw new QuoteException(409, "OUT_OF_STOCK", $"{row.ProductName} ({row.Sku}) chỉ còn {row.Available} sản phẩm khả dụng.");
            lines.Add(new(ApiContract.Id(row.Id), quantity, row.ProductName, row.ProductSlug, row.Sku, row.Color, row.StorageGb, row.RamGb, row.Available,
                row.Price, 0, SafeMoney(row.Price * quantity), null));
        }
        var result = Calculate(lines, fee);
        if (request.QuoteHash is not null && request.QuoteHash != result.QuoteHash)
            throw new QuoteException(409, "PRICE_CHANGED", "Giá hoặc phí đã thay đổi. Cần xem và xác nhận báo giá mới.");
        return result;
    }
    public static bool IsHoChiMinh(string province)
    {
        var decomposed = province.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var plain = new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c != '.').ToArray());
        var normalized = string.Join(' ', plain.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized is "ho chi minh" or "tp ho chi minh" or "thanh pho ho chi minh" or "hcm" or "tphcm" or "tp hcm" or "ho chi minh city";
    }
    private static decimal SafeMoney(decimal amount)
    {
        if (amount < 0 || amount > ApiContract.MaxSafeMoney || decimal.Truncate(amount) != amount)
            throw new QuoteException(400, "QUOTE_LIMIT", "Giá trị giỏ hàng vượt giới hạn số tiền được hỗ trợ.");
        return amount;
    }
    public static QuoteDto Calculate(IReadOnlyList<QuoteLine> lines, decimal fee)
    {
        // P5 extends discount selection here; P3 deliberately has no promotion/tier client input.
        foreach (var line in lines) SafeMoney(line.UnitPrice);
        var subtotal = SafeMoney(lines.Sum(x => x.LineTotal));
        SafeMoney(fee); var total = SafeMoney(subtotal + fee);
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new { version = "quote-v1", currency = "VND", items = lines.Select(x => new { x.VariantId, x.Quantity, x.UnitPrice, x.UnitDiscount, x.LineTotal, x.PromotionName }), shippingFee = fee });
        var hash = Convert.ToHexStringLower(SHA256.HashData(canonical));
        return new(lines, subtotal, 0, fee, total, "VND", hash);
    }
}
