using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Facet;

internal sealed record WeatherRegion(string Id, string DisplayName, double Latitude, double Longitude)
{
    public WeatherLocation Location => new(Latitude, Longitude, DisplayName, "KR");
    public override string ToString() => DisplayName;
}

internal static class WeatherRegions
{
    private sealed record Catalog(WeatherRegion[] Regions);
    internal static readonly IReadOnlyList<WeatherRegion> All = Load();
    internal static readonly IReadOnlyList<WeatherRegion> Options = Array.AsReadOnly(
        new[] { new WeatherRegion("", "Windows 자동 위치", 0, 0) }.Concat(All).ToArray());

    internal static WeatherRegion? Find(string? id) => All.FirstOrDefault(region => region.Id == id);

    private static IReadOnlyList<WeatherRegion> Load()
    {
        var uri = new Uri("pack://application:,,,/Facet;component/DashboardWeather/WeatherRegions.json");
        using var stream = Application.GetResourceStream(uri)?.Stream ?? throw new InvalidDataException("날씨 지역 목록이 없습니다.");
        var regions = JsonSerializer.Deserialize<Catalog>(stream)?.Regions ?? throw new InvalidDataException("날씨 지역 목록이 올바르지 않습니다.");
        if (regions.Length < 200 || regions.Select(r => r.Id).Distinct().Count() != regions.Length ||
            regions.Any(r => string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.DisplayName) || r.DisplayName.Any(char.IsControl) ||
                !double.IsFinite(r.Latitude) || !double.IsFinite(r.Longitude) || r.Latitude is < 30 or > 40 || r.Longitude is < 120 or > 135))
            throw new InvalidDataException("날씨 지역 목록을 확인할 수 없습니다.");
        return Array.AsReadOnly(regions);
    }

    internal static Task<WeatherLocation> ResolveAsync(string? id, CancellationToken token,
        Func<CancellationToken, Task<WeatherLocation>>? locate = null)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(id)) return (locate ?? WindowsWeatherLocation.GetAsync)(token);
        var region = Find(id) ?? throw new WeatherLocationUnavailableException("설정에서 날씨 지역을 다시 선택해 주세요.");
        return Task.FromResult(region.Location);
    }
}
