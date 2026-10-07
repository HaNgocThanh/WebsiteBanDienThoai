using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using PhoneStore.Api.DTOs.Common;

namespace PhoneStore.UnitTests;

public class ContractTests
{
    [Theory]
    [InlineData(9007199254740993L)]
    [InlineData(long.MaxValue)]
    public void BigintIdsRemainExactStrings(long id)
    {
        var json = JsonSerializer.Serialize(new { id = ApiContract.Id(id) });
        Assert.Equal(id.ToString(), JsonDocument.Parse(json).RootElement.GetProperty("id").GetString());
        Assert.True(ApiContract.TryParseId(ApiContract.Id(id), out var parsed));
        Assert.Equal(id, parsed);
    }

    [Theory]
    [InlineData(null)] [InlineData("0")] [InlineData("01")] [InlineData("-1")]
    [InlineData("+1")] [InlineData("1.0")] [InlineData("9223372036854775808")]
    public void InvalidIdsAreRejected(string? value) => Assert.False(ApiContract.TryParseId(value, out _));

    [Fact]
    public void MoneyRejectsFractionsNegativesAndUnsafeNumbers()
    {
        Assert.Equal(0, ApiContract.Money(0));
        Assert.Equal(ApiContract.MaxSafeMoney, ApiContract.Money(ApiContract.MaxSafeMoney));
        foreach (var value in new[] { -1m, 1.5m, ApiContract.MaxSafeMoney + 1 })
            Assert.Throws<ArgumentOutOfRangeException>(() => ApiContract.Money(value));
    }

    [Theory]
    [InlineData(0,20)] [InlineData(1,0)] [InlineData(1,101)]
    public void InvalidPaginationFailsValidation(int page, int size)
    {
        var query = new PageQuery { Page = page, PageSize = size };
        Assert.False(Validator.TryValidateObject(query, new ValidationContext(query), [], true));
    }

    [Fact]
    public void PaginationOffsetDoesNotOverflowInt()
    {
        Assert.Equal(20, new PageQuery().PageSize);
        Assert.Equal(214748364600L, new PageQuery { Page = int.MaxValue, PageSize = 100 }.Offset);
    }

    [Fact]
    public void RowversionUsesSingleStrongTag()
    {
        var bytes = new byte[] { 0,0,0,0,0,0,0,1 };
        var tag = ApiContract.ETag(bytes);
        Assert.True(ApiContract.TryParseETag(tag, out var parsed));
        Assert.Equal(bytes, parsed);
        foreach (var invalid in new[] { "*", "W/" + tag, tag + "," + tag, "\"AA==\"", "garbage" })
            Assert.False(ApiContract.TryParseETag(invalid, out _));
    }

    [Fact]
    public void VietnamDateRangeCrossesUtcDateAtSeventeenHours()
    {
        var (from, to) = ApiContract.VietnamDay(new DateOnly(2026,10,7));
        Assert.Equal(new DateTime(2026,10,6,17,0,0,DateTimeKind.Utc), from);
        Assert.Equal(new DateTime(2026,10,7,17,0,0,DateTimeKind.Utc), to);
        Assert.Equal(DateTimeKind.Utc, from.Kind);
    }
    [Fact]
    public void TimestampJsonRequiresExplicitUtcAndEmitsZ()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new UtcDateTimeJsonConverter());
        var utc = new DateTime(2026, 10, 6, 17, 0, 0, DateTimeKind.Utc);
        Assert.Equal("\"2026-10-06T17:00:00Z\"", JsonSerializer.Serialize(utc, options));
        Assert.Equal(utc, JsonSerializer.Deserialize<DateTime>("\"2026-10-06T17:00:00Z\"", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime>("\"2026-10-06T17:00:00\"", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified), options));
    }
}


