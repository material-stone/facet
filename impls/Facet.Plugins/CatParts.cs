using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Facet.Plugins;

namespace Facet.Widgets;

// The face a cat head shows: eyes dot/angry/tear/flat/happy/heart, mouth w/frown/flat/wobble/open or none.
public sealed record CatFace(string Eyes = "dot", string Mouth = "", bool FlatEars = false, bool Sparkle = false);

// Drawing kit of the cat widget form (ADR 0107): a fixed chibi palette, one thick outline and a soft shadow down-right.
// Shapes are SVG path data in a local unit space; callers push a transform to place them.
public static class Cat
{
    public static readonly Brush Outline = Make("#5A4238"), Ink = Make("#4A3A33"), Sub = Make("#A07F70"), Fur = Make("#F2A65A"), Stripe = Make("#D9843A"),
        Cream = Make("#FFF1DF"), CreamLight = Make("#FFF7EF"), Pink = Make("#F4A7B0"), DarkPink = Make("#E8798A"), Mint = Make("#9FD9C3"), MintLight = Make("#BFE3D4"),
        Butter = Make("#FFE08A"), Wood = Make("#D6A46B"), WoodMid = Make("#C48F57"), WoodDark = Make("#A9753F"), Cork = Make("#ECC49B"), CorkDot = Make("#D9A877"),
        White = Make("#FFFFFF"), Shadow = Make("#386E4128");

    private static readonly Dictionary<string, Geometry> Geometries = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");

    public static double Seconds => Clock.Elapsed.TotalSeconds;

    public static Brush Make(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public static Geometry G(string data)
    {
        if (Geometries.TryGetValue(data, out var cached)) return cached;
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return Geometries[data] = geometry;
    }

    public static Pen Line(double width, Brush? brush = null)
    {
        var pen = new Pen(brush ?? Outline, width) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }

    // The skin's heading font (Jua in the cat room), else the app font.
    public static FontFamily Font => Application.Current?.TryFindResource("HudHeadingFont") as FontFamily ?? Application.Current?.TryFindResource("AppFontFamily") as FontFamily ?? new FontFamily("Segoe UI");

    public static FormattedText Text(Visual visual, string text, double size, Brush? brush = null) =>
        new(text, Korean, FlowDirection.LeftToRight, new Typeface(Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), Math.Max(1, size), brush ?? Ink, VisualTreeHelper.GetDpi(visual).PixelsPerDip);

    public static void Centered(DrawingContext dc, FormattedText text, Point center) => dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));

    // A filled shape with its outline, optionally over a soft copy shifted down-right.
    public static void Shape(DrawingContext dc, Geometry geometry, Brush fill, double stroke, bool shadow = false)
    {
        if (shadow) Shadowed(dc, geometry, stroke);
        dc.DrawGeometry(fill, stroke > 0 ? Line(stroke) : null, geometry);
    }

    public static void Shadowed(DrawingContext dc, Geometry geometry, double stroke)
    {
        double offset = Math.Max(2, stroke * 1.1);
        dc.PushTransform(new TranslateTransform(offset * .8, offset));
        dc.DrawGeometry(Shadow, stroke > 0 ? Line(stroke, Shadow) : null, geometry);
        dc.Pop();
    }

    public static void Plate(DrawingContext dc, Rect rect, double radius, Brush fill, double stroke, bool shadow = true)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        Shape(dc, new RectangleGeometry(rect, radius, radius), fill, stroke, shadow);
    }

    // A cream label with the outline, sized to its text. Returns the drawn box.
    public static Rect Tag(DrawingContext dc, Visual visual, string text, Point topLeft, double size, Brush? fill = null, Brush? ink = null)
    {
        var label = Text(visual, text, size, ink);
        var box = new Rect(topLeft.X, topLeft.Y, label.Width + size * 1.1, label.Height + size * .2);
        Plate(dc, box, size * .55, fill ?? CreamLight, Math.Max(1.5, size * .16));
        dc.DrawText(label, new Point(box.X + size * .55, box.Y + size * .1));
        return box;
    }

    private const string HeadPath = "M0,-32 C24.6,-32 41,-16.6 41,6.6 C41,23.3 23.8,32 0,32 C-23.8,32 -41,23.3 -41,6.6 C-41,-16.6 -24.6,-32 0,-32Z";
    private const string EarPath = "M-38,-7 L-31,-47 L-9,-31 L-20,-10Z", EarInnerPath = "M-30,-20 L-28,-38 L-17,-30Z";

    public static string HeartPath(double cx, double cy, double s) => string.Format(CultureInfo.InvariantCulture,
        "M{0},{1} C{2},{3} {4},{5} {0},{6} C{7},{5} {8},{3} {0},{1}Z", cx, cy + s * .35, cx - s * 1.1, cy - s * .35, cx - s * .45, cy - s * 1.05, cy - s * .45, cx + s * .45, cx + s * 1.1);

    private static string F(FormattableString value) => FormattableString.Invariant(value);

    // The cheese cat head in its -50..50 unit box: mochi head, pink-lined ears, three forehead stripes, face by kind.
    // earLift raises the ears (the hour pop); eyeShift moves the pupils sideways.
    public static void Head(DrawingContext dc, CatFace face, double sw = 4, bool plate = false, double earLift = 0, double eyeShift = 0, bool shadow = true)
    {
        var head = G(HeadPath);
        if (shadow) {
            Shadowed(dc, head, sw);
            if (!face.FlatEars) { Shadowed(dc, G(EarPath), sw); dc.PushTransform(new ScaleTransform(-1, 1)); dc.PushTransform(new TranslateTransform(-2 * Math.Max(2, sw * 1.1) * .8, 0)); Shadowed(dc, G(EarPath), sw); dc.Pop(); dc.Pop(); }
        }
        for (int side = 0; side < 2; side++) {
            dc.PushTransform(new ScaleTransform(side == 0 ? 1 : -1, 1));
            dc.PushTransform(new TranslateTransform(0, -earLift));
            dc.PushTransform(new RotateTransform(-earLift * 1.6, -22, -12));
            if (face.FlatEars) dc.PushTransform(new RotateTransform(-42, -22, -22));
            dc.DrawGeometry(Fur, Line(sw), G(EarPath));
            dc.DrawGeometry(Pink, null, G(EarInnerPath));
            if (face.FlatEars) dc.Pop();
            dc.Pop(); dc.Pop(); dc.Pop();
        }
        dc.DrawGeometry(Fur, null, head);
        dc.PushClip(head);
        dc.DrawGeometry(null, Line(sw * 1.1, Stripe), G("M-9,-28 L-7,-19 M0,-30 L0,-20 M9,-28 L7,-19"));
        dc.Pop();
        dc.DrawGeometry(null, Line(sw), head);
        double dy = plate ? -6 : 0;
        dc.PushTransform(new TranslateTransform(0, plate ? 0 : 3));
        if (plate) dc.DrawEllipse(CreamLight, Line(sw), new Point(0, 27), 35, 15.5);
        else dc.DrawEllipse(Cream, null, new Point(0, 12), 14, 8);
        dc.DrawEllipse(Pink, null, new Point(-27, 11 + dy), 6, 3.6);
        dc.DrawEllipse(Pink, null, new Point(27, 11 + dy), 6, 3.6);
        double ny = 5 + dy;
        dc.DrawGeometry(DarkPink, Line(sw * .4), G(F($"M-4,{ny} L4,{ny} L0,{ny + 4}Z")));
        var eye = Line(sw, Ink);
        void Dot(double x)
        {
            dc.DrawEllipse(Ink, null, new Point(x + eyeShift, -3 + dy), 5.5, 6.5);
            dc.DrawEllipse(White, null, new Point(x - 1.8 + eyeShift * 1.15, -5.8 + dy), 1.9, 1.9);
        }
        switch (face.Eyes) {
            case "angry": Dot(-17); Dot(17); dc.DrawGeometry(null, eye, G("M-27,-16 L-10,-9 M27,-16 L10,-9")); break;
            case "tear": Dot(-17); Dot(17); dc.DrawGeometry(Mint, Line(sw * .4), G("M-21,5 Q-26,13 -21,15.5 Q-16,13 -21,5Z M21,5 Q16,13 21,15.5 Q26,13 21,5Z")); break;
            case "flat": dc.DrawGeometry(null, eye, G(F($"M-23,{-3 + dy} H-11 M11,{-3 + dy} H23"))); break;
            case "happy": dc.DrawGeometry(null, eye, G(F($"M-23,{dy} Q-17,{-10 + dy} -11,{dy} M11,{dy} Q17,{-10 + dy} 23,{dy}"))); break;
            case "heart": dc.DrawGeometry(DarkPink, Line(sw * .4), G(HeartPath(-17, -1 + dy, 9) + " " + HeartPath(17, -1 + dy, 9))); break;
            default: Dot(-17); Dot(17); break;
        }
        var mouth = Line(sw * .6);
        switch (face.Mouth) {
            case "w": dc.DrawGeometry(null, mouth, G("M-7,15 Q-3.5,20 0,15 Q3.5,20 7,15")); break;
            case "frown": dc.DrawGeometry(null, mouth, G("M-7,19 Q0,13 7,19")); break;
            case "flat": dc.DrawGeometry(null, mouth, G("M-6,17 H6")); break;
            case "wobble": dc.DrawGeometry(null, mouth, G("M-8,18 Q-4,15 0,18 Q4,21 8,18")); break;
            case "open":
                dc.DrawGeometry(DarkPink, mouth, G("M-8,13 Q0,29 8,13Z"));
                dc.DrawGeometry(Pink, null, G("M-4.5,19.5 Q0,25 4.5,19.5 Q0,17.5 -4.5,19.5Z"));
                break;
        }
        dc.PushOpacity(.8);
        dc.DrawGeometry(null, Line(sw * .4), G(F($"M-34,{9 + dy} L-47,{6.5 + dy} M-34,{14 + dy} L-47,{16.5 + dy} M34,{9 + dy} L47,{6.5 + dy} M34,{14 + dy} L47,{16.5 + dy}")));
        dc.Pop();
        dc.Pop();
        if (face.Sparkle) dc.DrawGeometry(Butter, Line(sw * .35), G("M40,-40 L42,-34 L48,-32 L42,-30 L40,-24 L38,-30 L32,-32 L38,-34Z M-44,-30 L-43,-27 L-40,-26 L-43,-25 L-44,-22 L-45,-25 L-48,-26 L-45,-27Z"));
    }

    // The pink paw stamp in a 24 unit box.
    public static void Paw(DrawingContext dc, Rect box, Brush? fill = null)
    {
        fill ??= DarkPink;
        dc.PushTransform(new TranslateTransform(box.X, box.Y));
        dc.PushTransform(new ScaleTransform(box.Width / 24, box.Height / 24));
        dc.DrawEllipse(fill, null, new Point(12, 15.5), 5.2, 4.3);
        foreach (var (x, y, r) in new[] { (5.6, 10, 2.2), (9.4, 6.4, 2.3), (14.6, 6.4, 2.3), (18.4, 10, 2.2) }) dc.DrawEllipse(fill, null, new Point(x, y), r, r);
        dc.Pop(); dc.Pop();
    }

    // Weather kind from a Korean condition line: clear, night, cloudy, rain or snow.
    public static string WeatherKind(string description)
    {
        if (description.Contains('눈')) return "snow";
        if (description.Contains('비') || description.Contains("뇌우") || description.Contains("소나기")) return "rain";
        if (description.Contains('밤')) return "night";
        if (description.Contains('맑')) return "clear";
        return "cloudy";
    }

    // A hand-drawn weather icon in a 24 unit box.
    public static void WeatherIcon(DrawingContext dc, string kind, Rect box)
    {
        dc.PushTransform(new TranslateTransform(box.X, box.Y));
        dc.PushTransform(new ScaleTransform(box.Width / 24, box.Height / 24));
        var line = Line(1.6);
        switch (kind) {
            case "clear":
                dc.DrawGeometry(null, line, G("M12,1.5V4 M12,20V22.5 M1.5,12H4 M20,12H22.5 M4.6,4.6L6.3,6.3 M17.7,17.7L19.4,19.4 M4.6,19.4L6.3,17.7 M17.7,6.3L19.4,4.6"));
                dc.DrawEllipse(Butter, line, new Point(12, 12), 5.6, 5.6);
                break;
            case "rain":
                dc.DrawGeometry(CreamLight, line, G("M6.5,14.5 H17.5 A4,4 0 0 0 17.5,6.5 A5.5,5.5 0 0 0 7,5.5 A4.5,4.5 0 0 0 6.5,14.5Z"));
                dc.DrawGeometry(null, Line(2, Mint), G("M8,17.5 L7,20.5 M12.5,17.5 L11.5,20.5 M17,17.5 L16,20.5"));
                break;
            case "snow":
                dc.DrawGeometry(CreamLight, line, G("M6.5,14 H17.5 A4,4 0 0 0 17.5,6 A5.5,5.5 0 0 0 7,5 A4.5,4.5 0 0 0 6.5,14Z"));
                foreach (var (x, y) in new[] { (8.0, 18.5), (12.5, 20.0), (17.0, 18.5) }) dc.DrawEllipse(CreamLight, Line(1), new Point(x, y), 1.6, 1.6);
                break;
            case "night":
                dc.DrawGeometry(Butter, line, G("M15,3.5 A8.5,8.5 0 1 0 20.5,16 A7,7 0 0 1 15,3.5Z"));
                dc.DrawGeometry(CreamLight, Line(.8), G("M19,3 L19.7,5 L21.7,5.7 L19.7,6.4 L19,8.4 L18.3,6.4 L16.3,5.7 L18.3,5Z"));
                break;
            default:
                dc.DrawGeometry(CreamLight, line, G("M6.5,18.5 H17.5 A4,4 0 0 0 17.5,10.5 A5.5,5.5 0 0 0 7,9.5 A4.5,4.5 0 0 0 6.5,18.5Z"));
                break;
        }
        dc.Pop(); dc.Pop();
    }

    // Repaints an element about 30 times a second while it is loaded and visible; step gets the elapsed seconds first.
    public static void Animate(FrameworkElement element, Action<double>? step = null, int intervalMs = 33)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        double last = Seconds;
        timer.Tick += (_, _) => {
            double now = Seconds;
            step?.Invoke(Math.Min(.1, now - last));
            last = now;
            element.InvalidateVisual();
        };
        void Sync()
        {
            if (element.IsLoaded && element.IsVisible) { if (!timer.IsEnabled) { last = Seconds; timer.Start(); } }
            else timer.Stop();
        }
        element.Loaded += (_, _) => Sync();
        element.Unloaded += (_, _) => timer.Stop();
        element.IsVisibleChanged += (_, _) => Sync();
    }
}

// A cream title tag with the outline, tilted a little, standing in for the HUD header line.
public sealed class CatTag : FrameworkElement
{
    private string text = "";
    public CatTag() { HorizontalAlignment = HorizontalAlignment.Left; IsHitTestVisible = false; }
    public string Text { get => text; set { text = value; InvalidateMeasure(); InvalidateVisual(); } }
    public double Size { get; init; } = 14;

    protected override Size MeasureOverride(Size available)
    {
        var label = Cat.Text(this, text.Length == 0 ? " " : text, Size);
        return new Size(label.Width + Size * 1.1 + 6, label.Height + Size * .2 + 8);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (text.Length == 0) return;
        dc.PushTransform(new RotateTransform(-2, ActualWidth / 2, ActualHeight / 2));
        Cat.Tag(dc, this, text, new Point(2, 3), Size);
        dc.Pop();
    }
}

// The cat clock: a cheese cat head holding an oval dial with HH:mm, a weekday tag under it and a striped tail pendulum.
// The eyes follow the tail and the ears pop on the hour.
public sealed class CatClock : FrameworkElement
{
    public const double DesignWidth = 120, DesignHeight = 176;
    private const double K = 1.2;
    private static readonly string[] Days = ["일요일", "월요일", "화요일", "수요일", "목요일", "금요일", "토요일"];
    private const string TailPath = "M10,0 C10,13 4,19 6,28 C8,35 14,37 12,40";
    private DateTime now = DateTime.Now;
    private int lastHour = -1;
    private double popStart = -10;

    public CatClock()
    {
        Width = DesignWidth; Height = DesignHeight;
        Cat.Animate(this);
    }

    public DateTime Now => now;
    public string TimeLine => now.ToString("HH:mm", CultureInfo.InvariantCulture);

    public void Show(DateTime value)
    {
        if (lastHour != -1 && value.Hour != lastHour) popStart = Cat.Seconds;
        lastHour = value.Hour; now = value;
        InvalidateVisual();
    }

    // Pops the ears now, as on the hour.
    public void Pop() { popStart = Cat.Seconds; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double t = Cat.Seconds, swing = Math.Sin(t * Math.PI / 2);
        double pop = t - popStart is var p and >= 0 and < .6 ? Math.Sin(p / .6 * Math.PI) * 7 : 0;
        dc.PushTransform(new TranslateTransform(DesignWidth / 2, 54 * K));
        dc.PushTransform(new ScaleTransform(K, K));
        // Tail first, so the tag covers its root.
        dc.PushTransform(new TranslateTransform(-7.5, 53));
        dc.PushTransform(new RotateTransform(swing * 16, 7.5, 0));
        dc.PushTransform(new ScaleTransform(.75, .8));
        dc.DrawGeometry(null, Cat.Line(9), Cat.G(TailPath));
        dc.DrawGeometry(null, Cat.Line(6.2, Cat.Fur), Cat.G(TailPath));
        dc.DrawGeometry(null, Cat.Line(2.6, Cat.Stripe), Cat.G("M8.6,17.6 L5.2,19.8 M6.2,29.3 L10,30.4 M11.5,36.2 L13.4,38.4"));
        dc.Pop(); dc.Pop(); dc.Pop();
        Cat.Head(dc, new CatFace(), 3.4, plate: true, earLift: pop, eyeShift: -swing * 2.2);
        var time = Cat.Text(this, now.ToString("HH", CultureInfo.InvariantCulture) + " " + now.ToString("mm", CultureInfo.InvariantCulture), 21);
        Cat.Centered(dc, time, new Point(0, 27.5));
        if (now.Second % 2 == 0) Cat.Centered(dc, Cat.Text(this, ":", 21), new Point(0, 26.5));
        var day = Cat.Text(this, Days[(int)now.DayOfWeek], 10);
        var tag = new Rect(-day.Width / 2 - 6, 44, day.Width + 12, day.Height + 2);
        Cat.Plate(dc, tag, 6, Cat.CreamLight, 2.2);
        dc.DrawText(day, new Point(tag.X + 6, tag.Y + 1));
        dc.Pop(); dc.Pop();
    }
}

// The weather sign: a wooden board on two strings from a nail, with a hand-drawn icon and one text line.
// It sways like a damped pendulum and swings when hovered or pressed.
public sealed class CatSign : FrameworkElement
{
    private string kind = "cloudy", line = "날씨 확인 중";
    private double angle, velocity;

    public CatSign()
    {
        Cat.Animate(this, Step);
        MouseEnter += (_, e) => Kick(e.GetPosition(this).X < ActualWidth / 2 ? 34 : -34);
        PreviewMouseLeftButtonDown += (_, _) => Kick(velocity >= 0 ? 60 : -60);
    }

    public string Line => line;
    public double Angle => angle;

    public void Show(string description, string temperature, string humidity)
    {
        kind = Cat.WeatherKind(description);
        string condition = description.Length == 0 ? "날씨" : description;
        line = condition + " " + temperature + " · 습도 " + humidity;
        InvalidateVisual();
    }

    public void Kick(double speed) => velocity += speed;

    private void Step(double dt)
    {
        // A slow breeze keeps it moving; the spring and damping bring a kick back to rest.
        double breeze = 2.2 * Math.Sin(Cat.Seconds * 1.1);
        velocity += (-(angle - breeze) * 14 - velocity * 2.2) * dt;
        angle = Math.Clamp(angle + velocity * dt, -24, 24);
    }

    protected override Size MeasureOverride(Size available) => new(double.IsInfinity(available.Width) ? 300 : available.Width, 84);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 20 || h < 20) return;
        var probe = Cat.Text(this, line, 20);
        double unitWidth = (probe.Width / 20) + .5 + 1.55 + .4 + .75, unitHeight = 2.4 + 1.9 + .4;
        double f = Math.Clamp(Math.Min(w * .92 / unitWidth, h * .9 / unitHeight), 7, 34);
        var text = Cat.Text(this, line, f);
        double icon = f * 1.55, boardW = f * .5 + icon + f * .4 + text.Width + f * .75, boardH = icon + f * .35;
        double total = f * 2.4 + boardH;
        var nail = new Point(w / 2, Math.Max(f * .35, (h - total) / 2 + f * .3));
        var board = new Rect(w / 2 - boardW / 2, nail.Y + f * 2.1, boardW, boardH);
        double stroke = Math.Max(1.6, f * .2);
        dc.PushTransform(new RotateTransform(angle, nail.X, nail.Y));
        var strings = new StreamGeometry();
        using (var draw = strings.Open()) {
            draw.BeginFigure(new Point(board.X + boardW * .18, board.Y + stroke), false, false);
            draw.LineTo(nail, true, true);
            draw.LineTo(new Point(board.Right - boardW * .18, board.Y + stroke), true, true);
        }
        strings.Freeze();
        dc.DrawGeometry(null, Cat.Line(Math.Max(1.4, f * .13)), strings);
        var shape = new RectangleGeometry(board, f * .5, f * .5);
        Cat.Shape(dc, shape, Cat.Wood, stroke, shadow: true);
        dc.PushClip(shape);
        var grain = Cat.Line(Math.Max(1, f * .09), Cat.WoodMid);
        for (int i = 1; i <= 3; i++) {
            double y = board.Y + boardH * i / 4;
            var wave = new StreamGeometry();
            using (var draw = wave.Open()) {
                double x0 = board.X + boardW * (i == 2 ? .08 : .3);
                draw.BeginFigure(new Point(x0, y), false, false);
                draw.QuadraticBezierTo(new Point(x0 + boardW * .2, y - f * .15), new Point(x0 + boardW * .42, y), true, true);
                draw.QuadraticBezierTo(new Point(x0 + boardW * .55, y + f * .12), new Point(x0 + boardW * .62, y), true, true);
            }
            wave.Freeze();
            dc.PushOpacity(.7); dc.DrawGeometry(null, grain, wave); dc.Pop();
        }
        dc.Pop();
        dc.DrawGeometry(null, Cat.Line(stroke), shape);
        Cat.WeatherIcon(dc, kind, new Rect(board.X + f * .5, board.Y + (boardH - icon) / 2, icon, icon));
        dc.DrawText(text, new Point(board.X + f * .5 + icon + f * .4, board.Y + (boardH - text.Height) / 2));
        dc.DrawEllipse(Cat.WoodDark, Cat.Line(Math.Max(1, f * .11)), nail, f * .27, f * .27);
        dc.Pop();
    }
}
