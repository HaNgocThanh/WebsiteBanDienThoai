using System.Security.Cryptography;
using System.Text;
using PhoneStore.Api.DTOs.Payments;
using PhoneStore.Api.Services.Checkout;
namespace PhoneStore.Api.Services.Payments;

[AttributeUsage(AttributeTargets.Method)] public sealed class SePayIpnAttribute : Attribute;
public sealed class SePaySandbox(IConfiguration configuration)
{
    public const string CheckoutUrl = "https://pay-sandbox.sepay.vn/v1/checkout/init";
    private string? Value(string name) => configuration["SePay:Sandbox:" + name];
    public bool Configured => !string.IsNullOrWhiteSpace(Value("MerchantId")) && Value("MerchantId")!.Length <= 150
        && !string.IsNullOrWhiteSpace(Value("SecretKey")) && !string.IsNullOrWhiteSpace(Value("IpnSecret")) && ValidOrigin(Value("PublicBaseUrl"));
    private static bool ValidOrigin(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && !uri.IsLoopback
        && uri.Host.Contains('.') && !System.Net.IPAddress.TryParse(uri.Host, out _) && uri.UserInfo == "" && uri.AbsolutePath == "/" && uri.Query == "" && uri.Fragment == "";
    public void CheckIpn(string? provided)
    {
        if (!Configured) throw new CheckoutException(503, "SEPAY_UNAVAILABLE", "SePay Sandbox chưa được cấu hình.");
        if (provided is null || provided.Length > 4096 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(provided)), SHA256.HashData(Encoding.UTF8.GetBytes(Value("IpnSecret")!))))
            throw new CheckoutException(401, "SEPAY_IPN_UNAUTHORIZED", "Thông báo thanh toán không được xác thực.");
    }
    // Official SDK: transmitted name=value pairs joined with commas; HMAC-SHA256/base64.
    public static string Signature(IReadOnlyList<SePayField> fields, string secret) => Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(string.Join(',', fields.Select(f => f.Name + "=" + f.Value)))));
    public SePayCheckoutDto Form(long paymentId, long orderId, decimal amount)
    {
        if (!Configured) throw new CheckoutException(503, "SEPAY_UNAVAILABLE", "SePay Sandbox chưa được cấu hình.");
        var invoice = "PSB-" + paymentId; var origin = Value("PublicBaseUrl")!.TrimEnd('/');
        var fields = new List<SePayField> { new("merchant", Value("MerchantId")!), new("operation", "PURCHASE"), new("payment_method", "BANK_TRANSFER"), new("order_invoice_number", invoice), new("order_amount", amount.ToString("0", System.Globalization.CultureInfo.InvariantCulture)), new("currency", "VND"), new("order_description", "Thanh toan don PhoneStore " + orderId),
            new("success_url", origin + "/payments/sepay/result?orderId=" + orderId + "&result=success"), new("error_url", origin + "/payments/sepay/result?orderId=" + orderId + "&result=error"), new("cancel_url", origin + "/payments/sepay/result?orderId=" + orderId + "&result=cancel") };
        fields.Add(new("signature", Signature(fields, Value("SecretKey")!)));
        return new(CheckoutUrl, fields, invoice, paymentId.ToString(), amount);
    }
}
