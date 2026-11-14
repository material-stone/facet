using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Facet;

internal enum WeatherIconKind { Unknown, Sunny, ClearNight, PartlyCloudy, PartlyCloudyNight, Cloudy, Fog, Rain, Snow, Thunder }

internal sealed record WeatherLocation(double Latitude, double Longitude, string Area, string CountryCode = "", string RegionCode = "");

internal sealed record WeatherSnapshot(WeatherLocation Location, double? Temperature, int? Humidity,
    int? Code, bool IsDay, DateTimeOffset ObservedAt)
{
    public string Description => Code switch {
        0 => IsDay ? "맑음" : "맑은 밤", 1 => "대체로 맑음", 2 => "구름 조금", 3 => "흐림",
        45 or 48 => "안개", 51 or 53 or 55 => "이슬비", 56 or 57 => "어는 이슬비",
        61 or 63 or 65 => "비", 66 or 67 => "어는 비", 71 or 73 or 75 or 77 => "눈",
        80 or 81 or 82 => "소나기", 85 or 86 => "눈 소나기", 95 => "뇌우", 96 or 99 => "우박 동반 뇌우",
        _ => "날씨 미확인"
    };

    public WeatherIconKind Icon => Code switch {
        0 or 1 => IsDay ? WeatherIconKind.Sunny : WeatherIconKind.ClearNight,
        2 => IsDay ? WeatherIconKind.PartlyCloudy : WeatherIconKind.PartlyCloudyNight,
        3 => WeatherIconKind.Cloudy, 45 or 48 => WeatherIconKind.Fog,
        51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => WeatherIconKind.Rain,
        71 or 73 or 75 or 77 or 85 or 86 => WeatherIconKind.Snow,
        95 or 96 or 99 => WeatherIconKind.Thunder, _ => WeatherIconKind.Unknown
    };
}

internal sealed class WeatherClient
{
    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient client;
    private readonly Func<CancellationToken, Task<WeatherLocation>> locate;

    internal WeatherClient(HttpClient? client = null, Func<CancellationToken, Task<WeatherLocation>>? locate = null)
    {
        this.client = client ?? SharedClient;
        this.locate = locate ?? WindowsWeatherLocation.GetAsync;
    }

    private static HttpClient CreateClient()
    {
        var result = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) {
            Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 65536
        };
        result.DefaultRequestHeaders.UserAgent.ParseAdd("Facet/1.11");
        return result;
    }

    public async Task<WeatherSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var location = await locate(cancellationToken);
        WindowsWeatherLocation.ValidateCoordinates(location.Latitude, location.Longitude);
        string url = FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={location.Latitude:F4}&longitude={location.Longitude:F4}&current=temperature_2m,relative_humidity_2m,weather_code,is_day&temperature_unit=celsius&timeformat=unixtime&timezone=auto");
        string weatherJson = await client.GetStringAsync(url, cancellationToken);
        return ParseWeather(weatherJson, location);
    }

    internal static WeatherSnapshot ParseWeather(string json, WeatherLocation location)
    {
        using var document = ParseJson(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("current", out var current) || current.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("현재 날씨가 없습니다.");
        if (!current.TryGetProperty("time", out var time) || time.ValueKind != JsonValueKind.Number || !time.TryGetInt64(out long seconds))
            throw new InvalidDataException("날씨 기준 시각이 없습니다.");
        DateTimeOffset observed;
        try { observed = DateTimeOffset.FromUnixTimeSeconds(seconds); }
        catch (ArgumentOutOfRangeException ex) { throw new InvalidDataException("날씨 기준 시각이 올바르지 않습니다.", ex); }
        double? humidity = Number(current, "relative_humidity_2m", 0, 100), code = Number(current, "weather_code", 0, 99);
        if (humidity is double h && h != Math.Truncate(h) || code is double c && c != Math.Truncate(c))
            throw new InvalidDataException("날씨 응답이 올바르지 않습니다.");
        double? isDay = Number(current, "is_day", 0, 1);
        if (isDay == null || isDay != Math.Truncate(isDay.Value)) throw new InvalidDataException("낮/밤 정보가 없습니다.");
        return new(location, Number(current, "temperature_2m", -100, 70), (int?)humidity, (int?)code, isDay == 1, observed);
    }

    private static JsonDocument ParseJson(string json)
    {
        if (json.Length > 65536) throw new InvalidDataException("위치·날씨 응답이 너무 큽니다.");
        return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
    }

    private static double? Number(JsonElement parent, string name, double minimum, double maximum)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || !double.IsFinite(number) || number < minimum || number > maximum)
            throw new InvalidDataException("위치·날씨 수치가 올바르지 않습니다.");
        return number;
    }

}
