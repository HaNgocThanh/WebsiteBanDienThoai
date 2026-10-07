using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace PhoneStore.Api.DTOs.Common;

public static class ApiContract
{
    public const decimal MaxSafeMoney = 9_007_199_254_740_991m;

    public static string Id(long value) => value > 0
        ? value.ToString(CultureInfo.InvariantCulture)
        : throw new ArgumentOutOfRangeException(nameof(value));

    public static bool TryParseId(string? value, out long id)
    {
        id = 0;
        return value is { Length: > 0 } && value[0] != '0'
            && value.All(c => c is >= '0' and <= '9')
            && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }

    public static decimal Money(decimal value)
    {
        if (value < 0 || value > MaxSafeMoney || decimal.Truncate(value) != value)
            throw new ArgumentOutOfRangeException(nameof(value), "VND must be a nonnegative safe integer.");
        return value;
    }

    public static string ETag(byte[] version)
    {
        if (version.Length != 8) throw new ArgumentException("SQL rowversion must contain 8 bytes.", nameof(version));
        return $"\"{Convert.ToBase64String(version)}\"";
    }

    public static bool TryParseETag(string? header, out byte[] version)
    {
        version = [];
        if (header is not { Length: > 2 } || header[0] != '"' || header[^1] != '"') return false;
        try
        {
            var parsed = Convert.FromBase64String(header[1..^1]);
            if (parsed.Length != 8 || ETag(parsed) != header) return false;
            version = parsed;
            return true;
        }
        catch (FormatException) { return false; }
    }

    public static (DateTime From, DateTime To) VietnamDay(DateOnly date)
    {
        // Vietnam uses UTC+07 without daylight saving time.
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7));
        return (start.UtcDateTime, start.AddDays(1).UtcDateTime);
    }
}

public sealed class PageQuery
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    public long Offset => ((long)Page - 1) * PageSize;
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount);
