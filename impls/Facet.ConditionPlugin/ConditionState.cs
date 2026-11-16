using System.Globalization;
using System.Text.Json;

namespace Facet.ConditionPlugin;

// The state the condition widgets publish to a skin (ADR 0109). Pure, so the shapes can be checked without a window.
internal static class ConditionState
{
    internal const string CurrentType = "facet.condition/current@1", HistoryType = "facet.condition/history@1";
    private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");

    internal static string Current(IReadOnlyList<ConditionEntry> entries, DateTimeOffset now)
    {
        var style = ConditionIcons.Find(ConditionTracking.Current(entries, now)?.Kind);
        return JsonSerializer.Serialize(new {
            kinds = ConditionIcons.All.Select(item => new { id = item.Id, label = item.Name, color = item.Color }),
            current = style?.Id, currentLabel = style?.Name
        });
    }

    // level is the day's longest condition as its place in the kinds list plus one, or 0 when the day has none.
    internal static string History(IReadOnlyList<ConditionEntry> entries, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        static DateTimeOffset Midnight(DateOnly day) => new(DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Local));
        var start = Midnight(today.AddDays(-6)); var end = Midnight(today.AddDays(1));
        var spans = ConditionTracking.Project(entries, start, end, now);
        double Fraction(DateTimeOffset at) => Math.Round(Math.Clamp((at - start).TotalSeconds / (end - start).TotalSeconds, 0, 1), 4);
        var kinds = ConditionIcons.All.Select(item => item.Id).ToList();
        var days = Enumerable.Range(0, 7).Select(i => {
            var date = today.AddDays(i - 6);
            DateTimeOffset from = Midnight(date), to = Midnight(date.AddDays(1));
            string? kind = spans.Select(span => (span.Kind, Length: ((span.End < to ? span.End : to) - (span.Start > from ? span.Start : from)).Ticks)).Where(x => x.Length > 0)
                .GroupBy(x => x.Kind).OrderByDescending(group => group.Sum(x => x.Length)).Select(group => group.Key).FirstOrDefault();
            return new { date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), label = date.ToString("M.d ddd", Korean), level = kind == null ? 0 : kinds.IndexOf(kind) + 1, kind, isToday = date == today };
        }).ToArray();
        return JsonSerializer.Serialize(new {
            days, spans = spans.Select(span => new { kind = span.Kind, from = Fraction(span.Start), to = Fraction(span.End) })
        });
    }

    internal static string Argument(string argsJson, string name)
    {
        using var args = JsonDocument.Parse(argsJson);
        return args.RootElement.ValueKind == JsonValueKind.Object && args.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : throw new ArgumentException(name + " 인자가 필요합니다.");
    }

    // record {kind}: the same save the condition buttons make.
    internal static Task<string?> Record(string name, string argsJson, Func<string, DateTimeOffset, bool> select)
    {
        if (name != "record") throw new ArgumentException("알 수 없는 동작입니다: " + name);
        string kind = Argument(argsJson, "kind");
        if (ConditionIcons.Find(kind) == null) throw new ArgumentException("알 수 없는 컨디션입니다.");
        return Task.FromResult<string?>(select(kind, DateTimeOffset.Now) ? "true" : "false");
    }
}
