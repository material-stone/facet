using System.Text.Json;

namespace Facet;

internal static class WeatherAdministrativeRegions
{
    private sealed record Region(string Name, double[][][][] Polygons);
    private sealed record Catalog(Region[] Regions);
    private sealed record Polygon(double[][][] Rings, double MinX, double MinY, double MaxX, double MaxY)
    {
        internal bool Contains(double latitude, double longitude) => longitude >= MinX && longitude <= MaxX &&
            latitude >= MinY && latitude <= MaxY && ContainsPolygon(Rings, latitude, longitude);
    }
    private sealed record Boundary(string Name, Polygon[] Polygons);
    private static readonly Lazy<Boundary[]> Boundaries = new(Load);

    internal static string? FindName(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < 30 or > 40 || longitude is < 120 or > 135) return null;
        return Boundaries.Value.FirstOrDefault(region => region.Polygons.Any(polygon => polygon.Contains(latitude, longitude)))?.Name;
    }

    private static Boundary[] Load()
    {
        var uri = new Uri("pack://application:,,,/Facet;component/DashboardWeather/WeatherAdministrativeRegions.json");
        using var stream = Application.GetResourceStream(uri)?.Stream ?? throw new InvalidDataException("날씨 행정구역 경계가 없습니다.");
        var regions = JsonSerializer.Deserialize<Catalog>(stream)?.Regions ?? throw new InvalidDataException("날씨 행정구역 경계가 올바르지 않습니다.");
        if (regions.Length != 17 || regions.Select(region => region.Name).Distinct().Count() != 17)
            throw new InvalidDataException("날씨 행정구역 목록이 올바르지 않습니다.");
        return regions.Select(region => {
            if (string.IsNullOrEmpty(region.Name) || !region.Name.All(c => c is >= '\uAC00' and <= '\uD7A3') || region.Polygons is not { Length: > 0 })
                throw new InvalidDataException("날씨 행정구역 이름이 올바르지 않습니다.");
            return new Boundary(region.Name, region.Polygons.Select(rings => {
                if (rings is not { Length: > 0 } || rings.Any(ring => ring is not { Length: >= 4 } ||
                    ring.Any(point => point is not { Length: 2 } || !double.IsFinite(point[0]) || !double.IsFinite(point[1]) ||
                        point[0] is < 120 or > 135 || point[1] is < 30 or > 40) || !ring[0].SequenceEqual(ring[^1])))
                    throw new InvalidDataException("날씨 행정구역 다각형이 올바르지 않습니다.");
                var outer = rings[0];
                return new Polygon(rings, outer.Min(p => p[0]), outer.Min(p => p[1]), outer.Max(p => p[0]), outer.Max(p => p[1]));
            }).ToArray());
        }).ToArray();
    }

    internal static bool ContainsPolygon(double[][][] rings, double latitude, double longitude) =>
        ContainsRing(rings[0], latitude, longitude) && !rings.Skip(1).Any(ring => ContainsRing(ring, latitude, longitude));

    private static bool ContainsRing(double[][] ring, double latitude, double longitude)
    {
        bool inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++) {
            var a = ring[j]; var b = ring[i];
            double cross = (longitude - a[0]) * (b[1] - a[1]) - (latitude - a[1]) * (b[0] - a[0]);
            if (Math.Abs(cross) <= 1e-10 && longitude >= Math.Min(a[0], b[0]) && longitude <= Math.Max(a[0], b[0]) &&
                latitude >= Math.Min(a[1], b[1]) && latitude <= Math.Max(a[1], b[1])) return true;
            if ((a[1] > latitude) != (b[1] > latitude) && longitude < a[0] + (latitude - a[1]) * (b[0] - a[0]) / (b[1] - a[1])) inside = !inside;
        }
        return inside;
    }
}
