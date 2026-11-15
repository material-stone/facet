using System.Globalization;
using System.Text.Json.Nodes;
using Facet.Plugins;

namespace Facet.SchedulePlugin;

// Calendar, today's agenda and the schedule agent tools, moved out of the app (ADR 0101).
public sealed class SchedulePlugin : IFacetPlugin
{
    internal const string Id = "facet.schedule", CalendarWidget = "calendar", AgendaWidget = "agenda";
    private ScheduleRecords? records;

    public void Register(IWidgetRegistry registry)
    {
        records = new(registry.Store);
        registry.AddWidget(new WidgetDescriptor(CalendarWidget, "달력·일정", context => new ScheduleBoard(records, context.Layer))
        { Description = "달력과 날짜별 일정·할 일·기록", DefaultColumns = 14, DefaultRows = 8, MinColumns = 8, MinRows = 5, AllowMultiple = false, AllowMultipleTabs = false });
        registry.AddWidget(new WidgetDescriptor(AgendaWidget, "오늘 일정", _ => new AgendaView(records))
        { Description = "오늘 주요 일정과 할 일", DefaultColumns = 9, DefaultRows = 2, MinColumns = 3, MinRows = 2, AllowMultiple = false, AllowMultipleTabs = false, HasHudPanel = true });
        foreach (var tool in Tools(records)) registry.AddAgentTool(tool);
    }

    internal static IEnumerable<PluginAgentTool> Tools(ScheduleRecords records)
    {
        const string empty = """{"type":"object","properties":{},"additionalProperties":false}""";
        JsonObject? Args(string json) => JsonNode.Parse(json) as JsonObject;
        yield return new("facet_today", "오늘 날짜·시각, 오늘 일정과 할 일을 한 번에 확인합니다.", empty, json => Task.FromResult(Today(records, DateTimeOffset.Now).ToJsonString()));
        yield return new("facet_list_schedules", "특정 날짜의 일정을 조회합니다.",
            """{"type":"object","properties":{"date":{"type":"string","description":"yyyy-MM-dd. 생략하면 오늘"}},"additionalProperties":false}""",
            json => Task.FromResult(AgentToolCatalog.Schedules(records.Data, AgentToolCatalog.Date(Args(json), "date", DateOnly.FromDateTime(DateTime.Now))).ToJsonString()));
        yield return new("facet_add_schedule", "캘린더에 일정을 등록합니다. 시작 시각과 길이는 30분 단위로 반올림됩니다.",
            """{"type":"object","properties":{"date":{"type":"string","description":"yyyy-MM-dd"},"start":{"type":"string","description":"HH:mm"},"duration_minutes":{"type":"integer","minimum":30},"title":{"type":"string","maxLength":200},"content":{"type":"string"},"label":{"type":"string","maxLength":40}},"required":["date","start","title"],"additionalProperties":false}""",
            json => {
                var args = Args(json); var now = DateTime.Now; DaySchedule? added = null;
                if (!records.Commit(data => added = AgentToolCatalog.AddSchedule(data, args, now))) throw new IOException("일정을 저장하지 못했습니다.");
                return Task.FromResult(AgentToolCatalog.Schedule(added!, AgentToolCatalog.Date(args, "date", DateOnly.FromDateTime(now))).ToJsonString());
            });
        yield return new("facet_delete_schedule", "일정을 삭제합니다. id는 facet_list_schedules 결과의 id입니다.",
            """{"type":"object","properties":{"date":{"type":"string","description":"일정 시작일 yyyy-MM-dd"},"id":{"type":"string"}},"required":["date","id"],"additionalProperties":false}""",
            json => {
                var args = Args(json);
                if (!records.Commit(data => AgentToolCatalog.DeleteSchedule(data, args, DateTime.Now))) throw new IOException("일정을 저장하지 못했습니다.");
                return Task.FromResult(JsonValue.Create("삭제했습니다.").ToJsonString());
            });
    }

    internal static JsonObject Today(ScheduleRecords records, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        return new() {
            ["now"] = now.ToString("yyyy-MM-dd'T'HH:mm:sszzz"), ["weekday"] = now.ToString("dddd", CultureInfo.GetCultureInfo("ko-KR")),
            ["schedules"] = AgentToolCatalog.Schedules(records.Data, today),
            ["agenda"] = new JsonArray(DashboardAgenda.Today(records.Data, now).Select(x => (JsonNode)new JsonObject { ["title"] = x.Title, ["label"] = x.Label, ["at"] = x.At?.ToString("HH:mm") }).ToArray()) };
    }
}