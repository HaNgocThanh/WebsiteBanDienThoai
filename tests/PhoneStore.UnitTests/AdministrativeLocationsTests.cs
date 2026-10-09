using PhoneStore.Api.Services;

namespace PhoneStore.UnitTests;
public sealed class AdministrativeLocationsTests
{
    [Fact]
    public void Current_official_snapshot_has_complete_unique_two_level_hierarchy_and_2026_names()
    {
        var locations = new AdministrativeLocations(); var snapshot = locations.Snapshot;
        Assert.Equal("2026-10-09", snapshot.AsOf); Assert.Equal(34, snapshot.Provinces.Length); Assert.Equal(3321, snapshot.Wards.Length);
        Assert.Equal(34, snapshot.Provinces.Select(p => p.Code).Distinct().Count()); Assert.Equal(3321, snapshot.Wards.Select(w => w.Code).Distinct().Count());
        foreach (var p in snapshot.Provinces) { Assert.Matches("^[0-9]{2}$", p.Code); Assert.NotEmpty(locations.Wards(p.Code)); }
        foreach (var w in snapshot.Wards) { Assert.Matches("^[0-9]{5}$", w.Code); Assert.Single(snapshot.Provinces, p => p.Code == w.ProvinceCode); }
        Assert.Contains(snapshot.Provinces, p => p.Code == "22" && p.Name == "Thành phố Quảng Ninh");
        Assert.Contains(snapshot.Provinces, p => p.Code == "24" && p.Name == "Thành phố Bắc Ninh");
        Assert.Contains(snapshot.Provinces, p => p.Code == "75" && p.Name == "Thành phố Đồng Nai");
        Assert.Contains(snapshot.Wards, w => w.Name.StartsWith("Đặc khu "));
        Assert.Equal(locations.Provinces.OrderBy(p => AdministrativeLocations.SortName(p.Name), AdministrativeLocations.NameComparer), locations.Provinces);
    }
}
