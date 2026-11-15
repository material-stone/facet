using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Facet.Plugins;

namespace Facet.ConditionPlugin;

public sealed class ConditionPlugin : IFacetPlugin
{
    private ConditionRecords? records;

    public void Register(IWidgetRegistry registry)
    {
        records = new(registry.Store);
        registry.AddWidget(new WidgetDescriptor("current", "컨디션", _ => {
            if (WidgetTheme.IsHud) {
                var hud = new ConditionHudView { Record = (kind, at) => records.Commit(entries => ConditionTracking.Record(entries, kind, at)) };
                hud.Live = new(hud, records, hud.Refresh);
                return hud;
            }
            var view = new ConditionSelector { Record = (kind, at) => records.Commit(entries => ConditionTracking.Record(entries, kind, at)) };
            view.Live = new(view, records, view.Refresh);
            return view;
        }) { Description = "현재 컨디션 기록", DefaultColumns = 2, DefaultRows = 1, MinColumns = 1, MinRows = 1, AllowMultiple = false, AllowMultipleTabs = false, HasHudPanel = true });
        registry.AddWidget(new WidgetDescriptor("history", "7일 컨디션", _ => {
            var view = new ConditionHistory(WidgetTheme.IsHud) { Commit = records.Commit };
            view.Live = new(view, records, view.Refresh);
            return view;
        }) { Description = "최근 7일 컨디션", DefaultColumns = 9, DefaultRows = 2, MinColumns = 4, MinRows = 1, AllowMultiple = false, AllowMultipleTabs = false, HasHudPanel = true });
        registry.AddAgentTool(new("facet_set_condition", "현재(또는 지정 시각) 컨디션을 기록합니다. kind: good, okay, tired, sleepy, unwell, stressed.",
            """{"type":"object","properties":{"kind":{"type":"string","enum":["good","okay","tired","sleepy","unwell","stressed"]},"at":{"type":"string","description":"시간대 포함 ISO 8601. 생략하면 지금"}},"required":["kind"],"additionalProperties":false}""",
            arguments => Task.FromResult(SetCondition(arguments, DateTimeOffset.Now))));
        registry.AddAgentTool(new("facet_list_conditions", "최근 컨디션 기록을 조회합니다.",
            """{"type":"object","properties":{"days":{"type":"integer","minimum":1,"maximum":90}},"additionalProperties":false}""",
            arguments => Task.FromResult(ListConditions(arguments, DateTimeOffset.Now))));
    }

    private string SetCondition(string arguments, DateTimeOffset now)
    {
        var args = JsonNode.Parse(arguments);
        string kind = args?["kind"]?.GetValue<string>()?.Trim() ?? "", at = args?["at"]?.GetValue<string>()?.Trim() ?? "";
        var when = at.Length == 0 ? now : Instant(at);
        if (when > now) throw new ArgumentException("미래 시각으로 기록할 수 없습니다.");
        if (!records!.Commit(entries => ConditionTracking.Record(entries, kind, when))) throw new IOException("컨디션을 저장하지 못했습니다.");
        return new JsonObject { ["at"] = when.ToString("o"), ["kind"] = kind }.ToJsonString();
    }

    private string ListConditions(string arguments, DateTimeOffset now)
    {
        var since = now.AddDays(-(JsonNode.Parse(arguments)?["days"]?.GetValue<int>() ?? 7));
        return new JsonArray(records!.Entries.Where(entry => entry.At >= since && entry.At <= now).OrderBy(entry => entry.At)
            .Select(entry => (JsonNode)new JsonObject { ["at"] = entry.At.ToString("o"), ["kind"] = entry.Kind }).ToArray()).ToJsonString();
    }

    private static DateTimeOffset Instant(string value) =>
        Regex.IsMatch(value, @"(Z|[+-]\d{2}:\d{2})$") && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at : throw new ArgumentException("at에는 시간대가 있는 ISO 8601 시각이 필요합니다.");
}

// Keeps a widget in step with the shared records: at once on a change, and each minute while on screen for the moving "now".
internal sealed class ConditionLive : IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly ConditionRecords records;
    private readonly Action<IReadOnlyList<ConditionEntry>, DateTimeOffset> refresh;
    private readonly FrameworkElement view;

    internal ConditionLive(FrameworkElement view, ConditionRecords records, Action<IReadOnlyList<ConditionEntry>, DateTimeOffset> refresh)
    {
        (this.view, this.records, this.refresh) = (view, records, refresh);
        timer.Tick += (_, _) => Update();
        records.Changed += Update;
        view.IsVisibleChanged += VisibilityChanged;
        Update();
    }

    private void Update() => refresh(records.Entries, DateTimeOffset.Now);

    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (view.IsVisible) { Update(); timer.Start(); } else timer.Stop();
    }

    public void Dispose()
    {
        timer.Stop();
        records.Changed -= Update;
        view.IsVisibleChanged -= VisibilityChanged;
    }
}

internal static class PopupKeys
{
    // The overlay handles Escape on its own window, and a popup is outside that route only while it holds the focus.
    internal static void TakeFocus(Popup popup, FrameworkElement target)
    {
        target.Focusable = true;
        target.FocusVisualStyle = null;
        IInputElement? previous = null;
        popup.Opened += (_, _) => { previous = System.Windows.Input.Keyboard.FocusedElement; target.Focus(); };
        popup.Closed += (_, _) => { if (target.IsKeyboardFocusWithin) System.Windows.Input.Keyboard.Focus(previous); previous = null; };
    }
}
