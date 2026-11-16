using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Threading;
using Facet.Plugins;
using Facet.Widgets;

namespace Facet;

public partial class OverlayTabs : UserControl, IWidgetLayer
{
    private Controller? app;
    private LayoutInspector? inspector;
    internal bool DebugEnabled => inspector != null;
    internal LayoutInspector? Inspector => inspector;
    internal void StopWidgets() { modals.CloseAll(); DisposeWidgets(); }
    // Releases this instance before a replacement takes over; playback and sessions keep running in the Controller.
    internal void Retire() => StopWidgets();
    // Kept across a skin switch, which rebuilds the tabs.
    internal string SettingsSection {
        get => SettingsView.CurrentSection;
        set => SettingsView.ShowSection(value);
    }
    internal double SettingsScrollOffset {
        get => SettingsView.ScrollOffset;
        set => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => SettingsView.ScrollOffset = value));
    }
    private bool dismissingModalClick;
    internal bool IsSettingsActive => SettingsModal.Visibility == Visibility.Visible;
    internal bool IsWidgetModalOpen => modals.IsOpen;
    public OverlayTabs()
    {
        InitializeComponent();
        modals=new(WidgetModal,WidgetModalFrame);
        InitializeWorkspace();
        SizeChanged += (_, _) => {
            foreach (var item in Tabs.Items.OfType<TabItem>()) item.Padding = new Thickness(ActualWidth < 600 ? 10 : 20, 12, ActualWidth < 600 ? 10 : 20, 12);
        };
        PreviewMouseUp+=(_,e)=>{if(dismissingModalClick && e.ChangedButton==MouseButton.Left){e.Handled=true;dismissingModalClick=false;ReleaseMouseCapture();}};
        LostMouseCapture+=(_,e)=>{if(ReferenceEquals(e.OriginalSource,this))dismissingModalClick=false;};
        IsVisibleChanged+=(_,_)=>{if(!IsVisible){modals.TryClose();SetWorkspaceEditing(false);}};
        SizeChanged += (_, _) => { foreach (var view in workspaces.Values) view.RefreshTabBar(); };
        Tabs.ItemContainerGenerator.ItemsChanged += (_, _) => UpdateTabCorners();
        Tabs.ItemContainerGenerator.StatusChanged += (_, _) => UpdateTabCorners();
        UpdateTabCorners();
        Tabs.SelectionChanged += (_,e) => {
            if (!ReferenceEquals(e.OriginalSource,Tabs)) return;
            modals.TryClose();
            UpdateWorkspaceBar();
            SelectedWorkspace?.RefreshTabBar();
            PlaceTabStrip();
            RequestSkinFrame();
        };
    }

    // IWidgetLayer: a widget's own full-overlay layer (ADR 0101).
    public void Add(FrameworkElement layer) { if (layer.Parent == null) WidgetLayers.Children.Add(layer); }
    public void Remove(FrameworkElement layer) => WidgetLayers.Children.Remove(layer);

    // The default tab bar placement follows the strip's natural size.
    private void TabStrip_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DashboardViewport == null) return;
        foreach (var view in workspaces.Values) view.RefreshTabBar();
        PlaceTabStrip();
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
        string dataRoot = Path.GetDirectoryName(PluginFolder) ?? "";
        SettingsView.PluginPackages.Connect(this, controller, dataRoot, () => { RefreshSettingsPanels(); SettingsView.ReloadSkins(); });
        SettingsView.SkinPanel.Connect(controller, dataRoot, () => { RefreshSettingsPanels(); SettingsView.ReloadSkins(); });
        ApplyWorkspace();
        SettingsView.Connect(controller);
    }

    // Widget views that take keys: those on the selected tab for Escape and keys, all of them before leaving.
    private IEnumerable<IWidgetInput> SelectedInputs => (Tabs.SelectedItem as TabItem)?.Content is DependencyObject page ? Inputs(page).Distinct().ToArray() : [];
    private static IEnumerable<IWidgetInput> Inputs(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) {
            if (child is IWidgetInput input) { yield return input; continue; }
            foreach (var nested in Inputs(child)) yield return nested;
        }
    }
    private IEnumerable<IWidgetInput> AllInputs => pluginViews.Values.OfType<IWidgetInput>().ToArray();
    internal bool HasSelectedInput => SelectedInputs.Any();

    // Plugin code can throw anything; a key must not take the overlay down.
    private static bool Ask(Func<bool> run)
    {
        try { return run(); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Widget input failed: " + ex.Message); return false; }
    }

    public bool HandleEscape()
    {
        if(modals.IsOpen) { modals.HandleEscape();return true; }
        if (IsSettingsActive) { CloseSettings(); return true; }
        if (HandleWorkspaceEscape()) return true;
        return SelectedInputs.Any(input => Ask(input.HandleEscape));
    }

    internal bool HandleWidgetKey(Key key, ModifierKeys modifiers) =>
        !modals.IsOpen && !IsSettingsActive && !workspaceEditing && SelectedInputs.Any(input => Ask(() => input.HandleKey(key, modifiers)));

    public bool TrySaveEdits()
    {
        // A widget modal saves through its own close rule; a failed save keeps it open and stops the caller.
        if (!modals.TryClose()) return false;
        foreach (var input in AllInputs) if (!Ask(input.TrySave)) return false;
        if (SettingsView.TrySave()) return true;
        ShowSettings();
        return false;
    }
    internal void OpenSettings() { ShowSettings(); SettingsView.FocusSearch(); }
    internal void OpenPluginSettings(string pluginId) { ShowSettings(); SettingsView.ShowSection("플러그인"); SettingsView.PluginPanel.Expand(pluginId); }
    private void ShowSettings()
    {
        if (IsSettingsActive) return;
        SetWorkspaceEditing(false);
        modals.TryClose();
        SettingsModal.Visibility = Visibility.Visible;
    }
    // Closing saves first; a save that fails keeps the modal open with its message, like leaving the old settings tab did.
    internal bool CloseSettings()
    {
        if (!IsSettingsActive) return true;
        if (!SettingsView.TrySave()) return false;
        SettingsModal.Visibility = Visibility.Collapsed;
        Focus();
        return true;
    }
    private void SettingsOpen_Click(object sender, RoutedEventArgs e) { if (IsSettingsActive) CloseSettings(); else OpenSettings(); }
    private void SettingsModal_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !ReferenceEquals(e.OriginalSource, SettingsModal)) return;
        e.Handled = true; dismissingModalClick = CaptureMouse(); CloseSettings();
    }
    // Selects the tab that holds the widget; false when it is not on a visible tab.
    internal bool ShowWidget(string widgetId)
    {
        var tab = WorkspaceLayouts.Current(Workspace).Tabs.FirstOrDefault(t => !t.Hidden && t.Widgets.Any(w => w.WidgetId == widgetId));
        if (tab == null || !workspaces.TryGetValue(tab.Id, out var view) || TabOf(view) is not { } item) return false;
        Tabs.SelectedItem = item; UpdateLayout();
        return true;
    }
    internal bool SaveShortcut() => IsSettingsActive ? SettingsView.TrySave() : SelectedInputs.All(input => Ask(input.TrySave));

    internal bool HandleDebugKey(Key key, ModifierKeys modifiers)
    {
        if (key != Key.F8 || modifiers != ModifierKeys.None) return false;
        if (inspector != null) { inspector.Detach(); inspector = null; }
        else if (AdornerLayer.GetAdornerLayer(Tabs) is AdornerLayer layer) inspector = new LayoutInspector(Tabs, layer);
        return true;
    }

    public void MoveTab(int direction)
    {
        if (modals.IsOpen || IsSettingsActive) return;
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
        e.Handled=true;dismissingModalClick=CaptureMouse();modals.TryClose();
    }
}
