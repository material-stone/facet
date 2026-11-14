using System.Text.Json;
using System.Text.Json.Nodes;

namespace Facet.Core;

// One-time move into the sleep plugin's store. Delete once no such records remain (ADR 0083).
public static class LegacySleep
{
    public const string Plugin = "facet.sleep", TransparencyKey = "backgroundTransparency";
    public const string PatternWidget = "plugin." + Plugin + ".pattern", DurationWidget = "plugin." + Plugin + ".duration";

    // Writes the plugin's store format; what the store already holds wins per wake date.
    public static string Merge(string? stored, IEnumerable<SleepEntry> sleep, IEnumerable<SleepEntry> removed)
    {
        var node = string.IsNullOrWhiteSpace(stored) ? null : JsonNode.Parse(stored);
        var existing = node == null ? [] : node["Sleep"]?.Deserialize<List<SleepEntry>>() ?? throw new InvalidDataException("수면 저장소 형식이 올바르지 않습니다.");
        var gone = node == null ? [] : node["Removed"]?.Deserialize<List<SleepEntry>>() ?? throw new InvalidDataException("수면 저장소 형식이 올바르지 않습니다.");
        var merged = existing.Concat(sleep.Where(entry => existing.All(item => item.Date != entry.Date))).OrderBy(entry => entry.Date).ToList();
        var known = gone.Select(entry => JsonSerializer.Serialize(entry)).ToHashSet();
        return JsonSerializer.Serialize(new { SchemaVersion = 1, Sleep = merged, Removed = gone.Concat(removed.Where(entry => !known.Contains(JsonSerializer.Serialize(entry)))).ToList() });
    }
}
