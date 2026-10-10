using System.Text;
using PhoneStore.Api.Services.Catalog;

namespace PhoneStore.UnitTests;

public class CatalogImageInputTests
{
    [Theory]
    [InlineData("image.jpg", "image/jpeg", "ffd8ff")]
    [InlineData("image.JPEG", "image/jpeg", "ffd8ff")]
    [InlineData("image.png", "image/png", "89504e470d0a1a0a")]
    [InlineData("image.gif", "image/gif", "474946383961")]
    [InlineData("image.bmp", "image/bmp", "424d")]
    [InlineData("image.webp", "image/webp", "524946460000000057454250")]
    public void SupportedFileHeadersPassPreflightButProviderStillMustDecode(string name, string mime, string hex)
        => CatalogImageInput.Validate(name, mime, Convert.FromHexString(hex));
    [Fact]
    public void SvgPreflightRejectsDtdAndRasterNeverReturnsActiveMarkup()
    {
        CatalogImageInput.Validate("safe.svg", "image/svg+xml", Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"));
        Assert.Throws<CatalogException>(() => CatalogImageInput.Validate("bad.svg", "image/svg+xml", Encoding.UTF8.GetBytes("<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///secret'>]><svg xmlns='http://www.w3.org/2000/svg'>&x;</svg>")));
    }
    [Fact]
    public void MismatchedEmptyOversizedAndUnsupportedFilesAreRejected()
    {
        foreach (var (name, mime, bytes) in new[] { ("bad.jpg", "image/jpeg", Encoding.UTF8.GetBytes("not an image")), ("bad.jpg", "image/png", Convert.FromHexString("ffd8ff")), ("bad.txt", "image/png", Convert.FromHexString("89504e470d0a1a0a")), ("empty.png", "image/png", Array.Empty<byte>()) })
            Assert.Throws<CatalogException>(() => CatalogImageInput.Validate(name, mime, bytes));
        Assert.Throws<CatalogException>(() => CatalogImageInput.Validate("large.png", "image/png", new byte[CatalogImageInput.MaxBytes + 1]));
    }
    [Theory]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png", true)]
    [InlineData("cloud-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png", true)]
    [InlineData("cloud-../foreign.png", false)]
    [InlineData("cloud-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.png", false)]
    [InlineData("cloud-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.jpg", false)]
    public void ManagedKeysRejectArbitraryCloudAssets(string name, bool valid) => Assert.Equal(valid, CatalogImageInput.IsManagedName(name));
}
