namespace Facet.Core;

public sealed class WorkspaceLayout
{
    // Below 2, placements are the old 12x8 grid's top-left based cells.
    public int GridVersion { get; set; }
    public List<WorkspaceTab> Tabs { get; set; } = [];
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

public sealed class WidgetPlacement
{
    public string Id { get; set; } = "";
    public string WidgetId { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class PluginGrant
{
    public string Id { get; set; } = "";
    public string Fingerprint { get; set; } = "";
}

// How the board's cells look while editing. Cells touch; the marks sit on their shared edges.
public static class WorkspaceGrid
{
    public const int DefaultCellSize = 96, MinCellSize = 24, MaxCellSize = 160;
    public const string Dots = "dots", Lines = "lines", Crosses = "crosses";
    public static readonly (string Id, string Name)[] Styles = [(Dots, "점"), (Lines, "선"), (Crosses, "십자")];
    public static int CellSize(Settings settings) => Math.Clamp(settings.GridCellSize, MinCellSize, MaxCellSize);
    public static string Style(Settings settings) => Styles.Any(s => s.Id == settings.GridStyle) ? settings.GridStyle : Dots;
}

public static class WorkspaceLayouts
{
    // Columns and Rows size the default area around the origin; the board itself has no edge in practice.
    public const int Columns = 12, Rows = 8, MaxTabs = 12, MaxTitleLength = 20, GridVersion = 6, MaxCell = 100000, CalendarColumns = 14, CalendarRows = 8;
    // The board is three screens across, so the smallest zoom shows all of it.
    public const double MinZoom = 1d / 3, MaxZoom = 3, MaxOffset = 10000000;
    public const string DashboardTab = "dashboard", ScheduleTab = "schedule", SleepTab = "sleep", ChatTab = "chat", SettingsTab = "settings";
    // Widget ids from when the app had its own widgets; PluginData moves their placements to the plugin widgets once (ADR 0101).
    public const string ClockWidget = "builtin.clock", WeatherWidget = "builtin.weather",
        AgendaWidget = "builtin.agenda", CalendarWidget = "builtin.calendar";
    public const string PluginClockWidget = "plugin.facet.clock.clock", PluginWeatherWidget = "plugin.facet.weather.weather",
        PluginAgendaWidget = "plugin.facet.schedule.agenda", PluginCalendarWidget = "plugin.facet.schedule.calendar";
    public static string CurrentWidget(string widgetId) => widgetId switch {
        ClockWidget => PluginClockWidget, WeatherWidget => PluginWeatherWidget, AgendaWidget => PluginAgendaWidget, CalendarWidget => PluginCalendarWidget, _ => widgetId };
    private static readonly (string Id, string Title)[] BuiltIn = [(DashboardTab, "대시보드"), (ScheduleTab, "일정"), (SleepTab, "수면"), (ChatTab, "채팅"), (SettingsTab, "설정")];

    // Settings is the recovery path and stays free of widgets; every other tab holds a board.
    public static bool HoldsWidgets(string tabId) => tabId != SettingsTab;

    // A fresh install has an empty dashboard and the settings tab; widgets come with plugins (ADR 0101).
    // The layout a never-edited install showed before the widgets became plugins, with the plugins' widget ids.
    public static WorkspaceLayout LegacyDefault()
    {
        var layout = new WorkspaceLayout { GridVersion = GridVersion, Tabs = BuiltIn.Select(tab => new WorkspaceTab { Id = tab.Id, Title = tab.Title }).ToList() };
        WidgetPlacement Place(string id, int x, int y, int width, int height) => new() { Id = id, WidgetId = CurrentWidget(id), X = x, Y = y, Width = width, Height = height };
        layout.Tabs[0].Widgets = [Place(ClockWidget, -6, -4, 3, 1), Place(WeatherWidget, -3, -4, 4, 1), Place(AgendaWidget, -6, 2, 9, 2)];
        layout.Tabs[1].Widgets = [Place(CalendarWidget, -CalendarColumns / 2, -CalendarRows / 2, CalendarColumns, CalendarRows)];
        return layout;
    }

    public static WorkspaceLayout Default() => new() { GridVersion = GridVersion, Tabs = BuiltIn.Where(tab => tab.Id is DashboardTab or SettingsTab).Select(tab => new WorkspaceTab { Id = tab.Id, Title = tab.Title }).ToList() };

    public static WorkspaceLayout Current(AppData data) => data.Workspace ?? Default();

    private static WorkspaceLayout Editable(AppData data) => data.Workspace ??= Default();

    // Keeps the settings tab present, visible and last; drops only malformed entries.
    // Layouts from before GridVersion 6 could not remove the first tabs, so a tab missing from one is restored once.
    public static WorkspaceLayout Normalize(WorkspaceLayout? layout)
    {
        var result = new WorkspaceLayout { GridVersion = GridVersion };
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
                        kept.Widgets.Add(new() { Id = item.Id, WidgetId = item.WidgetId, X = x, Y = y, Width = item.Width, Height = item.Height });
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
            if (FindSlot(schedule, CalendarColumns, CalendarRows, -CalendarColumns / 2, -CalendarRows / 2, MaxCell, MaxCell) is { } slot)
                schedule.Widgets.Add(new() { Id = CalendarWidget, WidgetId = PluginCalendarWidget, X = slot.X, Y = slot.Y, Width = CalendarColumns, Height = CalendarRows });
        }
        // The sleep tab's fixed page became a widget in layouts from before GridVersion 4; it comes once and can then be removed.
        if (layout != null && layout.GridVersion < 4 && result.Tabs.All(t => t.Widgets.All(w => w.WidgetId != LegacySleep.PatternWidget))) {
            var sleep = result.Tabs.First(t => t.Id == SleepTab);
            if (FindSlot(sleep, 9, 7, -5, -4, MaxCell, MaxCell) is { } slot)
                sleep.Widgets.Add(new() { Id = LegacySleep.PatternWidget, WidgetId = LegacySleep.PatternWidget, X = slot.X, Y = slot.Y, Width = 9, Height = 7 });
        }
        // The chat tab's fixed page became a widget in layouts from before GridVersion 5; it comes once and can then be removed.
        if (layout != null && layout.GridVersion < 5 && result.Tabs.All(t => t.Widgets.All(w => w.WidgetId != LegacyChat.Widget))) {
            var chat = result.Tabs.First(t => t.Id == ChatTab);
            if (FindSlot(chat, Columns, Rows, -Columns / 2, -Rows / 2, MaxCell, MaxCell) is { } slot)
                chat.Widgets.Add(new() { Id = LegacyChat.Widget, WidgetId = LegacyChat.Widget, X = slot.X, Y = slot.Y, Width = Columns, Height = Rows });
        }
        var settings = result.Tabs.First(t => t.Id == SettingsTab);
        result.Tabs.Remove(settings); settings.Hidden = false; result.Tabs.Add(settings);
        return result;
    }

    private static string Trim(string title) { title = title.Trim(); return title.Length > MaxTitleLength ? title[..MaxTitleLength] : title; }

    private static BoardView? NormalizeView(BoardView? view) =>
        view == null || !double.IsFinite(view.X) || !double.IsFinite(view.Y) || !double.IsFinite(view.Zoom) ? null
        : new() { X = Math.Clamp(view.X, -MaxOffset, MaxOffset), Y = Math.Clamp(view.Y, -MaxOffset, MaxOffset), Zoom = Math.Clamp(view.Zoom, MinZoom, MaxZoom) };

    public static void SetView(AppData data, string tabId, double x, double y, double zoom)
    {
        if (!HoldsWidgets(tabId)) throw new ArgumentException("이 탭에는 위젯 보드가 없습니다.");
        Tab(Editable(data), tabId).View = NormalizeView(new() { X = x, Y = y, Zoom = zoom }) ?? throw new ArgumentException("보드 시점이 올바르지 않습니다.");
    }

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

    // Reads rows down from the area's top-left, then the whole board within halfColumns and halfRows of the origin.
    public static (int X, int Y)? FindSlot(WorkspaceTab tab, int width, int height, int left, int top, int halfColumns, int halfRows)
    {
        if (width > halfColumns * 2 || height > halfRows * 2) return null;
        left = Math.Clamp(left, -halfColumns, halfColumns - width);
        top = Math.Clamp(top, -halfRows, halfRows - height);
        for (int y = top; y + height <= halfRows; y++)
            for (int x = left; x == left || (x + width <= left + Columns && x + width <= halfColumns); x++)
                if (IsFree(tab, x, y, width, height, null)) return (x, y);
        for (int y = -halfRows; y + height <= halfRows; y++)
            for (int x = -halfColumns; x + width <= halfColumns; x++)
                if (IsFree(tab, x, y, width, height, null)) return (x, y);
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

    // left and top are the cells at the corner of the area to fill, normally around what the user is looking at.
    public static WidgetPlacement AddWidget(AppData data, string tabId, string widgetId, int width, int height, bool single, int left = -Columns / 2, int top = -Rows / 2, int halfColumns = MaxCell, int halfRows = MaxCell, bool onePerTab = false, bool oneTabOnly = false)
    {
        var layout = Editable(data); var tab = Tab(layout, tabId);
        if (!HoldsWidgets(tabId)) throw new ArgumentException("이 탭에는 위젯을 놓을 수 없습니다.");
        var problem = PlacementProblem(layout, tabId, widgetId, single || onePerTab, single || oneTabOnly);
        if (problem != null) throw new ArgumentException(problem);
        var slot = FindSlot(tab, width, height, left, top, halfColumns, halfRows) ?? throw new ArgumentException("빈 공간이 부족합니다. 다른 위젯을 옮기거나 줄여 주세요.");
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
        layout.Tabs.Insert(layout.Tabs.Count - 1, tab); return tab;
    }

    public static void RenameTab(AppData data, string tabId, string title)
    {
        title = title.Trim();
        if (tabId == SettingsTab) throw new ArgumentException("설정 탭의 이름은 바꿀 수 없습니다.");
        if (title.Length == 0) throw new ArgumentException("탭 이름을 입력해 주세요.");
        Tab(Editable(data), tabId).Title = Trim(title);
    }

    public static void SetIcon(AppData data, string tabId, string icon)
    {
        if (tabId == SettingsTab) throw new ArgumentException("설정 탭의 아이콘은 바꿀 수 없습니다.");
        Tab(Editable(data), tabId).Icon = icon;
    }

    public static void RemoveTab(AppData data, string tabId)
    {
        if (tabId == SettingsTab) throw new ArgumentException("설정 탭은 삭제할 수 없습니다.");
        Editable(data).Tabs.RemoveAll(t => t.Id == tabId);
    }

    public static void SetHidden(AppData data, string tabId, bool hidden)
    {
        if (tabId == SettingsTab) throw new ArgumentException("설정 탭은 숨길 수 없습니다.");
        Tab(Editable(data), tabId).Hidden = hidden;
    }

    // Settings stays last, so a tab can move only within the positions before it.
    public static void MoveTab(AppData data, string tabId, int direction)
    {
        var layout = Editable(data); var tab = Tab(layout, tabId);
        if (tabId == SettingsTab) throw new ArgumentException("설정 탭은 항상 마지막입니다.");
        int index = layout.Tabs.IndexOf(tab), next = index + Math.Sign(direction);
        if (next < 0 || next >= layout.Tabs.Count - 1) return;
        layout.Tabs.RemoveAt(index); layout.Tabs.Insert(next, tab);
    }
}
