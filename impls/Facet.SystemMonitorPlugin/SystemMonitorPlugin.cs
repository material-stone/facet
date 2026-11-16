using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Facet.Plugins;
using Facet.Widgets;

namespace Facet.SystemMonitorPlugin;

public sealed class SystemMonitorPlugin : IFacetPlugin, IDisposable
{
    // One sampler for every placed widget, so two monitors never read the machine twice.
    private readonly SystemSampler sampler = new();

    public void Register(IWidgetRegistry registry) =>
        registry.AddWidget(new WidgetDescriptor("monitor", "시스템 모니터", context => new SystemMonitorView(sampler))
        { Description = "CPU·GPU·메모리 게이지와 네트워크 그래프", DefaultColumns = 4, DefaultRows = 2, MinColumns = 2, MinRows = 1, AllowMultiple = true, AllowMultipleTabs = true, HasPanel = true, HasHudPanel = true });

    public void Dispose() => sampler.Dispose();
}

// Fills its tile with a fixed 360×200 face, so the gauges keep their shape at any size.
public sealed class SystemMonitorView : Viewbox, IDisposable
{
    private readonly SystemSampler? sampler;
    private IDisposable? subscription;
    private readonly Stopwatch clock = new();
    private TimeSpan lastFrame;
    private bool animating;

    public MonitorFace Face { get; } = new();

    public SystemMonitorView(SystemSampler? sampler)
    {
        this.sampler = sampler;
        AutomationProperties.SetName(this, "시스템 모니터");
        Stretch = Stretch.Uniform;
        Child = Face;
        IsVisibleChanged += (_, _) => { if (IsVisible) Start(); else Stop(); };
    }

    private void Start()
    {
        Face.Boot();
        if (sampler?.Last is { } last) Face.Feed(last);
        subscription ??= sampler?.Subscribe(sample => Dispatcher.BeginInvoke(() => { if (IsVisible) Face.Feed(sample); }));
        if (animating) return;
        animating = true; clock.Restart(); lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Stop()
    {
        subscription?.Dispose(); subscription = null;
        if (!animating) return;
        animating = false; clock.Stop();
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = clock.Elapsed;
        Face.Advance((now - lastFrame).TotalSeconds);
        lastFrame = now;
    }

    public void Dispose() => Stop();
}

// Draws three ring gauges and a scrolling network graph. All colors are skin tokens.
public sealed class MonitorFace : FrameworkElement
{
    public const double FaceWidth = 360, FaceHeight = 200;
    private static readonly string[] Labels = ["CPU", "GPU", "RAM"];
    public readonly GaugeMotion[] Gauges = [new(), new(), new()];
    public readonly History Down = new(40), Up = new(40);
    private string memoryLine = "", downLine = "↓ 0 B/s", upLine = "↑ 0 B/s";
    private double boot = 1, sinceSample, flash, time;

    public MonitorFace() { Width = FaceWidth; Height = FaceHeight; SnapsToDevicePixels = true; }

    public string MemoryLine => memoryLine;
    public string NetworkLine => downLine + "  " + upLine;

    public void Boot()
    {
        boot = 0;
        for (int i = 0; i < Gauges.Length; i++) Gauges[i].Boot(0.25 + i * 0.12);
        InvalidateVisual();
    }

    public void Feed(SystemSample sample)
    {
        Gauges[0].Feed(sample.Cpu); Gauges[1].Feed(sample.Gpu); Gauges[2].Feed(sample.Ram);
        memoryLine = sample.RamTotalGb > 0 ? $"{sample.RamUsedGb:0.0}/{sample.RamTotalGb:0}G" : "";
        Down.Add(sample.DownBytesPerSecond); Up.Add(sample.UpBytesPerSecond);
        downLine = "↓ " + SystemMath.FormatRate(sample.DownBytesPerSecond);
        upLine = "↑ " + SystemMath.FormatRate(sample.UpBytesPerSecond);
        sinceSample = 0; flash = 1;
        InvalidateVisual();
    }

    public void Advance(double seconds)
    {
        seconds = Math.Clamp(seconds, 0, 0.1);
        time += seconds; sinceSample += seconds;
        boot = Math.Min(1, boot + seconds * 2.2);
        flash = Math.Max(0, flash - seconds * 2.5);
        foreach (var gauge in Gauges) gauge.Step(seconds);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var mint = Token("Mint", Color.FromRgb(108, 242, 194));
        var ink = Token("Ink", Colors.White);
        var muted = Token("Muted", Color.FromRgb(140, 150, 160));
        var danger = Token("Danger", Color.FromRgb(255, 120, 110));
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var mono = Hud.NumberFont;

        // Title and the live dot that blinks on every reading.
        Text(dc, "SYSTEM", 11, Alpha(mint, 220), mono, new Point(16, 8), dip);
        dc.DrawEllipse(Brush(Alpha(mint, (byte)(90 + 165 * flash))), null, new Point(78, 15.5), 2.5 + flash * 1.5, 2.5 + flash * 1.5);

        for (int i = 0; i < Gauges.Length; i++) DrawGauge(dc, i, mint, ink, muted, danger, mono, dip);
        DrawGraph(dc, mint, muted, dip, mono);
    }

    private void DrawGauge(DrawingContext dc, int i, Color mint, Color ink, Color muted, Color danger, FontFamily mono, double dip)
    {
        var g = Gauges[i];
        const double radius = 34, thickness = 7;
        double shake = g.Shake * Math.Sin(time * 70) * 3.5;
        var center = new Point(60 + i * 120 + shake, 72);
        var color = Mix(mint, danger, g.Heat);
        if (!g.Available) color = muted;

        // The track sweeps in on boot, then the value arc rides the spring and may overshoot past its reading.
        dc.DrawGeometry(null, Pen(Alpha(muted, 60), thickness), Arc(center, radius, boot));
        double fraction = Math.Clamp(g.Spring.Value / 100, 0, 1.04) * boot;
        if (g.Available && fraction > 0.002) {
            dc.DrawGeometry(null, Pen(Alpha(color, 50), thickness + 6), Arc(center, radius, fraction));
            dc.DrawGeometry(null, Pen(color, thickness, PenLineCap.Round), Arc(center, radius, fraction));
        }
        // The peak marker holds the recent high for a moment, then falls back.
        if (g.Available && g.Peak > 1) {
            double a = (g.Peak / 100 * 360 - 90) * Math.PI / 180;
            dc.DrawLine(Pen(Alpha(ink, 200), 2), Polar(center, radius - 6, a), Polar(center, radius + 6, a));
        }
        // The alert ring spreads out once when the value crosses the limit.
        if (g.Pulse > 0) dc.DrawEllipse(null, Pen(Alpha(danger, (byte)(255 * g.Pulse)), 2), center, radius + 4 + (1 - g.Pulse) * 22, radius + 4 + (1 - g.Pulse) * 22);

        string number = g.Available ? Math.Round(g.Shown).ToString(CultureInfo.InvariantCulture) : "—";
        double scale = 1 + g.Pulse * 0.25;
        var big = Format(number, 24 * scale, g.Alert ? danger : ink, mono, dip, true);
        var unit = Format("%", 11, muted, mono, dip);
        double total = big.Width + (g.Available ? unit.Width : 0);
        dc.DrawText(big, new Point(center.X - total / 2, center.Y - big.Height / 2));
        if (g.Available) dc.DrawText(unit, new Point(center.X - total / 2 + big.Width, center.Y - big.Height / 2 + big.Height - unit.Height - 4));

        var label = Format(Labels[i], 11, Alpha(mint, 220), mono, dip);
        dc.DrawText(label, new Point(center.X - shake - label.Width / 2, center.Y + radius + 6));
        if (i == 2 && memoryLine.Length > 0) {
            var sub = Format(memoryLine, 10, muted, mono, dip);
            dc.DrawText(sub, new Point(center.X - shake - sub.Width / 2, center.Y + radius + 19));
        }
    }

    private void DrawGraph(DrawingContext dc, Color mint, Color muted, double dip, FontFamily mono)
    {
        var area = new Rect(16, 140, 328, 38);
        dc.DrawLine(Pen(Alpha(muted, 50), 1), area.BottomLeft, area.BottomRight);
        double top = Math.Max(Math.Max(Down.Max, Up.Max), 64 * 1024);
        // Between readings the graph slides left, so it scrolls instead of jumping once a second.
        double shift = Math.Clamp(sinceSample / SystemSampler.Interval.TotalSeconds, 0, 1);
        double step = area.Width / (Down.Capacity - 2);
        dc.PushClip(new RectangleGeometry(area));
        Point? head = null;
        foreach (var (history, color, fill) in new[] { (Up, muted, false), (Down, mint, true) }) {
            var values = history.ToArray();
            if (values.Length < 2) continue;
            var line = new StreamGeometry();
            using (var draw = line.Open()) {
                for (int n = 0; n < values.Length; n++) {
                    var p = new Point(area.Right - (values.Length - 1 - n + shift) * step, area.Bottom - values[n] / top * area.Height * boot);
                    if (n == 0) draw.BeginFigure(fill ? new Point(p.X, area.Bottom) : p, fill, false);
                    if (n > 0 || fill) draw.LineTo(p, true, true);
                    if (n == values.Length - 1) { if (fill) draw.LineTo(new Point(p.X, area.Bottom), false, false); if (fill) head = p; }
                }
            }
            line.Freeze();
            if (fill) dc.DrawGeometry(Brush(Alpha(color, 40)), null, line);
            dc.DrawGeometry(null, Pen(color, 1.5), line);
        }
        dc.Pop();
        if (head is { } h) dc.DrawEllipse(Brush(mint), null, h, 2 + flash * 2.5, 2 + flash * 2.5);
        Text(dc, downLine, 11, mint, mono, new Point(16, 182), dip);
        var right = Format(upLine, 11, muted, mono, dip);
        dc.DrawText(right, new Point(area.Right - right.Width, 182));
    }

    private Color Token(string key, Color fallback) => TryFindResource(key) is SolidColorBrush b ? b.Color : fallback;
    private static Color Alpha(Color c, byte a) => Color.FromArgb((byte)(c.A * a / 255), c.R, c.G, c.B);
    private static Color Mix(Color a, Color b, double t) => Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    private static SolidColorBrush Brush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static Pen Pen(Color c, double width, PenLineCap cap = PenLineCap.Flat) { var p = new Pen(Brush(c), width) { StartLineCap = cap, EndLineCap = cap }; p.Freeze(); return p; }
    private static Point Polar(Point c, double r, double angle) => new(c.X + r * Math.Cos(angle), c.Y + r * Math.Sin(angle));

    // Clockwise from twelve o'clock.
    private static Geometry Arc(Point c, double r, double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 0.9999);
        double start = -Math.PI / 2, end = start + fraction * 2 * Math.PI;
        var g = new StreamGeometry();
        using (var draw = g.Open()) {
            draw.BeginFigure(Polar(c, r, start), false, false);
            draw.ArcTo(Polar(c, r, end), new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    private static FormattedText Format(string text, double size, Color color, FontFamily font, double dip, bool strong = false) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(font, FontStyles.Normal, strong ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, Brush(color), dip);

    private static void Text(DrawingContext dc, string text, double size, Color color, FontFamily font, Point at, double dip) => dc.DrawText(Format(text, size, color, font, dip), at);
}
