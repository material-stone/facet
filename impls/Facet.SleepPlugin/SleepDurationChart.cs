using System.Globalization;
using System.Windows.Automation;
using System.Windows.Shapes;
using Facet.Plugins;
using Facet.Widgets;

namespace Facet.SleepPlugin;

internal sealed class SleepDurationChart : UserControl, IDisposable
{
    private readonly bool hud = WidgetTheme.IsHud;
    private readonly SleepDurationPlot plot = new() { IsHitTestVisible = false };
    private readonly List<Button> dayButtons = [];
    private readonly List<Ellipse> markers = [];
    private readonly List<TextBlock> dateLabels = [];
    private readonly TextBlock rangeText = new() { HorizontalAlignment = HorizontalAlignment.Right, FontSize = 12 };
    private readonly TextBlock averageText = new() { Text = "평균 수면 시간 —", FontSize = 13, Margin = new(0,12,0,0) };
    private string? fingerprint;
    private double PlotHeight => hud ? 124 : 250;
    internal Canvas PlotCanvas { get; } = new();
    internal SleepEditing? Editing { get; init; }
    internal SleepLive? Live { get; set; }
    internal string AverageText => averageText.Text;
    internal IReadOnlyList<SleepDay> Days { get; private set; } = [];
    internal IReadOnlyList<Button> DayButtons => dayButtons;
    internal IReadOnlyList<(Point Start, Point End)> Connections => plot.Connections;
    internal IReadOnlyList<Point> PointLocations => plot.Locations;
    internal event Action<DateOnly>? EditRequested;

    public SleepDurationChart()
    {
        AutomationProperties.SetName(this, "최근 7일 수면량");
        plot.Height = PlotCanvas.Height = PlotHeight;
        var body = new StackPanel(); var title = new Grid { Margin = new(0,0,0,8) };
        title.Children.Add(new TextBlock { Text = "최근 7일 수면량", FontSize = 16, FontWeight = FontWeights.SemiBold });
        rangeText.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); title.Children.Add(rangeText);
        averageText.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        if (hud) {
            PlotCanvas.Margin = new(0,6,0,0);
            body.Children.Add(Facet.Widgets.Hud.Header("7일 수면")); body.Children.Add(PlotCanvas);
            if (WidgetTheme.IsSoftHud) {
                // The plot takes all spare height; its scale follows in LayoutPoints.
                var fill = new Grid();
                fill.RowDefinitions.Add(new() { Height = GridLength.Auto }); fill.RowDefinitions.Add(new());
                var heading = body.Children[0]; body.Children.Clear();
                Grid.SetRow(PlotCanvas, 1); PlotCanvas.Height = double.NaN; PlotCanvas.MinHeight = PlotHeight;
                fill.Children.Add(heading); fill.Children.Add(PlotCanvas);
                Content = fill;
                PlotCanvas.Children.Add(plot);
                PlotCanvas.SizeChanged += (_, _) => LayoutPoints();
                // Sleeping cats breathe on their cushions (ADR 0107).
                if (WidgetTheme.IsCatHud) { PlotCanvas.MinHeight = 70; Facet.Widgets.Cat.Animate(plot, intervalMs: 50); }
                return;
            }
        }
        else { body.Children.Add(title); body.Children.Add(PlotCanvas); body.Children.Add(averageText); }
        Content = body;
        PlotCanvas.Children.Add(plot);
        PlotCanvas.SizeChanged += (_, _) => LayoutPoints();
    }

    internal void Refresh(IReadOnlyList<SleepEntry> entries, DateOnly today)
    {
        var days = SleepTracking.Week(entries, today);
        string next = System.Text.Json.JsonSerializer.Serialize(days);
        if (next == fingerprint) return;
        fingerprint = next; Days = days;
        rangeText.Text = $"{days[0].Date:MM.dd} — {today:MM.dd}";
        averageText.Text = SleepTracking.Average(days);
        PlotCanvas.Children.Clear(); dayButtons.Clear(); markers.Clear(); dateLabels.Clear();
        foreach (var day in days) {
            double? amount = SleepTracking.Minutes(day);
            bool estimated = day.Entry?.EstimatedSleepMinutes != null;
            string value = amount is double minutes ? "수면 시간 " + SleepTracking.FormatDuration(minutes) : day.Entry == null ? "미기록" : "계산 정보 부족";
            var marker = new Ellipse { Width = 10, Height = 10, StrokeThickness = 2, IsHitTestVisible = false, Fill = Brushes.Transparent };
            bool isToday = hud && day.Date == days[^1].Date;
            if (hud && amount.HasValue && !isToday) { if (WidgetTheme.IsSoftHud) marker.SetResourceReference(Shape.FillProperty, "Bg"); else marker.Fill = new SolidColorBrush(Color.FromRgb(2, 20, 14)); }
            if (isToday && amount.HasValue) marker.SetResourceReference(Shape.FillProperty, "ScheduleNow");
            else if (!hud && amount.HasValue && estimated) marker.SetResourceReference(Shape.FillProperty, "Mint");
            marker.SetResourceReference(Shape.StrokeProperty, isToday && amount.HasValue ? "ScheduleNow" : amount.HasValue ? "Mint" : "Muted");
            var button = Hit(day.Date, value);
            var date = new TextBlock { Text = day.Date.ToString(hud ? "ddd" : "MM.dd (ddd)", CultureInfo.GetCultureInfo("ko-KR")), FontSize = 11, Width = 88, Height = 26, Padding = new(0,6,0,0), TextAlignment = TextAlignment.Center, IsHitTestVisible = false };
            dayButtons.Add(button); markers.Add(marker); dateLabels.Add(date); PlotCanvas.Children.Add(button);
        }
        if (WidgetTheme.IsCatHud) {
            // The bench draws the days and the sleepers; markers and labels stay for the checks only.
            foreach (var marker in markers) marker.Visibility = Visibility.Collapsed;
            foreach (var label in dateLabels) label.Visibility = Visibility.Collapsed;
        }
        // Keep hover backgrounds behind the chart; markers and dates never intercept a day's click.
        PlotCanvas.Children.Add(plot);
        for (int i = 0; i < markers.Count; i++) { PlotCanvas.Children.Add(markers[i]); PlotCanvas.Children.Add(dateLabels[i]); }
        LayoutPoints();
    }

    private Button Hit(DateOnly date, string value)
    {
        var button = new Button { ContentTemplate = null, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new(0), Padding = new(0), Margin = new(0), Height = PlotHeight, Tag = date, ToolTip = $"{date:MM.dd} · {value}" };
        button.SetResourceReference(StyleProperty, "ButtonWithoutFocusOutline");
        AutomationProperties.SetName(button, $"{date:yyyy년 M월 d일} · {value} · 수면 기록 수정");
        button.Click += (_, _) => { EditRequested?.Invoke(date); Editing?.Open(date); };
        return button;
    }

    private void LayoutPoints()
    {
        plot.Width = Math.Max(1, PlotCanvas.ActualWidth);
        double stretch = hud && WidgetTheme.IsSoftHud ? Math.Max(1, PlotCanvas.ActualHeight / PlotHeight) : 1;
        plot.VerticalScale = stretch; plot.Height = WidgetTheme.IsCatHud ? Math.Max(1, PlotCanvas.ActualHeight) : PlotHeight * stretch;
        foreach (var button in dayButtons) button.Height = plot.Height;
        plot.SetDays(Days);
        for (int i = 0; i < dayButtons.Count; i++) {
            var location = plot.Locations[i];
            double left = i == 0 ? Math.Max(0, location.X - 44) : (plot.Locations[i - 1].X + location.X) / 2;
            double right = i == dayButtons.Count - 1 ? Math.Max(left, plot.Width) : (location.X + plot.Locations[i + 1].X) / 2;
            dayButtons[i].Width = Math.Max(0, right - left); Canvas.SetLeft(dayButtons[i], left); Canvas.SetTop(dayButtons[i], 0);
            Canvas.SetLeft(markers[i], location.X - 5); Canvas.SetTop(markers[i], location.Y - 5);
            Canvas.SetLeft(dateLabels[i], location.X - 44); Canvas.SetTop(dateLabels[i], hud ? PlotHeight * stretch - 26 : 218);
        }
    }

    public void Dispose() => Live?.Dispose();
}

internal sealed class SleepDurationPlot : FrameworkElement
{
    private static bool Hud => WidgetTheme.IsHud;
    private static bool IsCat => WidgetTheme.IsCatHud;
    private static double Left => Hud ? 26 : 88;
    private const double Top = 28, Bottom = 150, Unknown = 192;
    private IReadOnlyList<SleepDay> days = [];
    private double maximum = 480;
    internal double VerticalScale { get; set; } = 1;
    internal IReadOnlyList<Point> Locations { get; private set; } = [];
    internal IReadOnlyList<(Point Start, Point End)> Connections { get; private set; } = [];

    internal void SetDays(IReadOnlyList<SleepDay> values)
    {
        days = values;
        maximum = Math.Max(480, Math.Ceiling(days.Select(d => SleepTracking.Minutes(d) ?? 0).DefaultIfEmpty().Max() / 120) * 120);
        double right = Math.Max(Left, Width - 44);
        // The HUD scale is fixed around the usual range: 8h and 6h stay on their lines and the rest clamps inside the plot.
        double Y(double minutes) => Hud ? Math.Clamp(14 + (480 - minutes) / 60 * 24, 6, 88) * VerticalScale : Bottom - minutes / maximum * (Bottom - Top);
        double missing = Hud ? 88 * VerticalScale : Unknown;
        Locations = IsCat
            ? days.Select((day, i) => new Point(Width * (i + .5) / Math.Max(1, days.Count), Height / 2)).ToArray()
            : days.Select((day, i) => new Point(Left + (right - Left) * i / 6, SleepTracking.Minutes(day) is double minutes ? Y(minutes) : missing)).ToArray();
        Connections = Enumerable.Range(1, Math.Max(0, days.Count - 1)).Where(i => SleepTracking.Minutes(days[i - 1]) != null && SleepTracking.Minutes(days[i]) != null).Select(i => (Locations[i - 1], Locations[i])).ToArray();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (IsCat) { RenderCat(dc); return; }
        var edge = ((SolidColorBrush)FindResource("Edge")).Clone(); edge.Opacity = .4; edge.Freeze();
        double right = Math.Max(Left, ActualWidth - 44);
        if (Hud) {
            foreach (var (name, y) in new[] { ("8h", 14 * VerticalScale), ("6h", 62 * VerticalScale) }) {
                dc.DrawLine(new Pen(edge, 1), new Point(Left, y), new Point(ActualWidth, y));
                dc.DrawText(SleepText.Format(this, name, "Ink", 10), new Point(0, y - 7));
            }
            var mint = new Pen((Brush)FindResource("Mint"), 2); mint.Freeze();
            foreach (var segment in Connections) dc.DrawLine(mint, segment.Start, segment.End);
            if (days.Count > 0 && SleepTracking.Minutes(days[^1]) is double last) {
                var value = SleepText.Format(this, $"{(int)last / 60}:{(int)last % 60:00}", "ScheduleNow", 12);
                dc.DrawText(value, new Point(Math.Min(ActualWidth - value.Width, Locations[^1].X + 9), Locations[^1].Y - 15));
            }
            return;
        }
        for (double value = 0; value <= maximum; value += maximum > 960 ? 240 : 120) {
            double y = Bottom - value / maximum * (Bottom - Top);
            dc.DrawLine(new Pen(edge, 1), new Point(Left, y), new Point(right, y));
            dc.DrawText(SleepText.Format(this, $"{value / 60:0}시간", "Muted", 11), new Point(0, y - 7));
        }
        dc.DrawText(SleepText.Format(this, "미확인", "Muted", 10), new Point(0, Unknown - 7));
        var line = new Pen((Brush)FindResource("Mint"), 2); line.Freeze();
        foreach (var segment in Connections) dc.DrawLine(line, segment.Start, segment.End);
        for (int i = 0; i < days.Count; i++)
            if (SleepTracking.Minutes(days[i]) is double minutes) {
                var text = SleepText.Format(this, SleepTracking.FormatDuration(minutes), "Ink", 11);
                dc.DrawText(text, new Point(Locations[i].X - text.Width / 2, Locations[i].Y - 24));
            }
    }

    private const string NapBody = "M28,14.5 C38.5,14.5 47,19.2 47,25 C47,30.8 38.5,35.5 28,35.5 C17.5,35.5 9,30.8 9,25 C9,19.2 17.5,14.5 28,14.5Z";
    private const string NapEars = "M38.05,20.55 L40,9.5 L47.4,17.6Z M44.25,17.6 L51,9.5 L52.25,20.75Z";
    private const string NapHead = "M45.5,16 C51.1,16 54.9,19.5 54.9,24.8 C54.9,28.6 51,30.6 45.5,30.6 C40,30.6 36.1,28.6 36.1,24.8 C36.1,19.5 39.9,16 45.5,16Z";
    private const string NapTail = "M10,27 Q11,37 28,35.5 Q40,34.5 48,29.5";

    // Seven days as cheese cats curled on mint cushions along a wooden bench; a longer night is a bigger cat.
    private void RenderCat(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 40 || h < 30 || days.Count == 0) return;
        double cell = w / days.Count, front = Math.Clamp(h * .2, 14, 24), legs = Math.Clamp(h * .09, 5, 10);
        // The bench sits right under the title; a tall tile leaves room below instead of stretching the cats apart.
        double sleeper = Math.Min(cell * 1.1 / 64 * 42 * 1.15, h - front - legs - 4);
        double benchTop = Math.Min(h - legs - front, sleeper + 6), stroke = Math.Clamp(h * .02, 1.6, 2.6);
        foreach (double x in new[] { w * .05, w * .95 - 8 })
            Facet.Widgets.Cat.Plate(dc, new Rect(x, benchTop + front - 2, 8, legs), 2, Facet.Widgets.Cat.WoodDark, stroke);
        Facet.Widgets.Cat.Plate(dc, new Rect(2, benchTop, w - 6, front), 4, Facet.Widgets.Cat.WoodMid, stroke);
        dc.DrawLine(new Pen(Facet.Widgets.Cat.Wood, Math.Max(1.5, front * .14)), new Point(8, benchTop + front * .22), new Point(w - 12, benchTop + front * .22));
        // A cushion is 64 units wide; the biggest cat (1.7) still fits its cell and the height above the bench.
        double unit = Math.Min(cell * 1.1 / 64, (benchTop - 2) / 42 / 1.15);
        double font = Math.Clamp(front * .55, 8, 14), t = Facet.Widgets.Cat.Seconds;
        for (int i = 0; i < days.Count; i++) {
            double cx = cell * (i + .5);
            bool today = i == days.Count - 1;
            var name = Facet.Widgets.Cat.Text(this, today ? "오늘" : days[i].Date.ToString("ddd", System.Globalization.CultureInfo.GetCultureInfo("ko-KR")), font, today ? Facet.Widgets.Cat.Pink : Facet.Widgets.Cat.CreamLight);
            Facet.Widgets.Cat.Centered(dc, name, new Point(cx, benchTop + front * .58));
            double? minutes = SleepTracking.Minutes(days[i]);
            double em = minutes is double m ? Math.Clamp(.8 + (m / 60 - 5) / 4 * .9, .8, 1.7) : .8;
            double scale = unit * Math.Min(1, em / 1.7) * 1.15;
            if (unit <= 0) continue;
            dc.PushTransform(new TranslateTransform(cx - 32 * scale, benchTop - 41 * scale));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushOpacity(minutes == null ? .45 : 1);
            dc.DrawEllipse(Facet.Widgets.Cat.Mint, Facet.Widgets.Cat.Line(2.2), new Point(32, 35), 30, 6.5);
            dc.DrawEllipse(Facet.Widgets.Cat.MintLight, null, new Point(32, 33.6), 24, 3.6);
            if (minutes != null) {
                double breath = 1 + .035 * Math.Sin((t + i * .37) * Math.PI * 2 / 3);
                dc.PushTransform(new ScaleTransform(1 + (breath - 1) * .5, breath, 32, 36));
                var body = Facet.Widgets.Cat.G(NapBody);
                Facet.Widgets.Cat.Shape(dc, body, Facet.Widgets.Cat.Fur, 0);
                dc.PushClip(body);
                dc.DrawGeometry(null, Facet.Widgets.Cat.Line(2.2, Facet.Widgets.Cat.Stripe), Facet.Widgets.Cat.G("M21,15.4 L23,20 M28,14.6 L28.6,19.6 M35,15.4 L34.6,20"));
                dc.Pop();
                dc.DrawGeometry(null, Facet.Widgets.Cat.Line(2.2), body);
                dc.DrawGeometry(Facet.Widgets.Cat.Fur, Facet.Widgets.Cat.Line(2), Facet.Widgets.Cat.G(NapEars));
                dc.DrawGeometry(Facet.Widgets.Cat.Fur, Facet.Widgets.Cat.Line(2.2), Facet.Widgets.Cat.G(NapHead));
                dc.DrawGeometry(null, Facet.Widgets.Cat.Line(1.3, Facet.Widgets.Cat.Ink), Facet.Widgets.Cat.G("M41,23 Q42.6,24.8 44.2,23 M47,23 Q48.6,24.8 50.2,23"));
                dc.DrawGeometry(null, Facet.Widgets.Cat.Line(7.6), Facet.Widgets.Cat.G(NapTail));
                dc.DrawGeometry(null, Facet.Widgets.Cat.Line(5.2, Facet.Widgets.Cat.Fur), Facet.Widgets.Cat.G(NapTail));
                dc.DrawLine(new Pen(Facet.Widgets.Cat.Stripe, 5.2), new Point(42, 32.4), new Point(45, 31));
                dc.Pop();
                if (today && minutes is double last) {
                    var value = Facet.Widgets.Cat.Text(this, $"{(int)last / 60}:{(int)last % 60:00}", 9, Facet.Widgets.Cat.Ink);
                    dc.DrawText(value, new Point(50, 0));
                }
            }
            dc.Pop(); dc.Pop(); dc.Pop();
        }
    }
}
