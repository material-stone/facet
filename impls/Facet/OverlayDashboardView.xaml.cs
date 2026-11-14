using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using Facet.Design;
using Facet.Plugins;
using Facet.Widgets;
using System.Windows.Threading;

namespace Facet;

public partial class OverlayDashboardView : UserControl
{
    private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Func<CancellationToken, Task<WeatherSnapshot>> fetch;
    private CancellationTokenSource? pending;
    private bool keepRequestWhenHidden;
    private WeatherSnapshot? weather;
    private DateTimeOffset nextAttempt = DateTimeOffset.MinValue;
    private Controller? controller;
    private IReadOnlyList<AgendaCard>? lastAgenda;
    private readonly List<FrameworkElement> sections = [];

    public OverlayDashboardView() : this(null) { }

    internal OverlayDashboardView(Func<CancellationToken, Task<WeatherSnapshot>>? fetch)
    {
        this.fetch = fetch ?? new WeatherClient(locate: token => WeatherRegions.ResolveAsync(controller == null ? null : PluginData.WeatherRegion(controller.Data), token)).FetchAsync;
        InitializeComponent();
        if (WidgetTheme.IsHud) UseHudForm();
        UpdateClock(DateTime.Now);
        RefreshAgenda(DateTimeOffset.Now);
        clock.Tick += (_, _) => Tick();
        foreach (var section in new FrameworkElement[] { ClockGroup, WeatherHoverArea, AgendaSection }) {
            sections.Add(section);
            section.Loaded += (_, _) => UpdateActivity();
            section.IsVisibleChanged += (_, _) => UpdateActivity();
            section.Unloaded += (_, _) => UpdateActivity();
        }
        AgendaScroll.PreviewMouseWheel += (_, e) => {
            if (AgendaScroll.ScrollableWidth <= 0) return;
            AgendaScroll.ScrollToHorizontalOffset(AgendaScroll.HorizontalOffset-e.Delta); e.Handled = true;
        };
    }

    internal void UpdateClock(DateTime now)
    {
        DateText.Text = now.ToString("yyyy년 M월 d일 dddd", Korean);
        TimeText.Text = now.ToString("tt hh:mm:ss", Korean);
        hudClock?.Show(now);
    }

    // Sections can sit on different tabs as widgets, so updates run while any of them is on screen.
    private void UpdateActivity()
    {
        if (sections.Any(section => section.IsLoaded && section.IsVisible && Window.GetWindow(section)?.IsVisible == true)) {
            if (!clock.IsEnabled) { Tick(); clock.Start(); }
        }
        else PauseUpdates();
    }

    // Hands the XAML sections to the widget grid; this view keeps their logic and named elements.
    internal IReadOnlyDictionary<string, FrameworkElement> DetachSections()
    {
        FrameworkElement Take(FrameworkElement section)
        {
            ((Panel)section.Parent).Children.Remove(section);
            section.Margin = new Thickness(0);
            // Tiles live under OverlayTabs' name scope; storyboards in a section still target names declared here.
            if (NameScope.GetNameScope(section) == null) NameScope.SetNameScope(section, NameScope.GetNameScope(this));
            return section;
        }
        return new Dictionary<string, FrameworkElement> {
            [WorkspaceLayouts.ClockWidget] = Take(ClockGroup), [WorkspaceLayouts.WeatherWidget] = Take(WeatherHoverArea),
            [WorkspaceLayouts.AgendaWidget] = Take(AgendaSection)
        };
    }

    private void Tick()
    {
        UpdateClock(DateTime.Now);
        if (hudAgenda != null) RefreshAgenda(DateTimeOffset.Now);
        _ = RefreshWeatherAsync(DateTimeOffset.Now);
    }

    internal Task RefreshWeatherOnLaunchAsync() => RefreshWeatherAsync(DateTimeOffset.Now, force: true, keepWhenHidden: true);

    internal void ReloadWeatherLocation()
    {
        PauseUpdates(cancelStartup: true);
        nextAttempt = DateTimeOffset.MinValue;
        _ = RefreshWeatherOnLaunchAsync();
        UpdateActivity();
    }

    private void RefreshWeatherClicked(object sender, RoutedEventArgs e) => _ = RefreshWeatherAsync(DateTimeOffset.Now, force: true);

    private void SetWeatherLoading(bool loading)
    {
        WeatherLoading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        WeatherRefresh.IsEnabled = !loading;
    }

    internal void PauseUpdates(bool cancelStartup = false)
    {
        clock.Stop();
        if (keepRequestWhenHidden && !cancelStartup) return;
        var request = pending; pending = null; keepRequestWhenHidden = false; request?.Cancel();
        SetWeatherLoading(false);
        if (request != null) {
            WeatherStatus.Text = weather == null ? "날씨 연결 대기" : weather.ObservedAt.ToLocalTime().ToString("HH:mm", Korean) + " 기준";
            if (weather == null) ConditionText.Text = "날씨 확인 중";
        }
    }

    internal async Task RefreshWeatherAsync(DateTimeOffset now, bool force = false, bool keepWhenHidden = false)
    {
        if (pending != null) {
            keepRequestWhenHidden |= keepWhenHidden;
            return;
        }
        if (!force && now < nextAttempt) return;
        var request = new CancellationTokenSource(TimeSpan.FromSeconds(25)); pending = request;
        keepRequestWhenHidden = keepWhenHidden;
        SetWeatherLoading(true);
        try {
            var result = await fetch(request.Token);
            if (request.IsCancellationRequested || !ReferenceEquals(pending, request)) return;
            ApplyWeather(result); nextAttempt = now.AddMinutes(10);
        }
        catch (OperationCanceledException) when (!ReferenceEquals(pending, request)) { }
        catch (WeatherLocationUnavailableException ex) {
            if (!ReferenceEquals(pending, request)) return;
            nextAttempt = now.AddMinutes(5);
            if (weather == null) { LocationText.Text = "위치 확인 필요"; ConditionText.Text = "날씨 미확인"; }
            WeatherStatus.Text = weather == null ? ex.Message : weather.ObservedAt.ToLocalTime().ToString("HH:mm", Korean) + " 기준 · 날씨 지역 설정 필요";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or OperationCanceledException) {
            if (!ReferenceEquals(pending, request)) return;
            nextAttempt = now.AddMinutes(5);
            if (weather == null) {
                LocationText.Text = "위치·날씨 연결 대기";
                ConditionText.Text = "날씨 미확인";
                WeatherStatus.Text = "연결 후 자동 재시도";
            }
            else WeatherStatus.Text = weather.ObservedAt.ToLocalTime().ToString("HH:mm", Korean) + " 기준 · 연결 대기";
        }
        finally {
            if (ReferenceEquals(pending, request)) {
                pending = null; keepRequestWhenHidden = false; SetWeatherLoading(false);
            }
            request.Dispose();
        }
    }

    internal void ApplyWeather(WeatherSnapshot result)
    {
        // Decode/validate the local icon before replacing any displayed weather values.
        var icon = WeatherIcons.Load(result.Icon);
        weather = result;
        LocationText.Text = KoreanPlaces.Display(result.Location);
        ConditionText.Text = result.Description;
        TemperatureText.Text = result.Temperature?.ToString("0.#", Korean) is string temperature ? temperature + "°C" : "--°C";
        HumidityText.Text = (result.Humidity?.ToString(Korean) ?? "--") + "%";
        AutomationProperties.SetName(HumidityText, "습도 " + HumidityText.Text);
        WeatherStatus.Text = result.ObservedAt.ToLocalTime().ToString("HH:mm", Korean) + " 기준";
        WeatherIcon.Source = icon;
        WeatherIcon.Visibility = icon == null ? Visibility.Hidden : Visibility.Visible;
        AutomationProperties.SetName(WeatherIcon, result.Description);
    }

    internal void Connect(Controller app)
    {
        controller = app; RefreshAgenda(DateTimeOffset.Now);
    }

    private HudClockFace? hudClock;
    private StackPanel? hudAgenda;
    private string hudAgendaShown = "";

    // The same sections and data as the normal view, in the theme's instrument form (ADR 0086).
    private void UseHudForm()
    {
        hudClock = new();
        ClockGroup.Children.Clear();
        ClockGroup.Width = hudClock.Width;
        ClockGroup.Children.Add(hudClock);
        var face = new HudWeatherFace();
        WeatherHoverArea.HorizontalAlignment = HorizontalAlignment.Stretch;
        WeatherHoverArea.VerticalAlignment = VerticalAlignment.Top;
        HoverChrome.Margin = new(-6);
        WeatherGroup.Children.Clear();
        WeatherRefresh.HorizontalAlignment = HorizontalAlignment.Stretch;
        WeatherRefresh.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        WeatherRefresh.VerticalAlignment = VerticalAlignment.Top;
        WeatherRefresh.Content = face;
        Hud.WhenChanged(() => face.Show(LocationText.Text, ConditionText.Text, TemperatureText.Text, HumidityText.Text), LocationText, ConditionText, TemperatureText, HumidityText);
        AgendaSection.Children.Clear();
        AgendaSection.Children.Add(Hud.Header("오늘 일정"));
        hudAgenda = new() { Margin = new(0,WidgetTheme.IsSoftHud ? 8 : 16,0,0) };
        AgendaSection.Children.Add(hudAgenda);
    }

    private void ShowHudAgenda(DateTime now)
    {
        var (previous, current, next) = controller == null ? default : DaySchedules.Around(controller.Data, now);
        string minutes = current == null ? "" : Math.Ceiling((current.End - now).TotalMinutes).ToString("0", Korean);
        string shown = string.Join("|", previous, current, next, minutes);
        if (shown == hudAgendaShown) return;
        hudAgendaShown = shown; hudAgenda!.Children.Clear();
        if (WidgetTheme.IsSoftHud) { ShowSoftAgenda(previous, current, next, minutes, now); return; }
        TextBlock Slot(ScheduleSlot slot, double size, string brush, bool strong = false, double top = 0)
        {
            var line = Hud.Text("", size, brush, strong: strong);
            line.Margin = new(0,top,0,0);
            line.Inlines.Add(new System.Windows.Documents.Run(slot.Start.ToString("HH:mm", Korean)) { FontFamily = Hud.NumberFont });
            line.Inlines.Add(new System.Windows.Documents.Run("   " + slot.Title));
            return line;
        }
        var body = new StackPanel();
        if (previous != null) {
            var line = Slot(previous, 16, "Ink"); line.Opacity = .72; line.Margin = new(0,0,0,18);
            body.Children.Add(line);
        }
        if (current != null) {
            string color = current.IsEnding(now) ? "ScheduleNow" : "Mint";
            var label = Hud.Text("지금 · 종료까지 " + minutes + "분", 13, color);
            label.Margin = new(0,0,0,4);
            body.Children.Add(label);
            var title = new Grid();
            var mark = new System.Windows.Shapes.Path { Data = Geometry.Parse("M6,0L12,6L6,12L0,6Z"), Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new(-28.5,0,0,0) };
            mark.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, color);
            title.Children.Add(mark); title.Children.Add(Slot(current, 22, color, strong: true));
            body.Children.Add(title);
            if (current.Memo.Length > 0) {
                var memo = Hud.Text(current.Memo, 14, color);
                memo.Margin = new(0,6,0,0);
                body.Children.Add(memo);
            }
        }
        else body.Children.Add(Hud.Text("지금 진행 중인 일정이 없습니다", 16, "Muted"));
        if (next != null) body.Children.Add(Slot(next, 16, "Ink", top: 18));
        var rail = new Border { BorderThickness = new(1,0,0,0), Padding = new(22,0,0,0), Child = body };
        var edge = ((SolidColorBrush)FindResource("Mint")).CloneCurrentValue(); edge.Opacity = .55; edge.Freeze();
        rail.BorderBrush = edge;
        hudAgenda.Children.Add(rail);
    }

    // Current schedule as the main line with its time at the right, the neighbours as quiet rows.
    private void ShowSoftAgenda(ScheduleSlot? previous, ScheduleSlot? current, ScheduleSlot? next, string minutes, DateTime now)
    {
        Grid Row(string label, ScheduleSlot slot, double size, string brush, bool strong, double top)
        {
            var row = new Grid { Margin = new(0,top,0,0) };
            row.ColumnDefinitions.Add(new());
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var name = Hud.Text(label + slot.Title, size, brush);
            if (strong) name.FontFamily = Hud.NumberFont;
            var time = Hud.Text(slot.Start.ToString("HH:mm", Korean), size, brush, mono: true);
            time.Margin = new(12,0,0,0); Grid.SetColumn(time, 1);
            row.Children.Add(name); row.Children.Add(time);
            return row;
        }
        if (previous != null) hudAgenda!.Children.Add(Row("이전 · ", previous, 14, "Muted", false, 0));
        if (current != null) {
            string color = current.IsEnding(now) ? "ScheduleNow" : "Ink";
            hudAgenda!.Children.Add(Row("", current, 24, color, true, previous == null ? 0 : 10));
            string detail = current.Memo.Length > 0 ? current.Memo : "종료까지 " + minutes + "분";
            var memo = Hud.Text(detail, 14, "Muted"); memo.Margin = new(0,3,0,0);
            hudAgenda.Children.Add(memo);
        }
        else {
            var empty = Hud.Text("지금 진행 중인 일정이 없습니다", 16, "Muted"); empty.Margin = new(0,previous == null ? 0 : 10,0,0);
            hudAgenda!.Children.Add(empty);
        }
        if (next != null) hudAgenda!.Children.Add(Row("다음 · ", next, 14, "Muted", false, 10));
    }

    internal void RefreshAgenda(DateTimeOffset now)
    {
        if (hudAgenda != null) { ShowHudAgenda(now.LocalDateTime); return; }
        IReadOnlyList<AgendaCard> items = controller == null ? Array.Empty<AgendaCard>() : DashboardAgenda.Today(controller.Data, now);
        ShowAgenda(items);
    }

    internal void ShowAgenda(IReadOnlyList<AgendaCard> items)
    {
        if (lastAgenda != null && lastAgenda.SequenceEqual(items)) return;
        lastAgenda = items; AgendaItems.Children.Clear();
        if (items.Count == 0) {
            AgendaItems.Children.Add(CreateAgendaCard(new("empty", "오늘 등록된 일정이 없습니다", "등록한 일정과 알림이 여기에 표시됩니다.", "오늘", null)));
            return;
        }
        foreach (var item in items) AgendaItems.Children.Add(CreateAgendaCard(item));
    }

    private Border CreateAgendaCard(AgendaCard item)
    {
        var stack = new StackPanel();
        var heading = new Grid(); heading.ColumnDefinitions.Add(new()); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.Children.Add(IconUi.Label(item.Label == "일정" ? IconKind.Calendar : item.Label == "할 일" ? IconKind.Check : IconKind.Bell, item.Label, (Brush)FindResource("Muted"), 12));
        var time = new TextBlock { Text = item.At?.ToString("HH:mm", Korean) ?? "오늘", FontSize = 12, Foreground = (Brush)FindResource("Muted") };
        Grid.SetColumn(time, 1); heading.Children.Add(time); stack.Children.Add(heading);
        stack.Children.Add(new TextBlock { Text = item.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new(0,8,0,0),
            MaxHeight = 40, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextTrimming = TextTrimming.CharacterEllipsis });
        if (item.Body.Length > 0) stack.Children.Add(new TextBlock { Text = item.Body, FontSize = 12, Foreground = (Brush)FindResource("Muted"),
            Margin = new(0,4,0,0), MaxHeight = 16, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis });
        var card = new Border { Width = 236, Height = 124, CornerRadius = new(16), Padding = new(16), Margin = new(0,0,16,8),
            Background = (Brush)FindResource("Panel"), BorderBrush = (Brush)FindResource("Edge"), BorderThickness = new(1), Child = stack };
        AutomationProperties.SetName(card, item.Label + " · " + time.Text + " · " + item.Title); return card;
    }
}
