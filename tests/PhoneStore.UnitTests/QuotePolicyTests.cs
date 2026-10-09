using PhoneStore.Api.Services.Pricing;

namespace PhoneStore.UnitTests;
public sealed class QuotePolicyTests
{
    [Theory]
    [InlineData("TP. Hồ Chí Minh", true)]
    [InlineData(" thành  phố HỒ CHÍ MINH ", true)]
    [InlineData("Ho Chi Minh", true)]
    [InlineData("TPHCM", true)]
    [InlineData("Đà Nẵng", false)]
    [InlineData("Hà Nội", false)]
    [InlineData("Hồ Chí Minh ABC", false)]
    public void City_aliases_are_exact_not_substring_matches(string province, bool expected) => Assert.Equal(expected, QuoteService.IsHoChiMinh(province));
}
