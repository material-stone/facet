using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Facet.Plugins;
using Windows.Devices.Geolocation;
using Windows.Security.Authorization.AppCapabilityAccess;

namespace Facet.WeatherPlugin;

public sealed class WeatherPlugin : IFacetPlugin
{
    public const string RegionKey = "region";

    public void Register(IWidgetRegistry registry)
    {
        registry.AddWidget(new WidgetDescriptor("weather", "날씨 (플러그인)", context => new WeatherView(context))
        { Description = "현재 날씨와 습도", DefaultColumns = 4, DefaultRows = 1, MinColumns = 3, MinRows = 1, AllowMultiple = false, AllowMultipleTabs = true, UsesData = true, HasHudPanel = true });
        var regions = new List<SettingOption> { new("", "Windows 자동 위치") };
        regions.AddRange(WeatherAssets.Regions.Select(r => new SettingOption(r.Id, r.DisplayName)));
        registry.AddSettings(new PluginSettingsSection([
            new ChoiceSetting(RegionKey, "날씨 지역", regions, "") { Description = "Windows 자동 위치를 사용하거나 한국 시·도와 시·군·구를 직접 선택합니다. 현재 좌표는 저장하지 않습니다." },
            new ActionSetting("access", "위치 접근", "위치 접근 허용", RequestAccessAsync) { Description = "Windows 위치 권한을 요청합니다. 지역 좌표는 GeoNames 자료를 사용합니다." },
            new ActionSetting("windows", "Windows 위치 설정", "설정 열기", OpenWindowsSettingsAsync)
        ]));
    }

    private static async Task<string> RequestAccessAsync() =>
        await Geolocator.RequestAccessAsync() == GeolocationAccessStatus.Allowed
            ? "위치 접근이 허용되었습니다. 날씨 지역을 Windows 자동 위치로 선택하면 적용됩니다."
            : "위치를 사용할 수 없습니다. Windows 위치 설정을 확인하거나 지역을 선택하세요.";

    private static Task<string> OpenWindowsSettingsAsync()
    {
        Process.Start(new ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true })?.Dispose();
        return Task.FromResult("");
    }
}

public sealed record WeatherRegion(string Id, string DisplayName, double Latitude, double Longitude);

public sealed record WeatherReading(double? Temperature, int? Humidity, int? Code, bool IsDay, DateTimeOffset ObservedAt)
{
    public string Description => Code switch {
        0 => IsDay ? "맑음" : "맑은 밤", 1 => "대체로 맑음", 2 => "구름 조금", 3 => "흐림",
        45 or 48 => "안개", 51 or 53 or 55 => "이슬비", 56 or 57 => "어는 이슬비",
        61 or 63 or 65 => "비", 66 or 67 => "어는 비", 71 or 73 or 75 or 77 => "눈",
        80 or 81 or 82 => "소나기", 85 or 86 => "눈 소나기", 95 => "뇌우", 96 or 99 => "우박 동반 뇌우",
        _ => "날씨 미확인"
    };

    // The skin's weather keys (ADR 0109): clear, partly, cloud, fog, rain, snow, or night for a clear night.
    public string SkinKey => Code switch {
        0 or 1 => IsDay ? "clear" : "night",
        2 => "partly", 3 => "cloud", 45 or 48 => "fog",
        71 or 73 or 75 or 77 or 85 or 86 => "snow",
        51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 or 95 or 96 or 99 => "rain",
        _ => "partly"
    };

    public string? IconName => Code switch {
        0 or 1 => IsDay ? "sunny" : "clear-night",
        2 => IsDay ? "partly-cloudy" : "partly-cloudy-night",
        3 => "cloudy", 45 or 48 => "fog",
        51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "rain",
        71 or 73 or 75 or 77 or 85 or 86 => "snow",
        95 or 96 or 99 => "thunder", _ => null
    };

    public static WeatherReading Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("current", out var current) || current.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("현재 날씨가 없습니다.");
        if (!current.TryGetProperty("time", out var time) || !time.TryGetInt64(out long seconds)) throw new InvalidDataException("날씨 기준 시각이 없습니다.");
        double? Number(string name, double minimum, double maximum)
        {
            if (!current.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
            if (!value.TryGetDouble(out double number) || !double.IsFinite(number) || number < minimum || number > maximum) throw new InvalidDataException("날씨 수치가 올바르지 않습니다.");
            return number;
        }
        return new(Number("temperature_2m", -100, 70), (int?)Number("relative_humidity_2m", 0, 100), (int?)Number("weather_code", 0, 99), Number("is_day", 0, 1) == 1, DateTimeOffset.FromUnixTimeSeconds(seconds));
    }
}

public static class WeatherAssets
{
    private sealed record Catalog(WeatherRegion[] Regions);
    private static readonly Assembly Own = typeof(WeatherAssets).Assembly;
    public static readonly IReadOnlyList<WeatherRegion> Regions = LoadRegions();
    private static readonly Dictionary<string, BitmapImage> Icons = [];
    private sealed record Boundary(string Name, double[][][][] Polygons);
    private sealed record BoundaryCatalog(Boundary[] Regions);
    private static readonly Lazy<Boundary[]> Boundaries = new(LoadBoundaries);

    private static Boundary[] LoadBoundaries()
    {
        using var stream = Own.GetManifestResourceStream("boundaries.json") ?? throw new InvalidDataException("날씨 행정구역 경계가 없습니다.");
        return JsonSerializer.Deserialize<BoundaryCatalog>(stream)?.Regions ?? throw new InvalidDataException("날씨 행정구역 경계가 올바르지 않습니다.");
    }

    // The Korean province or city holding a coordinate, as the built-in weather names an automatic location.
    public static string? PlaceName(double latitude, double longitude)
    {
        if (latitude is < 30 or > 40 || longitude is < 120 or > 135) return null;
        return Boundaries.Value.FirstOrDefault(region => region.Polygons.Any(rings => Inside(rings[0], latitude, longitude) && !rings.Skip(1).Any(hole => Inside(hole, latitude, longitude))))?.Name;
    }

    private static bool Inside(double[][] ring, double latitude, double longitude)
    {
        bool inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++) {
            var a = ring[j]; var b = ring[i];
            if ((a[1] > latitude) != (b[1] > latitude) && longitude < a[0] + (latitude - a[1]) * (b[0] - a[0]) / (b[1] - a[1])) inside = !inside;
        }
        return inside;
    }

    private static WeatherRegion[] LoadRegions()
    {
        using var stream = Own.GetManifestResourceStream("regions.json") ?? throw new InvalidDataException("날씨 지역 목록이 없습니다.");
        return JsonSerializer.Deserialize<Catalog>(stream)?.Regions ?? throw new InvalidDataException("날씨 지역 목록이 올바르지 않습니다.");
    }

    public static ImageSource? Icon(string? name)
    {
        if (name == null) return null;
        if (Icons.TryGetValue(name, out var cached)) return cached;
        using var stream = Own.GetManifestResourceStream("icons/" + name + ".png");
        if (stream == null) return null;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = stream; bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 168;
        bitmap.EndInit(); bitmap.Freeze();
        return Icons[name] = bitmap;
    }
}

// Fills its tile. The region comes from the plugin's settings section; each reading is published in the widget's store.
public sealed class WeatherView : Grid, IDisposable, IWidgetState
{
    private sealed class LocationUnavailableException(string message) : Exception(message);

    private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");
    private static readonly HttpClient Client = CreateClient();
    private readonly WidgetContext context;
    private readonly Image icon = new() { Width = 84, Height = 84, Margin = new Thickness(0, 0, 16, 0), Stretch = Stretch.Uniform, Visibility = Visibility.Hidden };
    private readonly TextBlock place = new() { FontSize = 12, MaxWidth = 126, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock condition = new() { Text = "날씨 확인 중", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 0, 0), MaxWidth = 98, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock temperature = new() { Text = "--°C", FontSize = 40, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock humidity = new() { Text = "--%", FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = new() { Text = "날씨 연결 대기", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly System.Windows.Shapes.Path spinner = new() { Width = 12, Height = 12, Margin = new Thickness(0, 0, 6, 0), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Data = Geometry.Parse("M6,1A5,5 0 1 1 1,6"), RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Focusable = false, Visibility = Visibility.Collapsed };
    private readonly Grid content = new();
    private readonly Grid fitHost = new() { Margin = new Thickness(12, 6, 12, 6) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private WeatherReading? reading;
    private CancellationTokenSource? pending;
    private DateTimeOffset nextAttempt = DateTimeOffset.MinValue;

    public WeatherRegion? Region => WeatherAssets.Regions.FirstOrDefault(r => r.Id == context.Settings.Get(WeatherPlugin.RegionKey));
    public string PlaceLine => place.Text;
    public string ConditionLine => condition.Text;
    public string TemperatureLine => temperature.Text;
    public string HumidityLine => humidity.Text;
    public ImageSource? IconSource => icon.Source;

    public WeatherView(WidgetContext widget)
    {
        context = widget;
        AutomationProperties.SetName(this, "날씨");
        AutomationProperties.SetName(spinner, "날씨 불러오는 중");
        spinner.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Muted");
        // Spins only while actually on screen, so a hidden tab costs nothing.
        spinner.IsVisibleChanged += (_, _) => spinner.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, spinner.IsVisible ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever } : null);
        foreach (var muted in new[] { place, humidity, status }) muted.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        // The fixed-width right cell keeps the time at the top-right edge; the spinner opens to its left without moving it.
        var topLine = new Grid();
        topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        var freshness = new Grid { Margin = new Thickness(12, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        freshness.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        freshness.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(status, 1);
        freshness.Children.Add(spinner); freshness.Children.Add(status);
        Grid.SetColumn(freshness, 1);
        topLine.Children.Add(Row(place, condition)); topLine.Children.Add(freshness);
        details.Children.Add(topLine);
        var humidityGroup = Row(Droplet(), humidity); humidityGroup.Margin = new Thickness(16, 0, 0, 0); humidityGroup.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(humidityGroup, "습도");
        var middle = Row(temperature, humidityGroup); middle.Margin = new Thickness(0, 4, 0, 0);
        details.Children.Add(middle);
        // fitHost's margin is the tile's padding; the Viewbox scales the icon and text down together to fit inside it.
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(details, 1);
        content.Children.Add(icon); content.Children.Add(details);
        fitHost.Children.Add(new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Child = content });
        fitHost.SizeChanged += (_, _) => FitContent();
        if (WidgetTheme.IsHud) {
            var face = new Facet.Widgets.HudWeatherFace { VerticalAlignment = WidgetTheme.IsSoftHud ? VerticalAlignment.Stretch : VerticalAlignment.Top };
            BuildRefreshSurface(face);
            ShowPlace();
            Facet.Widgets.Hud.WhenChanged(() => face.Show(place.Text, condition.Text, temperature.Text, humidity.Text), place, condition, temperature, humidity);
        }
        else BuildRefreshSurface(fitHost);
        ShowPlace();
        context.Settings.Changed += SettingChanged;
        timer.Tick += (_, _) => _ = RefreshAsync();
        IsVisibleChanged += (_, _) => {
            if (IsVisible || changed != null) { timer.Start(); _ = RefreshAsync(); }
            else timer.Stop();
        };
    }

    // A skin can draw the weather from this state (ADR 0109); while one listens the reading keeps refreshing even when hidden.
    public string Type => "facet.weather/now@1";
    public string Json() => JsonSerializer.Serialize(new {
        condition = reading?.SkinKey, label = condition.Text, temperature = reading?.Temperature is double t ? Math.Round(t, 1) : (double?)null,
        humidity = reading?.Humidity, place = place.Text, status = status.Text, loading = pending != null,
        observedAt = reading?.ObservedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
    });
    private Action? changed;
    public event Action? Changed {
        add { changed += value; if (!timer.IsEnabled) { timer.Start(); _ = RefreshAsync(); } }
        remove { changed -= value; if (changed == null && !IsVisible) timer.Stop(); }
    }
    // refresh {} fetches now, like pressing the tile.
    public async Task<string?> Act(string name, string argsJson)
    {
        if (name != "refresh") throw new ArgumentException("알 수 없는 동작입니다: " + name);
        await RefreshAsync(force: true);
        return null;
    }

    // A uniform Viewbox on a wider tile would leave all spare width on the right. Widening the content to the
    // tile's proportions makes it fill the width, so the right edge has the same padding as the left.
    private void FitContent()
    {
        double width = fitHost.ActualWidth, height = fitHost.ActualHeight;
        content.Width = double.NaN;
        if (width <= 0 || height <= 0) return;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var natural = content.DesiredSize;
        if (natural.Height > 0 && width / height > natural.Width / natural.Height) content.Width = natural.Height * width / height;
    }

    private void SettingChanged(string key)
    {
        if (key != WeatherPlugin.RegionKey) return;
        reading = null; ShowPlace();
        if (IsVisible) _ = RefreshAsync(force: true);
        else nextAttempt = DateTimeOffset.MinValue;
    }

    // The whole tile is the refresh button, with the built-in widget's hover highlight. The template has its own
    // transparent surface: the app's Button template would print a panel as text, and without a background only
    // the text and icons would take clicks.
    private void BuildRefreshSurface(UIElement content)
    {
        var chrome = new Border { CornerRadius = new CornerRadius(8), Opacity = 0, IsHitTestVisible = false };
        chrome.SetResourceReference(Border.BackgroundProperty, "Hover");
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        surface.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var button = new Button { Content = content, Template = new ControlTemplate(typeof(Button)) { VisualTree = surface }, ContentTemplate = null, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0), Focusable = false, Cursor = System.Windows.Input.Cursors.Hand };
        if (WidgetTheme.IsSoftHud) button.VerticalAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(button, "날씨 새로고침");
        AutomationProperties.SetHelpText(button, "현재 날씨를 다시 불러옵니다.");
        button.Click += (_, _) => _ = RefreshAsync(force: true);
        button.MouseEnter += (_, _) => chrome.BeginAnimation(OpacityProperty, new DoubleAnimation(.4, TimeSpan.FromMilliseconds(140)));
        button.MouseLeave += (_, _) => chrome.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(100)));
        Children.Add(chrome); Children.Add(button);
    }

    // The same drop as the app's humidity icon.
    private static Viewbox Droplet()
    {
        var path = new System.Windows.Shapes.Path { Width = 24, Height = 24, Stretch = Stretch.None, StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Data = Geometry.Parse("M12,2C10,6 5,10 5,15A7,7 0 0 0 19,15C19,10 14,6 12,2Z M8,15Q8,19 12,19") };
        path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "CalendarSaturday");
        return new Viewbox { Width = 16, Height = 18, Margin = new Thickness(0, 0, 6, 0), Child = path };
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 65536 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Facet-WeatherPlugin/1.0");
        return client;
    }

    private static StackPanel Row(params UIElement[] parts)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var part in parts) row.Children.Add(part);
        return row;
    }

    private void ShowPlace() { place.Text = Region?.DisplayName ?? "현재 위치"; FitContent(); changed?.Invoke(); }

    private async Task<(double Latitude, double Longitude)> LocateAsync(WeatherRegion? region, CancellationToken token)
    {
        if (region != null) return (region.Latitude, region.Longitude);
        try {
            if (AppCapability.Create("location").CheckAccess() != AppCapabilityAccessStatus.Allowed) throw new LocationUnavailableException("설정에서 위치를 허용하거나 날씨 지역을 선택해 주세요.");
            var position = await new Geolocator { DesiredAccuracyInMeters = 100 }.GetGeopositionAsync(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8)).AsTask(token);
            var point = position.Coordinate.Point.Position;
            place.Text = WeatherAssets.PlaceName(point.Latitude, point.Longitude) ?? "현재 위치";
            return (point.Latitude, point.Longitude);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or TimeoutException or InvalidOperationException or NotSupportedException) {
            throw new LocationUnavailableException("Windows 위치를 쓸 수 없습니다. 설정에서 날씨 지역을 선택해 주세요.");
        }
    }

    private async Task RefreshAsync(bool force = false)
    {
        var now = DateTimeOffset.Now;
        if (!force && (pending != null || now < nextAttempt)) return;
        pending?.Cancel();
        spinner.Visibility = Visibility.Visible;
        changed?.Invoke();
        var request = new CancellationTokenSource(TimeSpan.FromSeconds(25)); pending = request;
        try {
            var (latitude, longitude) = await LocateAsync(Region, request.Token);
            string url = FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={latitude:F4}&longitude={longitude:F4}&current=temperature_2m,relative_humidity_2m,weather_code,is_day&temperature_unit=celsius&timeformat=unixtime&timezone=auto");
            var result = WeatherReading.Parse(await Client.GetStringAsync(url, request.Token));
            if (!ReferenceEquals(pending, request)) return;
            Show(result); nextAttempt = now.AddMinutes(10);
        }
        // Plugin failures stay inside the tile: anything unexpected only marks the reading as out of date.
        catch (Exception ex) {
            if (!ReferenceEquals(pending, request)) return;
            nextAttempt = now.AddMinutes(5);
            string prefix = reading == null ? "" : reading.ObservedAt.ToLocalTime().ToString("HH:mm", Korean) + " 기준 · ";
            status.Text = prefix + (ex is LocationUnavailableException ? ex.Message : "연결 후 자동 재시도");
            if (reading == null) condition.Text = "날씨 미확인";
        }
        finally {
            if (ReferenceEquals(pending, request)) { pending = null; spinner.Visibility = Visibility.Collapsed; FitContent(); changed?.Invoke(); }
            request.Dispose();
        }
    }

    public void Show(WeatherReading result)
    {
        var image = WeatherAssets.Icon(result.IconName);
        reading = result;
        condition.Text = result.Description;
        temperature.Text = result.Temperature?.ToString("0.#", Korean) is string value ? value + "°C" : "--°C";
        humidity.Text = (result.Humidity?.ToString(Korean) ?? "--") + "%";
        AutomationProperties.SetName(humidity, "습도 " + humidity.Text);
        status.Text = result.ObservedAt.ToLocalTime().ToString("HH:mm", Korean) + " 기준";
        icon.Source = image;
        icon.Visibility = image == null ? Visibility.Hidden : Visibility.Visible;
        AutomationProperties.SetName(icon, result.Description);
        FitContent();
        // Other widgets can read the latest reading through the workspace lookup.
        context.Data?.Set("reading", JsonSerializer.Serialize(new { place = place.Text, description = result.Description, result.Temperature, result.Humidity, observedAt = result.ObservedAt }));
    }

    public void Dispose()
    {
        timer.Stop();
        context.Settings.Changed -= SettingChanged;
        var request = pending; pending = null; request?.Cancel();
    }
}
