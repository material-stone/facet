#if PLUGIN_CHECKS
using System.Text.Json;
using System.Text.RegularExpressions;
using Facet.Plugins;

// Each first-party widget's skin state (ADR 0109): its type name and JSON shape, and that actions run the widget's own code.
internal static partial class Program
{
    private sealed class MemoryStore : IPluginStore
    {
        private string? text;
        public string? Read() => text;
        public bool Write(string value) { text = value; return true; }
    }

    private static bool Has(JsonElement element, params string[] keys) => element.ValueKind == JsonValueKind.Object && keys.All(key => element.TryGetProperty(key, out _));
    private static bool Refuses(Func<Task> act) { try { act().GetAwaiter().GetResult(); return false; } catch (ArgumentException) { return true; } }

    private static void CheckWidgetStates()
    {
        var timeView = new Facet.ClockPlugin.ClockFaceView(new WidgetContext("t", ""), "시계", Facet.ClockPlugin.FaceChoices.Time);
        var weekdayView = new Facet.ClockPlugin.ClockFaceView(new WidgetContext("d", ""), "요일", Facet.ClockPlugin.FaceChoices.Weekday);
        var clockView = new Facet.ClockPlugin.ClockView(new WidgetContext("c", ""));
        using (var time = JsonDocument.Parse(timeView.Json())) {
            var root = time.RootElement;
            Assert(timeView.Type == "facet.clock/time@1" && weekdayView.Type == "facet.clock/weekday@1" && clockView.Type == "facet.clock/date-time@1", "the clock widgets name their state types");
            Assert(Has(root, "time", "hour", "minute", "seconds", "weekday", "weekdayIndex", "date", "dateLabel", "mode", "style") && Regex.IsMatch(root.GetProperty("time").GetString()!, "^\\d{2}:\\d{2}$") && Regex.IsMatch(root.GetProperty("date").GetString()!, "^\\d{4}-\\d{2}-\\d{2}$") && root.GetProperty("mode").GetString() == "analog", "the clock state carries the time, weekday, date and face");
        }
        Assert(timeView.Act("select", "{\"mode\":\"digital\",\"style\":\"plain\"}").GetAwaiter().GetResult() == "true" && timeView.Choice.Style == "plain" && Refuses(() => timeView.Act("select", "{\"mode\":\"x\"}")), "select {mode, style} changes the face the settings picker would");
        clockView.Dispose(); timeView.Dispose(); weekdayView.Dispose();

        var weather = new Facet.WeatherPlugin.WeatherView(new WidgetContext("w", ""));
        weather.Show(new(12.34, 55, 0, false, DateTimeOffset.Now));
        using (var now = JsonDocument.Parse(weather.Json())) {
            var root = now.RootElement;
            Assert(weather.Type == "facet.weather/now@1" && Has(root, "condition", "label", "temperature", "humidity", "place", "status", "loading", "observedAt") && root.GetProperty("condition").GetString() == "night" && root.GetProperty("temperature").GetDouble() == 12.3 && root.GetProperty("humidity").GetInt32() == 55, "the weather state carries the sky key, label, temperature and humidity");
        }
        Assert(new Facet.WeatherPlugin.WeatherReading(1, 1, 3, true, default).SkinKey == "cloud" && new Facet.WeatherPlugin.WeatherReading(1, 1, 65, true, default).SkinKey == "rain" && new Facet.WeatherPlugin.WeatherReading(1, 1, 0, true, default).SkinKey == "clear", "weather codes map to the skin's sky keys");
        Assert(Refuses(() => weather.Act("nope", "{}")), "the weather widget refuses unknown actions");
        weather.Dispose();

        var schedule = new Facet.SchedulePlugin.ScheduleRecords(new MemoryStore());
        var start = DateTime.Now.AddHours(1);
        Facet.SchedulePlugin.SchedulePlugin.Tools(schedule).Single(tool => tool.Name == "facet_add_schedule").Run(JsonSerializer.Serialize(new { date = start.ToString("yyyy-MM-dd"), start = start.ToString("HH:mm"), title = "검토 회의", duration_minutes = 30 })).GetAwaiter().GetResult();
        var agenda = new Facet.SchedulePlugin.AgendaView(schedule);
        using (var state = JsonDocument.Parse(agenda.StateJson(start.AddMinutes(-90)))) {
            var root = state.RootElement;
            var next = root.GetProperty("next");
            Assert(agenda.Type == "facet.schedule/agenda@1" && Has(root, "previous", "current", "next") && Has(next, "id", "title", "start", "end", "minutesLeft", "ending", "memo") && next.GetProperty("title").GetString() == "검토 회의" && next.GetProperty("minutesLeft").ValueKind == JsonValueKind.Null, "the agenda state carries the previous, current and next schedule");
        }
        agenda.Dispose();

        var conditions = new Facet.ConditionPlugin.ConditionRecords(new MemoryStore());
        var selector = new Facet.ConditionPlugin.ConditionSelector { Record = (kind, at) => conditions.Commit(entries => Facet.ConditionPlugin.ConditionTracking.Record(entries, kind, at)) };
        selector.Live = new(selector, conditions, selector.Refresh) { State = selector.Json };
        Assert(selector.Act("record", "{\"kind\":\"good\"}").GetAwaiter().GetResult() == "true" && Refuses(() => selector.Act("record", "{\"kind\":\"angry\"}")) && Refuses(() => selector.Act("delete", "{}")), "record {kind} saves through the condition buttons' path and bad kinds are refused");
        using (var current = JsonDocument.Parse(selector.Json())) {
            var root = current.RootElement;
            Assert(selector.Type == "facet.condition/current@1" && root.GetProperty("current").GetString() == "good" && root.GetProperty("kinds").EnumerateArray().All(kind => Has(kind, "id", "label", "color")) && root.GetProperty("kinds").GetArrayLength() == 6, "the condition state lists the kinds and the current one");
        }
        var history = new Facet.ConditionPlugin.ConditionHistory(true) { Commit = conditions.Commit };
        history.Live = new(history, conditions, history.Refresh) { State = history.Json };
        using (var week = JsonDocument.Parse(history.Json())) {
            var days = week.RootElement.GetProperty("days").EnumerateArray().ToArray();
            Assert(history.Type == "facet.condition/history@1" && days.Length == 7 && days.All(day => Has(day, "date", "label", "level", "kind", "isToday") && day.GetProperty("level").GetInt32() is >= 0 and <= 6) && days[^1].GetProperty("isToday").GetBoolean() && days[^1].GetProperty("level").GetInt32() == 1 && days[^1].GetProperty("kind").GetString() == "good" && week.RootElement.TryGetProperty("spans", out _), "the condition history state has seven days with a level and kind");
        }
        Assert(Refuses(() => history.Act("edit", "{\"date\":\"nope\"}")) && history.Act("edit", "{\"date\":\"2001-01-01\"}").GetAwaiter().GetResult() == "false", "edit {date} checks its date and does nothing for a day without records");
        selector.Dispose(); history.Dispose();

        var sleep = new Facet.SleepPlugin.SleepRecords(new MemoryStore());
        var editing = new Facet.SleepPlugin.SleepEditing(sleep, new WidgetContext("", "").Modal);
        var chart = new Facet.SleepPlugin.SleepDurationChart { Editing = editing };
        chart.Live = new(chart, sleep, chart.Refresh, null) { State = () => Facet.SleepPlugin.SleepState.Week(sleep.Entries, DateOnly.FromDateTime(DateTime.Now)) };
        var fit = new Facet.SleepPlugin.SleepDurationFit(chart.Live, editing) { Child = chart };
        using (var week = JsonDocument.Parse(fit.Json())) {
            var days = week.RootElement.GetProperty("days").EnumerateArray().ToArray();
            Assert(fit.Type == "facet.sleep/week@1" && days.Length == 7 && days.All(day => Has(day, "date", "label", "hours", "recorded", "isToday")) && days[^1].GetProperty("isToday").GetBoolean() && days[0].GetProperty("hours").ValueKind == JsonValueKind.Null, "the sleep state has seven days with hours");
        }
        Assert(Refuses(() => fit.Act("edit", "{}")) && fit.Act("edit", "{\"date\":\"" + DateTime.Now.AddDays(3).ToString("yyyy-MM-dd") + "\"}").GetAwaiter().GetResult() == "false", "edit {date} refuses a missing or future date");
        fit.Dispose();
    }
}
#endif
