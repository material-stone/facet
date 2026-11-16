using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using System.Windows.Input;
using Facet.Plugins;

namespace Facet.ConditionPlugin;

public sealed class ConditionHistory : UserControl, IDisposable, IWidgetState
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

    // A skin can draw this widget from its state (ADR 0109).
    public string Type => ConditionState.HistoryType;
    public string Json() => ConditionState.History(Live?.Entries ?? [], DateTimeOffset.Now);
    event Action? IWidgetState.Changed { add => Live?.Watch(value, true); remove => Live?.Watch(value, false); }
    // edit {date}: opens the record editor at the pointer for the latest record that covers that day, like clicking its span.
    public Task<string?> Act(string name, string argsJson)
    {
        if (name != "edit") throw new ArgumentException("알 수 없는 동작입니다: " + name);
        if (!DateOnly.TryParseExact(ConditionState.Argument(argsJson, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new ArgumentException("date는 yyyy-MM-dd 형식이어야 합니다.");
        var from = new DateTimeOffset(DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Local)); var to = from.AddDays(1);
        if (Spans.Where(span => span.Start < to && span.End > from).OrderByDescending(span => span.RecordedAt).FirstOrDefault() is not { } span) return Task.FromResult<string?>("false");
        Editor.Open(new() { At = span.RecordedAt, Kind = span.Kind });
        RecordPopup.Placement = PlacementMode.MousePoint;
        RecordPopup.IsOpen = true;
        return Task.FromResult<string?>("true");
    }
    private readonly bool compact;
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
        RecordPopup.Closed += (_,_) => { Editor.CloseTimePicker(); RecordPopup.Placement = PlacementMode.Bottom; };
        PopupKeys.TakeFocus(RecordPopup, Editor);
        Editor.PreviewKeyDown += (_,e) => { if (e.Key != Key.Escape) return; if(!Editor.HandleEscape())RecordPopup.IsOpen = false; e.Handled = true; };
        bar.MouseLeftButtonDown += (_,e) => {
            var point=e.GetPosition(bar);var span=SpanAt(point.X);if(span==null)return;
            pressedRecord=new(){At=span.RecordedAt,Kind=span.Kind};pressedPoint=point;bar.CaptureMouse();e.Handled=true;
        };
        bar.MouseLeftButtonUp += (_,e) => {
            var pressed=pressedRecord;var point=e.GetPosition(bar);pressedRecord=null;
            if(bar.IsMouseCaptured)bar.ReleaseMouseCapture();
            var span=SpanAt(point.X);
            if(pressed==null || span==null || point.Y<0 || point.Y>=bar.ActualHeight || pressed.At!=span.RecordedAt || pressed.Kind!=span.Kind ||
                Math.Abs(point.X-pressedPoint.X)>SystemParameters.MinimumHorizontalDragDistance || Math.Abs(point.Y-pressedPoint.Y)>SystemParameters.MinimumVerticalDragDistance)return;
            e.Handled=OpenAt(point.X);
        };
        bar.LostMouseCapture += (_,_) => pressedRecord=null;
        Unloaded += (_,_) => RecordPopup.IsOpen = false;
        IsVisibleChanged += (_,_) => { if (!IsVisible && RecordPopup.Placement != PlacementMode.MousePoint) RecordPopup.IsOpen = false; };
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

    private void Draw()
    {
        bar.Children.Clear(); if (End <= Start || bar.ActualWidth <= 0) return;
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
