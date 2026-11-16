using Facet.Widgets;
using System.Globalization;
using System.Windows.Automation;
using System.Windows.Threading;
using Facet.Plugins;

namespace Facet.SchedulePlugin;

// "오늘 일정": today's schedules and open todos, or the previous, current and next schedule in a HUD skin.
// Moved from the app's dashboard (ADR 0101). The app's own reminders are not shown here.
internal sealed class AgendaView : StackPanel, IDisposable
{
    private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");
    private readonly ScheduleRecords records;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly StackPanel items = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer scroll;
    private readonly StackPanel? hud;
    private string hudShown = "";
    private IReadOnlyList<AgendaCard>? lastCards;

    internal AgendaView(ScheduleRecords records)
    {
        this.records = records;
        AutomationProperties.SetName(this, "오늘 일정");
        if (WidgetTheme.IsCatHud) {
            // The cork board draws itself over the whole tile (ADR 0107); the notes are part of that drawing.
            hud = new();
            scroll = new();
            Cat.Animate(this);
        }
        else if (WidgetTheme.IsHud) {
            Children.Add(Hud.Header("오늘 일정"));
            hud = new() { Margin = new(0, WidgetTheme.IsSoftHud ? 8 : 16, 0, 0) };
            Children.Add(hud);
            scroll = new();
        }
        else {
            Children.Add(new TextBlock { Text = "오늘 주요 일정·할 일", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 16) });
            scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = items };
            scroll.PreviewMouseWheel += (_, e) => {
                if (scroll.ScrollableWidth <= 0) return;
                scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta); e.Handled = true;
            };
            Children.Add(scroll);
        }
        timer.Tick += (_, _) => Refresh(DateTimeOffset.Now);
        records.Changed += OnChanged;
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(DateTimeOffset.Now); timer.Start(); } else timer.Stop(); };
        Refresh(DateTimeOffset.Now);
    }

    private void OnChanged() => Refresh(DateTimeOffset.Now);

    internal void Refresh(DateTimeOffset now)
    {
        if (hud != null) { ShowHud(now.LocalDateTime); return; }
        Show(DashboardAgenda.Today(records.Data, now));
    }

    internal void Show(IReadOnlyList<AgendaCard> cards)
    {
        if (lastCards != null && lastCards.SequenceEqual(cards)) return;
        lastCards = cards; items.Children.Clear();
        if (cards.Count == 0) { items.Children.Add(Card(new("empty", "오늘 등록된 일정이 없습니다", "등록한 일정과 할 일이 여기에 표시됩니다.", "오늘", null))); return; }
        foreach (var card in cards) items.Children.Add(Card(card));
    }

    private Border Card(AgendaCard item)
    {
        var stack = new StackPanel();
        var heading = new Grid(); heading.ColumnDefinitions.Add(new()); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new Icon { Kind = item.Label == "일정" ? IconKind.Calendar : item.Label == "할 일" ? IconKind.Check : IconKind.Bell, Width = 14, Height = 14, Margin = new(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(Icon.ForegroundProperty, "Muted");
        var labelText = new TextBlock { Text = item.Label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }; labelText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        label.Children.Add(icon); label.Children.Add(labelText); heading.Children.Add(label);
        var time = new TextBlock { Text = item.At?.ToString("HH:mm", Korean) ?? "오늘", FontSize = 12 }; time.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Grid.SetColumn(time, 1); heading.Children.Add(time); stack.Children.Add(heading);
        stack.Children.Add(new TextBlock { Text = item.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new(0, 8, 0, 0),
            MaxHeight = 40, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextTrimming = TextTrimming.CharacterEllipsis });
        if (item.Body.Length > 0) {
            var body = new TextBlock { Text = item.Body, FontSize = 12, Margin = new(0, 4, 0, 0), MaxHeight = 16, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
            body.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); stack.Children.Add(body);
        }
        var card = new Border { Width = 236, Height = 124, CornerRadius = new(16), Padding = new(16), Margin = new(0, 0, 16, 8), BorderThickness = new(1), Child = stack };
        card.SetResourceReference(Border.BackgroundProperty, "Panel"); card.SetResourceReference(Border.BorderBrushProperty, "Edge");
        AutomationProperties.SetName(card, item.Label + " · " + time.Text + " · " + item.Title); return card;
    }

    private void ShowHud(DateTime now)
    {
        var (previous, current, next) = DaySchedules.Around(records.Data, now);
        string minutes = current == null ? "" : Math.Ceiling((current.End - now).TotalMinutes).ToString("0", Korean);
        string shown = string.Join("|", previous, current, next, minutes);
        if (shown == hudShown) return;
        hudShown = shown; hud!.Children.Clear();
        if (WidgetTheme.IsCatHud) { ShowCat(previous, current, next, minutes, now); return; }
        if (WidgetTheme.IsSoftHud) { ShowSoft(previous, current, next, minutes, now); return; }
        TextBlock Slot(ScheduleSlot slot, double size, string brush, bool strong = false, double top = 0)
        {
            var line = Hud.Text("", size, brush, strong: strong);
            line.Margin = new(0, top, 0, 0);
            line.Inlines.Add(new System.Windows.Documents.Run(slot.Start.ToString("HH:mm", Korean)) { FontFamily = Hud.NumberFont });
            line.Inlines.Add(new System.Windows.Documents.Run("   " + slot.Title));
            return line;
        }
        var body = new StackPanel();
        if (previous != null) {
            var line = Slot(previous, 16, "Ink"); line.Opacity = .72; line.Margin = new(0, 0, 0, 18);
            body.Children.Add(line);
        }
        if (current != null) {
            string color = current.IsEnding(now) ? "ScheduleNow" : "Mint";
            var label = Hud.Text("지금 · 종료까지 " + minutes + "분", 13, color);
            label.Margin = new(0, 0, 0, 4);
            body.Children.Add(label);
            var title = new Grid();
            var mark = new System.Windows.Shapes.Path { Data = Geometry.Parse("M6,0L12,6L6,12L0,6Z"), Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new(-28.5, 0, 0, 0) };
            mark.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, color);
            title.Children.Add(mark); title.Children.Add(Slot(current, 22, color, strong: true));
            body.Children.Add(title);
            if (current.Memo.Length > 0) {
                var memo = Hud.Text(current.Memo, 14, color);
                memo.Margin = new(0, 6, 0, 0);
                body.Children.Add(memo);
            }
        }
        else body.Children.Add(Hud.Text("지금 진행 중인 일정이 없습니다", 16, "Muted"));
        if (next != null) body.Children.Add(Slot(next, 16, "Ink", top: 18));
        var rail = new Border { BorderThickness = new(1, 0, 0, 0), Padding = new(22, 0, 0, 0), Child = body };
        var edge = ((TryFindResource("Mint") as SolidColorBrush)?.CloneCurrentValue() ?? new SolidColorBrush(Colors.White)); edge.Opacity = .55; edge.Freeze();
        rail.BorderBrush = edge;
        hud.Children.Add(rail);
    }

    private void ShowSoft(ScheduleSlot? previous, ScheduleSlot? current, ScheduleSlot? next, string minutes, DateTime now)
    {
        Grid Row(string label, ScheduleSlot slot, double size, string brush, bool strong, double top)
        {
            var row = new Grid { Margin = new(0, top, 0, 0) };
            row.ColumnDefinitions.Add(new());
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var name = Hud.Text(label + slot.Title, size, brush);
            if (strong) name.FontFamily = Hud.NumberFont;
            var time = Hud.Text(slot.Start.ToString("HH:mm", Korean), size, brush, mono: true);
            time.Margin = new(12, 0, 0, 0); Grid.SetColumn(time, 1);
            row.Children.Add(name); row.Children.Add(time);
            return row;
        }
        if (previous != null) hud!.Children.Add(Row("이전 · ", previous, 14, "Muted", false, 0));
        if (current != null) {
            string color = current.IsEnding(now) ? "ScheduleNow" : "Ink";
            hud!.Children.Add(Row("", current, 24, color, true, previous == null ? 0 : 10));
            string detail = current.Memo.Length > 0 ? current.Memo : "종료까지 " + minutes + "분";
            var memo = Hud.Text(detail, 14, "Muted"); memo.Margin = new(0, 3, 0, 0);
            hud.Children.Add(memo);
        }
        else {
            var empty = Hud.Text("지금 진행 중인 일정이 없습니다", 16, "Muted"); empty.Margin = new(0, previous == null ? 0 : 10, 0, 0);
            hud!.Children.Add(empty);
        }
        if (next != null) hud!.Children.Add(Row("다음 · ", next, 14, "Muted", false, 10));
    }

    // One sticky note of the cork board: past notes are struck through and stamped, the current one wobbles.
    private sealed record CatNote(string Text, Brush Paper, bool Past, bool Current, string Detail);
    private IReadOnlyList<CatNote> catNotes = [];
    internal IReadOnlyList<string> CatLines => catNotes.Select(n => n.Text).ToArray();

    private void ShowCat(ScheduleSlot? previous, ScheduleSlot? current, ScheduleSlot? next, string minutes, DateTime now)
    {
        string Line(ScheduleSlot slot) => slot.Start.ToString("HH:mm", Korean) + " " + slot.Title;
        var notes = new List<CatNote>();
        if (previous != null) notes.Add(new(Line(previous), Cat.Butter, true, false, ""));
        if (current != null) notes.Add(new(Line(current), current.IsEnding(now) ? Cat.DarkPink : Cat.Pink, false, true, current.Memo.Length > 0 ? current.Memo : minutes + "분 남음"));
        else notes.Add(new("지금 진행 중인 일정이 없어요", Cat.CreamLight, false, false, ""));
        if (next != null) notes.Add(new(Line(next), Cat.Mint, false, false, ""));
        catNotes = notes;
        AutomationProperties.SetName(this, "오늘 일정: " + string.Join(", ", notes.Select(n => (n.Current ? "지금 " : n.Past ? "지난 일정 " : "") + n.Text)));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (WidgetTheme.IsCatHud) DrawCork(dc, ActualWidth, ActualHeight);
    }

    private void DrawCork(DrawingContext dc, double w, double h)
    {
        if (w < 40 || h < 40) return;
        double frame = Math.Clamp(Math.Min(w, h) * .045, 5, 10), t = Cat.Seconds;
        var outer = new Rect(2, 2, w - 8, h - 8);
        Cat.Plate(dc, outer, 10, Cat.WoodMid, 3);
        var cork = new Rect(outer.X + frame, outer.Y + frame, Math.Max(0, outer.Width - frame * 2), Math.Max(0, outer.Height - frame * 2));
        dc.DrawRoundedRectangle(Cat.Cork, Cat.Line(1.6), cork, 5, 5);
        dc.PushClip(new RectangleGeometry(cork));
        for (double y = cork.Y + 6, row = 0; y < cork.Bottom; y += 11, row++)
            for (double x = cork.X + 5 + row % 2 * 5.5; x < cork.Right; x += 11)
                dc.DrawEllipse(Cat.CorkDot, null, new Point(x + Math.Sin(x * 7.3 + y) * 2, y + Math.Cos(x * 3.1 - y) * 2), .9, .9);
        dc.Pop();
        double tagSize = Math.Clamp(h * .07, 10, 16), pad = Math.Clamp(cork.Width * .03, 6, 14);
        var tag = Cat.Tag(dc, this, "오늘 할 일", new Point(cork.X + pad * .7, cork.Y + pad * .5), tagSize);
        double top = tag.Bottom + pad * .7, bottom = cork.Bottom - pad * .6, gap = Math.Clamp(h * .03, 4, 10);
        var notes = catNotes.ToList();
        // A short board keeps the current note first, then the next one.
        while (notes.Count > 1 && (bottom - top - gap * (notes.Count - 1)) / notes.Count < 22) notes.RemoveAt(notes.FindIndex(n => n.Past) is >= 0 and var past ? past : notes.Count - 1);
        if (notes.Count == 0 || bottom - top < 14) return;
        double noteH = Math.Min(54, (bottom - top - gap * (notes.Count - 1)) / notes.Count), font = Math.Clamp(noteH * .44, 9, 20);
        double y0 = top + Math.Max(0, (bottom - top - (noteH * notes.Count + gap * (notes.Count - 1))) / 2);
        double[] tilt = [-.8, .6, -.5];
        for (int i = 0; i < notes.Count; i++) {
            var note = notes[i];
            var box = new Rect(cork.X + pad, y0 + i * (noteH + gap), Math.Max(10, cork.Width - pad * 2), noteH);
            double angle = tilt[i % 3] + (note.Current ? 1.3 * Math.Sin(t * 2.6) : 0);
            dc.PushTransform(new RotateTransform(angle, box.X + box.Width / 2, box.Y));
            Cat.Plate(dc, box, 3, note.Paper, 2.4);
            var text = Cat.Text(this, note.Text, font);
            double textX = box.X + font * .7, textY = box.Y + (noteH - text.Height) / 2;
            text.MaxTextWidth = Math.Max(1, box.Width - font * (note.Current ? 4.6 : 3)); text.MaxLineCount = 1; text.Trimming = TextTrimming.CharacterEllipsis;
            dc.PushOpacity(note.Past ? .55 : 1); dc.DrawText(text, new Point(textX, textY)); dc.Pop();
            if (note.Past) {
                double end = Math.Min(textX + text.Width + font * .3, box.Right - font * 2.2);
                dc.PushTransform(new RotateTransform(-1.2, textX, box.Y + noteH / 2));
                dc.DrawLine(Cat.Line(Math.Max(1.5, font * .16)), new Point(textX - font * .2, box.Y + noteH / 2), new Point(end, box.Y + noteH / 2));
                dc.Pop();
                double paw = Math.Min(noteH * .7, font * 1.45);
                dc.PushTransform(new RotateTransform(-25, box.Right - paw * .9, box.Y + noteH / 2));
                dc.PushOpacity(.9); Cat.Paw(dc, new Rect(box.Right - paw * 1.4, box.Y + (noteH - paw) / 2, paw, paw)); dc.Pop();
                dc.Pop();
            }
            if (note.Current) {
                var chip = Cat.Text(this, "지금", font * .78);
                double chipX = Math.Min(textX + text.Width + font * .35, box.Right - chip.Width - font);
                dc.DrawRoundedRectangle(Cat.CreamLight, null, new Rect(chipX, box.Y + (noteH - chip.Height) / 2 - 1, chip.Width + font * .5, chip.Height + 2), font * .35, font * .35);
                dc.DrawText(chip, new Point(chipX + font * .25, box.Y + (noteH - chip.Height) / 2));
                var detail = Cat.Text(this, note.Detail, font * .7, Cat.Outline);
                double detailX = box.Right - detail.Width - font * .6;
                if (detailX > chipX + chip.Width + font) dc.DrawText(detail, new Point(detailX, box.Y + (noteH - detail.Height) / 2));
                var clip = new RectangleGeometry(new Rect(box.X - font * .3, box.Y - font * .55, font * .6, font * 1.3), font * .3, font * .3);
                dc.PushTransform(new RotateTransform(-12, box.X, box.Y));
                Cat.Shape(dc, clip, Cat.Mint, Math.Max(1.2, font * .15));
                dc.Pop();
            }
            else dc.DrawEllipse(Cat.DarkPink, Cat.Line(Math.Max(1.2, font * .12)), new Point(box.X + box.Width / 2, box.Y + 1), font * .35, font * .35);
            dc.Pop();
        }
    }

    public void Dispose() { timer.Stop(); records.Changed -= OnChanged; }
}
