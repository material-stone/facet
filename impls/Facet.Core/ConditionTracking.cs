using System.Text.Json;
using System.Text.Json.Nodes;

namespace Facet.Core;

// Input only: the shape of condition records from when they lived in records.dat.
public sealed record ConditionEntry
{
    public DateTimeOffset At { get; set; }
    public string Kind { get; set; } = "";
}

// One-time move into the condition plugin's store. Delete once no such records remain (ADR 0081).
public static class LegacyConditions
{
    public const string Plugin = "facet.condition";

    // Writes the plugin's store format; what the store already holds wins at an identical instant.
    public static string Merge(string? stored, IEnumerable<ConditionEntry> legacy)
    {
        var existing = string.IsNullOrWhiteSpace(stored) ? [] : JsonNode.Parse(stored)?["Conditions"]?.Deserialize<List<ConditionEntry>>() ?? throw new InvalidDataException("컨디션 저장소 형식이 올바르지 않습니다.");
        var merged = existing.Concat(legacy.Where(entry => existing.All(item => item.At != entry.At))).OrderBy(entry => entry.At).ToList();
        return JsonSerializer.Serialize(new { SchemaVersion = 1, Conditions = merged });
    }
}
