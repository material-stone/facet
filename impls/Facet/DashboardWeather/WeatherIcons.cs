using System.Windows.Media.Imaging;

namespace Facet;

internal enum WeatherIconKind { Unknown, Sunny, ClearNight, PartlyCloudy, PartlyCloudyNight, Cloudy, Fog, Rain, Snow, Thunder }

internal static class WeatherIcons
{
    private static readonly Dictionary<WeatherIconKind, BitmapImage> Cache = new();

    internal static ImageSource? Load(WeatherIconKind kind)
    {
        if (kind == WeatherIconKind.Unknown) return null;
        if (Cache.TryGetValue(kind, out var cached)) return cached;
        string name = kind switch {
            WeatherIconKind.Sunny => "sunny", WeatherIconKind.ClearNight => "clear-night",
            WeatherIconKind.PartlyCloudy => "partly-cloudy", WeatherIconKind.PartlyCloudyNight => "partly-cloudy-night", WeatherIconKind.Cloudy => "cloudy",
            WeatherIconKind.Fog => "fog", WeatherIconKind.Rain => "rain", WeatherIconKind.Snow => "snow",
            WeatherIconKind.Thunder => "thunder", _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri($"pack://application:,,,/Facet;component/DashboardWeather/Icons/{name}.png", UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 168;
        bitmap.EndInit(); bitmap.Freeze(); Cache[kind] = bitmap;
        return bitmap;
    }
}
