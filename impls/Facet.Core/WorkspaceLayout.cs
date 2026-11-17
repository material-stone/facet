namespace Facet.Core;

public sealed class WorkspaceLayout
{
    // Below 2, placements are the old 12x8 grid's top-left based cells; below 7 they are cells around the origin; from 7 board pixels.
    public int GridVersion { get; set; }
    public List<WorkspaceTab> Tabs { get; set; } = [];
    // The tab bar widget's one placement, shared by every tab; null keeps it at the top center of the first view (ADR 0109).
    public WidgetPlacement? TabBar { get; set; }
    // Edit mode snaps positions and sizes to the grid; off moves them by single pixels.
    public bool Snap { get; set; } = true;
}

// X and Y move the origin from the board's center in screen pixels.
public sealed class BoardView
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Zoom { get; set; } = 1;
}

public sealed class WorkspaceTab
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Hidden { get; set; }
    // Optional preset id; the app resolves it and falls back to the tab's default icon.
    public string? Icon { get; set; }
    public BoardView? View { get; set; }
    public List<WidgetPlacement> Widgets { get; set; } = [];
}

// Board pixels at 100% zoom, measured from the board's origin (ADR 0109).
public sealed class WidgetPlacement
{
    public string Id { get; set; } = "";
    public string WidgetId { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    // null follows the widget's own default; true or false is the user's choice for this placement.
    public bool? Panel { get; set; }
}

public sealed class PluginGrant
{
    public string Id { get; set; } = "";
    public string Fingerprint { get; set; } = "";
}

// How the board's grid looks while editing and what edit mode snaps to. Cells touch; the marks sit on their shared edges.
public static class WorkspaceGrid
{
    public const int DefaultCellSize = 96, MinCellSize = 2, MaxCellSize = 160, LegacyMinCellSize = 24;
    public const string Dots = "dots", Lines = "lines", Crosses = "crosses";
    public static readonly (string Id, string Name)[] Styles = [(Dots, "점"), (Lines, "선"), (Crosses, "십자")];
    public static int CellSize(Settings settings) => Math.Clamp(settings.GridCellSize, MinCellSize, MaxCellSize);
    // The cell size the saved cell placements were drawn with, for the one-time move to pixels.
    public static int LegacyCellSize(Settings? settings) => Math.Clamp(settings?.GridCellSize ?? DefaultCellSize, LegacyMinCellSize, MaxCellSize);
    public static string Style(Settings settings) => Styles.Any(s => s.Id == settings.GridStyle) ? settings.GridStyle : Dots;
}

public static class WorkspaceLayouts
{
    // Columns and Rows size the default area around the origin in cells of the old grid; the board itself has no edge in practice.
    // MaxCell bounds coordinates and sizes in board pixels.
    public const int Columns = 12, Rows = 8, MaxTabs = 12, MaxTitleLength = 20, GridVersion = 7, MaxCell = 100000, CalendarColumns = 14, CalendarRows = 8;
    // Placements before GridVersion 7 counted cells; Normalize multiplies them by the cell size once.
    private const int CellGridVersion = 6;
    // The smallest side a widget can be dragged to.
    public const int MinWidgetSize = 24;
    private const int MinSearchStep = 8;
    // The board is three screens across, so the smallest zoom shows all of it.
    // The wheel works between MinZoom and MaxZoom; only the whole-board fit may go further out, down to MinFitZoom.
    public const double MinZoom = 1d / 3, MaxZoom = 3, MinFitZoom = .02, MaxOffset = 10000000;
    // SettingsTab is the id of the former settings tab, which saved layouts may still hold; settings now open from the icon menu (ADR 0109).
    public const string DashboardTab = "dashboard", ScheduleTab = "schedule", SleepTab = "sleep", ChatTab = "chat", SettingsTab = "settings";
    // The app's own tab bar widget; its placement is WorkspaceLayout.TabBar.
    public const string TabBarWidget = "builtin.tabs";
    // Widget ids from when the app had its own widgets; PluginData moves their placements to the plugin widgets once (ADR 0101).
    public const string ClockWidget = "builtin.clock", WeatherWidget = "builtin.weather",
        AgendaWidget = "builtin.agenda", CalendarWidget = "builtin.calendar";
    public const string PluginClockWidget = "plugin.facet.clock.clock", PluginWeatherWidget = "plugin.facet.weather.weather",
        PluginAgendaWidget = "plugin.facet.schedule.agenda", PluginCalendarWidget = "plugin.facet.schedule.calendar";
    public static string CurrentWidget(string widgetId) => widgetId switch {
        ClockWidget => PluginClockWidget, WeatherWidget => PluginWeatherWidget, AgendaWidget => PluginAgendaWidget, CalendarWidget => PluginCalendarWidget, _ => widgetId };
    private static readonly (string Id, string Title)[] BuiltIn = [(DashboardTab, "대시보드"), (ScheduleTab, "일정"), (SleepTab, "수면"), (ChatTab, "채팅"), (SettingsTab, "설정")];

    // Every tab holds a board; only the former settings id never does.
    public static bool HoldsWidgets(string tabId) => tabId != SettingsTab;

    // A fresh install has an empty dashboard; widgets come with plugins (ADR 0101).
    // The layout a never-edited install showed before the widgets became plugins, with the plugins' widget ids.
    public static WorkspaceLayout LegacyDefault(int cellSize = WorkspaceGrid.DefaultCellSize)
    {
        var layout = new WorkspaceLayout { GridVersion = CellGridVersion, Tabs = BuiltIn.Select(tab => new WorkspaceTab { Id = tab.Id, Title = tab.Title }).ToList() };
        WidgetPlacement Place(string id, int x, int y, int width, int height) => new() { Id = id, WidgetId = CurrentWidget(id), X = x, Y = y, Width = width, Height = height };
        layout.Tabs[0].Widgets = [Place(ClockWidget, -6, -4, 3, 1), Place(WeatherWidget, -3, -4, 4, 1), Place(AgendaWidget, -6, 2, 9, 2)];
        layout.Tabs[1].Widgets = [Place(CalendarWidget, -CalendarColumns / 2, -CalendarRows / 2, CalendarColumns, CalendarRows)];
        return Normalize(layout, cellSize);
    }

    public static WorkspaceLayout Default() => new() { GridVersion = GridVersion, Tabs = BuiltIn.Where(tab => tab.Id is DashboardTab).Select(tab => new WorkspaceTab { Id = tab.Id, Title = tab.Title }).ToList() };

    public static WorkspaceLayout Current(AppData data) => data.Workspace ?? Default();

    private static WorkspaceLayout Editable(AppData data) => data.Workspace ??= Default();

    // Drops malformed entries and the former settings tab, and keeps at least one visible tab.
    // A layout from before GridVersion 7 first runs the cell-era rules, then its cells become pixels of cellSize (ADR 0109).
    public static WorkspaceLayout Normalize(WorkspaceLayout? layout, int cellSize = WorkspaceGrid.DefaultCellSize)
    {
        if (layout == null || layout.GridVersion < GridVersion) layout = Scale(NormalizeCells(layout), Math.Clamp(cellSize, WorkspaceGrid.LegacyMinCellSize, WorkspaceGrid.MaxCellSize));
        var result = new WorkspaceLayout { GridVersion = GridVersion, Snap = layout.Snap };
        foreach (var tab in layout.Tabs ?? []) {
            if (tab == null || string.IsNullOrWhiteSpace(tab.Id) || !HoldsWidgets(tab.Id) || result.Tabs.Any(t => t.Id == tab.Id) || result.Tabs.Count >= MaxTabs) continue;
            string? firstTitle = BuiltIn.FirstOrDefault(b => b.Id == tab.Id).Title;
            if (firstTitle == null && string.IsNullOrWhiteSpace(tab.Title)) continue;
            var kept = new WorkspaceTab { Id = tab.Id, Hidden = tab.Hidden, Icon = string.IsNullOrWhiteSpace(tab.Icon) ? null : tab.Icon, Title = string.IsNullOrWhiteSpace(tab.Title) ? firstTitle! : Trim(tab.Title), View = NormalizeView(tab.View) };
            foreach (var item in tab.Widgets ?? []) {
                if (item == null) continue;
                if (!string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.WidgetId) && item.WidgetId != TabBarWidget && InBounds(item.X, item.Y, item.Width, item.Height) && IsFree(kept, item.X, item.Y, item.Width, item.Height, null)
                    && !result.Tabs.Concat([kept]).SelectMany(t => t.Widgets).Any(w => w.Id == item.Id))
                    kept.Widgets.Add(new() { Id = item.Id, WidgetId = item.WidgetId, X = item.X, Y = item.Y, Width = item.Width, Height = item.Height });
            }
            result.Tabs.Add(kept);
        }
        if (result.Tabs.Count == 0) result.Tabs.Add(new() { Id = DashboardTab, Title = BuiltIn[0].Title });
        if (result.Tabs.All(t => t.Hidden)) result.Tabs[0].Hidden = false;
        if (layout.TabBar is { } bar && InBounds(bar.X, bar.Y, bar.Width, bar.Height))
            result.TabBar = new() { Id = TabBarWidget, WidgetId = TabBarWidget, X = bar.X, Y = bar.Y, Width = bar.Width, Height = bar.Height };
        return result;
    }

    // The rules of GridVersion 6 and before, in cells. Settings stays last, and missing first tabs come back once.
    private static WorkspaceLayout NormalizeCells(WorkspaceLayout? layout)
    {
        var result = new WorkspaceLayout { GridVersion = CellGridVersion, Snap = layout?.Snap ?? true };
        bool centerOldGrid = layout != null && layout.GridVersion < 2;
        foreach (var tab in layout?.Tabs ?? []) {
            if (tab == null || string.IsNullOrWhiteSpace(tab.Id) || result.Tabs.Any(t => t.Id == tab.Id)) continue;
            string? firstTitle = BuiltIn.FirstOrDefault(b => b.Id == tab.Id).Title;
            bool settingsTab = tab.Id == SettingsTab;
            if (!settingsTab && ((firstTitle == null && string.IsNullOrWhiteSpace(tab.Title)) || result.Tabs.Count(t => t.Id != SettingsTab) >= MaxTabs - 1)) continue;
            var kept = new WorkspaceTab { Id = tab.Id, Hidden = tab.Hidden, Icon = settingsTab || string.IsNullOrWhiteSpace(tab.Icon) ? null : tab.Icon, Title = settingsTab || string.IsNullOrWhiteSpace(tab.Title) ? firstTitle! : Trim(tab.Title) };
            if (HoldsWidgets(tab.Id)) {
                kept.View = NormalizeView(tab.View);
                foreach (var item in tab.Widgets ?? []) {
                    if (item == null) continue;
                    int x = item.X - (centerOldGrid ? Columns / 2 : 0), y = item.Y - (centerOldGrid ? Rows / 2 : 0);
                    if (!string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.WidgetId) && InBounds(x, y, item.Width, item.Height) && IsFree(kept, x, y, item.Width, item.Height, null)
                        && !result.Tabs.Concat([kept]).SelectMany(t => t.Widgets).Any(w => w.Id == item.Id))
                        kept.Widgets.Add(new() { Id = item.Id, WidgetId = item.WidgetId, X = x, Y = y, Width = item.Width, Height = item.Height, Panel = item.Panel });
                }
            }
            result.Tabs.Add(kept);
        }
        bool restoreFirstTabs = layout == null || layout.GridVersion < 6;
        foreach (var tab in BuiltIn)
            if ((restoreFirstTabs || tab.Id == SettingsTab) && result.Tabs.All(t => t.Id != tab.Id)) result.Tabs.Add(new() { Id = tab.Id, Title = tab.Title });
        // Saved layouts from before the calendar widget get it once; removing it later stays removed.
        if (layout != null && layout.GridVersion < 3 && result.Tabs.All(t => t.Widgets.All(w => w.WidgetId is not (CalendarWidget or PluginCalendarWidget)))) {
            var schedule = result.Tabs.First(t => t.Id == ScheduleTab);
            if (FindCellSlot(schedule, CalendarColumns, CalendarRows, -CalendarColumns / 2, -CalendarRows / 2) is { } slot)
                schedule.Widgets.Add(new() { Id = CalendarWidget, WidgetId = PluginCalendarWidget, X = slot.X, Y = slot.Y, Width = CalendarColumns, Height = CalendarRows });
        }
        // The sleep tab's fixed page became a widget in layouts from before GridVersion 4; it comes once and can then be removed.
        if (layout != null && layout.GridVersion < 4 && result.Tabs.All(t => t.Widgets.All(w => w.WidgetId != LegacySleep.PatternWidget))) {
            var sleep = result.Tabs.First(t => t.Id == SleepTab);
            if (FindCellSlot(sleep, 9, 7, -5, -4) is { } slot)
                sleep.Widgets.Add(new() { Id = LegacySleep.PatternWidget, WidgetId = LegacySleep.PatternWidget, X = slot.X, Y = slot.Y, Width = 9, Height = 7 });
        }
        // The chat tab's fixed page became a widget in layouts from before GridVersion 5; it comes once and can then be removed.
        if (layout != null && layout.GridVersion < 5 && result.Tabs.All(t => t.Widgets.All(w => w.WidgetId != LegacyChat.Widget))) {
            var chat = result.Tabs.First(t => t.Id == ChatTab);
            if (FindCellSlot(chat, Columns, Rows, -Columns / 2, -Rows / 2) is { } slot)
                chat.Widgets.Add(new() { Id = LegacyChat.Widget, WidgetId = LegacyChat.Widget, X = slot.X, Y = slot.Y, Width = Columns, Height = Rows });
        }
        return result;
    }

    // Cell placements become board pixels; views were already screen pixels.
    private static WorkspaceLayout Scale(WorkspaceLayout layout, int cell)
    {
        foreach (var item in layout.Tabs.SelectMany(t => t.Widgets)) {
            item.X = (int)Math.Clamp((long)item.X * cell, -MaxCell, MaxCell); item.Y = (int)Math.Clamp((long)item.Y * cell, -MaxCell, MaxCell);
            item.Width = (int)Math.Clamp((long)item.Width * cell, 1, MaxCell); item.Height = (int)Math.Clamp((long)item.Height * cell, 1, MaxCell);
        }
        layout.GridVersion = GridVersion;
        return layout;
    }

    // The first free cell reading rows down from left and top, used only by the cell-era migrations.
    private static (int X, int Y)? FindCellSlot(WorkspaceTab tab, int width, int height, int left, int top)
    {
        for (int y = top; y + height <= 1000; y++)
            for (int x = left; x + width <= left + Columns || x == left; x++)
                if (IsFree(tab, x, y, width, height, null)) return (x, y);
        return null;
    }

    private static string Trim(string title) { title = title.Trim(); return title.Length > MaxTitleLength ? title[..MaxTitleLength] : title; }

    private static BoardView? NormalizeView(BoardView? view) =>
        view == null || !double.IsFinite(view.X) || !double.IsFinite(view.Y) || !double.IsFinite(view.Zoom) ? null
        : new() { X = Math.Clamp(view.X, -MaxOffset, MaxOffset), Y = Math.Clamp(view.Y, -MaxOffset, MaxOffset), Zoom = Math.Clamp(view.Zoom, MinFitZoom, MaxZoom) };

    public static void SetView(AppData data, string tabId, double x, double y, double zoom)
    {
        if (!HoldsWidgets(tabId)) throw new ArgumentException("이 탭에는 위젯 보드가 없습니다.");
        Tab(Editable(data), tabId).View = NormalizeView(new() { X = x, Y = y, Zoom = zoom }) ?? throw new ArgumentException("보드 시점이 올바르지 않습니다.");
    }

    public static void SetSnap(AppData data, bool snap) => Editable(data).Snap = snap;

    public static bool InBounds(int x, int y, int width, int height) =>
        width >= 1 && height >= 1 && width <= MaxCell && height <= MaxCell && Math.Abs(x) <= MaxCell && Math.Abs(y) <= MaxCell;

    public static bool IsFree(WorkspaceTab tab, int x, int y, int width, int height, string? ignoredId) =>
        !tab.Widgets.Any(w => w.Id != ignoredId && x < w.X + w.Width && w.X < x + width && y < w.Y + w.Height && w.Y < y + height);

    public static bool CanPlace(WorkspaceTab tab, string placementId, int x, int y, int width, int height) =>
        InBounds(x, y, width, height) && IsFree(tab, x, y, width, height, placementId);

    private static WorkspaceTab Tab(WorkspaceLayout layout, string id) =>
        layout.Tabs.FirstOrDefault(t => t.Id == id) ?? throw new ArgumentException("탭을 찾을 수 없습니다.");

    public static void MoveWidget(AppData data, string tabId, string placementId, int x, int y, int width, int height)
    {
        var tab = Tab(Editable(data), tabId);
        var item = tab.Widgets.FirstOrDefault(w => w.Id == placementId) ?? throw new ArgumentException("위젯을 찾을 수 없습니다.");
        if (!CanPlace(tab, placementId, x, y, width, height)) throw new ArgumentException("다른 위젯과 겹칩니다.");
        item.X = x; item.Y = y; item.Width = width; item.Height = height;
    }

    // panel null returns the placement to its widget's default background panel.
    public static void SetPanel(AppData data, string tabId, string placementId, bool? panel)
    {
        var item = Tab(Editable(data), tabId).Widgets.FirstOrDefault(w => w.Id == placementId) ?? throw new ArgumentException("위젯을 찾을 수 없습니다.");
        item.Panel = panel;
    }

    // The tab bar floats over every tab, so it is not held to the widgets' overlap rule.
    public static void MoveTabBar(AppData data, int x, int y, int width, int height)
    {
        if (!InBounds(x, y, width, height)) throw new ArgumentException("탭 막대를 놓을 수 없는 위치입니다.");
        Editable(data).TabBar = new() { Id = TabBarWidget, WidgetId = TabBarWidget, X = x, Y = y, Width = width, Height = height };
    }

    // Rounds a board coordinate to the grid; a step of 1 keeps every pixel.
    // The zoom and offset that show every rectangle with a margin of the screen, centred; null when there is nothing.
    public static (double Zoom, double X, double Y)? Fit(IEnumerable<(int X, int Y, int Width, int Height)> rects, double screenWidth, double screenHeight, double margin = .08)
    {
        var all = rects.ToArray();
        if (all.Length == 0 || screenWidth <= 0 || screenHeight <= 0) return null;
        double left = all.Min(r => r.X), top = all.Min(r => r.Y), right = all.Max(r => r.X + r.Width), bottom = all.Max(r => r.Y + r.Height);
        double usable = 1 - 2 * margin;
        double zoom = Math.Clamp(Math.Min(screenWidth * usable / Math.Max(1, right - left), screenHeight * usable / Math.Max(1, bottom - top)), MinFitZoom, MaxZoom);
        return (zoom, -(left + right) / 2 * zoom, -(top + bottom) / 2 * zoom);
    }

    public static int Snap(double value, int step) => step <= 1 ? (int)Math.Round(value) : (int)Math.Round(value / step) * step;

    // A corner drag: free by default, or keeping the start's aspect ratio. Snapping moves the right edge to the grid,
    // and the bottom too unless the ratio decides it. Sizes never go under MinWidgetSize nor past maxRight and maxBottom.
    public static (int Width, int Height) Resize(int x, int y, int width, int height, double dx, double dy, bool keepRatio, int step, int maxRight = MaxCell, int maxBottom = MaxCell)
    {
        double w = width + dx, h = height + dy;
        if (keepRatio && width > 0 && height > 0) {
            double factor = Math.Max(w / width, h / height);
            factor = Math.Max(factor, Math.Max((double)MinWidgetSize / width, (double)MinWidgetSize / height));
            w = width * factor;
            if (step > 1) w = Math.Max(MinWidgetSize, Snap(x + w, step) - x);
            w = Math.Min(w, maxRight - x);
            h = w * height / width;
            if (y + h > maxBottom) { h = maxBottom - y; w = h * width / height; }
            return (Math.Max(1, (int)Math.Round(w)), Math.Max(1, (int)Math.Round(h)));
        }
        if (step > 1) { w = Snap(x + w, step) - x; h = Snap(y + h, step) - y; }
        return ((int)Math.Clamp(Math.Round(w), MinWidgetSize, Math.Max(MinWidgetSize, maxRight - x)), (int)Math.Clamp(Math.Round(h), MinWidgetSize, Math.Max(MinWidgetSize, maxBottom - y)));
    }

    // The free spot nearest to centering the widget on (centerX, centerY), searched in rings of step pixels within
    // halfWidth and halfHeight of the origin. Null when nothing fits.
    public static (int X, int Y)? FindSlot(WorkspaceTab tab, int width, int height, int centerX, int centerY, int halfWidth, int halfHeight, int step)
    {
        if (width > halfWidth * 2 || height > halfHeight * 2) return null;
        // Without snapping the start keeps its exact pixel, but rings stay coarse enough to search quickly.
        int ringStep = Math.Max(MinSearchStep, step);
        step = Math.Max(1, step);
        int startX = Math.Clamp(Snap(centerX - width / 2.0, step), -halfWidth, halfWidth - width);
        int startY = Math.Clamp(Snap(centerY - height / 2.0, step), -halfHeight, halfHeight - height);
        bool Fits(int x, int y) => x >= -halfWidth && x + width <= halfWidth && y >= -halfHeight && y + height <= halfHeight && IsFree(tab, x, y, width, height, null);
        int rings = (int)Math.Ceiling(2.0 * Math.Max(halfWidth, halfHeight) / ringStep) + 1;
        for (int ring = 0; ring <= rings; ring++) {
            (int X, int Y)? best = null; long bestDistance = long.MaxValue;
            for (int i = -ring; i <= ring; i++)
                foreach (var (cx, cy) in new[] { (i, -ring), (i, ring), (-ring, i), (ring, i) }) {
                    int x = startX + cx * ringStep, y = startY + cy * ringStep;
                    long distance = (long)cx * cx + (long)cy * cy;
                    if (distance < bestDistance && Fits(x, y)) { best = (x, y); bestDistance = distance; }
                }
            // Rings grow outward, so the first ring with a free spot holds the nearest one by ring distance.
            if (best != null) return best;
        }
        return null;
    }

    // Why this widget cannot go on this tab under its placement rule, or null when it can.
    public static string? PlacementProblem(WorkspaceLayout layout, string tabId, string widgetId, bool onePerTab, bool oneTabOnly)
    {
        var holders = layout.Tabs.Where(t => t.Widgets.Any(w => w.WidgetId == widgetId)).Select(t => t.Id).ToArray();
        if (onePerTab && holders.Contains(tabId)) return "이미 이 탭에 배치된 위젯입니다.";
        if (oneTabOnly && holders.Any(id => id != tabId)) return "이 위젯은 한 탭에만 놓을 수 있습니다.";
        return null;
    }

    // centerX and centerY are the board point the new widget is centered on, normally the middle of what the user is looking at.
    public static WidgetPlacement AddWidget(AppData data, string tabId, string widgetId, int width, int height, bool single, int centerX = 0, int centerY = 0, int halfWidth = MaxCell, int halfHeight = MaxCell, bool onePerTab = false, bool oneTabOnly = false, int step = WorkspaceGrid.DefaultCellSize)
    {
        var layout = Editable(data); var tab = Tab(layout, tabId);
        if (!HoldsWidgets(tabId)) throw new ArgumentException("이 탭에는 위젯을 놓을 수 없습니다.");
        if (widgetId == TabBarWidget) throw new ArgumentException("탭 막대는 하나만 있습니다.");
        var problem = PlacementProblem(layout, tabId, widgetId, single || onePerTab, single || oneTabOnly);
        if (problem != null) throw new ArgumentException(problem);
        var slot = FindSlot(tab, width, height, centerX, centerY, halfWidth, halfHeight, step) ?? throw new ArgumentException("빈 공간이 부족합니다. 다른 위젯을 옮기거나 줄여 주세요.");
        var item = new WidgetPlacement { Id = Guid.NewGuid().ToString("N"), WidgetId = widgetId, X = slot.X, Y = slot.Y, Width = width, Height = height };
        tab.Widgets.Add(item); return item;
    }

    public static void RemoveWidget(AppData data, string tabId, string placementId) =>
        Tab(Editable(data), tabId).Widgets.RemoveAll(w => w.Id == placementId);

    public static WorkspaceTab AddTab(AppData data, string title)
    {
        var layout = Editable(data); title = title.Trim();
        if (title.Length == 0) throw new ArgumentException("탭 이름을 입력해 주세요.");
        if (layout.Tabs.Count >= MaxTabs) throw new ArgumentException($"탭은 {MaxTabs}개까지 만들 수 있습니다.");
        var tab = new WorkspaceTab { Id = "tab-" + Guid.NewGuid().ToString("N"), Title = Trim(title) };
        layout.Tabs.Add(tab); return tab;
    }

    public static void RenameTab(AppData data, string tabId, string title)
    {
        title = title.Trim();
        if (title.Length == 0) throw new ArgumentException("탭 이름을 입력해 주세요.");
        Tab(Editable(data), tabId).Title = Trim(title);
    }

    public static void SetIcon(AppData data, string tabId, string icon) => Tab(Editable(data), tabId).Icon = icon;

    // The overlay always keeps one visible tab, so the last one cannot be removed or hidden.
    public static void RemoveTab(AppData data, string tabId)
    {
        var layout = Editable(data); var tab = Tab(layout, tabId);
        if (layout.Tabs.Count(t => !t.Hidden && t != tab) == 0) throw new ArgumentException("보이는 탭이 하나는 있어야 합니다.");
        layout.Tabs.Remove(tab);
    }

    public static void SetHidden(AppData data, string tabId, bool hidden)
    {
        var layout = Editable(data); var tab = Tab(layout, tabId);
        if (hidden && layout.Tabs.Count(t => !t.Hidden && t != tab) == 0) throw new ArgumentException("보이는 탭이 하나는 있어야 합니다.");
        tab.Hidden = hidden;
    }

    public static void MoveTab(AppData data, string tabId, int direction)
    {
        var layout = Editable(data); var tab = Tab(layout, tabId);
        int index = layout.Tabs.IndexOf(tab), next = index + Math.Sign(direction);
        if (next < 0 || next >= layout.Tabs.Count) return;
        layout.Tabs.RemoveAt(index); layout.Tabs.Insert(next, tab);
    }
}
