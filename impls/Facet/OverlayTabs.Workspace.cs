using System.Diagnostics;
using System.Windows.Controls.Primitives;
using Facet.Design;
using Facet.Plugins;

namespace Facet;

// Widget catalog, tab sync and widget lifetimes for the tile workspace (ADR 0073).
public partial class OverlayTabs
{
    private sealed record WidgetEntry(string Id, WidgetDescriptor Descriptor, string? PluginId, bool Fill);

    private readonly Dictionary<string, WidgetEntry> catalog = [];
    private readonly Dictionary<string, FrameworkElement> pluginViews = [];
    private readonly Dictionary<string, TabItem> addedTabs = [];
    private readonly Dictionary<string, OverlayWorkspaceView> workspaces = [];
    private readonly Dictionary<string, string> pluginStates = [];
    private readonly WidgetModalHost modals;
    internal IWidgetModal WidgetModalFor(string owner) => modals.For(owner);
    private PluginHost? plugins;
    private PluginRuntime? runtime;
    private bool workspaceEditing;
    private string workspaceError = "";
    private AppData Workspace => app?.Data ?? previewData;
    private readonly AppData previewData = new();

    internal bool IsEditingWorkspace => workspaceEditing;

    private void InitializeWorkspace()
    {
        // The app has no widgets of its own; clock, weather and schedule come from plugins (ADR 0101).
        workspaces[WorkspaceLayouts.DashboardTab] = NewWorkspace(WorkspaceLayouts.DashboardTab);
        DashboardViewport.Children.Insert(0, workspaces[WorkspaceLayouts.DashboardTab]);
        // Fixed pages stay as they are; their boards lie on top.
        foreach (var (id, host) in new (string, Grid)[] { (WorkspaceLayouts.ScheduleTab, ManagementPage), (WorkspaceLayouts.SleepTab, SleepPage), (WorkspaceLayouts.ChatTab, ChatPage) }) {
            workspaces[id] = NewWorkspace(id);
            host.Children.Add(workspaces[id]);
        }
        ApplyWorkspace();
    }

    private OverlayWorkspaceView NewWorkspace(string tabId)
    {
        var board = CreateWorkspace(tabId);
        board.ViewChanged += UpdateWorkspaceZoom;
        board.ViewChanged += () => { if (ReferenceEquals(board, SelectedWorkspace)) PlaceTabStrip(); };
        board.TabBarMoved += () => { if (ReferenceEquals(board, SelectedWorkspace)) PlaceTabStrip(); };
        return board;
    }

    private void UpdateWorkspaceZoom()
    {
        if (SelectedWorkspace is { } view) WorkspaceZoom.Text = Math.Round(view.Zoom * 100) + "%";
    }

    private OverlayWorkspaceView CreateWorkspace(string tabId)
    {
        OverlayWorkspaceView? board = null;
        board = new(tabId) {
        CreateView = item => CreateWidgetView(tabId, item),
        // A missing plugin's placeholder is laid out at the tile's own size.
        BaseSize = item => catalog.TryGetValue(item.WidgetId, out var entry) ? (entry.Descriptor.BaseWidth, entry.Descriptor.BaseHeight) : (item.Width, item.Height),
        TabBar = () => TabBarPlacement(board!),
        CommitTabBar = (x, y, width, height) => ChangeWorkspace(data => WorkspaceLayouts.MoveTabBar(data, x, y, width, height)),
        HasPanel = item => catalog.TryGetValue(item.WidgetId, out var entry) && (WidgetTheme.IsHud ? entry.Descriptor.HasHudPanel && (!WidgetTheme.IsCatHud || entry.Descriptor.HasCatPanel) : entry.Descriptor.HasPanel),
        CommitMove = (id, x, y, width, height) => ChangeWorkspace(data => WorkspaceLayouts.MoveWidget(data, tabId, id, x, y, width, height)),
        RemoveRequested = id => RemovePlacement(tabId, id)
        };
        return board;
    }

    private FrameworkElement? TabStripElement { get { Tabs.ApplyTemplate(); return Tabs.Template.FindName("TabStrip", Tabs) as FrameworkElement; } }

    // The saved tab bar placement, or by default the strip's natural size at the top center of a board's first view.
    private WidgetPlacement TabBarPlacement(OverlayWorkspaceView board)
    {
        if (WorkspaceLayouts.Current(Workspace).TabBar is { } bar) return bar;
        var strip = TabStripElement;
        int width = Math.Max(1, (int)Math.Ceiling(strip?.ActualWidth ?? 0)), height = Math.Max(1, (int)Math.Ceiling(strip?.ActualHeight ?? 0));
        double screen = board.ActualHeight > 0 ? board.ActualHeight : ActualHeight;
        return new() { Id = WorkspaceLayouts.TabBarWidget, WidgetId = WorkspaceLayouts.TabBarWidget, X = -width / 2, Y = (int)Math.Round(-screen / 2 + TabBarMargin), Width = width, Height = height };
    }

    private const double TabBarMargin = 24;

    // Moves and stretches the real tab strip, its shadow and its marks onto the selected board's tab bar tile.
    private void PlaceTabStrip()
    {
        if (TabStripElement is not { } strip || SelectedWorkspace is not { } board || board.TabBarRect is not { } bar || strip.ActualWidth <= 0 || strip.ActualHeight <= 0 || !Tabs.IsAncestorOf(board)) return;
        var at = board.TranslatePoint(board.ToScreen(bar.X, bar.Y), Tabs);
        var transform = new MatrixTransform(bar.Width * board.Zoom / strip.ActualWidth, 0, 0, bar.Height * board.Zoom / strip.ActualHeight, at.X, at.Y);
        transform.Freeze();
        foreach (string name in new[] { "TabShadow", "TabStrip", "TabMarks" })
            if (Tabs.Template.FindName(name, Tabs) is UIElement part) part.RenderTransform = transform;
    }
    internal Rect TabStripBoundsForCheck => TabStripElement is { } strip ? strip.RenderTransform.TransformBounds(new Rect(0, 0, strip.ActualWidth, strip.ActualHeight)) : Rect.Empty;

    private FrameworkElement CreateWidgetView(string tabId, WidgetPlacement item)
    {
        if (!catalog.TryGetValue(item.WidgetId, out var entry)) return Placeholder("플러그인을 찾을 수 없습니다", item.WidgetId);
        if (pluginViews.TryGetValue(item.Id, out var existing)) return existing;
        try {
            string storage = Path.Combine(plugins!.Root, entry.PluginId);
            var view = entry.Descriptor.Create(new(item.Id, plugins.Discover().FirstOrDefault(p => p.Id == entry.PluginId)?.Directory ?? storage) {
                TabId = tabId, Settings = runtime!.SettingsFor(entry.PluginId), Workspace = runtime, Modal = modals.For(item.Id), Layer = this,
                Data = entry.Descriptor.UsesData ? runtime.DataFor(item.Id, entry.PluginId, item.WidgetId) : null
            });
            if (view == null || view.Parent != null) throw new InvalidOperationException("위젯 뷰가 올바르지 않습니다.");
            return pluginViews[item.Id] = view;
        }
        catch (Exception ex) { return Placeholder(entry.Descriptor.Title + " 위젯 오류", ex.Message); }
    }

    private Border Placeholder(string title, string detail)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = (Brush)FindResource("Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
        return new Border { Background = (Brush)FindResource("Panel"), BorderBrush = (Brush)FindResource("Edge"), BorderThickness = new(1), CornerRadius = new(12), Padding = new(12), Child = stack };
    }

    // Saves through Controller.Commit; invalid or unsaved changes keep the shown layout and report why.
    private bool ChangeWorkspace(Action<AppData> change)
    {
        workspaceError = "";
        bool saved = false;
        try {
            var candidate = new AppData { Workspace = WorkspaceLayouts.Normalize(WorkspaceLayouts.Current(Workspace)), EnabledPlugins = Workspace.EnabledPlugins?.ToList(), WidgetStores = Workspace.WidgetStores?.ToDictionary() };
            change(candidate);
            PluginData.PurgeStores(candidate);
            saved = app == null ? Accept(candidate) : app.Commit(data => { data.Workspace = candidate.Workspace; data.EnabledPlugins = candidate.EnabledPlugins; data.WidgetStores = candidate.WidgetStores; });
            if (!saved) workspaceError = app?.LastError ?? "저장하지 못했습니다.";
        }
        catch (ArgumentException ex) { workspaceError = ex.Message; }
        ApplyWorkspace();
        return saved;
    }

    private bool Accept(AppData candidate) { previewData.Workspace = candidate.Workspace; previewData.EnabledPlugins = candidate.EnabledPlugins; previewData.WidgetStores = candidate.WidgetStores; return true; }

    private void RefreshSettingsPanels() { SettingsView.WorkspacePanel.Refresh(); SettingsView.PluginPanel.Refresh(); }

    internal void ApplyWorkspace()
    {
        var layout = WorkspaceLayouts.Current(Workspace);
        foreach (var view in workspaces.Values) view.ReleaseViews();
        foreach (var stale in addedTabs.Keys.Where(id => layout.Tabs.All(t => t.Id != id)).ToArray()) {
            Tabs.Items.Remove(addedTabs[stale]); addedTabs.Remove(stale); workspaces.Remove(stale);
        }
        var selected = Tabs.SelectedItem;
        // The first tabs live in XAML, so a removed one leaves the strip here.
        var wanted = layout.Tabs.Select(TabFor).ToHashSet();
        foreach (var removed in Tabs.Items.OfType<TabItem>().Where(item => !wanted.Contains(item)).ToArray()) Tabs.Items.Remove(removed);
        for (int index = 0; index < layout.Tabs.Count; index++) {
            var item = TabFor(layout.Tabs[index]);
            item.Visibility = layout.Tabs[index].Hidden ? Visibility.Collapsed : Visibility.Visible;
            if (index < Tabs.Items.Count && ReferenceEquals(Tabs.Items[index], item)) continue;
            Tabs.Items.Remove(item); Tabs.Items.Insert(index, item);
        }
        if (selected is TabItem { Visibility: Visibility.Visible } current && Tabs.Items.Contains(current)) Tabs.SelectedItem = current;
        else Tabs.SelectedItem = Tabs.Items.OfType<TabItem>().First(tab => tab.Visibility == Visibility.Visible);
        foreach (var tab in layout.Tabs)
            if (workspaces.TryGetValue(tab.Id, out var view)) { view.Snap = layout.Snap; view.Render(tab, workspaceEditing); }
        WorkspaceSnap.IsChecked = layout.Snap;
        var placed = layout.Tabs.SelectMany(t => t.Widgets).Select(w => w.Id).ToHashSet();
        foreach (var id in pluginViews.Keys.Where(id => !placed.Contains(id)).ToArray()) DisposeWidget(id);
        UpdateTabCorners();
        UpdateWorkspaceBar();
        RefreshSettingsPanels();
        PlaceTabStrip();
    }

    private TabItem TabFor(WorkspaceTab tab)
    {
        TabItem? known = tab.Id switch {
            WorkspaceLayouts.DashboardTab => DashboardTab, WorkspaceLayouts.ScheduleTab => ManagementTab, WorkspaceLayouts.SleepTab => SleepTab,
            WorkspaceLayouts.ChatTab => ChatTab,
            _ => addedTabs.GetValueOrDefault(tab.Id)
        };
        if (known != null) {
            System.Windows.Automation.AutomationProperties.SetName(known, tab.Title);
            ((Icon)((StackPanel)known.Header).Children[0]).Kind = TabIcons.For(tab);
            ((TextBlock)((StackPanel)known.Header).Children[1]).Text = tab.Title;
            return known;
        }
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new Icon { Kind = TabIcons.For(tab), Width = 18, Height = 18, Margin = new(0, 0, 8, 0) };
        var label = new TextBlock { Text = tab.Title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(icon); header.Children.Add(label);
        var view = NewWorkspace(tab.Id);
        var host = new Grid(); host.Children.Add(view);
        var item = new TabItem { Header = header, Content = host, Padding = DashboardTab.Padding };
        System.Windows.Automation.AutomationProperties.SetName(item, tab.Title);
        foreach (var part in new FrameworkElement[] { icon, label })
            part.SetBinding(part is Icon ? Control.ForegroundProperty : TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(TabItem.Foreground)) { Source = item });
        addedTabs[tab.Id] = item; workspaces[tab.Id] = view;
        return item;
    }

    internal OverlayWorkspaceView? SelectedWorkspace => Tabs.SelectedItem is TabItem item ? workspaces.Values.FirstOrDefault(view => ReferenceEquals(TabOf(view), item)) : null;
    private TabItem? TabOf(OverlayWorkspaceView view) => view.TabId switch {
        WorkspaceLayouts.DashboardTab => DashboardTab, WorkspaceLayouts.ScheduleTab => ManagementTab, WorkspaceLayouts.SleepTab => SleepTab, WorkspaceLayouts.ChatTab => ChatTab,
        _ => addedTabs.GetValueOrDefault(view.TabId)
    };


    internal void SetWorkspaceEditing(bool editing)
    {
        if (workspaceEditing == editing) return;
        workspaceEditing = editing; workspaceError = "";
        if (!editing) WidgetPicker.IsOpen = false;
        OverlayWorkspaceView[] moved = editing ? [] : workspaces.Values.Where(view => view.ViewDirty).ToArray();
        if (moved.Length == 0) { ApplyWorkspace(); return; }
        // A failed save keeps the views marked, so the next exit tries again.
        if (ChangeWorkspace(data => { foreach (var view in moved) WorkspaceLayouts.SetView(data, view.TabId, view.ViewX, view.ViewY, view.Zoom); }))
            foreach (var view in moved) view.MarkViewSaved();
    }

    private bool HandleWorkspaceEscape()
    {
        if (!workspaceEditing) return false;
        if (WidgetPicker.IsOpen) { WidgetPicker.IsOpen = false; return true; }
        if (SelectedWorkspace?.CancelDrag() == true) return true;
        SetWorkspaceEditing(false); return true;
    }

    private void UpdateWorkspaceBar()
    {
        WorkspaceEdit.IsEnabled = SelectedWorkspace != null;
        WorkspaceEdit.Tag = workspaceEditing ? "on" : null;
        WorkspaceTools.Visibility = workspaceEditing ? Visibility.Visible : Visibility.Collapsed;
        UpdateWorkspaceZoom();
        WorkspaceMessage.Text = workspaceError;
        WorkspaceMessage.Visibility = workspaceError.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void WorkspaceEdit_Click(object sender, RoutedEventArgs e) => SetWorkspaceEditing(!workspaceEditing);
    private void WorkspaceSnap_Click(object sender, RoutedEventArgs e) => SetWorkspaceSnap(WorkspaceSnap.IsChecked == true);
    internal bool SetWorkspaceSnap(bool snap) => ChangeWorkspace(data => WorkspaceLayouts.SetSnap(data, snap));
    private void WorkspaceDone_Click(object sender, RoutedEventArgs e) => SetWorkspaceEditing(false);
    private void WorkspaceHome_Click(object sender, RoutedEventArgs e) => SelectedWorkspace?.ReturnToOrigin();

    private void WorkspaceAddWidget_Click(object sender, RoutedEventArgs e)
    {
        var layout = WorkspaceLayouts.Current(Workspace);
        WidgetPickerItems.Children.Clear();
        string tabId = SelectedWorkspace?.TabId ?? "";
        var available = catalog.Values.Where(entry => WorkspaceLayouts.PlacementProblem(layout, tabId, entry.Id, !entry.Descriptor.AllowMultiple, !entry.Descriptor.AllowMultipleTabs) == null).ToArray();
        if (available.Length == 0) WidgetPickerItems.Children.Add(new TextBlock { Text = "추가할 수 있는 위젯이 없습니다.", Margin = new(8), Foreground = (Brush)FindResource("Muted") });
        foreach (var entry in available) {
            var button = new Button { Content = entry.Descriptor.Title, ToolTip = entry.Descriptor.Description.Length > 0 ? entry.Descriptor.Description : null, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new(0, 0, 0, 4) };
            button.Click += (_, _) => { WidgetPicker.IsOpen = false; AddWidget(entry.Id); };
            WidgetPickerItems.Children.Add(button);
        }
        WidgetPicker.IsOpen = true;
    }

    internal bool AddWidget(string widgetId)
    {
        if (SelectedWorkspace is not { } view || !catalog.TryGetValue(widgetId, out var entry)) return false;
        // At its base size, centered on what the user is looking at, or the nearest free spot (ADR 0108).
        var (centerX, centerY) = view.ViewedCenter();
        int step = WorkspaceLayouts.Current(Workspace).Snap ? (int)OverlayWorkspaceView.Pitch : 1;
        return ChangeWorkspace(data => {
            var placed = WorkspaceLayouts.AddWidget(data, view.TabId, widgetId, entry.Descriptor.BaseWidth, entry.Descriptor.BaseHeight, false, centerX, centerY, view.HalfWidth, view.HalfHeight, !entry.Descriptor.AllowMultiple, !entry.Descriptor.AllowMultipleTabs, step);
            // The app creates the widget's logical store when the widget that asked for one is placed.
            if (entry.Descriptor.UsesData && entry.PluginId != null) PluginData.EnsureStore(data, placed.Id, entry.PluginId, widgetId);
        });
    }

    private void RemovePlacement(string tabId, string id) => ChangeWorkspace(data => WorkspaceLayouts.RemoveWidget(data, tabId, id));
    internal void RemoveWorkspaceWidgetForCheck(string tabId, string id) => RemovePlacement(tabId, id);

    private void DisposeWidget(string id)
    {
        modals.CloseOwned(id);
        if (!pluginViews.Remove(id, out var view)) return;
        try { (view as IDisposable)?.Dispose(); }
        catch (Exception ex) { Trace.TraceWarning("Plugin widget dispose failed: " + ex.Message); }
    }

    private void DisposeWidgets() { foreach (var id in pluginViews.Keys.ToArray()) DisposeWidget(id); }

    // Plugins: only user-enabled plugins run. At launch an enabled plugin loads its current files even if they changed
    // (ADR 0079). The new fingerprint is kept in memory and saved with the next change: Commit is unsafe while the
    // Controller is still being constructed, and losing it only repeats this approval next launch.
    private void ConnectPlugins(Controller controller)
    {
        plugins = controller.Plugins;
        runtime = controller.PluginRuntime;
        runtime.PluginOf = id => catalog.GetValueOrDefault(id)?.PluginId;
        foreach (var plugin in plugins.Discover()) {
            var grant = controller.Data.EnabledPlugins?.FirstOrDefault(g => g.Id == plugin.Id);
            if (grant == null) continue;
            string fingerprint;
            try { fingerprint = PluginHost.Fingerprint(plugin); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { pluginStates[plugin.Id] = "파일을 읽지 못했습니다: " + ex.Message; continue; }
            // A failed load keeps the old approval and reports itself in the settings panel.
            if (LoadPlugin(plugin)) grant.Fingerprint = fingerprint;
        }
    }

    private const int MaxBaseSize = 4096;

    private bool LoadPlugin(PluginManifest plugin)
    {
        try {
            var (widgets, section) = plugins!.Load(plugin, runtime!.SettingsFor(plugin.Id));
            runtime!.SetSection(plugin.Id, section);
            foreach (var widget in widgets) {
                if (string.IsNullOrWhiteSpace(widget.Id) || widget.Create == null) throw new InvalidDataException("위젯 정의가 올바르지 않습니다.");
                string id = WidgetIds.Of(plugin.Id, widget.Id);
                var safe = widget with {
                    DefaultColumns = Math.Clamp(widget.DefaultColumns, 1, WorkspaceLayouts.Columns), DefaultRows = Math.Clamp(widget.DefaultRows, 1, WorkspaceLayouts.Rows),
                    MinColumns = Math.Clamp(widget.MinColumns, 1, WorkspaceLayouts.Columns), MinRows = Math.Clamp(widget.MinRows, 1, WorkspaceLayouts.Rows),
                    Width = widget.Width > 0 ? Math.Clamp(widget.Width, WorkspaceLayouts.MinWidgetSize, MaxBaseSize) : 0,
                    Height = widget.Height > 0 ? Math.Clamp(widget.Height, WorkspaceLayouts.MinWidgetSize, MaxBaseSize) : 0
                };
                catalog[id] = new(id, safe, plugin.Id, true);
            }
            pluginStates.Remove(plugin.Id);
            return true;
        }
        catch (Exception ex) {
            // Plugin code can throw anything; one bad plugin must not stop the overlay.
            pluginStates[plugin.Id] = "불러오지 못했습니다: " + (ex.InnerException ?? ex).Message;
            runtime?.SetSection(plugin.Id, null);
            foreach (var id in catalog.Where(pair => pair.Value.PluginId == plugin.Id).Select(pair => pair.Key).ToArray()) catalog.Remove(id);
            return false;
        }
    }

    internal PluginRuntime? PluginRuntime => runtime;
    internal IReadOnlyList<PluginManifest> DiscoverPlugins() => plugins?.Discover() ?? [];
    internal string PluginState(string id) => pluginStates.GetValueOrDefault(id, "");
    internal bool IsPluginEnabled(string id) => Workspace.EnabledPlugins?.Any(g => g.Id == id) == true && !pluginStates.ContainsKey(id);
    internal string PluginFolder => plugins?.Root ?? "";

    internal bool SetPluginEnabled(PluginManifest plugin, bool enabled)
    {
        if (enabled) {
            string fingerprint;
            try { fingerprint = PluginHost.Fingerprint(plugin); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { pluginStates[plugin.Id] = "파일을 읽지 못했습니다: " + ex.Message; RefreshSettingsPanels(); return false; }
            if (!LoadPlugin(plugin)) { RefreshSettingsPanels(); return false; }
            return ChangeWorkspace(data => data.EnabledPlugins = (data.EnabledPlugins ?? []).Where(g => g.Id != plugin.Id).Append(new PluginGrant { Id = plugin.Id, Fingerprint = fingerprint }).ToList());
        }
        foreach (var id in catalog.Where(pair => pair.Value.PluginId == plugin.Id).Select(pair => pair.Key).ToArray()) catalog.Remove(id);
        foreach (var item in WorkspaceLayouts.Current(Workspace).Tabs.SelectMany(t => t.Widgets).Where(w => w.WidgetId.StartsWith("plugin." + plugin.Id + ".", StringComparison.Ordinal))) DisposeWidget(item.Id);
        plugins?.Forget(plugin.Id);
        pluginStates.Remove(plugin.Id);
        runtime?.SetSection(plugin.Id, null);
        return ChangeWorkspace(data => data.EnabledPlugins = data.EnabledPlugins?.Where(g => g.Id != plugin.Id).ToList());
    }

    // Turns a plugin off, takes its widgets off the boards and deletes its folder. Its records in plugin-data and its
    // settings go too only when asked. Returns a message for the settings panel (ADR 0097).
    internal string UninstallPlugin(PluginManifest plugin, bool removeData)
    {
        if (HasPluginGrant(plugin.Id) && !SetPluginEnabled(plugin, false)) return "플러그인을 끄지 못해 삭제하지 않았습니다. " + workspaceError;
        string prefix = "plugin." + plugin.Id + ".";
        if (!ChangeWorkspace(data => { foreach (var tab in data.Workspace!.Tabs) tab.Widgets.RemoveAll(w => w.WidgetId.StartsWith(prefix, StringComparison.Ordinal)); }))
            return "위젯을 정리하지 못해 삭제하지 않았습니다. " + workspaceError;
        plugins?.Forget(plugin.Id);
        pluginStates.Remove(plugin.Id);
        bool now;
        try { now = PluginHost.RemoveFolder(plugin.Directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "플러그인 폴더를 지우지 못했습니다: " + ex.Message; }
        if (removeData) {
            if (app == null) previewData.PluginSettings?.Remove(plugin.Id);
            else if (!app.Commit(data => data.PluginSettings?.Remove(plugin.Id))) return "플러그인은 지웠지만 설정을 지우지 못했습니다. " + app.LastError;
            string root = Path.GetDirectoryName(Path.GetDirectoryName(plugin.Directory)!)!, data = Path.Combine(root, "plugin-data");
            try {
                if (Directory.Exists(Path.Combine(data, plugin.Id))) Directory.Delete(Path.Combine(data, plugin.Id), true);
                if (Directory.Exists(data)) foreach (string file in Directory.GetFiles(data, plugin.Id + ".dat*")) File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "플러그인은 지웠지만 데이터 일부를 지우지 못했습니다: " + ex.Message; }
        }
        RefreshSettingsPanels();
        return now ? "삭제했습니다." : "삭제했습니다. 사용 중인 파일은 앱을 다시 시작하면 지워집니다.";
    }

    // Runs a plugin chosen from the package list: an update replaces the running code, a new one is enabled.
    internal bool StartPackagePlugin(string id)
    {
        if (HasPluginGrant(id)) return ReloadPlugin(id);
        return DiscoverPlugins().FirstOrDefault(plugin => plugin.Id == id) is { } plugin && SetPluginEnabled(plugin, true);
    }

    // A plugin the user enabled at some point, even if its files have changed since.
    internal bool HasPluginGrant(string id) => Workspace.EnabledPlugins?.Any(g => g.Id == id) == true;

    // Replaces a plugin's code and widgets from its folder without restarting. Asking for this approves the current files.
    internal bool ReloadPlugin(string id)
    {
        if (plugins == null || !HasPluginGrant(id) || plugins.Discover().FirstOrDefault(p => p.Id == id) is not { } plugin) return false;
        string fingerprint;
        try { fingerprint = PluginHost.Fingerprint(plugin); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { pluginStates[id] = "파일을 읽지 못했습니다: " + ex.Message; RefreshSettingsPanels(); return false; }
        foreach (var item in WorkspaceLayouts.Current(Workspace).Tabs.SelectMany(t => t.Widgets).Where(w => catalog.GetValueOrDefault(w.WidgetId)?.PluginId == id)) DisposeWidget(item.Id);
        foreach (var widget in catalog.Where(pair => pair.Value.PluginId == id).Select(pair => pair.Key).ToArray()) catalog.Remove(widget);
        plugins.Forget(id);
        // On failure the placed widgets show placeholders and the old approval stays until a reload succeeds.
        if (!LoadPlugin(plugin)) { ApplyWorkspace(); return false; }
        return ChangeWorkspace(data => data.EnabledPlugins = (data.EnabledPlugins ?? []).Where(g => g.Id != id).Append(new PluginGrant { Id = id, Fingerprint = fingerprint }).ToList());
    }

    internal IReadOnlyList<WorkspaceTab> WorkspaceTabs => WorkspaceLayouts.Current(Workspace).Tabs;
    internal string WorkspaceError => workspaceError;
    internal bool AddWorkspaceTab(string title) => ChangeWorkspace(data => WorkspaceLayouts.AddTab(data, title));
    internal bool RenameWorkspaceTab(string id, string title) => ChangeWorkspace(data => WorkspaceLayouts.RenameTab(data, id, title));
    internal bool SetWorkspaceTabIcon(string id, IconKind kind) => ChangeWorkspace(data => WorkspaceLayouts.SetIcon(data, id, TabIcons.IdOf(kind)));
    internal bool RemoveWorkspaceTab(string id) => ChangeWorkspace(data => WorkspaceLayouts.RemoveTab(data, id));
    internal bool SetWorkspaceTabHidden(string id, bool hidden) => ChangeWorkspace(data => WorkspaceLayouts.SetHidden(data, id, hidden));
    internal bool MoveWorkspaceTab(string id, int direction) => ChangeWorkspace(data => WorkspaceLayouts.MoveTab(data, id, direction));
}
