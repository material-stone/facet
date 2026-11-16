using Facet.Plugins;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using Facet.Widgets;

namespace Facet.ConditionPlugin;

// The HUD condition widget: the current state over every state as a colored button (ADR 0086).
public sealed class ConditionHudView : UserControl, IDisposable
{
    private readonly TextBlock state = new() { FontSize = 30, FontWeight = FontWeights.SemiBold, Text = "기록하기", Margin = new(0,6,0,6) };
    private readonly TextBlock error = new() { Text = "저장하지 못했습니다. 다시 선택해 주세요.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly bool soft;
    internal IReadOnlyList<Button> Options { get; }
    internal Func<string, DateTimeOffset, bool>? Record { get; set; }
    internal ConditionLive? Live { get; set; }
    internal string StateText => state.Text;

    internal ConditionHudView()
    {
        Focusable = false; KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
        var template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate TargetType="Button" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
             <Border Background="Transparent" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" Padding="8,0"><ContentPresenter VerticalAlignment="Center"/></Border>
            </ControlTemplate>
            """);
        bool soft = WidgetTheme.IsSoftHud;
        if (soft) template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate TargetType="Button" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
             <Border Background="Transparent"><ContentPresenter/></Border>
            </ControlTemplate>
            """);
        this.soft = soft;
        var tiles = new UniformGrid { Columns = soft ? 6 : 3, Margin = new(-3,0,-3,0) };
        var options = new List<Button>();
        foreach (var style in ConditionIcons.All) {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border { Width = 16, Height = 16, Background = ConditionIcons.Color(style.Id), Margin = new(0,0,7,0), VerticalAlignment = VerticalAlignment.Center });
            var name = Hud.Text(style.Name, 13); name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
            var button = new Button { Content = row, ContentTemplate = null, Template = template, Height = 36, Margin = new(3), Cursor = Cursors.Hand, Tag = style.Id, Focusable = false, IsTabStop = false };
            if (WidgetTheme.IsCatHud) { var token = new CatToken(style.Id); tokens[style.Id] = token; button.Content = token; button.Height = double.NaN; button.Margin = new(1); button.ToolTip = style.Name; }
            else if (soft) { button.Content = Cube(style.Id, style.Name); button.Height = double.NaN; button.Margin = new(1); button.ToolTip = style.Name; }
            AutomationProperties.SetName(button, style.Name + "으로 기록");
            button.Click += (_, _) => Select(style.Id, DateTimeOffset.Now);
            tiles.Children.Add(button); options.Add(button);
        }
        Options = options.AsReadOnly();
        var body = new StackPanel();
        if (WidgetTheme.IsCatHud) {
            // Cat faces on a wooden shelf (ADR 0107).
            var heading = Hud.Header("오늘 기분");
            var fill = new Grid();
            fill.RowDefinitions.Add(new() { Height = GridLength.Auto }); fill.RowDefinitions.Add(new() { Height = GridLength.Auto }); fill.RowDefinitions.Add(new() { Height = GridLength.Auto }); fill.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var shelf = new CatShelf { Height = 16, Margin = new(-4,-3,-4,0) };
            Grid.SetRow(tiles, 1); Grid.SetRow(shelf, 2); Grid.SetRow(error, 3);
            fill.Children.Add(heading); fill.Children.Add(tiles); fill.Children.Add(shelf); fill.Children.Add(error);
            fill.VerticalAlignment = VerticalAlignment.Center;
            Content = fill;
            SetResourceReference(ForegroundProperty, "Ink");
            error.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
            return;
        }
        if (soft) {
            var heading = Hud.Header("컨디션"); heading.Margin = new(0,0,0,2);
            // The cubes take all spare height, in one row with their labels below.
            var fill = new Grid();
            fill.RowDefinitions.Add(new() { Height = GridLength.Auto }); fill.RowDefinitions.Add(new()); fill.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetRow(tiles, 1); Grid.SetRow(error, 2);
            fill.Children.Add(heading); fill.Children.Add(tiles); fill.Children.Add(error);
            Content = fill;
            SetResourceReference(ForegroundProperty, "Ink");
            error.SetResourceReference(TextBlock.ForegroundProperty, "DangerSoft");
            return;
        }
        else { body.Children.Add(Hud.Header("컨디션")); body.Children.Add(state); body.Children.Add(tiles); body.Children.Add(error); }
        Content = body;
        SetResourceReference(ForegroundProperty, "Ink");
        error.SetResourceReference(TextBlock.ForegroundProperty, "DangerSoft");
    }

    // An isometric voxel with its label below; the current condition lifts and its label takes the accent.
    private readonly Dictionary<string, (TranslateTransform Lift, TextBlock Label)> cubes = new();
    private readonly Dictionary<string, CatToken> tokens = new();
    private Grid Cube(string kind, string name)
    {
        var color = ConditionIcons.Color(kind);
        System.Windows.Shapes.Polygon Face(Brush fill, params double[] points)
        {
            var collection = new PointCollection();
            for (int i = 0; i < points.Length; i += 2) collection.Add(new(points[i], points[i + 1]));
            return new System.Windows.Shapes.Polygon { Points = collection, Fill = fill };
        }
        var lift = new TranslateTransform();
        var shape = new Canvas { Width = 44, Height = 60, RenderTransform = lift };
        shape.Children.Add(Face(ConditionIcons.Shade(color, .86), 0,20, 22,31, 22,60, 0,49));
        shape.Children.Add(Face(ConditionIcons.Shade(color, .6), 44,20, 44,49, 22,60, 22,31));
        shape.Children.Add(Face(ConditionIcons.Shade(color, 1.16), 22,9, 44,20, 22,31, 0,20));
        var label = Hud.Text(name, 11, "Muted");
        label.TextAlignment = TextAlignment.Center; label.HorizontalAlignment = HorizontalAlignment.Center; label.TextTrimming = TextTrimming.None; label.Margin = new(0,3,0,0);
        cubes[kind] = (lift, label);
        var cell = new Grid();
        cell.RowDefinitions.Add(new()); cell.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var fit = new Viewbox { Child = shape, Stretch = Stretch.Uniform };
        Grid.SetRow(label, 1);
        cell.Children.Add(fit); cell.Children.Add(label);
        return cell;
    }

    internal bool Select(string kind, DateTimeOffset at)
    {
        bool saved = Record?.Invoke(kind, at) == true;
        error.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
        return saved;
    }

    internal void Refresh(IReadOnlyList<ConditionEntry> entries, DateTimeOffset now)
    {
        var style = ConditionIcons.Find(ConditionTracking.Current(entries, now)?.Kind);
        state.Text = style?.Name ?? "기록하기";
        if (style != null && !soft) state.Foreground = ConditionIcons.Color(style.Id); else if (style != null) state.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); else state.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        foreach (var (kind, token) in tokens) token.Selected = kind == style?.Id;
        foreach (var (kind, cube) in cubes) {
            bool current = kind == style?.Id;
            cube.Lift.Y = current ? -9 : 0;
            cube.Label.SetResourceReference(TextBlock.ForegroundProperty, current ? "Mint" : "Muted");
        }
        foreach (var option in Options) {
            if (soft) continue;
            if (Equals(option.Tag, style?.Id)) option.BorderBrush = ConditionIcons.Color(style!.Id);
            else {
                var quiet = new SolidColorBrush(Color.FromArgb(90, 124, 255, 212));
                if (TryFindResource("Mint") is SolidColorBrush mint) quiet.Color = Color.FromArgb(90, mint.Color.R, mint.Color.G, mint.Color.B);
                option.BorderBrush = quiet;
            }
        }
    }

    public void Dispose() => Live?.Dispose();
}

// A round cream token with a cat face for one condition; the current one turns pink, grows and pops once.
internal sealed class CatToken : FrameworkElement
{
    private static readonly Dictionary<string, CatFace> Faces = new() {
        ["good"] = new("heart", "w", Sparkle: true), ["okay"] = new("happy", "open"), ["sleepy"] = new("flat", "w"),
        ["tired"] = new("flat", "flat"), ["unwell"] = new("tear", "wobble"), ["stressed"] = new("angry", "frown", FlatEars: true) };
    private readonly CatFace face;
    private bool selected;
    private double popStart = -10;
    private readonly System.Windows.Threading.DispatcherTimer pop = new() { Interval = TimeSpan.FromMilliseconds(33) };

    internal CatToken(string kind)
    {
        face = Faces.GetValueOrDefault(kind) ?? new CatFace("dot", "w");
        pop.Tick += (_, _) => { if (Cat.Seconds - popStart > .5) pop.Stop(); InvalidateVisual(); };
        Unloaded += (_, _) => pop.Stop();
        MouseEnter += (_, _) => InvalidateVisual(); MouseLeave += (_, _) => InvalidateVisual();
    }

    internal bool Selected
    {
        get => selected;
        set { if (value && !selected && IsLoaded) { popStart = Cat.Seconds; pop.Start(); } selected = value; InvalidateVisual(); }
    }

    protected override Size MeasureOverride(Size available) { double side = double.IsInfinity(available.Width) ? 44 : Math.Min(available.Width, 72); return new(side, side * .95); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, d = Math.Min(w * .84, h * .82);
        if (d < 8) return;
        double p = Cat.Seconds - popStart, scale = selected ? 1.12 : 1;
        if (p is >= 0 and < .5) scale *= 1 + .18 * Math.Sin(p / .5 * Math.PI) * (1 - p / .5);
        double lift = IsMouseOver ? d * .08 : 0, r = d / 2, stroke = Math.Max(1.5, d * .055);
        var center = new Point(w / 2, h - r * scale - stroke - lift);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        dc.PushTransform(new ScaleTransform(scale, scale, center.X, center.Y));
        var circle = new EllipseGeometry(center, r, r);
        Cat.Shape(dc, circle, selected ? Cat.Pink : Cat.CreamLight, stroke, shadow: true);
        dc.PushTransform(new TranslateTransform(center.X, center.Y + r * .1));
        dc.PushTransform(new ScaleTransform(r * .78 / 50, r * .78 / 50));
        Cat.Head(dc, face, 5, shadow: false);
        dc.Pop(); dc.Pop(); dc.Pop();
    }
}

// The wooden shelf under the tokens, on two brackets.
internal sealed class CatShelf : FrameworkElement
{
    public CatShelf() { IsHitTestVisible = false; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 10 || h < 6) return;
        double board = Math.Min(8, h * .5);
        foreach (double x in new[] { w * .14, w * .86 }) Cat.Plate(dc, new Rect(x - 4, 1, 8, h - 3), 3, Cat.WoodDark, 2, shadow: false);
        Cat.Plate(dc, new Rect(2, 0, w - 6, board), 3, Cat.WoodMid, 2.4);
    }
}
