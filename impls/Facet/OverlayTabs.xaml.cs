using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Documents;
using System.Threading.Tasks;
using System.Globalization;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Facet;

public partial class OverlayTabs : UserControl
{
    private Controller? app;
    private LayoutInspector? inspector;
    internal bool DebugEnabled => inspector != null;
    internal LayoutInspector? Inspector => inspector;
    internal Task RefreshWeatherOnLaunchAsync() => Dashboard.RefreshWeatherOnLaunchAsync();
    internal void StopWeatherUpdates() => Dashboard.PauseUpdates(cancelStartup: true);
    internal void StopWidgets() { modals.CloseAll(); DisposeWidgets(); }
    // Releases this instance before a replacement takes over; playback and sessions keep running in the Controller.
    internal void Retire() { StopWeatherUpdates(); StopWidgets(); }
    internal double SettingsScrollOffset {
        get => SettingsView.ScrollOffset;
        set => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => SettingsView.ScrollOffset = value));
    }
    internal void ReloadWeatherLocation() => Dashboard.ReloadWeatherLocation();
    private const double WidgetBarInset = 92;
    private bool dayOpen;
    private bool dismissingScheduleClick;
    private int transitionVersion;
    internal bool AnimateDayTransitions { get; set; } = true;
    public bool IsEditingDay => IsCalendarTabSelected && ManagementCarousel.SelectedIndex == 0 && dayOpen;
    internal bool IsSettingsActive => SettingsTab.IsSelected;
    private bool IsCalendarActive => IsCalendarTabSelected && ManagementCarousel.SelectedIndex == 0 && !dayOpen;
    internal bool IsCarouselMoving => IsCalendarTabSelected && ManagementCarousel.IsMotionActive;
    internal bool IsWidgetModalOpen => modals.IsOpen;
    public OverlayTabs()
    {
        InitializeComponent();
        modals=new(WidgetModal,WidgetModalFrame);
        InitializeWorkspace();
        ScheduleLegendScroll.SizeChanged+=(_,_)=>UpdateScheduleLegendBounds();
        ScheduleDial.SizeChanged+=(_,_)=>UpdateScheduleLegendBounds();
        SizeChanged += (_, _) => {
            foreach (var item in Tabs.Items.OfType<TabItem>()) item.Padding = new Thickness(ActualWidth < 600 ? 10 : 20, 12, ActualWidth < 600 ? 10 : 20, 12);
        };
        ScheduleTimeHost.Children.Add(ScheduleEditor.TimePicker);
        ScheduleTimeHost.Children.Add(ScheduleEditor.PresetPicker);
        ScheduleTimeHost.Children.Add(ScheduleEditor.PresetDialog);
        ScheduleEditor.TimePickerOpened+=()=>{ScheduleTimeModal.Visibility=Visibility.Visible;PositionTimePicker();ScheduleTimeModal.UpdateLayout();};
        ScheduleEditor.TimePickerClosed+=()=>ScheduleTimeModal.Visibility=Visibility.Collapsed;
        ScheduleEditor.PresetOpened+=()=>{ScheduleTimeModal.Visibility=Visibility.Visible;PositionTimePicker();ScheduleTimeModal.UpdateLayout();};
        ScheduleEditor.PresetClosed+=()=>ScheduleTimeModal.Visibility=Visibility.Collapsed;
        ScheduleTimeModal.MouseDown+=(_,e)=>{if(ReferenceEquals(e.OriginalSource,ScheduleTimeModal)){e.Handled=true;if(ScheduleEditor.IsPresetOpen)ScheduleEditor.ClosePresetPicker();else ScheduleEditor.CloseTimePicker();}};
        ScheduleModal.MouseDown+=(_,e)=>{
            if(e.ChangedButton!=MouseButton.Left || !ReferenceEquals(e.OriginalSource,ScheduleModal))return;
            e.Handled=true;dismissingScheduleClick=CaptureMouse();ScheduleEditor.Submit();
        };
        PreviewMouseUp+=(_,e)=>{if(dismissingScheduleClick && e.ChangedButton==MouseButton.Left){e.Handled=true;dismissingScheduleClick=false;ReleaseMouseCapture();}};
        LostMouseCapture+=(_,e)=>{if(ReferenceEquals(e.OriginalSource,this))dismissingScheduleClick=false;};
        ScheduleModal.SizeChanged+=(_,_)=>PositionTimePicker();
        ScheduleEditor.TimeRangeButton.SizeChanged+=(_,_)=>PositionTimePicker();
        ScheduleEditor.CategoryButton.SizeChanged+=(_,_)=>PositionTimePicker();
        ScheduleEditor.PresetDialog.SizeChanged+=(_,_)=>PositionTimePicker();
        IsVisibleChanged+=(_,_)=>{if(!IsVisible){ScheduleEditor.CloseTimePicker();ScheduleEditor.ClosePresetPicker();modals.TryClose();SetWorkspaceEditing(false);}};
        Tabs.ItemContainerGenerator.ItemsChanged += (_, _) => UpdateTabCorners();
        Tabs.ItemContainerGenerator.StatusChanged += (_, _) => UpdateTabCorners();
        UpdateTabCorners();
        Tabs.SelectionChanged += (_,e) => {
            if (!ReferenceEquals(e.OriginalSource,Tabs)) return;
            modals.TryClose();
            UpdateWorkspaceBar();
            FocusSettingsSearch();
        };
        Calendar.DateSelected += OpenDay;
        DayEditor.DayChanged += RefreshSchedules;
        ScheduleDial.RangeSelected += (start, duration) => ShowSchedule(new() { StartMinute=(start%1440+1440)%1440, StartDayOffset=(int)Math.Floor(start/1440d), DurationMinutes=duration },false);
        ScheduleDial.EditRequested += item => ShowSchedule(item,true);
        ScheduleDial.InteractionStarted+=()=>FocusElement(DayPage);
        ScheduleDial.TimeChangeRequested+=item=>{
            if(DayEditor.SaveSchedule(item,false))RefreshToday();
            else {ShowSchedule(item,true);ScheduleEditor.ShowSaveError("시간 변경을 저장하지 못했습니다. 입력을 유지했습니다. 다시 확인해 주세요.");}
        };
        ScheduleEditor.Closed += () => { ScheduleModal.Visibility=Visibility.Collapsed; FocusElement(DayPage); };
        ManagementCarousel.BeforeNavigate = () => {
            if (!DayEditor.TrySave()) return false;
            DayEditor.FinishNoteEdit(restoreFocus: false); return true;
        };
        ManagementCarousel.NavigationCompleted += () => {
            if (IsCalendarTabSelected) FocusElement(ManagementCarousel.SelectedIndex == 0 ? (dayOpen ? DayPage : Calendar.SelectedDateFocusTarget()) : ManagementCarousel.SelectedContent);
        };
        PreviewKeyDown += (_, e) => {
            if (!IsCalendarTabSelected || modals.IsOpen || ScheduleModal.Visibility==Visibility.Visible || e.Key != Key.Tab || Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Shift)) return;
            e.Handled = true; MoveOverlayFocus(Keyboard.Modifiers == ModifierKeys.Shift,e.OriginalSource as DependencyObject);
        };
        var today=DateOnly.FromDateTime(DateTime.Now); Calendar.SetSelectedDate(today); DayEditor.Open(today);
    }

    private void TabStrip_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DashboardViewport == null || sender is not FrameworkElement strip) return;
        WorkspaceRow.Height = strip.ActualHeight; // Keeps the edit controls centered on the tab row.
        double inset = strip.Margin.Top + strip.ActualHeight + 48;
        SettingsViewport.Margin = new Thickness(24, inset, 24, WidgetBarInset);
        // The chat composer ends above the widget bar.
    }

    private void PositionTimePicker()
    {
        if((!ScheduleEditor.IsTimePickerOpen && !ScheduleEditor.IsPresetOpen) || ScheduleModal.ActualWidth<=0 || ScheduleModal.ActualHeight<=0)return;
        FrameworkElement target=ScheduleEditor.IsPresetOpen?ScheduleEditor.CategoryButton:ScheduleEditor.TimeRangeButton;
        var anchor=target.TransformToAncestor(ScheduleModal).TransformBounds(new Rect(target.RenderSize));
        double top=anchor.Bottom+6,width=Math.Min(Math.Max(280,anchor.Width),Math.Min(436,Math.Max(1,ScheduleModal.ActualWidth-24)));
        FrameworkElement picker=ScheduleEditor.IsPresetDialogOpen?ScheduleEditor.PresetDialog:ScheduleEditor.IsPresetPickerOpen?ScheduleEditor.PresetPicker:ScheduleEditor.TimePicker;
        picker.Width=width;picker.MaxHeight=Math.Min(400,Math.Max(1,ScheduleModal.ActualHeight-top-12));
        Canvas.SetLeft(ScheduleTimeHost,Math.Clamp(anchor.Left,12,Math.Max(12,ScheduleModal.ActualWidth-width-12)));Canvas.SetTop(ScheduleTimeHost,top);
    }

    private void UpdateTabCorners()
    {
        double radius = Design.Skins.Visuals?.TabCornerRadius ?? Design.ThemeVisuals.DefaultTabCornerRadius;
        var visible = Tabs.Items.OfType<TabItem>().Where(item => item.Visibility == Visibility.Visible).ToArray();
        for (int index = 0; index < visible.Length; index++)
            visible[index].Tag = new CornerRadius(index == 0 ? radius : 0, index == visible.Length - 1 ? radius : 0,
                index == visible.Length - 1 ? radius : 0, index == 0 ? radius : 0);
        Tabs.ApplyTemplate();
        if (Tabs.Template.FindName("TabShadow", Tabs) is CachedShadow shadow) shadow.CornerRadius = new(radius);
        if (Tabs.Template.FindName("TabMarks", Tabs) is Border marks) Design.ThemePanels.Track(marks);
    }

    public void Connect(Controller controller)
    {
        app = controller;
        ConnectPlugins(controller);
        SettingsView.WorkspacePanel.Connect(this);
        SettingsView.PluginPanel.Connect(this);
        SettingsView.PackagesPanel.Connect(this, controller, Path.GetDirectoryName(PluginFolder) ?? "", () => { RefreshSettingsPanels(); SettingsView.ReloadSkins(); });
        ApplyWorkspace();
        Dashboard.Connect(controller);
        SettingsView.Connect(controller);
        DayEditor.Configure(date => CalendarNotes.Get(controller.Data,date), entry => controller.Commit(data => CalendarNotes.Save(data,entry,DateTime.Now)),
            date=>CalendarNotes.SchedulesForDate(controller.Data,date),
            (entry,item,remove)=>controller.Commit(data=>CalendarNotes.SaveSchedule(data,entry,item,remove,DateTime.Now)));
        ScheduleEditor.ReadPresets=()=>CalendarNotes.SchedulePresets(controller.Data);
        ScheduleEditor.WritePreset=(preset,remove)=> {
            try{CalendarNotes.SaveSchedulePreset(new AppData{CalendarDays=controller.Data.CalendarDays,SchedulePresets=controller.Data.SchedulePresets?.ToList()},preset,remove);}
            catch(ArgumentException ex){ScheduleEditor.PresetDialog.ShowError(ex.Message);return false;}
            return controller.Commit(data=>CalendarNotes.SaveSchedulePreset(data,preset,remove));
        };
        RefreshToday();
        DayEditor.Open(Calendar.SelectedDate ?? DateOnly.FromDateTime(DateTime.Now));
    }

    public void OpenDay(DateOnly date)
    {
        if (!DayEditor.Open(date)) return;
        Calendar.SetSelectedDate(date);
        DayHeading.Text=date.ToString("yyyy년 M월 d일 dddd",CultureInfo.GetCultureInfo("ko-KR"));
        if (!dayOpen) {dayOpen=true;ManagementCarousel.NavigationLocked=true;TransitionDay(true);}
        RefreshToday();
        if (IsEditingDay) FocusElement(DayPage);
    }

    internal bool ReturnToCalendar()
    {
        if (!dayOpen || ScheduleModal.Visibility==Visibility.Visible) return false;
        if (!DayEditor.TrySave()) return false;
        DayEditor.FinishNoteEdit(restoreFocus:false);ScheduleDial.CancelSelection();
        dayOpen=false;ManagementCarousel.NavigationLocked=false;RefreshToday();TransitionDay(false);return true;
    }

    public bool HandleEscape()
    {
        if(modals.IsOpen) { modals.HandleEscape();return true; }
        if (HandleWorkspaceEscape()) return true;
        if (!IsEditingDay) return false;
        if (ScheduleModal.Visibility==Visibility.Visible) {ScheduleEditor.HandleEscape();return true;}
        if (DayEditor.IsNoteEditing) {DayEditor.FinishNoteEdit();return true;}
        if (ScheduleDial.IsSelecting) {ScheduleDial.CancelSelection();return true;}
        ReturnToCalendar();return true;
    }

    private void BackToCalendar_Click(object sender,RoutedEventArgs e) => ReturnToCalendar();
    internal void ShowSchedule(DaySchedule item,bool existing)
    {
        if (!dayOpen) return;
        var color=OverlayAppearance.BackgroundColor(app?.Data.Settings ?? new Settings());
        color.A=184;
        var backdrop=new SolidColorBrush(color);backdrop.Freeze();ScheduleModal.Background=backdrop;
        ScheduleModal.Visibility=Visibility.Visible;
        ScheduleEditor.Open(item,existing,(value,remove)=> {bool saved=DayEditor.SaveSchedule(value,remove);if(saved)RefreshToday();return saved;},DayEditor.CurrentDate);
    }

    private void RefreshSchedules()
    {
        var items=DayEditor.Schedules;ScheduleDial.ViewDate=DayEditor.CurrentDate;ScheduleDial.SetSchedules(items);ScheduleLegend.Children.Clear();
        foreach(var pair in items.Select(s=>(s.Color,s.Label)).Distinct()) {
            var row=new StackPanel {Orientation=Orientation.Horizontal,Margin=new(0,0,0,6)};
            row.Children.Add(new Border {Width=10,Height=10,CornerRadius=new(3),Margin=new(0,0,8,0),Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Color))});
            row.Children.Add(new TextBlock {Text=pair.Label,FontSize=12,TextWrapping=TextWrapping.Wrap,MaxWidth=140});ScheduleLegend.Children.Add(row);
        }

    }

    private void UpdateScheduleLegendBounds()
    {
        var bounds=new Rect(ScheduleLegendScroll.TranslatePoint(new Point(),ScheduleDial),ScheduleLegendScroll.RenderSize);
        if(bounds.Width<=0 || bounds.Height<=0)bounds=Rect.Empty;
        else bounds.Inflate(12,12);
        ScheduleDial.SetLegendBounds(bounds);
    }

    private void TransitionDay(bool opening)
    {
        int version=++transitionVersion;
        FrameworkElement incoming=opening?DayPage:CalendarHost, outgoing=opening?CalendarHost:DayPage;
        double incomingOpacity=incoming.Visibility==Visibility.Visible?incoming.Opacity:0, outgoingOpacity=outgoing.Opacity;
        double incomingX=incoming.Visibility==Visibility.Visible && incoming.RenderTransform is TranslateTransform currentIn?currentIn.X:opening?80:-80;
        double outgoingX=outgoing.RenderTransform is TranslateTransform currentOut?currentOut.X:0;
        foreach(var view in new FrameworkElement[]{incoming,outgoing}) {
            view.BeginAnimation(OpacityProperty,null);view.Visibility=Visibility.Visible;
            if(view.RenderTransform is not TranslateTransform)view.RenderTransform=new TranslateTransform();
            ((TranslateTransform)view.RenderTransform).BeginAnimation(TranslateTransform.XProperty,null);
        }
        incoming.IsHitTestVisible=true;outgoing.IsHitTestVisible=false;
        KeyboardNavigation.SetTabNavigation(outgoing,KeyboardNavigationMode.None);
        KeyboardNavigation.SetTabNavigation(incoming,KeyboardNavigationMode.Continue);
        incoming.Opacity=1;outgoing.Opacity=0;
        var incomingMove=(TranslateTransform)incoming.RenderTransform;var outgoingMove=(TranslateTransform)outgoing.RenderTransform;
        incomingMove.X=outgoingMove.X=0;
        void Complete() {
            if(version!=transitionVersion)return;
            outgoing.Visibility=Visibility.Collapsed;
            if(IsCalendarTabSelected)FocusElement(opening?DayPage:Calendar.SelectedDateFocusTarget());
        }
        if(!AnimateDayTransitions){Complete();return;}
        var duration=TimeSpan.FromMilliseconds(240);var easing=new CubicEase {EasingMode=EasingMode.EaseOut};
        incoming.BeginAnimation(OpacityProperty,new DoubleAnimation(incomingOpacity,1,duration){FillBehavior=FillBehavior.Stop});
        var fade=new DoubleAnimation(outgoingOpacity,0,duration){FillBehavior=FillBehavior.Stop};fade.Completed+=(_,_)=>Complete();outgoing.BeginAnimation(OpacityProperty,fade);
        incomingMove.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(incomingX,0,duration){EasingFunction=easing,FillBehavior=FillBehavior.Stop});
        outgoingMove.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(outgoingX,opening?-80:80,duration){EasingFunction=easing,FillBehavior=FillBehavior.Stop});
    }

    public bool TrySaveEdits()
    {
        // A widget modal saves through its own close rule; a failed save keeps it open and stops the caller.
        if (!modals.TryClose()) return false;
        if (!DayEditor.TrySave()) return false;
        if (SettingsView.TrySave()) return true;
        Tabs.SelectedItem = SettingsTab;
        return false;
    }
    internal void OpenSettings() { Tabs.SelectedItem = SettingsTab; SettingsView.FocusSearch(); }
    internal void OpenPluginSettings(string pluginId) { Tabs.SelectedItem = SettingsTab; SettingsView.PluginPanel.Expand(pluginId); }
    // Selects the tab that holds the widget; false when it is not on a visible tab.
    internal bool ShowWidget(string widgetId)
    {
        var tab = WorkspaceLayouts.Current(Workspace).Tabs.FirstOrDefault(t => !t.Hidden && t.Widgets.Any(w => w.WidgetId == widgetId));
        if (tab == null || !workspaces.TryGetValue(tab.Id, out var view) || TabOf(view) is not { } item) return false;
        Tabs.SelectedItem = item; UpdateLayout();
        return true;
    }
    internal bool SaveShortcut() => IsSettingsActive ? SettingsView.TrySave() : ScheduleModal.Visibility==Visibility.Visible ? ScheduleEditor.Submit() : TrySaveEdits();
    public bool FinishNoteEdit() => IsEditingDay && DayEditor.FinishNoteEdit();

    public void RefreshToday()
    {
        Dashboard.RefreshAgenda(DateTimeOffset.Now);
        Calendar.Refresh(DateOnly.FromDateTime(DateTime.Now));
        if (app != null) Calendar.SetRecordedDateLookup(date=>CalendarNotes.HasContentOnDate(app.Data,date));
    }

    internal bool HandleDebugKey(Key key, ModifierKeys modifiers)
    {
        if (key != Key.F8 || modifiers != ModifierKeys.None) return false;
        if (inspector != null) { inspector.Detach(); inspector = null; }
        else if (AdornerLayer.GetAdornerLayer(Tabs) is AdornerLayer layer) inspector = new LayoutInspector(Tabs, layer);
        return true;
    }

    public bool MoveCalendarMonth(int direction)
    {
        if (!IsCalendarActive || IsTyping) return false;
        Calendar.MoveMonth(direction); return true;
    }

    public bool GoToCalendarToday()
    {
        if (!IsCalendarActive || IsTyping) return false;
        Calendar.GoToToday(); return true;
    }

    private bool IsTyping => DayEditor.IsKeyboardFocusWithin && Keyboard.FocusedElement is TextBoxBase;

    internal bool MoveOverlayFocus(bool backwards, DependencyObject? current)
    {
        if (!IsCalendarTabSelected) return false;
        if (ReferenceEquals(current,DayEditor.TodoInput) && !string.IsNullOrWhiteSpace(DayEditor.TodoInput.Text) && !DayEditor.TrySave()) return false;
        FrameworkElement[] stops = ManagementCarousel.SelectedIndex == 0
            ? dayOpen ? new FrameworkElement[] { (TabItem)Tabs.SelectedItem }.Concat(DayEditor.TabStops).ToArray()
                : new FrameworkElement[] { (TabItem)Tabs.SelectedItem, Calendar }
            : new FrameworkElement[] { (TabItem)Tabs.SelectedItem, ManagementCarousel.SelectedContent };
        if(dayOpen && ReferenceEquals(current,DayPage)){FocusElement(stops[backwards?0:1]);return true;}
        int index = Array.FindIndex(stops, stop => ReferenceEquals(stop,current) || current is Visual visual && stop.IsAncestorOf(visual));
        int next = index < 0 ? (backwards ? stops.Length-1 : 0) : (index+(backwards ? -1 : 1)+stops.Length)%stops.Length;
        FocusElement(ReferenceEquals(stops[next],Calendar) ? Calendar.SelectedDateFocusTarget() : stops[next]);
        return true;
    }

    private static void FocusElement(FrameworkElement target)
    {
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(target),target); target.Focus();
    }

    public void MoveTab(int direction)
    {
        if (modals.IsOpen || ScheduleModal.Visibility==Visibility.Visible) return;
        var visible = Tabs.Items.OfType<TabItem>().Where(item => item.Visibility == Visibility.Visible).ToList();
        if (visible.Count == 0) return;
        if (!TrySaveEdits()) return;
        Tabs.SelectedItem = visible[(visible.IndexOf((TabItem)Tabs.SelectedItem) + direction + visible.Count) % visible.Count];
        ((TabItem)Tabs.SelectedItem).Focus();
    }

    internal bool FocusSettingsSearch()
    {
        if (!IsSettingsActive) return false;
        SettingsView.FocusSearch();
        return true;
    }

    private void WidgetModal_MouseDown(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Left || !ReferenceEquals(e.OriginalSource,WidgetModal))return;
        e.Handled=true;dismissingScheduleClick=CaptureMouse();modals.TryClose();
    }
}
