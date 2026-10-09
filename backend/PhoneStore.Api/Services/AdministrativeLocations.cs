using System.Globalization;
using System.Text.Json;

namespace PhoneStore.Api.Services;

public sealed record ProvinceLocation(string Code, string Name);
public sealed record WardLocation(string Code, string Name, string ProvinceCode);
public sealed record LocationSnapshot(string AsOf, string Source, ProvinceLocation[] Provinces, WardLocation[] Wards);
public sealed class AdministrativeLocations
{
    public LocationSnapshot Snapshot { get; }
    public AdministrativeLocations()
    {
        using var stream = typeof(AdministrativeLocations).Assembly.GetManifestResourceStream("PhoneStore.Api.Resources.vietnam-administrative-units.json")
            ?? throw new InvalidOperationException("Missing administrative catalog.");
        Snapshot = JsonSerializer.Deserialize<LocationSnapshot>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Invalid administrative catalog.");
        if (Snapshot.Provinces.Length == 0 || Snapshot.Wards.Length == 0 || Snapshot.Provinces.Select(x => x.Code).Distinct().Count() != Snapshot.Provinces.Length
            || Snapshot.Wards.Select(x => x.Code).Distinct().Count() != Snapshot.Wards.Length
            || Snapshot.Wards.Any(w => !Snapshot.Provinces.Any(p => p.Code == w.ProvinceCode))) throw new InvalidOperationException("Invalid administrative hierarchy.");
    }
    public static string SortName(string name) => System.Text.RegularExpressions.Regex.Replace(name, "^(Tỉnh|Thành phố|Phường|Xã|Đặc khu) ", "");
    public static IComparer<string> NameComparer { get; } = StringComparer.Create(CultureInfo.GetCultureInfo("vi-VN"), true);
    public IReadOnlyList<ProvinceLocation> Provinces => Snapshot.Provinces.OrderBy(p => SortName(p.Name), NameComparer).ToArray();
    public IReadOnlyList<WardLocation> Wards(string code) => Snapshot.Wards.Where(w => w.ProvinceCode == code).OrderBy(w => SortName(w.Name), NameComparer).ToArray();
    public bool Matches(string? provinceCode, string? wardCode, string province, string? locality) =>
        Snapshot.Provinces.Any(p => p.Code == provinceCode && p.Name == province)
        && Snapshot.Wards.Any(w => w.Code == wardCode && w.ProvinceCode == provinceCode && w.Name == locality);
}
