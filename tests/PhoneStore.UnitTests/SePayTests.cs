using Microsoft.Extensions.Configuration;
using PhoneStore.Api.DTOs.Payments;
using PhoneStore.Api.Services.Payments;
namespace PhoneStore.UnitTests;
public sealed class SePayTests
{
    [Fact]
    public void Signature_matches_independent_Node_crypto_golden_vector()
    {
        SePayField[] fields = [new("merchant", "SYNTHETIC"), new("operation", "PURCHASE"), new("payment_method", "BANK_TRANSFER"), new("order_invoice_number", "PSB-1"), new("order_amount", "1000000"), new("currency", "VND")];
        Assert.Equal("aRceOjqh0ZSJkVI3u7ZUE2VGmICFFEospKzagexn7pY=", SePaySandbox.Signature(fields, "synthetic-signing-key"));
    }
    [Theory]
    [InlineData("http://example.invalid")]
    [InlineData("https://localhost")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://user:password@example.invalid")]
    [InlineData("https://example.invalid/path")]
    public void Unsafe_callback_origin_disables_gateway(string origin)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["SePay:Sandbox:MerchantId"]="SYN",["SePay:Sandbox:SecretKey"]="synthetic",["SePay:Sandbox:IpnSecret"]="synthetic",["SePay:Sandbox:PublicBaseUrl"]=origin }).Build(); Assert.False(new SePaySandbox(config).Configured);
    }
}
