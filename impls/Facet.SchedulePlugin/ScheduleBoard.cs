using Facet.Widgets;
using System.Globalization;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Facet.Plugins;

namespace Facet.SchedulePlugin;

// The calendar widget: month calendar and day page in one tile, plus the schedule editor on its own overlay layer.
// Moved from the app's OverlayTabs with the same names and behavior (ADR 0101).
internal sealed class ScheduleBoard : Grid, IWidgetInput, IDisposable
{
    private readonly ScheduleRecords records;
    private readonly IWidgetLayer layer;
    private readonly NameScope names = new();
    private bool dayOpen, dismissingScheduleClick, disposed;
    private int transitionVersion;
    internal bool AnimateDayTransitions { get; set; } = true;

    internal readonly OverlayCalendarView Calendar = new();
    internal readonly OverlayDayEditor DayEditor = new();
    internal readonly OverlayScheduleDial ScheduleDial = new();
    internal readonly OverlayScheduleEditor ScheduleEditor = new();
    internal readonly Viewbox CalendarHost, DayViewport;
    internal readonly Grid DayPage, ScheduleModal, ScheduleTimeModal, ScheduleTimeHost;
    internal readonly Canvas ScheduleTimeCanvas = new();
    internal readonly TextBlock DayHeading = new() { FontSize = 28, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    internal readonly Button BackToCalendar;
    private readonly ScrollViewer ScheduleLegendScroll;
    private readonly StackPanel ScheduleLegend = new();

    internal bool IsEditingDay => dayOpen;
    internal ScheduleRecords Records => records;
    private bool IsCalendarActive => !dayOpen;

    internal ScheduleBoard(ScheduleRecords records, IWidgetLayer layer)
    {
        this.records = records; this.layer = layer;
        NameScope.SetNameScope(this, names);
        ClipToBounds = true;
        AutomationProperties.SetName(this, "일정 달력 화면");
        var shadow = new DropShadowEffect { Color = Colors.Black, Opacity = 0.42, BlurRadius = 24, ShadowDepth = 6, Direction = 270 };
        shadow.Freeze();
        CachedShadow Shadow(double radius) { var s = new CachedShadow { CornerRadius = new(radius), Shadow = shadow, IsHitTestVisible = false }; s.SetResourceReference(BackgroundProperty, "Panel"); return s; }

        // Month calendar.
        var calendarGrid = new Grid(); calendarGrid.Children.Add(Shadow(20)); calendarGrid.Children.Add(Calendar);
        CalendarHost = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = calendarGrid };
        var calendarLayout = new Grid { Margin = new(24), MaxWidth = 1220 }; calendarLayout.Children.Add(CalendarHost);
        Children.Add(calendarLayout);

        // Day page.
        BackToCalendar = new Button { Width = 40, Height = 40, Padding = new(8), Margin = new(0, 0, 16, 0), Focusable = false, IsTabStop = false, VerticalAlignment = VerticalAlignment.Center,
            ContentTemplate = null, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "뒤로 가기 (Esc)", Content = new Icon { Kind = IconKind.Left, Width = 20, Height = 20 } };
        AutomationProperties.SetName(BackToCalendar, "뒤로 가기"); AutomationProperties.SetAcceleratorKey(BackToCalendar, "Esc");
        DockPanel.SetDock(BackToCalendar, Dock.Left);
        BackToCalendar.Click += (_, _) => ReturnToCalendar();
        var header = new DockPanel { Margin = new(12, 8, 0, 16) }; header.Children.Add(BackToCalendar); header.Children.Add(DayHeading);
        ScheduleLegendScroll = new ScrollViewer { Width = 180, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(12, 20, 0, 20),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = ScheduleLegend };
        var dialArea = new Grid(); dialArea.Children.Add(ScheduleDial); dialArea.Children.Add(ScheduleLegendScroll); Grid.SetRow(dialArea, 1);
        var left = new Grid(); left.RowDefinitions.Add(new() { Height = GridLength.Auto }); left.RowDefinitions.Add(new());
        left.Children.Add(header); left.Children.Add(dialArea);
        var right = new Grid(); right.Children.Add(Shadow(20)); right.Children.Add(DayEditor); Grid.SetColumn(right, 2);
        DayPage = new Grid { Visibility = Visibility.Collapsed, Focusable = true, FocusVisualStyle = null };
        KeyboardNavigation.SetIsTabStop(DayPage, false);
        foreach (double width in new double[] { 1140, 24, 396 }) DayPage.ColumnDefinitions.Add(new() { Width = new GridLength(width) });
        DayPage.Children.Add(left); DayPage.Children.Add(right);
        var dayFrame = new Grid { Width = 1560, Height = 752 }; dayFrame.Children.Add(DayPage);
        DayViewport = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = dayFrame };
        DayViewport.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(Visibility)) { Source = DayPage });
        Children.Add(DayViewport);

        // Schedule editor layer over the whole overlay.
        var editorShadow = Shadow(20); editorShadow.HorizontalAlignment = HorizontalAlignment.Center; editorShadow.VerticalAlignment = VerticalAlignment.Center;
        editorShadow.SetBinding(WidthProperty, new System.Windows.Data.Binding(nameof(ActualWidth)) { Source = ScheduleEditor });
        editorShadow.SetBinding(HeightProperty, new System.Windows.Data.Binding(nameof(ActualHeight)) { Source = ScheduleEditor });
        ScheduleEditor.HorizontalAlignment = HorizontalAlignment.Center; ScheduleEditor.VerticalAlignment = VerticalAlignment.Center;
        var editorFrame = new Grid { Width = 1560, Height = 752 }; editorFrame.Children.Add(editorShadow); editorFrame.Children.Add(ScheduleEditor);
        ScheduleTimeHost = new Grid(); ScheduleTimeHost.Children.Add(Shadow(12));
        ScheduleTimeCanvas.Children.Add(ScheduleTimeHost);
        ScheduleTimeModal = new Grid { Background = Brushes.Transparent, Visibility = Visibility.Collapsed }; Panel.SetZIndex(ScheduleTimeModal, 10);
        ScheduleTimeModal.Children.Add(ScheduleTimeCanvas);
        ScheduleModal = new Grid { Visibility = Visibility.Collapsed };
        ScheduleModal.SetResourceReference(BackgroundProperty, "ModalBackdrop");
        ScheduleModal.Children.Add(new Viewbox { Margin = new(56, 116, 56, 116), Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = editorFrame });
        ScheduleModal.Children.Add(ScheduleTimeModal);
        layer.Add(ScheduleModal);

        foreach (var (name, element) in new (string, object)[] { ("CalendarWidgetView", this), ("CalendarHost", CalendarHost), ("Calendar", Calendar), ("DayViewport", DayViewport), ("DayPage", DayPage),
            ("BackToCalendar", BackToCalendar), ("DayHeading", DayHeading), ("ScheduleDial", ScheduleDial), ("ScheduleLegendScroll", ScheduleLegendScroll), ("ScheduleLegend", ScheduleLegend),
            ("DayEditor", DayEditor), ("ScheduleModal", ScheduleModal), ("ScheduleEditor", ScheduleEditor), ("ScheduleTimeModal", ScheduleTimeModal), ("ScheduleTimeCanvas", ScheduleTimeCanvas), ("ScheduleTimeHost", ScheduleTimeHost) })
            names.RegisterName(name, element);

        ScheduleLegendScroll.SizeChanged += (_, _) => UpdateScheduleLegendBounds();
        ScheduleDial.SizeChanged += (_, _) => UpdateScheduleLegendBounds();
        ScheduleTimeHost.Children.Add(ScheduleEditor.TimePicker);
        ScheduleTimeHost.Children.Add(ScheduleEditor.PresetPicker);
        ScheduleTimeHost.Children.Add(ScheduleEditor.PresetDialog);
        ScheduleEditor.TimePickerOpened += () => { ScheduleTimeModal.Visibility = Visibility.Visible; PositionTimePicker(); ScheduleTimeModal.UpdateLayout(); };
        ScheduleEditor.TimePickerClosed += () => ScheduleTimeModal.Visibility = Visibility.Collapsed;
        ScheduleEditor.PresetOpened += () => { ScheduleTimeModal.Visibility = Visibility.Visible; PositionTimePicker(); ScheduleTimeModal.UpdateLayout(); };
        ScheduleEditor.PresetClosed += () => ScheduleTimeModal.Visibility = Visibility.Collapsed;
        ScheduleTimeModal.MouseDown += (_, e) => { if (ReferenceEquals(e.OriginalSource, ScheduleTimeModal)) { e.Handled = true; if (ScheduleEditor.IsPresetOpen) ScheduleEditor.ClosePresetPicker(); else ScheduleEditor.CloseTimePicker(); } };
        ScheduleModal.MouseDown += (_, e) => {
            if (e.ChangedButton != MouseButton.Left || !ReferenceEquals(e.OriginalSource, ScheduleModal)) return;
            e.Handled = true; dismissingScheduleClick = ScheduleModal.CaptureMouse(); ScheduleEditor.Submit();
        };
        ScheduleModal.PreviewMouseUp += (_, e) => { if (dismissingScheduleClick && e.ChangedButton == MouseButton.Left) { e.Handled = true; dismissingScheduleClick = false; ScheduleModal.ReleaseMouseCapture(); } };
        ScheduleModal.LostMouseCapture += (_, e) => { if (ReferenceEquals(e.OriginalSource, ScheduleModal)) dismissingScheduleClick = false; };
        ScheduleModal.SizeChanged += (_, _) => PositionTimePicker();
        ScheduleEditor.TimeRangeButton.SizeChanged += (_, _) => PositionTimePicker();
        ScheduleEditor.CategoryButton.SizeChanged += (_, _) => PositionTimePicker();
        ScheduleEditor.PresetDialog.SizeChanged += (_, _) => PositionTimePicker();
        IsVisibleChanged += (_, _) => { if (IsVisible) RefreshToday(); else { ScheduleEditor.CloseTimePicker(); ScheduleEditor.ClosePresetPicker(); } };
        Calendar.DateSelected += OpenDay;
        DayEditor.DayChanged += RefreshSchedules;
        ScheduleDial.RangeSelected += (start, duration) => ShowSchedule(new() { StartMinute = (start % 1440 + 1440) % 1440, StartDayOffset = (int)Math.Floor(start / 1440d), DurationMinutes = duration }, false);
        ScheduleDial.EditRequested += item => ShowSchedule(item, true);
        ScheduleDial.InteractionStarted += () => FocusElement(DayPage);
        ScheduleDial.TimeChangeRequested += item => {
            if (DayEditor.SaveSchedule(item, false)) RefreshToday();
            else { ShowSchedule(item, true); ScheduleEditor.ShowSaveError("시간 변경을 저장하지 못했습니다. 입력을 유지했습니다. 다시 확인해 주세요."); }
        };
        ScheduleEditor.Closed += () => { ScheduleModal.Visibility = Visibility.Collapsed; FocusElement(DayPage); };
        PreviewKeyDown += (_, e) => {
            if (e.Key != Key.Tab || Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Shift)) return;
            e.Handled = true; MoveOverlayFocus(Keyboard.Modifiers == ModifierKeys.Shift, e.OriginalSource as DependencyObject);
        };

        ConnectEditor();
        ScheduleEditor.ReadPresets = () => CalendarNotes.SchedulePresets(records.Data);
        ScheduleEditor.WritePreset = (preset, remove) => {
            try { CalendarNotes.SaveSchedulePreset(new AppData { CalendarDays = records.Data.CalendarDays, SchedulePresets = records.Data.SchedulePresets?.ToList() }, preset, remove); }
            catch (ArgumentException ex) { ScheduleEditor.PresetDialog.ShowError(ex.Message); return false; }
            return Commit(data => CalendarNotes.SaveSchedulePreset(data, preset, remove));
        };
        records.Changed += OnRecordsChanged;
        var today = DateOnly.FromDateTime(DateTime.Now); Calendar.SetSelectedDate(today); DayEditor.Open(today);
        RefreshToday();
    }

    // The day editor reads and saves through this widget's records.
    internal void ConnectEditor() =>
        DayEditor.Configure(date => CalendarNotes.Get(records.Data, date), entry => Commit(data => CalendarNotes.Save(data, entry, DateTime.Now)),
            date => CalendarNotes.SchedulesForDate(records.Data, date),
            (entry, item, remove) => Commit(data => CalendarNotes.SaveSchedule(data, entry, item, remove, DateTime.Now)));

    private bool saving;
    // This view's own saves do not reload the day they came from.
    private bool Commit(Action<AppData> change)
    {
        saving = true;
        try { return records.Commit(change); }
        finally { saving = false; }
    }

    // An agent tool or another view changed the records; the open day reloads unless the user is typing in it.
    private void OnRecordsChanged()
    {
        RefreshToday();
        if (saving) return;
        if (!DayEditor.IsKeyboardFocusWithin && ScheduleModal.Visibility != Visibility.Visible) DayEditor.Open(DayEditor.CurrentDate);
    }

    private void PositionTimePicker()
    {
        if ((!ScheduleEditor.IsTimePickerOpen && !ScheduleEditor.IsPresetOpen) || ScheduleModal.ActualWidth <= 0 || ScheduleModal.ActualHeight <= 0) return;
        FrameworkElement target = ScheduleEditor.IsPresetOpen ? ScheduleEditor.CategoryButton : ScheduleEditor.TimeRangeButton;
        var anchor = target.TransformToAncestor(ScheduleModal).TransformBounds(new Rect(target.RenderSize));
        double top = anchor.Bottom + 6, width = Math.Min(Math.Max(280, anchor.Width), Math.Min(436, Math.Max(1, ScheduleModal.ActualWidth - 24)));
        FrameworkElement picker = ScheduleEditor.IsPresetDialogOpen ? ScheduleEditor.PresetDialog : ScheduleEditor.IsPresetPickerOpen ? ScheduleEditor.PresetPicker : ScheduleEditor.TimePicker;
        picker.Width = width; picker.MaxHeight = Math.Min(400, Math.Max(1, ScheduleModal.ActualHeight - top - 12));
        Canvas.SetLeft(ScheduleTimeHost, Math.Clamp(anchor.Left, 12, Math.Max(12, ScheduleModal.ActualWidth - width - 12))); Canvas.SetTop(ScheduleTimeHost, top);
    }

    internal void OpenDay(DateOnly date)
    {
        if (!DayEditor.Open(date)) return;
        Calendar.SetSelectedDate(date);
        DayHeading.Text = date.ToString("yyyy년 M월 d일 dddd", CultureInfo.GetCultureInfo("ko-KR"));
        if (!dayOpen) { dayOpen = true; TransitionDay(true); }
        RefreshToday();
        if (IsEditingDay) FocusElement(DayPage);
    }

    internal bool ReturnToCalendar()
    {
        if (!dayOpen || ScheduleModal.Visibility == Visibility.Visible) return false;
        if (!DayEditor.TrySave()) return false;
        DayEditor.FinishNoteEdit(restoreFocus: false); ScheduleDial.CancelSelection();
        dayOpen = false; RefreshToday(); TransitionDay(false); return true;
    }

    public bool HandleEscape()
    {
        if (!IsEditingDay) return false;
        if (ScheduleModal.Visibility == Visibility.Visible) { ScheduleEditor.HandleEscape(); return true; }
        if (DayEditor.IsNoteEditing) { DayEditor.FinishNoteEdit(); return true; }
        if (ScheduleDial.IsSelecting) { ScheduleDial.CancelSelection(); return true; }
        ReturnToCalendar(); return true;
    }

    // Ctrl+S, hiding the overlay and switching tabs: the open editor submits, the day page saves its draft.
    public bool TrySave()
    {
        if (ScheduleModal.Visibility == Visibility.Visible) return ScheduleEditor.Submit();
        return DayEditor.TrySave();
    }

    public bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.None) return false;
        if (key == Key.Home) return GoToCalendarToday();
        if (key is Key.PageUp or Key.PageDown) return MoveCalendarMonth(key == Key.PageUp ? -1 : 1);
        return false;
    }

    internal bool FinishNoteEdit() => IsEditingDay && DayEditor.FinishNoteEdit();

    internal void ShowSchedule(DaySchedule item, bool existing)
    {
        if (!dayOpen) return;
        var color = (TryFindResource("Bg") as SolidColorBrush)?.Color ?? Colors.Black;
        color.A = 184;
        var backdrop = new SolidColorBrush(color); backdrop.Freeze(); ScheduleModal.Background = backdrop;
        ScheduleModal.Visibility = Visibility.Visible;
        ScheduleEditor.Open(item, existing, (value, remove) => { bool saved = DayEditor.SaveSchedule(value, remove); if (saved) RefreshToday(); return saved; }, DayEditor.CurrentDate);
    }

    private void RefreshSchedules()
    {
        var items = DayEditor.Schedules; ScheduleDial.ViewDate = DayEditor.CurrentDate; ScheduleDial.SetSchedules(items); ScheduleLegend.Children.Clear();
        foreach (var pair in items.Select(s => (s.Color, s.Label)).Distinct()) {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 6) };
            row.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new(3), Margin = new(0, 0, 8, 0), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Color)) });
            row.Children.Add(new TextBlock { Text = pair.Label, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 140 }); ScheduleLegend.Children.Add(row);
        }
    }

    private void UpdateScheduleLegendBounds()
    {
        var bounds = new Rect(ScheduleLegendScroll.TranslatePoint(new Point(), ScheduleDial), ScheduleLegendScroll.RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0) bounds = Rect.Empty;
        else bounds.Inflate(12, 12);
        ScheduleDial.SetLegendBounds(bounds);
    }

    private void TransitionDay(bool opening)
    {
        int version = ++transitionVersion;
        FrameworkElement incoming = opening ? DayPage : CalendarHost, outgoing = opening ? CalendarHost : DayPage;
        double incomingOpacity = incoming.Visibility == Visibility.Visible ? incoming.Opacity : 0, outgoingOpacity = outgoing.Opacity;
        double incomingX = incoming.Visibility == Visibility.Visible && incoming.RenderTransform is TranslateTransform currentIn ? currentIn.X : opening ? 80 : -80;
        double outgoingX = outgoing.RenderTransform is TranslateTransform currentOut ? currentOut.X : 0;
        foreach (var view in new FrameworkElement[] { incoming, outgoing }) {
            view.BeginAnimation(OpacityProperty, null); view.Visibility = Visibility.Visible;
            if (view.RenderTransform is not TranslateTransform) view.RenderTransform = new TranslateTransform();
            ((TranslateTransform)view.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null);
        }
        incoming.IsHitTestVisible = true; outgoing.IsHitTestVisible = false;
        KeyboardNavigation.SetTabNavigation(outgoing, KeyboardNavigationMode.None);
        KeyboardNavigation.SetTabNavigation(incoming, KeyboardNavigationMode.Continue);
        incoming.Opacity = 1; outgoing.Opacity = 0;
        var incomingMove = (TranslateTransform)incoming.RenderTransform; var outgoingMove = (TranslateTransform)outgoing.RenderTransform;
        incomingMove.X = outgoingMove.X = 0;
        void Complete() {
            if (version != transitionVersion) return;
            outgoing.Visibility = Visibility.Collapsed;
            if (IsVisible) FocusElement(opening ? DayPage : Calendar.SelectedDateFocusTarget());
        }
        if (!AnimateDayTransitions) { Complete(); return; }
        var duration = TimeSpan.FromMilliseconds(240); var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(incomingOpacity, 1, duration) { FillBehavior = FillBehavior.Stop });
        var fade = new DoubleAnimation(outgoingOpacity, 0, duration) { FillBehavior = FillBehavior.Stop }; fade.Completed += (_, _) => Complete(); outgoing.BeginAnimation(OpacityProperty, fade);
        incomingMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(incomingX, 0, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
        outgoingMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(outgoingX, opening ? -80 : 80, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
    }

    internal void RefreshToday()
    {
        Calendar.Refresh(DateOnly.FromDateTime(DateTime.Now));
        Calendar.SetRecordedDateLookup(date => CalendarNotes.HasContentOnDate(records.Data, date));
    }

    internal bool MoveCalendarMonth(int direction)
    {
        if (!IsCalendarActive || IsTyping) return false;
        Calendar.MoveMonth(direction); return true;
    }

    internal bool GoToCalendarToday()
    {
        if (!IsCalendarActive || IsTyping) return false;
        Calendar.GoToToday(); return true;
    }

    private bool IsTyping => DayEditor.IsKeyboardFocusWithin && Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;

    // The tab header that holds this widget, so Tab cycles between it and the widget as before (ADR 0101).
    private TabItem? HostTab()
    {
        for (DependencyObject? node = this; node != null; node = LogicalTreeHelper.GetParent(node) ?? (node is Visual visual ? VisualTreeHelper.GetParent(visual) : null))
            if (node is TabItem tab) return tab;
        return null;
    }

    // Tab cycles through the tab header and the calendar, or the header and the day page's own stops.
    internal bool MoveOverlayFocus(bool backwards, DependencyObject? current)
    {
        if (ReferenceEquals(current, DayEditor.TodoInput) && !string.IsNullOrWhiteSpace(DayEditor.TodoInput.Text) && !DayEditor.TrySave()) return false;
        var header = HostTab() is { } tab ? new FrameworkElement[] { tab } : [];
        FrameworkElement[] stops = header.Concat(dayOpen ? DayEditor.TabStops : [Calendar]).ToArray();
        if (stops.Length == 0) return false;
        if (dayOpen && ReferenceEquals(current, DayPage)) { FocusElement(stops[backwards || stops.Length == 1 ? 0 : header.Length]); return true; }
        int index = Array.FindIndex(stops, stop => ReferenceEquals(stop, current) || current is Visual visual && stop.IsAncestorOf(visual));
        int next = index < 0 ? (backwards ? stops.Length - 1 : 0) : (index + (backwards ? -1 : 1) + stops.Length) % stops.Length;
        FocusElement(ReferenceEquals(stops[next], Calendar) ? Calendar.SelectedDateFocusTarget() : stops[next]);
        return true;
    }

    private static void FocusElement(FrameworkElement target)
    {
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(target), target); target.Focus();
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        records.Changed -= OnRecordsChanged;
        layer.Remove(ScheduleModal);
    }
}
