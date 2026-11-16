using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using System.Windows.Input;
using Facet.Plugins;
using Facet.Widgets;

namespace Facet.ConditionPlugin;

public sealed class ConditionHistory : UserControl, IDisposable
{
    private readonly TextBlock range = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Canvas bar = new() { Height = 16, Background = Brushes.Transparent };
    private readonly UniformGrid days = new() { Columns = 7, Margin = new(0,8,0,0) };
    private IReadOnlyList<ConditionEntry> entries = Array.Empty<ConditionEntry>();
    private DateTimeOffset now;
    private DateOnly shownDate;
    internal DateTimeOffset Start { get; private set; }
    internal DateTimeOffset End { get; private set; }
    internal IReadOnlyList<ConditionSpan> Spans { get; private set; } = Array.Empty<ConditionSpan>();
    internal Canvas Bar => bar;
    internal ConditionRecordEditor Editor { get; } = new();
    internal Popup RecordPopup { get; }
    internal Func<Action<List<ConditionEntry>>, bool>? Commit { get; set; }
    internal event Action? Changed;
    internal ConditionLive? Live { get; set; }
    private readonly bool compact;
    private readonly bool cat;
    private readonly ChuruTray tray = new() { IsHitTestVisible = false };
    internal IReadOnlyList<int> CatLevels => tray.Levels;
    private ConditionEntry? pressedRecord;
    private Point pressedPoint;

    public ConditionHistory() : this(false) { }

    // Compact is the HUD form: a small title and no legend, since the condition widget's buttons already show the colors.
    internal ConditionHistory(bool compact)
    {
        Focusable = false; KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.None);
        RecordPopup = new Popup { Child = Editor, PlacementTarget = bar, Placement = PlacementMode.Bottom, VerticalOffset = 8,
            StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
        Editor.Commit = change => Commit?.Invoke(change) == true;
        Editor.Saved += () => Changed?.Invoke();
        Editor.Deleted += () => { RecordPopup.IsOpen = false; Changed?.Invoke(); };
        RecordPopup.Closed += (_,_) => Editor.CloseTimePicker();
        PopupKeys.TakeFocus(RecordPopup, Editor);
        Editor.PreviewKeyDown += (_,e) => { if (e.Key != Key.Escape) return; if(!Editor.HandleEscape())RecordPopup.IsOpen = false; e.Handled = true; };
        bar.MouseLeftButtonDown += (_,e) => {
            var point=CatPoint(e.GetPosition(bar));var span=SpanAt(point.X);if(span==null)return;
            pressedRecord=new(){At=span.RecordedAt,Kind=span.Kind};pressedPoint=point;bar.CaptureMouse();e.Handled=true;
        };
        bar.MouseLeftButtonUp += (_,e) => {
            var pressed=pressedRecord;var point=CatPoint(e.GetPosition(bar));pressedRecord=null;
            if(bar.IsMouseCaptured)bar.ReleaseMouseCapture();
            var span=SpanAt(point.X);
            if(pressed==null || span==null || point.Y<0 || point.Y>=bar.ActualHeight || pressed.At!=span.RecordedAt || pressed.Kind!=span.Kind ||
                Math.Abs(point.X-pressedPoint.X)>SystemParameters.MinimumHorizontalDragDistance || Math.Abs(point.Y-pressedPoint.Y)>SystemParameters.MinimumVerticalDragDistance)return;
            e.Handled=OpenAt(point.X);
        };
        bar.LostMouseCapture += (_,_) => pressedRecord=null;
        Unloaded += (_,_) => RecordPopup.IsOpen = false;
        IsVisibleChanged += (_,_) => { if (!IsVisible) RecordPopup.IsOpen = false; };
        var root = new StackPanel(); var title = new Grid();
        title.Children.Add(new TextBlock { Text = "최근 7일 컨디션", FontSize = compact ? 12 : 16, FontWeight = compact ? FontWeights.Normal : FontWeights.SemiBold }); if (!compact) title.Children.Add(range);
        if (compact) {
            root.Children.Add(Facet.Widgets.Hud.Header("7일 컨디션"));
            range.FontFamily = Facet.Widgets.Hud.NumberFont; range.Margin = new(0,4,0,0); range.HorizontalAlignment = HorizontalAlignment.Right;
        }
        else root.Children.Add(title);
        this.compact = compact;
        bar.Margin = new(0,compact ? 6 : 16,0,0); root.Children.Add(bar); root.Children.Add(days);
        var legend = new WrapPanel { Margin = new(0,12,0,0) };
        foreach (var item in ConditionIcons.All) {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0,0,18,0) };
            row.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = ConditionIcons.Color(item.Id), VerticalAlignment = VerticalAlignment.Center, Margin = new(0,0,6,0) });
            row.Children.Add(new TextBlock { Text = item.Name, FontSize = 11 }); legend.Children.Add(row);
        }
        if (!compact) root.Children.Add(legend);
        if (compact) {
            // Title on top, gauge and dates in the middle of the tile, the period at the bottom right.
            if (WidgetTheme.IsSoftHud) bar.Height = 29;
            bar.Margin = new(0);
            var heading = root.Children[0];
            root.Children.Remove(heading);
            root.VerticalAlignment = VerticalAlignment.Center; root.Margin = new(0,6,0,6);
            var form = new Grid();
            form.RowDefinitions.Add(new() { Height = GridLength.Auto }); form.RowDefinitions.Add(new()); form.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetRow(root, 1); Grid.SetRow(range, 2);
            form.Children.Add(heading); form.Children.Add(root); form.Children.Add(range);
            Content = form;
            if (WidgetTheme.IsCatHud) {
                // Churu tubes on a tray fill the middle row; the tray draws its own day row (ADR 0107).
                cat = true;
                root.Children.Remove(bar); root.Visibility = Visibility.Collapsed; range.Visibility = Visibility.Collapsed;
                bar.Height = double.NaN; bar.MinHeight = 64; bar.Margin = new(0,2,0,0);
                Grid.SetRow(bar, 1); form.Children.Add(bar);
            }
        }
        else Content = root;
        range.SetResourceReference(TextBlock.ForegroundProperty, compact ? "Ink" : "Muted");
        legend.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        bar.SizeChanged += (_, _) => Draw();
    }

    internal void Refresh(IReadOnlyList<ConditionEntry> data, DateTimeOffset at)
    {
        entries = data; now = at;
        var date = DateOnly.FromDateTime(at.LocalDateTime);
        if (date != shownDate) {
            shownDate = date;
            // The desktop owns local timezone conversion; Core receives explicit instants.
            DateTime LocalMidnight(DateOnly day) => DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Local);
            Start = new DateTimeOffset(LocalMidnight(date.AddDays(-6))); End = new DateTimeOffset(LocalMidnight(date.AddDays(1)));
            range.Text = compact ? $"{Start:M.d} – {End.AddDays(-1):M.d}" : $"{Start:M.d} 00:00 – {End.AddDays(-1):M.d} 24:00";
            days.Children.Clear();
            for (int i = 0; i < 7; i++) {
                var day = date.AddDays(i-6);
                var label = new TextBlock { Text = day.ToString("M.d ddd", CultureInfo.GetCultureInfo("ko-KR")), FontSize = compact ? 10 : 11,
                    TextAlignment = TextAlignment.Center, FontWeight = i == 6 ? FontWeights.SemiBold : FontWeights.Normal };
                label.SetResourceReference(TextBlock.ForegroundProperty, i == 6 ? "Ink" : "Muted"); days.Children.Add(label);
            }
        }
        Spans = ConditionTracking.Project(entries, Start, End, now);
        Draw();
    }

    internal bool OpenAt(double x, bool show = true)
    {
        var span=SpanAt(x);
        if (span == null) return false;
        Editor.Open(new() { At = span.RecordedAt, Kind = span.Kind });
        RecordPopup.HorizontalOffset = Math.Clamp(x-160,0,Math.Max(0,bar.ActualWidth-Editor.Width));
        if (show) RecordPopup.IsOpen = true;
        return true;
    }

    public void Dispose() { RecordPopup.IsOpen = false; Live?.Dispose(); }

    private static double Radius => WidgetTheme.IsHud ? 0 : 8;

    private ConditionSpan? SpanAt(double x)
    {
        if(bar.ActualWidth<=0 || x<0 || x>=bar.ActualWidth)return null;
        var stamp=Start+TimeSpan.FromTicks((long)((End-Start).Ticks*x/bar.ActualWidth));
        return Spans.FirstOrDefault(item=>stamp>=item.Start && stamp<item.End);
    }

    // Level 1-6 of a condition for the tube height; the higher, the better the day.
    private static int Level(string kind) => kind switch { "good" => 6, "okay" => 5, "sleepy" => 3, "tired" => 3, "unwell" => 2, "stressed" => 1, _ => 3 };

    // The span that covers most of a day, or null for a day with no record.
    private ConditionSpan? Dominant(int day, out DateTimeOffset from, out DateTimeOffset to)
    {
        from = Start.AddDays(day); to = Start.AddDays(day + 1);
        DateTimeOffset a = from, b = to;
        return Spans.Where(s => s.End > a && s.Start < b).MaxBy(s => (s.End < b ? s.End : b) - (s.Start > a ? s.Start : a));
    }

    // In the cat form a press anywhere on a day's tube means that day's main record.
    private Point CatPoint(Point point)
    {
        if (!cat || bar.ActualWidth <= 0 || End <= Start) return point;
        int day = Math.Clamp((int)(point.X / bar.ActualWidth * 7), 0, 6);
        if (Dominant(day, out var from, out var to) is not { } span) return point;
        var a = span.Start > from ? span.Start : from; var b = span.End < to ? span.End : to;
        double mid = ((a - Start).TotalSeconds + (b - a).TotalSeconds / 2) / (End - Start).TotalSeconds * bar.ActualWidth;
        return new Point(Math.Clamp(mid, 0, bar.ActualWidth - .01), Math.Clamp(point.Y, 0, Math.Max(0, bar.ActualHeight - .01)));
    }

    private void DrawCat()
    {
        var levels = new int[7]; var tips = new List<string>();
        var korean = CultureInfo.GetCultureInfo("ko-KR");
        for (int i = 0; i < 7; i++) {
            var span = Dominant(i, out _, out _);
            levels[i] = span == null ? 0 : Level(span.Kind);
            tips.Add(shownDate.AddDays(i-6).ToString("M.d ddd", korean) + " · " + (span == null ? "기록 없음" : ConditionIcons.Find(span.Kind)!.Name));
        }
        tray.Show(levels, Enumerable.Range(0, 7).Select(i => i == 6 ? "오늘" : shownDate.AddDays(i-6).ToString("ddd", korean)).ToArray());
        tray.Width = bar.ActualWidth; tray.Height = bar.ActualHeight;
        bar.Children.Add(tray);
        bar.Cursor = Cursors.Hand;
        bar.ToolTip = string.Join(Environment.NewLine, tips) + Environment.NewLine + "눌러서 그날 기록 수정·삭제";
        AutomationProperties.SetName(bar, "최근 7일 컨디션. " + string.Join("; ", tips));
    }

    private void Draw()
    {
        bar.Children.Clear(); if (End <= Start || bar.ActualWidth <= 0) return;
        if (cat) { DrawCat(); return; }
        double X(DateTimeOffset time) => Math.Clamp((time-Start).TotalSeconds/(End-Start).TotalSeconds, 0, 1)*bar.ActualWidth;
        bar.Clip = new RectangleGeometry(new Rect(0,0,bar.ActualWidth,bar.ActualHeight),Radius,Radius);
        foreach (var span in Spans) {
            var segment = new Rectangle { Height = bar.ActualHeight, Width = X(span.End)-X(span.Start), Fill = ConditionIcons.Color(span.Kind),
                Cursor = Cursors.Hand, Focusable = false,
                ToolTip = $"{ConditionIcons.Find(span.Kind)!.Name} · {span.RecordedAt.ToLocalTime():M.d HH:mm:ss} 기록\n클릭하여 수정·삭제" };
            Canvas.SetLeft(segment,X(span.Start)); bar.Children.Add(segment);
            if (WidgetTheme.IsSoftHud) {
                // The same clickable span, drawn as a top, front and underside.
                segment.Height = 14; Canvas.SetTop(segment,9);
                var color = ConditionIcons.Color(span.Kind);
                var top = new Rectangle { Width = segment.Width, Height = 9, Fill = ConditionIcons.Shade(color,1.18), IsHitTestVisible = false };
                var under = new Rectangle { Width = segment.Width, Height = 6, Fill = ConditionIcons.Shade(color,.6), IsHitTestVisible = false };
                Canvas.SetLeft(top,X(span.Start)); Canvas.SetLeft(under,X(span.Start)); Canvas.SetTop(under,23);
                bar.Children.Add(top); bar.Children.Add(under);
            }
        }
        // A faint outline gives the transparent portion a visible extent without filling it.
        var outline = new Border { Width = bar.ActualWidth, Height = bar.ActualHeight, CornerRadius = new(Radius), BorderThickness = new(1), Opacity = .45, IsHitTestVisible = false };
        outline.SetResourceReference(Border.BorderBrushProperty,"Edge"); bar.Children.Add(outline);
        for (int i=1;i<7;i++) {
            var day = shownDate.AddDays(i-6);
            var stamp = new DateTimeOffset(DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue),DateTimeKind.Local));
            var tick = new Rectangle { Width = 1, Height = bar.ActualHeight, Opacity = .35, IsHitTestVisible = false };
            tick.SetResourceReference(Shape.FillProperty,"Edge"); Canvas.SetLeft(tick,X(stamp)); bar.Children.Add(tick);
        }
        AutomationProperties.SetName(bar,$"최근 7일 컨디션, {Start:M월 d일}부터 {End.AddDays(-1):M월 d일}까지. " +
            string.Join("; ",Spans.Select(span=>$"{ConditionIcons.Find(span.Kind)!.Name}, {span.Start.ToLocalTime():M월 d일 HH:mm}부터 {span.End.ToLocalTime():M월 d일 HH:mm}까지")));
    }
}

// Seven churu tubes on a wooden tray over a cream day row; a tube's height is the day's condition and today is pink.
internal sealed class ChuruTray : FrameworkElement
{
    private int[] levels = new int[7];
    private string[] days = new string[7];
    internal IReadOnlyList<int> Levels => levels;

    internal void Show(int[] values, string[] names) { levels = values; days = names; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 40 || h < 30) return;
        double pill = Math.Clamp(h * .17, 13, 22), board = Math.Clamp(pill * .5, 6, 11), stroke = Math.Clamp(h * .022, 1.6, 2.6);
        double pillTop = h - pill - 3, boardTop = pillTop - board - 3;
        double inner = w - 16, gap = inner / 7 * .26, tube = (inner - gap * 6) / 7;
        double cap = Math.Clamp(tube * .42, 5, 12), maxBody = boardTop - 2 - cap - 4;
        var stripes = Stripes(Cat.Butter, Cat.Fur, tube); var today = Stripes(Cat.Pink, Cat.DarkPink, tube);
        for (int i = 0; i < 7; i++) {
            double x = 8 + i * (tube + gap);
            if (levels[i] <= 0 || maxBody < 6) {
                dc.PushOpacity(.35);
                dc.DrawRoundedRectangle(null, new Pen(Cat.Outline, stroke) { DashStyle = DashStyles.Dash }, new Rect(x, boardTop - 8, tube, 8), 3, 3);
                dc.Pop();
                continue;
            }
            double body = Math.Max(cap, maxBody * levels[i] / 6), top = boardTop - body;
            var shape = new RectangleGeometry(new Rect(x, top, tube, body), Math.Min(4, tube * .25), Math.Min(4, tube * .25));
            Cat.Shape(dc, shape, i == 6 ? today : stripes, stroke, shadow: true);
            // The tear line just under the cap.
            dc.DrawLine(new Pen(Cat.Outline, Math.Max(1, stroke * .55)) { DashStyle = new DashStyle([1.6, 1.6], 0) }, new Point(x + stroke, top + cap * .55), new Point(x + tube - stroke, top + cap * .55));
            // The crimped seal cap with a tear notch on the right.
            double cx = x - stroke * .5, cw = tube + stroke, cy = top - cap;
            var seal = Cat.G(FormattableString.Invariant($"M{cx},{cy + cap} L{cx},{cy + cap * .2} Q{cx},{cy} {cx + cap * .2},{cy} L{cx + cw - cap * .2},{cy} Q{cx + cw},{cy} {cx + cw},{cy + cap * .2} L{cx + cw},{cy + cap * .35} L{cx + cw - cap * .35},{cy + cap * .55} L{cx + cw},{cy + cap * .75} L{cx + cw},{cy + cap}Z"));
            dc.DrawGeometry(Cat.Cream, new Pen(Cat.Outline, stroke) { LineJoin = PenLineJoin.Round }, seal);
            var crimp = new Pen(Cat.Sub, Math.Max(.8, stroke * .45));
            for (double k = cx + cw * .2; k < cx + cw * .72; k += Math.Max(2.5, cw * .14)) dc.DrawLine(crimp, new Point(k, cy + cap * .25), new Point(k, cy + cap * .8));
        }
        Cat.Plate(dc, new Rect(3, boardTop, w - 6, board), 3, Cat.WoodMid, stroke);
        var row = new Rect(4, pillTop, w - 8, pill);
        Cat.Plate(dc, row, pill / 2, Cat.CreamLight, Math.Max(1.4, stroke * .8), shadow: false);
        double font = Math.Clamp(pill * .62, 8, 14);
        for (int i = 0; i < 7; i++) {
            if (days[i] == null) continue;
            var text = Cat.Text(this, days[i], font, i == 6 ? Cat.DarkPink : Cat.Ink);
            Cat.Centered(dc, text, new Point(8 + i * (tube + gap) + tube / 2, pillTop + pill / 2));
        }
    }

    private static Brush Stripes(Brush light, Brush dark, double tube)
    {
        double step = Math.Max(4, tube * .28);
        Color a = ((SolidColorBrush)light).Color, b = ((SolidColorBrush)dark).Color;
        var brush = new LinearGradientBrush(new GradientStopCollection { new(a, 0), new(a, .5), new(b, .5), new(b, 1) }, new Point(0, 0), new Point(step, -step * .7))
        { MappingMode = BrushMappingMode.Absolute, SpreadMethod = GradientSpreadMethod.Repeat };
        brush.Freeze();
        return brush;
    }
}
