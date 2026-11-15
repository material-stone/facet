using System.Globalization;
using System.Windows.Automation;

namespace Facet.SleepPlugin;

internal sealed class SleepPatternView : UserControl, IDisposable
{
    private readonly StackPanel rows = new();
    private readonly TextBlock range = new() { FontSize = 13, Margin = new(0,8,0,24) };
    private readonly TextBlock totals = new() { FontSize = 13, FontWeight = FontWeights.Normal, Margin = new(0,12,0,0), TextWrapping = TextWrapping.Wrap };
    private readonly SleepTimeAxis axis = new() { Height = 32 };
    private string? lastFingerprint;
    internal Border ChartBackground { get; } = new() { Padding = new(16), CornerRadius = new(16) };
    internal SleepEditing? Editing { get; init; }
    internal Func<double>? Transparency { get; init; }
    internal SleepLive? Live { get; set; }
    internal Button TodayButton { get; }
    internal IReadOnlyList<SleepDay> Days { get; private set; } = [];
    internal IReadOnlyList<Button> DateRows => rows.Children.OfType<Button>().ToArray();
    internal string AverageText => totals.Text;

    public SleepPatternView()
    {
        Width = 800;
        var body = new StackPanel();
        var titleRow = new DockPanel();
        TodayButton = new Button { Content = "오늘 수면 기록", Margin = new(0), HorizontalAlignment = HorizontalAlignment.Right };
        TodayButton.SetResourceReference(StyleProperty, "ButtonWithoutFocusOutline"); TodayButton.BorderBrush = Brushes.Transparent; TodayButton.BorderThickness = new(0);
        DockPanel.SetDock(TodayButton, Dock.Right); titleRow.Children.Add(TodayButton);
        titleRow.Children.Add(new TextBlock { Text = "최근 7일 수면 패턴", FontSize = 24, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }); body.Children.Add(titleRow);
        TodayButton.Click += (_, _) => Editing?.Open(DateOnly.FromDateTime(DateTime.Now));
        range.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); body.Children.Add(range);
        var panel = new StackPanel();
        var columns = Columns(); columns.Margin = new(8,0,0,0); axis.Margin = new(0,0,12,0); Grid.SetColumn(axis, 1); columns.Children.Add(axis); panel.Children.Add(columns); panel.Children.Add(rows);
        ChartBackground.Child = panel; body.Children.Add(ChartBackground);
        totals.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); body.Children.Add(totals);
        Content = body;
    }

    private static Grid Columns()
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new(82) }); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new(170) }); return grid;
    }

    internal void Refresh(IReadOnlyList<SleepEntry> entries, DateOnly today)
    {
        double transparency = Transparency?.Invoke() ?? 0;
        var background = ((SolidColorBrush)FindResource("Panel")).Clone();
        background.Opacity = 1 - Math.Clamp(double.IsFinite(transparency) ? transparency : 0, 0, 100) / 100; background.Freeze();
        ChartBackground.Background = background;
        Days = SleepTracking.Week(entries, today);
        string fingerprint = System.Text.Json.JsonSerializer.Serialize(Days);
        // An unchanged week keeps its row buttons, and so keyboard focus.
        if (fingerprint == lastFingerprint) return;
        lastFingerprint = fingerprint;
        int left = Math.Min(-720, Days.Where(d => d.Span != null).Select(d => (int)Math.Floor(d.Span!.StartMinute / 360d) * 360).DefaultIfEmpty(-720).Min());
        int right = Math.Max(1080, Days.Where(d => d.Span != null).Select(d => (int)Math.Ceiling(d.Span!.EndMinute / 360d) * 360).DefaultIfEmpty(1080).Max());
        axis.Start = left; axis.End = right; axis.InvalidateVisual();
        range.Text = $"{Days[0].Date:yyyy.MM.dd} — {today:MM.dd} · 기상일 기준";
        rows.Children.Clear();
        foreach (var day in Days) {
            var content = Columns(); content.MinHeight = 58;
            var dateText = new TextBlock { Text = day.Date.ToString("MM.dd ddd", CultureInfo.GetCultureInfo("ko-KR")), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            if (day.Date == today) dateText.SetResourceReference(TextBlock.ForegroundProperty, "Mint");
            content.Children.Add(dateText);
            var bar = new SleepTimeBar { Span = day.Span, Start = left, End = right, Margin = new(0,0,12,0) }; Grid.SetColumn(bar, 1); content.Children.Add(bar);
            string times = day.Entry == null ? "미기록 · 눌러서 기록" : $"{day.Entry.Attempt ?? "—"} → {day.Entry.FinalWake ?? "—"}";
            string estimate = SleepTracking.Minutes(day) is double minutes ? "수면 시간 " + SleepTracking.FormatDuration(Math.Round(minutes)) : day.Entry == null ? "" : day.Span == null ? "시각 정보 부족" : "수면 시간 —";
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = times, FontSize = 13 });
            var detail = new TextBlock { Text = estimate, FontSize = 11, Margin = new(0,4,0,0) }; detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); text.Children.Add(detail);
            Grid.SetColumn(text, 2); content.Children.Add(text);
            var button = new Button { Content = content, ContentTemplate = null, Padding = new(8,0,0,0), Margin = new(0,2,0,2), BorderThickness = new(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = day.Date };
            button.SetResourceReference(StyleProperty, "ButtonWithoutFocusOutline"); button.BorderBrush = Brushes.Transparent;
            button.Background = Brushes.Transparent;
            // The shared button template centers content: explicitly stretch the row to the chart's width.
            button.SizeChanged += (_, _) => content.Width = Math.Max(1, button.ActualWidth - 8);
            AutomationProperties.SetName(button, $"{day.Date:yyyy년 M월 d일} 수면 · {times} · {estimate} · 클릭하여 수정");
            var date = day.Date;
            button.Click += (_, _) => Editing?.Open(date); rows.Children.Add(button);
        }
        totals.Text = SleepTracking.Average(Days);
    }

    public void Dispose() => Live?.Dispose();
}

internal class SleepTimeAxis : FrameworkElement
{
    internal int Start { get; set; } = -720;
    internal int End { get; set; } = 1080;
    protected override void OnRender(DrawingContext dc)
    {
        for (int minute = Start; minute <= End; minute += 360) {
            string label = (minute < 0 ? "전 " : "") + ((minute % 1440 + 1440) % 1440 / 60).ToString("00");
            var text = SleepText.Format(this, label, "Muted", 11);
            double x = (minute - Start) / (double)(End - Start) * ActualWidth;
            dc.DrawText(text, new Point(Math.Clamp(x - text.Width / 2, 0, Math.Max(0, ActualWidth - text.Width)), 6));
        }
    }
}

internal sealed class SleepTimeBar : SleepTimeAxis
{
    internal SleepTimeSpan? Span { get; init; }
    protected override void OnRender(DrawingContext dc)
    {
        double X(int minute) => (minute - Start) / (double)(End - Start) * ActualWidth;
        for (int minute = Start; minute <= End; minute += 360) dc.DrawLine(new Pen((Brush)FindResource("Edge"), minute == 0 ? 1 : .5), new Point(X(minute), 8), new Point(X(minute), ActualHeight - 8));
        if (Span is not { } span) return;
        dc.DrawRoundedRectangle((Brush)FindResource("Mint"), null, new Rect(X(span.StartMinute), ActualHeight / 2 - 7, Math.Max(2, X(span.EndMinute) - X(span.StartMinute)), 14), 7, 7);
    }
}

internal static class SleepText
{
    internal static FormattedText Format(FrameworkElement owner, string value, string color, double size) =>
        new(value, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight, new Typeface((FontFamily)owner.FindResource("AppFontFamily"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, (Brush)owner.FindResource(color), VisualTreeHelper.GetDpi(owner).PixelsPerDip);
}
