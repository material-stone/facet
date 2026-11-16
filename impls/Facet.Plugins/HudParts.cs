using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Facet.Plugins;

namespace Facet.Widgets;

// Shared look of the HUD widget forms (ADR 0086). Colors stay tokens, so a skin decides them.
public static class Hud
{
    // Installed plugins bind to this field, so it must stay a field (see the plugin compatibility check).
    public static readonly FontFamily Mono = new("Consolas");
    // Numbers follow the skin: the soft form keeps the app font, the tactical form reads as a terminal.
    public static FontFamily NumberFont => WidgetTheme.IsSoftHud && Application.Current?.TryFindResource("HudHeadingFont") is FontFamily heading ? heading : Mono;

    public static TextBlock Text(string text, double size, string brush = "Ink", bool mono = false, bool strong = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal };
        if (mono) block.FontFamily = NumberFont;
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    // A square mark, the title and a thin line to the right edge.
    public static Grid Header(TextBlock title)
    {
        var row = new Grid();
        if (WidgetTheme.IsCatHud) {
            var tag = new CatTag { Margin = new(-2, -2, 0, 2) };
            WhenChanged(() => tag.Text = title.Text, title);
            row.Children.Add(tag);
            return row;
        }
        if (WidgetTheme.IsSoftHud) {
            title.FontSize = 15; title.FontFamily = NumberFont; title.FontWeight = FontWeights.Normal; title.VerticalAlignment = VerticalAlignment.Center;
            title.SetResourceReference(TextBlock.ForegroundProperty, "Mint");
            row.Children.Add(title);
            return row;
        }
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new());
        var mark = new Border { Width = 7, Height = 7, Margin = new(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        mark.SetResourceReference(Border.BackgroundProperty, "Mint");
        title.FontSize = 13; title.FontWeight = FontWeights.SemiBold; title.VerticalAlignment = VerticalAlignment.Center;
        title.SetResourceReference(TextBlock.ForegroundProperty, "Mint");
        Grid.SetColumn(title, 1);
        var line = new Border { Height = 1, Margin = new(8, 0, 0, 0), Opacity = .45, VerticalAlignment = VerticalAlignment.Center };
        line.SetResourceReference(Border.BackgroundProperty, "Mint");
        Grid.SetColumn(line, 2);
        row.Children.Add(mark); row.Children.Add(title); row.Children.Add(line);
        return row;
    }

    public static Grid Header(string title) => Header(new TextBlock { Text = title });

    // Crisp text over a blurred copy: the effect stays off the text people read.
    public static Grid Glow(TextBlock text)
    {
        if (WidgetTheme.IsSoftHud) { var plain = new Grid(); plain.Children.Add(text); return plain; }
        var halo = new TextBlock { FontSize = text.FontSize, FontFamily = text.FontFamily, FontWeight = text.FontWeight, LineHeight = text.LineHeight, LineStackingStrategy = text.LineStackingStrategy, Opacity = .55, Effect = new BlurEffect { Radius = 12 }, IsHitTestVisible = false };
        halo.SetResourceReference(TextBlock.ForegroundProperty, "Mint");
        WhenChanged(() => halo.Text = text.Text, text);
        var grid = new Grid();
        grid.Children.Add(halo); grid.Children.Add(text);
        return grid;
    }

    // Runs now and whenever a source line changes, however many code paths write to it.
    public static void WhenChanged(Action update, params TextBlock[] sources)
    {
        var property = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        foreach (var source in sources) property.AddValueChanged(source, (_, _) => update());
        update();
    }
}

// The tile surface: a dark translucent plate, a faint outline and glowing corner brackets.
public sealed class HudPanel : FrameworkElement
{
    private const double Arm = 18;

    public HudPanel() { IsHitTestVisible = false; Focusable = false; SnapsToDevicePixels = true; }

    protected override void OnRender(DrawingContext context)
    {
        if (WidgetTheme.IsCatHud) { RenderCat(context); return; }
        if (WidgetTheme.IsSoftHud) { RenderSoft(context); return; }
        if (TryFindResource("Mint") is not SolidColorBrush mint) return;
        double w = ActualWidth, h = ActualHeight;
        var plate = new LinearGradientBrush(Color.FromArgb(219, 2, 12, 14), Color.FromArgb(189, 2, 8, 12), 90); plate.Freeze();
        context.DrawRectangle(plate, null, new Rect(0, 0, w, h));
        Pen Mint(byte alpha, double width)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, mint.Color.R, mint.Color.G, mint.Color.B)); brush.Freeze();
            var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square }; pen.Freeze();
            return pen;
        }
        context.DrawRectangle(null, Mint(41, 1), new Rect(.5, .5, Math.Max(0, w - 1), Math.Max(0, h - 1)));
        var marks = new StreamGeometry();
        using (var draw = marks.Open()) {
            void Corner(double x, double y, double dx, double dy)
            {
                draw.BeginFigure(new Point(x + dx * Arm, y), false, false);
                draw.LineTo(new Point(x, y), true, false);
                draw.LineTo(new Point(x, y + dy * Arm), true, false);
            }
            Corner(1, 1, 1, 1); Corner(w - 1, 1, -1, 1); Corner(1, h - 1, 1, -1); Corner(w - 1, h - 1, -1, -1);
        }
        marks.Freeze();
        context.DrawGeometry(null, Mint(26, 8), marks);
        context.DrawGeometry(null, Mint(60, 4), marks);
        context.DrawGeometry(null, Mint(255, 2), marks);
    }

    // A cream plate with the cat form's thick outline and soft shadow (ADR 0107).
    private void RenderCat(DrawingContext context)
    {
        double w = ActualWidth - 8, h = ActualHeight - 8;
        if (w <= 0 || h <= 0) return;
        Cat.Plate(context, new Rect(2, 2, w, h), 16, Cat.CreamLight, 3);
    }

    private void RenderSoft(DrawingContext context)
    {
        if (TryFindResource("Panel") is not Brush plate || TryFindResource("Mint") is not SolidColorBrush mint) return;
        var line = new SolidColorBrush(Color.FromArgb(70, mint.Color.R, mint.Color.G, mint.Color.B)); line.Freeze();
        var pen = new Pen(line, 1); pen.Freeze();
        context.DrawRoundedRectangle(plate, pen, new Rect(.5, .5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)), 14, 14);
    }
}

// Date header, large 24-hour time with small seconds, and the day ruler.
public sealed class HudClockFace : StackPanel
{
    private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");
    private readonly TextBlock date = new(), time = Hud.Text("", 72, "Mint", mono: true), seconds = Hud.Text("", 26, "Mint", mono: true);
    private readonly DayRuler ruler = new() { Height = 44, Margin = new(0, 2, 0, 0) };

    private readonly CatClock? cat;

    public HudClockFace()
    {
        Width = 328;
        if (WidgetTheme.IsCatHud) {
            Width = double.NaN;
            Children.Add(cat = new CatClock());
            return;
        }
        if (WidgetTheme.IsSoftHud) {
            time.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            time.Margin = new(0, 4, 0, 0); date.FontSize = 16; date.Margin = new(0, 4, 0, 0); date.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            Children.Add(Hud.Header("시계")); Children.Add(time); Children.Add(date);
            return;
        }
        time.LineHeight = 72; time.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        seconds.Margin = new(10, 0, 0, 6); seconds.VerticalAlignment = VerticalAlignment.Bottom;
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 8, 0, 0) };
        line.Children.Add(Hud.Glow(time)); line.Children.Add(seconds);
        Children.Add(Hud.Header(date)); Children.Add(line); Children.Add(ruler);
    }

    public string TimeLine => cat?.TimeLine ?? time.Text;

    public void Show(DateTime now)
    {
        cat?.Show(now);
        date.Text = now.ToString("M월 d일 dddd", Korean);
        time.Text = now.ToString("HH:mm", Korean);
        seconds.Text = now.ToString("ss", Korean);
        ruler.Now = now.TimeOfDay;
    }
}

// Place header, signed temperature, condition, and a named humidity bar.
public sealed class HudWeatherFace : Grid
{
    private readonly TextBlock title = new();
    private readonly TextBlock temperature = Hud.Text("--", 66, "Mint", mono: true), unit = Hud.Text("°C", 24, "Mint", mono: true);
    private readonly TextBlock condition = Hud.Text("", 18), humidity = Hud.Text("--%", 16, mono: true);
    private readonly SegmentBar bar = new() { Height = 8, Margin = new(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBlock softDetail = Hud.Text("", 14, "Muted");
    private readonly bool soft = WidgetTheme.IsSoftHud;
    private readonly CatSign? sign;

    public HudWeatherFace()
    {
        if (WidgetTheme.IsCatHud) {
            Children.Add(sign = new CatSign());
            return;
        }
        if (soft) {
            temperature.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); temperature.FontSize = 48; temperature.LineHeight = 56;
            condition.VerticalAlignment = VerticalAlignment.Center; condition.HorizontalAlignment = HorizontalAlignment.Right; condition.FontFamily = Hud.NumberFont;
            // The number grows with the tile; the place line stays at the bottom.
            var line = new Grid { Margin = new(0, 2, 0, 2) };
            line.ColumnDefinitions.Add(new()); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            temperature.HorizontalAlignment = HorizontalAlignment.Left; temperature.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(condition, 1);
            line.Children.Add(temperature); line.Children.Add(condition);
            line.SizeChanged += (_, e) => {
                double size = Math.Clamp(Math.Min(e.NewSize.Height * .8, e.NewSize.Width * .3), 32, 120);
                if (Math.Abs(temperature.FontSize - size) > .5) { temperature.FontSize = size; temperature.LineHeight = size * 1.15; }
            };
            RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new()); RowDefinitions.Add(new() { Height = GridLength.Auto });
            var heading = Hud.Header("날씨");
            Grid.SetRow(line, 1); Grid.SetRow(softDetail, 2);
            Children.Add(heading); Children.Add(line); Children.Add(softDetail);
            return;
        }
        temperature.LineHeight = 66; temperature.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        unit.Margin = new(2, 0, 0, 4); unit.VerticalAlignment = VerticalAlignment.Bottom;
        var numbers = new StackPanel { Orientation = Orientation.Horizontal };
        numbers.Children.Add(Hud.Glow(temperature)); numbers.Children.Add(unit);
        condition.HorizontalAlignment = HorizontalAlignment.Right; condition.VerticalAlignment = VerticalAlignment.Bottom; condition.Margin = new(12, 0, 0, 6);
        var middle = new Grid { Margin = new(0, 12, 0, 0) };
        middle.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        middle.ColumnDefinitions.Add(new());
        Grid.SetColumn(condition, 1);
        middle.Children.Add(numbers); middle.Children.Add(condition);
        var name = Hud.Text("습도", 13); name.VerticalAlignment = VerticalAlignment.Center;
        humidity.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { Margin = new(0, 14, 0, 0) };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new());
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumn(bar, 1); Grid.SetColumn(humidity, 2);
        row.Children.Add(name); row.Children.Add(bar); row.Children.Add(humidity);
        for (int i = 0; i < 3; i++) RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetRow(middle, 1); Grid.SetRow(row, 2);
        Children.Add(Hud.Header(title)); Children.Add(middle); Children.Add(row);
    }

    public string TemperatureLine => temperature.Text;

    // Takes the same display lines the normal view shows, so every state reaches both forms.
    public void Show(string place, string description, string temperatureLine, string humidityLine)
    {
        if (soft) {
            condition.Text = description;
            temperature.Text = double.TryParse(temperatureLine.Replace("°C", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double degrees) ? Math.Round(degrees, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "°" : "--";
            sign?.Show(description, temperature.Text, humidityLine);
            softDetail.Text = place + " · 습도 " + humidityLine;
            return;
        }
        title.Text = "날씨 · " + place;
        condition.Text = description;
        temperature.Text = FormatTemperature(temperatureLine);
        humidity.Text = humidityLine;
        bar.Value = double.TryParse(humidityLine.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double share) ? share / 100 : 0;
    }

    public static string FormatTemperature(string line)
    {
        if (!double.TryParse(line.Replace("°C", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return "--";
        int rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return (rounded < 0 ? "-" : "+") + Math.Abs(rounded).ToString("00", CultureInfo.InvariantCulture);
    }
}

// A 0-24 hour scale: ticks rise from one base line, hour numbers sit above, and a triangle under the line points up at the current time.
public sealed class DayRuler : FrameworkElement
{
    private const double Pad = 5, Base = 30;
    private TimeSpan now;
    public TimeSpan Now { get => now; set { now = value; InvalidateVisual(); } }
    public double MarkerX => Pad + (ActualWidth - Pad * 2) * now.TotalHours / 24;

    protected override void OnRender(DrawingContext context)
    {
        if (ActualWidth <= Pad * 2 || TryFindResource("Mint") is not Brush mint || TryFindResource("Ink") is not Brush ink || TryFindResource("ScheduleNow") is not Brush marker) return;
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip, span = ActualWidth - Pad * 2;
        var line = new Pen(mint, 1); line.Freeze();
        context.DrawLine(line, new Point(Pad, Base), new Point(ActualWidth - Pad, Base));
        for (int hour = 0; hour <= 24; hour++) {
            double x = Pad + span * hour / 24;
            context.DrawLine(line, new Point(x, Base), new Point(x, Base - (hour % 6 == 0 ? 15 : hour % 3 == 0 ? 9 : 5)));
            if (hour % 6 != 0) continue;
            var text = new FormattedText(hour.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 11, ink, dip);
            context.DrawText(text, new Point(Math.Clamp(x - text.Width / 2, 0, ActualWidth - text.Width), 0));
        }
        var arrow = new StreamGeometry();
        using (var draw = arrow.Open()) {
            draw.BeginFigure(new Point(MarkerX, Base + 2), true, true);
            draw.LineTo(new Point(MarkerX - 5, Base + 11), false, false);
            draw.LineTo(new Point(MarkerX + 5, Base + 11), false, false);
        }
        arrow.Freeze();
        context.DrawGeometry(marker, null, arrow);
    }
}

// A filled share of a row of short segments.
public sealed class SegmentBar : FrameworkElement
{
    private double value;
    public double Value { get => value; set { this.value = Math.Clamp(value, 0, 1); InvalidateVisual(); } }

    protected override void OnRender(DrawingContext context)
    {
        if (TryFindResource("Mint") is not SolidColorBrush mint) return;
        var dim = new SolidColorBrush(Color.FromArgb(70, mint.Color.R, mint.Color.G, mint.Color.B)); dim.Freeze();
        for (double x = 0; x < ActualWidth; x += 8)
            context.DrawRectangle(x / Math.Max(1, ActualWidth) < value ? mint : dim, null, new Rect(x, 0, Math.Min(5, ActualWidth - x), ActualHeight));
    }
}
