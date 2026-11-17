using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using Facet.Design;
using Facet.Plugins;

namespace Facet;

// The skin widget layer (ADR 0110): widgets whose state type the skin lists are drawn by the world page under the overlay.
// The app keeps their views alive for state, sends where they are and what they show, and forwards pointer input and actions.
public partial class OverlayTabs
{
    internal const string TabsStateType = "facet.app/tabs@1";
    private bool skinWorld;
    // Set by the overlay window while a world page is running.
    internal Action<string>? SkinFrames;
    internal Action<string>? SkinPointerOut;
    private DispatcherTimer? skinFrameTimer;
    private readonly HashSet<IWidgetState> watchedStates = [];
    private Dictionary<string, IWidgetState> shownStates = [];
    private TabsState? tabsState;
    internal IReadOnlyCollection<string> ShownSkinWidgetsForCheck => shownStates.Keys;

    private IReadOnlySet<string> SkinTypes => skinWorld && Skins.Visuals is { WidgetTypes.Count: > 0 } visuals ? visuals.WidgetTypes : EmptyTypes;
    private static readonly IReadOnlySet<string> EmptyTypes = new HashSet<string>();

    // Called by the overlay window when a world page starts or stops; the boards render again with or without skin tiles.
    internal void SetSkinWorld(bool on)
    {
        if (skinWorld == on) return;
        skinWorld = on;
        if (!on) UnwatchStates();
        ApplyWorkspace();
    }

    private static string? TypeOf(IWidgetState state)
    {
        // Plugin code; a throwing Type keeps the default look.
        try { return state.Type; } catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    private bool IsSkinDrawn(FrameworkElement? view) => view is IWidgetState state && SkinTypes.Count > 0 && TypeOf(state) is { } type && SkinTypes.Contains(type);
    private bool IsTabBarSkinDrawn() => SkinTypes.Contains(TabsStateType);

    private void WireSkinLayer(OverlayWorkspaceView board)
    {
        board.SkinDrawn = (_, view) => IsSkinDrawn(view);
        board.TabBarSkinDrawn = IsTabBarSkinDrawn;
        board.TabBarBaseSize = () => TabStripElement is { ActualWidth: > 0, ActualHeight: > 0 } strip ? ((int)Math.Ceiling(strip.ActualWidth), (int)Math.Ceiling(strip.ActualHeight)) : (0, 0);
        board.SkinPointer = (id, kind, at, delta, button) => {
            if (workspaceEditing || !ReferenceEquals(board, SelectedWorkspace) || !shownStates.ContainsKey(id)) return;
            SkinPointerOut?.Invoke(SkinWidgets.PointerMessage(id, kind, at, delta, button));
        };
        board.ViewChanged += RequestSkinFrame;
        board.TilesMoved += RequestSkinFrame;
        board.SizeChanged += (_, _) => RequestSkinFrame();
    }

    // Coalesces bursts (pan, zoom, drags, ticking clocks) into at most one message per frame interval.
    internal void RequestSkinFrame()
    {
        if (!skinWorld || SkinFrames == null) return;
        if (skinFrameTimer == null) {
            skinFrameTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
            skinFrameTimer.Tick += (_, _) => { skinFrameTimer.Stop(); SendSkinFrame(); };
        }
        if (!skinFrameTimer.IsEnabled) skinFrameTimer.Start();
    }

    // Builds and sends { widgets: [...] } for the shown tab, in overlay window pixels, and watches exactly those states.
    internal void SendSkinFrame()
    {
        // While the overlay is hidden its boards have no layout, so a frame now would tell the page there are no widgets and
        // nothing would correct it on show. Keep the page's last frame; showing the overlay sends a fresh one.
        if (skinWorld && !IsVisible) return;
        var frames = new List<SkinWidgetFrame>();
        var states = new Dictionary<string, IWidgetState>();
        if (skinWorld && SelectedWorkspace is { } board && Window.GetWindow(this) is { } window && board.IsVisible) {
            foreach (var (id, bounds, baseWidth, baseHeight) in board.SkinTiles()) {
                IWidgetState? state = id == WorkspaceLayouts.TabBarWidget ? tabsState ??= new(this) : pluginViews.GetValueOrDefault(id) as IWidgetState;
                if (state == null || TypeOf(state) is not { } type) continue;
                Rect at;
                try { at = new Rect(board.TranslatePoint(bounds.TopLeft, window), board.TranslatePoint(bounds.BottomRight, window)); }
                catch (InvalidOperationException) { continue; }
                string? json;
                try { json = state.Json(); } catch (Exception ex) when (ex is not OutOfMemoryException) { Trace.TraceWarning("Widget state failed: " + ex.Message); json = null; }
                frames.Add(new(id, type, at, baseWidth, baseHeight, json ?? "null"));
                states[id] = state;
            }
        }
        foreach (var gone in watchedStates.Where(state => !states.ContainsValue(state)).ToArray()) Unwatch(gone);
        foreach (var state in states.Values.Where(state => !watchedStates.Contains(state)).ToArray()) {
            try { state.Changed += RequestSkinFrame; watchedStates.Add(state); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Trace.TraceWarning("Widget state subscribe failed: " + ex.Message); }
        }
        shownStates = states;
        SkinFrames?.Invoke(SkinWidgets.WidgetsMessage(frames));
    }

    private void Unwatch(IWidgetState state)
    {
        watchedStates.Remove(state);
        try { state.Changed -= RequestSkinFrame; } catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }

    private void UnwatchStates() { foreach (var state in watchedStates.ToArray()) Unwatch(state); shownStates = []; }

    // The page's { type:'action', id, name, args }: only a widget the page was last told about on the shown tab, and never while editing.
    internal async Task<string> RunSkinAction(string json)
    {
        string reply = await SkinWidgets.RouteAction(json, id => workspaceEditing ? null : shownStates.GetValueOrDefault(id));
        RequestSkinFrame();
        return reply;
    }

    // The real tab strip stays laid out (its size is the tab bar's base size) but is not shown while the page draws the tab bar.
    private void ShowTabStrip()
    {
        var visibility = IsTabBarSkinDrawn() ? Visibility.Hidden : Visibility.Visible;
        foreach (string name in new[] { "TabShadow", "TabStrip", "TabMarks" })
            if (Tabs.Template.FindName(name, Tabs) is UIElement part) part.Visibility = visibility;
    }

    // builtin.tabs as a skin sees it: the visible tabs in order, and select {id}.
    private sealed class TabsState(OverlayTabs owner) : IWidgetState
    {
        public string Type => TabsStateType;
        public string Json()
        {
            var selected = owner.SelectedWorkspace?.TabId;
            return JsonSerializer.Serialize(new {
                tabs = owner.WorkspaceTabs.Where(tab => !tab.Hidden).Select(tab => new { id = tab.Id, name = tab.Title, icon = TabIcons.IdOf(TabIcons.For(tab)), selected = tab.Id == selected })
            });
        }
        // The overlay sends a new frame on every tab change, so nothing needs to be raised here.
        public event Action? Changed { add { } remove { } }
        public Task<string?> Act(string name, string argsJson)
        {
            if (name != "select") throw new ArgumentException("알 수 없는 동작입니다: " + name);
            using var args = JsonDocument.Parse(argsJson);
            string? id = args.RootElement.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (owner.WorkspaceTabs.FirstOrDefault(tab => tab.Id == id && !tab.Hidden) is not { } tab) throw new ArgumentException("알 수 없는 탭입니다.");
            owner.Tabs.SelectedItem = owner.TabFor(tab);
            return Task.FromResult<string?>(owner.SelectedWorkspace?.TabId == tab.Id ? "true" : "false");
        }
    }
    internal IWidgetState TabsStateForCheck => tabsState ??= new(this);
}
