using System.Globalization;
using System.Text.Json.Nodes;

namespace Facet.Core;

public sealed class AgentToolCall
{
    public int Version { get; set; } = 1;
    public string Tool { get; set; } = "";
    public JsonObject? Arguments { get; set; }
}

public sealed record AgentToolReply(bool Ok, string Code, string Message, JsonNode? Result = null);

public sealed record AgentToolDefinition(string Name, string Description, string InputSchema);

// Shared by the desktop host (dispatch) and Facet.Mcp (tools/list). Names are the MCP tool names.
public static class AgentToolCatalog
{
    private const string Empty = """{"type":"object","properties":{},"additionalProperties":false}""";
    public static readonly IReadOnlyList<AgentToolDefinition> Tools = [
        // The schedule tools (facet_today, facet_*_schedule) belong to the schedule plugin; their helpers below stay pure Core rules (ADR 0101).
        new("facet_add_reminder", "지정 시각에 Windows 알림을 띄울 알림을 등록합니다.",
            """{"type":"object","properties":{"title":{"type":"string"},"at":{"type":"string","description":"시간대 포함 ISO 8601. 예: 2026-10-31T18:00:00+09:00"},"body":{"type":"string"}},"required":["title","at"],"additionalProperties":false}"""),
        new("facet_weather", "대시보드 설정 지역의 현재 날씨를 확인합니다.", Empty),
    ];

    public static string Text(JsonObject? args, string name, int max = 20000)
    {
        string value = args?[name]?.GetValue<string>()?.Trim() ?? "";
        if (value.Length > max) throw new ArgumentException($"{name}이(가) 너무 깁니다.");
        return value;
    }
    public static int? Number(JsonObject? args, string name) => args?[name] is JsonValue value ? value.GetValue<int>() : null;
    public static DateOnly Date(JsonObject? args, string name, DateOnly fallback)
    {
        string value = Text(args, name, 10);
        if (value.Length == 0) return fallback;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date
            : throw new ArgumentException($"{name}은(는) yyyy-MM-dd 형식이어야 합니다.");
    }
    public static DateTimeOffset Instant(string value, string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"(Z|[+-]\d{2}:\d{2})$") ||
            !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            throw new ArgumentException($"{name}에는 시간대가 있는 ISO 8601 시각이 필요합니다.");
        return at;
    }

    public static JsonObject Schedule(DaySchedule item, DateOnly date) => new() {
        ["id"] = item.Id.ToString(), ["title"] = item.Name, ["label"] = item.Label, ["content"] = item.Content,
        ["start_date"] = (item.OwnerDate ?? date.AddDays(item.StartDayOffset)).ToString("yyyy-MM-dd"),
        ["start"] = $"{item.StartMinute / 60:00}:{item.StartMinute % 60:00}", ["duration_minutes"] = item.DurationMinutes };

    public static JsonArray Schedules(AppData data, DateOnly date) =>
        new(CalendarNotes.SchedulesForDate(data, date).OrderBy(x => x.VisibleStartMinute).Select(x => (JsonNode)Schedule(x, date)).ToArray());

    public static DaySchedule AddSchedule(AppData data, JsonObject? args, DateTime now)
    {
        var date = Date(args, "date", DateOnly.FromDateTime(now));
        var time = TimeOnly.TryParseExact(Text(args, "start", 5), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : throw new ArgumentException("start는 24시간 HH:mm 형식이어야 합니다.");
        int start = time.Hour * 60 + time.Minute;
        int duration = Number(args, "duration_minutes") ?? 60;
        var item = new DaySchedule {
            Name = Text(args, "title", 200), Content = Text(args, "content"),
            StartMinute = start / 30 * 30, DurationMinutes = Math.Max(30, (duration + 15) / 30 * 30) };
        string label = Text(args, "label", 40); if (label.Length > 0) item.Label = label;
        CalendarNotes.SaveSchedule(data, CalendarNotes.Get(data, date), item, false, now);
        return item;
    }

    public static void DeleteSchedule(AppData data, JsonObject? args, DateTime now)
    {
        var date = Date(args, "date", DateOnly.FromDateTime(now));
        if (!Guid.TryParse(Text(args, "id", 64), out var id)) throw new ArgumentException("id 형식이 올바르지 않습니다.");
        var item = CalendarNotes.Get(data, date).Schedules.FirstOrDefault(x => x.Id == id)?.Copy()
            ?? throw new ArgumentException("해당 날짜에 시작하는 그 일정이 없습니다.");
        item.OwnerDate = date;
        CalendarNotes.SaveSchedule(data, CalendarNotes.Get(data, date), item, true, now);
    }

}
