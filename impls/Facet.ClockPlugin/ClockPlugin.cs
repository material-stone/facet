using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Facet.Plugins;
using Facet.Widgets;

namespace Facet.ClockPlugin;

public sealed class ClockPlugin : IFacetPlugin
{
    public void Register(IWidgetRegistry registry)
    {
        registry.AddWidget(new WidgetDescriptor("clock", "날짜와 시계", context => new ClockView(context))
        { Description = "날짜, 요일과 현재 시각", DefaultColumns = 3, DefaultRows = 1, MinColumns = 2, MinRows = 1, AllowMultiple = false, AllowMultipleTabs = true, UsesData = true, HasHudPanel = true });
        // Each placement keeps its own alignment, mode and style, so both can be placed more than once.
        registry.AddWidget(new WidgetDescriptor("weekday", "요일", context => new ClockFaceView(context, "요일", FaceChoices.Weekday))
        { Description = "오늘 요일, 아날로그·디지털 선택", DefaultColumns = 2, DefaultRows = 1, MinColumns = 1, MinRows = 1, AllowMultiple = true, AllowMultipleTabs = true, UsesData = true });
        registry.AddWidget(new WidgetDescriptor("time", "시계", context => new ClockFaceView(context, "시계", FaceChoices.Time))
        { Description = "현재 시각, 아날로그·디지털 선택", DefaultColumns = 2, DefaultRows = 2, MinColumns = 1, MinRows = 1, AllowMultiple = true, AllowMultipleTabs = true, UsesData = true });
    }
}

// Fills its tile with the dashboard clock's layout: a date line above a stretched time line, both in fixed frames so the ratio never changes.
// The state every clock widget publishes: weekdayIndex 0 is Sunday; mode and style are null for the date and time widget.
public static class ClockState
{
    private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");
    public static string Json(DateTime now, string? mode, string? style) => System.Text.Json.JsonSerializer.Serialize(new {
        time = now.ToString("HH:mm", CultureInfo.InvariantCulture), hour = now.Hour, minute = now.Minute, seconds = now.Second,
        weekday = now.ToString("dddd", Korean), weekdayIndex = (int)now.DayOfWeek,
        date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), dateLabel = now.ToString("M월 d일", Korean), mode, style
    });
}

public sealed class ClockView : Decorator, IDisposable, IWidgetState
{
    private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");
    private readonly TextBlock date = new() { FontSize = 24, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock time = new() { FontSize = 48, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap };
    private readonly DispatcherTimer timer = new();
    private readonly HudClockFace? face;

    public ClockView() : this(new WidgetContext("", "")) { }

    public ClockView(WidgetContext context)
    {
        AutomationProperties.SetName(this, "시계");
        Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
        date.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        time.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var stack = new StackPanel { Width = 300 };
        stack.Children.Add(new Viewbox { Width = 300, Height = 38, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, Child = date });
        stack.Children.Add(new Viewbox { Width = 300, Height = 60, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), Child = time });
        FrameworkElement body = stack;
        if (WidgetTheme.IsHud) body = face = new HudClockFace();
        Child = new ClockFitButton(body, () => context.Modal, value => context.Data?.Set(ClockFitButton.AlignmentKey, value) == true) { Alignment = context.Data?.Get(ClockFitButton.AlignmentKey) ?? ClockFitButton.DefaultAlignment };
        ShowTime(DateTime.Now);
        timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) => { if (IsVisible || changed != null) Tick(); else timer.Stop(); };
    }

    // A skin can draw this clock from its state (ADR 0109); while one listens the clock keeps ticking even when hidden.
    public string Type => "facet.clock/date-time@1";
    public string Json() => ClockState.Json(DateTime.Now, null, null);
    private Action? changed;
    public event Action? Changed { add { changed += value; Tick(); } remove { changed -= value; if (changed == null && !IsVisible) timer.Stop(); } }
    public Task<string?> Act(string name, string argsJson) => throw new ArgumentException("알 수 없는 동작입니다: " + name);

    public string DateLine => date.Text;
    public string TimeLine => time.Text;

    public void ShowTime(DateTime now)
    {
        date.Text = now.ToString("yyyy년 M월 d일 dddd", Korean);
        time.Text = now.ToString("tt hh:mm:ss", Korean);
        face?.Show(now);
    }

    // Waits for the next second boundary so the shown seconds never skip from timer drift.
    private void Tick()
    {
        var now = DateTime.Now;
        ShowTime(now);
        changed?.Invoke();
        timer.Interval = TimeSpan.FromMilliseconds(1000 - now.Millisecond);
        timer.Start();
    }

    public void Dispose() => timer.Stop();
}
