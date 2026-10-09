using PhoneStore.Api.DTOs.Pricing;
using PhoneStore.Api.Services.Checkout;
namespace PhoneStore.UnitTests;
public sealed class CheckoutPolicyTests
{
    [Fact]
    public void Canonical_cart_merges_and_sorts_numeric_ids_without_overflow()
    {
        var items = CheckoutService.CanonicalItems([new("10", 2), new("2", 3), new("10", 1)]);
        Assert.Equal(new[] { new QuoteItemRequest("2", 3), new QuoteItemRequest("10", 3) }, items);
        Assert.Throws<CheckoutException>(() => CheckoutService.CanonicalItems([new("2", int.MaxValue), new("2", 1)]));
        Assert.Throws<CheckoutException>(() => CheckoutService.CanonicalItems([new("01", 1)]));
        Assert.Throws<CheckoutException>(() => CheckoutService.CanonicalItems([new("2", 0)]));
    }
}
