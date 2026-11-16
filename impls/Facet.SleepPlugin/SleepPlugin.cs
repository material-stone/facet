using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using Facet.Plugins;

namespace Facet.SleepPlugin;

public sealed class SleepPlugin : IFacetPlugin
{
    internal const string TransparencyKey = "backgroundTransparency";
    private SleepRecords? records;

    public void Register(IWidgetRegistry registry)
    {
        records = new(registry.Store);
        registry.AddWidget(new WidgetDescriptor("pattern", "최근 7일 수면 패턴", context => {
            var view = new SleepPatternView { Editing = new(records, context.Modal), Transparency = () => context.Settings.GetNumber(TransparencyKey) };
            view.Live = new(view, records, view.Refresh, context.Settings);
            return new SleepFit { Child = view };
        }) { Description = "기상일별 잠 시도~기상 구간과 기록", DefaultColumns = 9, DefaultRows = 7, MinColumns = 5, MinRows = 4, AllowMultiple = false, AllowMultipleTabs = false });
        registry.AddWidget(new WidgetDescriptor("duration", "최근 7일 수면량", context => {
            var editing = new SleepEditing(records, context.Modal);
            var view = new SleepDurationChart { Editing = editing };
            view.Live = new(view, records, view.Refresh, null) { State = () => SleepState.Week(records.Entries, DateOnly.FromDateTime(DateTime.Now)) };
            return new SleepDurationFit(view.Live, editing) { Child = view };
        }) { Description = "최근 7일 수면 시간 그래프", DefaultColumns = 9, DefaultRows = 3, MinColumns = 4, MinRows = 2, AllowMultiple = false, AllowMultipleTabs = false, HasHudPanel = true });
        registry.AddSettings(new PluginSettingsSection([
            new SliderSetting(TransparencyKey, "수면 패턴 배경 투명도", 0, 100, 0) { Description = "수면 패턴 위젯의 배경만 조절합니다. 0%는 불투명, 100%는 투명입니다." }]));
        registry.AddAgentTool(new("facet_record_sleep", "기상일 기준으로 수면을 기록합니다. 전달한 항목만 기존 기록에 덮어씁니다.",
            """{"type":"object","properties":{"date":{"type":"string","description":"기상일 yyyy-MM-dd. 생략하면 오늘"},"attempt":{"type":"string","description":"잠 시도 HH:mm"},"final_wake":{"type":"string","description":"최종 기상 HH:mm"},"latency_minutes":{"type":"integer","minimum":0,"maximum":1440},"awakenings":{"type":"integer","minimum":0},"awake_minutes":{"type":"integer","minimum":0,"maximum":1440},"nap_minutes":{"type":"integer","minimum":0},"note":{"type":"string","maxLength":4000}},"additionalProperties":false}""",
            arguments => Task.FromResult(Record(arguments, DateTime.Now))));
        registry.AddAgentTool(new("facet_list_sleep", "최근 수면 기록을 조회합니다.",
            """{"type":"object","properties":{"days":{"type":"integer","minimum":1,"maximum":90}},"additionalProperties":false}""",
            arguments => Task.FromResult(List(arguments, DateTime.Now))));
    }

    private string Record(string arguments, DateTime now)
    {
        var args = string.IsNullOrWhiteSpace(arguments) ? null : JsonNode.Parse(arguments)?.AsObject();
        var date = DateOnly.FromDateTime(now);
        if (args?["date"]?.GetValue<string>()?.Trim() is { Length: > 0 } text && !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            throw new ArgumentException("date은(는) yyyy-MM-dd 형식이어야 합니다.");
        var expected = SleepTracking.Get(records!.Entries, date);
        var draft = expected?.Copy() ?? new SleepEntry { Date = date };
        if (args?.ContainsKey("attempt") == true) draft.Attempt = args["attempt"]?.GetValue<string>();
        if (args?.ContainsKey("final_wake") == true) draft.FinalWake = args["final_wake"]?.GetValue<string>();
        if (args?.ContainsKey("latency_minutes") == true) draft.Latency = args["latency_minutes"]?.GetValue<int>();
        if (args?.ContainsKey("awakenings") == true) draft.Awakenings = args["awakenings"]?.GetValue<int>();
        if (args?.ContainsKey("awake_minutes") == true) draft.AwakeMinutes = args["awake_minutes"]?.GetValue<int>();
        if (args?.ContainsKey("nap_minutes") == true) draft.NapMinutes = args["nap_minutes"]?.GetValue<int>();
        if (args?.ContainsKey("note") == true) draft.Note = args["note"]?.GetValue<string>()?.Trim() ?? "";
        if (!records.Commit(book => SleepTracking.Save(book, draft, expected, now))) throw new IOException("수면 기록을 저장하지 못했습니다.");
        return Json(SleepTracking.Get(records.Entries, date) ?? draft).ToJsonString();
    }

    private string List(string arguments, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        int days = string.IsNullOrWhiteSpace(arguments) ? 7 : JsonNode.Parse(arguments)?["days"]?.GetValue<int>() ?? 7;
        var since = today.AddDays(1 - days);
        return new JsonArray(records!.Entries.Where(entry => entry.Date >= since && entry.Date <= today).OrderBy(entry => entry.Date).Select(entry => (JsonNode)Json(entry)).ToArray()).ToJsonString();
    }

    private static JsonObject Json(SleepEntry entry) => new() {
        ["date"] = entry.Date.ToString("yyyy-MM-dd"), ["attempt"] = entry.Attempt, ["final_wake"] = entry.FinalWake, ["latency_minutes"] = entry.Latency,
        ["awakenings"] = entry.Awakenings, ["awake_minutes"] = entry.AwakeMinutes, ["nap_minutes"] = entry.NapMinutes,
        ["estimated_sleep_minutes"] = entry.EstimatedSleepMinutes, ["note"] = entry.Note };
}

// Keeps a widget in step with the shared records: at once on a change, and while on screen for the date rollover.
internal sealed class SleepLive : IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly SleepRecords records;
    internal SleepRecords Records => records;
    private readonly Action<IReadOnlyList<SleepEntry>, DateOnly> refresh;
    private readonly FrameworkElement view;
    private readonly IPluginSettings? settings;

    internal SleepLive(FrameworkElement view, SleepRecords records, Action<IReadOnlyList<SleepEntry>, DateOnly> refresh, IPluginSettings? settings)
    {
        (this.view, this.records, this.refresh, this.settings) = (view, records, refresh, settings);
        timer.Tick += (_, _) => Update();
        records.Changed += Update;
        view.IsVisibleChanged += VisibilityChanged;
        if (settings != null) settings.Changed += SettingChanged;
        Update();
    }

    private void Update() { refresh(records.Entries, DateOnly.FromDateTime(DateTime.Now)); Publish(); }
    private void SettingChanged(string key) => Update();

    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (view.IsVisible || watchers != null) { Update(); timer.Start(); } else timer.Stop();
    }

    // A skin listening to the widget's state (ADR 0109) keeps it updating while the view is hidden; Changed fires only on a new state.
    private Action? watchers;
    private string published = "";
    internal Func<string>? State { get; init; }
    internal void Watch(Action? handler, bool add)
    {
        if (add) { watchers += handler; published = State?.Invoke() ?? ""; timer.Start(); }
        else { watchers -= handler; if (watchers == null && !view.IsVisible) timer.Stop(); }
    }
    private void Publish()
    {
        if (watchers == null || State == null) return;
        string json = State();
        if (json == published) return;
        published = json; watchers();
    }

    public void Dispose()
    {
        timer.Stop();
        records.Changed -= Update;
        view.IsVisibleChanged -= VisibilityChanged;
        if (settings != null) settings.Changed -= SettingChanged;
    }
}

// Shows a view at its natural size, shrinking it uniformly to fit: plugin widgets get the exact tile, and these are taller than small tiles.
internal class SleepFit : Decorator, IDisposable
{
    private double scale = 1;

    protected override Size MeasureOverride(Size available)
    {
        if (Child == null) return default;
        double natural = Child is FrameworkElement { Width: > 0 and var width } ? width : available.Width;
        scale = Math.Min(1, available.Width / natural);
        Child.Measure(new Size(natural, double.PositiveInfinity));
        if (Child.DesiredSize.Height * scale > available.Height) {
            scale = available.Height / Child.DesiredSize.Height;
            if (Child is not FrameworkElement { Width: > 0 }) Child.Measure(new Size(available.Width / scale, double.PositiveInfinity));
        }
        return available;
    }

    protected override Size ArrangeOverride(Size size)
    {
        if (Child == null) return size;
        Child.RenderTransform = scale < 1 ? new ScaleTransform(scale, scale) : Transform.Identity;
        Child.Arrange(new Rect(0, 0, Math.Max(size.Width / scale, 0), Child.DesiredSize.Height));
        return size;
    }

    public void Dispose() => (Child as IDisposable)?.Dispose();
}

// The duration chart's fit, which also publishes the week to a skin (ADR 0109).
internal sealed class SleepDurationFit(SleepLive live, SleepEditing editing) : SleepFit, IWidgetState
{
    public string Type => SleepState.WeekType;
    public string Json() => SleepState.Week(live.Records.Entries, DateOnly.FromDateTime(DateTime.Now));
    public event Action? Changed { add => live.Watch(value, true); remove => live.Watch(value, false); }
    // edit {date}: opens the record editor for that wake date, like clicking the day.
    public Task<string?> Act(string name, string argsJson)
    {
        if (name != "edit") throw new ArgumentException("알 수 없는 동작입니다: " + name);
        using var args = System.Text.Json.JsonDocument.Parse(argsJson);
        if (args.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object || !args.RootElement.TryGetProperty("date", out var value) || value.ValueKind != System.Text.Json.JsonValueKind.String
            || !DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new ArgumentException("date는 yyyy-MM-dd 형식이어야 합니다.");
        return Task.FromResult<string?>(editing.Open(date) ? "true" : "false");
    }
}

internal static class SleepState
{
    internal const string WeekType = "facet.sleep/week@1";
    // hours is the day's sleep rounded to a tenth, or null when it is not recorded or cannot be computed.
    internal static string Week(IReadOnlyList<SleepEntry> entries, DateOnly today) => System.Text.Json.JsonSerializer.Serialize(new {
        days = SleepTracking.Week(entries, today).Select(day => new {
            date = day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), label = day.Date.ToString("ddd", CultureInfo.GetCultureInfo("ko-KR")),
            hours = SleepTracking.Minutes(day) is double minutes ? Math.Round(minutes / 60, 1) : (double?)null, recorded = day.Entry != null, isToday = day.Date == today
        })
    });
}

// Opens the shared record editor over the overlay for one widget.
internal sealed class SleepEditing(SleepRecords records, IWidgetModal modal)
{
    internal SleepRecordEditor? Current { get; private set; }

    internal bool Open(DateOnly date)
    {
        if (date > DateOnly.FromDateTime(DateTime.Now)) return false;
        var editor = new SleepRecordEditor { Commit = records.Commit, Read = day => SleepTracking.Get(records.Entries, day) };
        editor.Open(date);
        // Escape closes a drop-down first; every other way out saves, and a failed save keeps the modal and its draft.
        var handle = modal.Show(editor, new() { CanClose = editor.SaveDraft, HandleEscape = editor.CloseDropDown });
        if (handle == null) return false;
        editor.Closed += handle.Close;
        handle.Closed += () => { editor.Finish(); if (ReferenceEquals(Current, editor)) Current = null; };
        Current = editor;
        return true;
    }
}
