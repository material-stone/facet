using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using Windows.Security.Authorization.AppCapabilityAccess;

namespace Facet;

internal sealed class WeatherLocationUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

internal static class WindowsWeatherLocation
{
    private static bool grantedThisSession;
    // Permission prompts belong to the explicit foreground settings action, never tray startup.
    internal static async Task<bool> RequestAccessAsync() => grantedThisSession = await Geolocator.RequestAccessAsync() == GeolocationAccessStatus.Allowed;

    internal static async Task<WeatherLocation> GetAsync(CancellationToken token)
    {
        try {
            token.ThrowIfCancellationRequested();
            if (!grantedThisSession && AppCapability.Create("location").CheckAccess() != AppCapabilityAccessStatus.Allowed)
                throw new WeatherLocationUnavailableException("설정에서 위치를 허용하거나 날씨 지역을 선택해 주세요.");
            var locator = new Geolocator { DesiredAccuracyInMeters = 100 };
            var position = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8)).AsTask(token);
            var point = position.Coordinate.Point.Position;
            return FromCoordinates(point.Latitude, point.Longitude);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or COMException or TimeoutException or InvalidOperationException or NotSupportedException) {
            grantedThisSession = false;
            throw new WeatherLocationUnavailableException("Windows 위치를 사용할 수 없습니다. 설정에서 날씨 지역을 선택해 주세요.", ex);
        }
    }

    internal static WeatherLocation FromCoordinates(double latitude, double longitude)
    {
        ValidateCoordinates(latitude, longitude);
        string? region = WeatherAdministrativeRegions.FindName(latitude, longitude);
        return new(latitude, longitude, region ?? "현재 위치", region == null ? "" : "KR");
    }

    internal static void ValidateCoordinates(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180)
            throw new WeatherLocationUnavailableException("위치 좌표를 확인할 수 없습니다. 설정에서 날씨 지역을 선택해 주세요.");
    }
}
