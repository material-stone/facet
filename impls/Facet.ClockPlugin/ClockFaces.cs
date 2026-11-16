using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Facet.Plugins;
using Facet.Widgets;

namespace Facet.ClockPlugin;

// One look a widget can take. Mode is "analog" or "digital"; Style names the look inside that mode.
public sealed record FaceChoice(string Mode, string Style, string Name, Func<ClockFace> Create);

public static class FaceChoices
{
    public const string ModeKey = "mode", StyleKey = "style";

    public static readonly IReadOnlyList<FaceChoice> Weekday = [
        new("digital", "strip", "요일 띠", () => new WeekdayStripFace()),
        new("digital", "large", "큰 글씨", () => new WeekdayLargeFace()),
        new("analog", "dial", "요일 다이얼", () => new WeekdayDialFace()),
    ];

    public static readonly IReadOnlyList<FaceChoice> Time = [
        new("analog", "classic", "클래식", () => new AnalogClockFace(minimal: false)),
        new("analog", "minimal", "미니멀", () => new AnalogClockFace(minimal: true)),
        new("digital", "plain", "숫자", () => new DigitalClockFace()),
    ];

    // Missing or unknown values fall back to the first choice.
    public static FaceChoice Find(IReadOnlyList<FaceChoice> choices, string? mode, string? style) =>
        choices.FirstOrDefault(c => c.Mode == mode && c.Style == style) ?? choices[0];

    public static string ModeName(string mode) => mode == "analog" ? "아날로그" : "디지털";
}

// A widget that shows one face picked per placement, with alignment, mode and style saved in its store.
public sealed class ClockFaceView : Decorator, IDisposable
{
    private readonly IReadOnlyList<FaceChoice> choices;
    private readonly IWidgetData? data;
    private readonly ClockFitButton button;
    private readonly DispatcherTimer timer = new();
    private ClockFace face;

    public ClockFaceView(WidgetContext context, string name, IReadOnlyList<FaceChoice> choices)
    {
        this.choices = choices; data = context.Data;
        AutomationProperties.SetName(this, name);
        Choice = FaceChoices.Find(choices, data?.Get(FaceChoices.ModeKey), data?.Get(FaceChoices.StyleKey));
        face = Choice.Create();
        button = new ClockFitButton(face, () => context.Modal, value => data?.Set(ClockFitButton.AlignmentKey, value) == true, name + " 설정", BuildFacePicker)
        { Alignment = data?.Get(ClockFitButton.AlignmentKey) ?? ClockFitButton.DefaultAlignment };
        Child = button;
        ShowTime(DateTime.Now);
        timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) => { if (IsVisible) Tick(); else timer.Stop(); };
    }

    public FaceChoice Choice { get; private set; }
    public ClockFace Face => face;
    internal ClockFitButton Button => button;

    public void ShowTime(DateTime now) => face.Show(now);

    // Saves both keys; a failed save keeps the old face.
    public bool Select(FaceChoice choice)
    {
        if (data != null && !(data.Set(FaceChoices.ModeKey, choice.Mode) && data.Set(FaceChoices.StyleKey, choice.Style))) {
            data.Set(FaceChoices.ModeKey, Choice.Mode); data.Set(FaceChoices.StyleKey, Choice.Style);
            return false;
        }
        Choice = choice;
        face = choice.Create();
        button.Face = face;
        ShowTime(DateTime.Now);
        return true;
    }

    public FrameworkElement BuildFacePicker()
    {
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed, Text = "저장하지 못했습니다. 다시 시도해 주세요." };
        error.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var buttons = new List<Button>();
        void Mark()
        {
            foreach (var b in buttons) b.SetResourceReference(Control.BorderBrushProperty, (FaceChoice)b.Tag == Choice ? "Mint" : "Edge");
        }
        foreach (var mode in choices.Select(c => c.Mode).Distinct()) {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var label = new TextBlock { Text = FaceChoices.ModeName(mode), Width = 72, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            row.Children.Add(label);
            var line = new WrapPanel();
            foreach (var choice in choices.Where(c => c.Mode == mode)) {
                var text = new TextBlock { Text = choice.Name, FontSize = 13 };
                text.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
                var b = new Button { Tag = choice, Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(12, 6, 12, 6), BorderThickness = new Thickness(2), Background = Brushes.Transparent, Template = ClockFitButton.Chrome(new CornerRadius(8)), ContentTemplate = null, Cursor = System.Windows.Input.Cursors.Hand };
                AutomationProperties.SetName(b, FaceChoices.ModeName(mode) + " " + choice.Name);
                b.Click += (_, _) => { bool saved = Select(choice); error.Visibility = saved ? Visibility.Collapsed : Visibility.Visible; Mark(); };
                buttons.Add(b); line.Children.Add(b);
            }
            row.Children.Add(line);
            root.Children.Add(row);
        }
        Mark();
        root.Children.Add(error);
        return root;
    }

    // Waits for the next second boundary so the shown seconds never skip from timer drift.
    private void Tick()
    {
        var now = DateTime.Now;
        ShowTime(now);
        timer.Interval = TimeSpan.FromMilliseconds(1000 - now.Millisecond);
        timer.Start();
    }

    public void Dispose() => timer.Stop();
}

// Draws at a fixed size so the fit button can scale it without the ratio changing; colors follow the skin tokens.
public abstract class ClockFace : FrameworkElement
{
    protected static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");
    protected static readonly string[] Days = ["일", "월", "화", "수", "목", "금", "토"];
    public static readonly DependencyProperty InkProperty = Token(nameof(Ink)), MutedProperty = Token(nameof(Muted)), AccentProperty = Token(nameof(Accent)), OnAccentProperty = Token(nameof(OnAccent)), EdgeProperty = Token(nameof(Edge));

    private static DependencyProperty Token(string name) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(ClockFace), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    protected ClockFace(double width, double height)
    {
        Width = width; Height = height;
        SetResourceReference(InkProperty, "Ink"); SetResourceReference(MutedProperty, "Muted");
        SetResourceReference(AccentProperty, "Mint"); SetResourceReference(OnAccentProperty, "OnAccent"); SetResourceReference(EdgeProperty, "Edge");
    }

    public Brush Ink => (Brush)GetValue(InkProperty);
    public Brush Muted => (Brush)GetValue(MutedProperty);
    public Brush Accent => (Brush)GetValue(AccentProperty);
    public Brush OnAccent => (Brush)GetValue(OnAccentProperty);
    public Brush Edge => (Brush)GetValue(EdgeProperty);
    public DateTime Now { get; private set; }

    public void Show(DateTime now) { Now = now; InvalidateVisual(); }

    // Text the screen reader and the checks can read.
    public abstract string Text { get; }

    protected FormattedText Label(string text, double size, Brush brush, bool bold = false)
    {
        var face = new Typeface(System.Windows.Documents.TextElement.GetFontFamily(this), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
        return new FormattedText(text, Korean, FlowDirection.LeftToRight, face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    protected void Centered(DrawingContext dc, string text, Point center, double size, Brush brush, bool bold = false)
    {
        var t = Label(text, size, brush, bold);
        dc.DrawText(t, new Point(center.X - t.Width / 2, center.Y - t.Height / 2));
    }

    protected static Point At(Point center, double radius, double degrees)
    {
        double a = (degrees - 90) * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a));
    }

    protected static void Hand(DrawingContext dc, Point center, double degrees, double length, double back, double thickness, Brush brush)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(pen, At(center, -back, degrees), At(center, length, degrees));
    }
}

// Seven cells with today filled.
public sealed class WeekdayStripFace() : ClockFace(296, 56)
{
    public override string Text => Days[(int)Now.DayOfWeek] + "요일";

    protected override void OnRender(DrawingContext dc)
    {
        int today = (int)Now.DayOfWeek;
        for (int i = 0; i < 7; i++) {
            var cell = new Rect(i * 43, 0, 38, 56);
            if (i == today) dc.DrawRoundedRectangle(Accent, null, cell, 8, 8);
            Centered(dc, Days[i], new Point(cell.X + 19, 28), 20, i == today ? OnAccent : Muted, i == today);
        }
    }
}

// The day name large with the date under it.
public sealed class WeekdayLargeFace() : ClockFace(200, 92)
{
    public override string Text => Now.ToString("dddd", Korean);

    protected override void OnRender(DrawingContext dc)
    {
        var name = Label(Text, 52, Ink, bold: true);
        dc.DrawText(name, new Point((200 - name.Width) / 2, 0));
        var date = Label(Now.ToString("M월 d일", Korean), 18, Muted);
        dc.DrawText(date, new Point((200 - date.Width) / 2, 66));
    }
}

// Seven days around a dial; the hand crosses today's slice over the day, at its middle at noon.
public sealed class WeekdayDialFace() : ClockFace(120, 120)
{
    public override string Text => Days[(int)Now.DayOfWeek] + "요일";

    public double HandDegrees => ((int)Now.DayOfWeek + Now.TimeOfDay.TotalHours / 24 - .5) * 360 / 7;

    protected override void OnRender(DrawingContext dc)
    {
        var c = new Point(60, 60);
        dc.DrawEllipse(null, new Pen(Edge, 1), c, 56, 56);
        int today = (int)Now.DayOfWeek;
        for (int i = 0; i < 7; i++) Centered(dc, Days[i], At(c, 42, i * 360.0 / 7), 12, i == today ? Accent : Muted, i == today);
        Hand(dc, c, HandDegrees, 30, 6, 2, Ink);
        dc.DrawEllipse(Ink, null, c, 3, 3);
    }
}

// Hour, minute and second hands; the classic face adds numbers and minute ticks.
public sealed class AnalogClockFace(bool minimal) : ClockFace(120, 120)
{
    public bool Minimal => minimal;
    public override string Text => Now.ToString("tt h시 m분", Korean);

    protected override void OnRender(DrawingContext dc)
    {
        var c = new Point(60, 60);
        dc.DrawEllipse(null, new Pen(Edge, 1), c, 56, 56);
        if (minimal)
            for (int q = 0; q < 4; q++) dc.DrawEllipse(Ink, null, At(c, 48, q * 90), 2.5, 2.5);
        else {
            for (int i = 0; i < 60; i++) {
                bool hour = i % 5 == 0;
                dc.DrawLine(new Pen(hour ? Ink : Muted, hour ? 1.4 : .6), At(c, hour ? 48 : 52, i * 6), At(c, 55, i * 6));
            }
            for (int h = 1; h <= 12; h++) Centered(dc, h.ToString(Korean), At(c, 40, h * 30), 9, Ink);
        }
        var t = Now.TimeOfDay;
        double hour12 = t.TotalHours % 12, minutes = t.TotalMinutes % 60;
        Hand(dc, c, hour12 * 30, minimal ? 28 : 26, minimal ? 0 : 5, minimal ? 6 : 3, Ink);
        Hand(dc, c, minutes * 6, minimal ? 42 : 38, minimal ? 0 : 6, minimal ? 4 : 2, Ink);
        Hand(dc, c, Now.Second * 6, 46, minimal ? 8 : 10, minimal ? 1 : .8, Accent);
        dc.DrawEllipse(Accent, null, c, minimal ? 3 : 2.5, minimal ? 3 : 2.5);
    }
}

// Large hours and minutes with small seconds, and 오전/오후 under them.
public sealed class DigitalClockFace() : ClockFace(220, 92)
{
    public override string Text => Now.ToString("HH:mm:ss", Korean);

    protected override void OnRender(DrawingContext dc)
    {
        var main = Label(Now.ToString("HH:mm", Korean), 56, Ink, bold: true);
        var seconds = Label(Now.ToString("ss", Korean), 22, Muted);
        double x = (220 - main.Width - 6 - seconds.Width) / 2;
        dc.DrawText(main, new Point(x, 0));
        dc.DrawText(seconds, new Point(x + main.Width + 6, main.Baseline - seconds.Baseline));
        var noon = Label(Now.Hour < 12 ? "오전" : "오후", 16, Muted);
        dc.DrawText(noon, new Point((220 - noon.Width) / 2, 70));
    }
}
