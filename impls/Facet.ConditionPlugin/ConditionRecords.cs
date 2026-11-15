using System.Text.Json;
using Facet.Plugins;

namespace Facet.ConditionPlugin;

internal sealed record ConditionEntry
{
    public DateTimeOffset At { get; set; }
    public string Kind { get; set; } = "";
}

internal sealed record ConditionSpan(DateTimeOffset Start, DateTimeOffset End, string Kind, DateTimeOffset RecordedAt);

internal static class ConditionTracking
{
    internal static bool IsKnown(string kind) => kind is "good" or "okay" or "tired" or "sleepy" or "unwell" or "stressed";

    internal static void Record(List<ConditionEntry> entries, string kind, DateTimeOffset at)
    {
        if (!IsKnown(kind)) throw new ArgumentException("알 수 없는 컨디션입니다.", nameof(kind));
        // Last selection wins at an identical instant, including different UTC offsets.
        entries.RemoveAll(entry => entry.At == at);
        entries.Add(new() { At = at, Kind = kind });
    }

    internal static ConditionEntry? Current(IEnumerable<ConditionEntry> entries, DateTimeOffset now) =>
        entries.Where(entry => entry.At <= now).OrderBy(entry => entry.At).LastOrDefault();

    internal static void Update(List<ConditionEntry> entries, ConditionEntry original, string kind, DateTimeOffset at, DateTimeOffset now)
    {
        if (!IsKnown(kind)) throw new ArgumentException("컨디션을 선택해 주세요.");
        if (at > now) throw new ArgumentException("미래 시각으로 기록할 수 없습니다.");
        int index = FindOriginal(entries, original);
        if (entries.Where((_, i) => i != index).Any(entry => entry.At == at))
            throw new ArgumentException("같은 시각의 기록이 있습니다. 다른 시각을 선택해 주세요.");
        entries[index] = new() { At = at, Kind = kind };
    }

    internal static void Delete(List<ConditionEntry> entries, ConditionEntry original) => entries.RemoveAt(FindOriginal(entries, original));

    private static int FindOriginal(List<ConditionEntry> entries, ConditionEntry original)
    {
        int index = entries.FindIndex(entry => entry.At == original.At && entry.Kind == original.Kind);
        if (index < 0) throw new ArgumentException("기록이 이미 변경되었거나 삭제되었습니다. 닫은 뒤 다시 선택해 주세요.");
        return index;
    }

    // Only known, elapsed spans are returned; callers leave all other areas transparent.
    // Unknown persisted kinds end the previous span rather than inventing a condition.
    internal static IReadOnlyList<ConditionSpan> Project(IEnumerable<ConditionEntry> entries,
        DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        if (end <= start) throw new ArgumentException("종료 시각은 시작 이후여야 합니다.");
        var stop = now < end ? now : end;
        if (stop <= start) return [];
        var ordered = entries.Where(entry => entry.At < stop).OrderBy(entry => entry.At).ToArray();
        var result = new List<ConditionSpan>();
        string? kind = null;
        var recordedAt = start;
        var cursor = start;
        foreach (var entry in ordered) {
            if (entry.At <= start) { kind = entry.Kind; recordedAt = entry.At; continue; }
            if (entry.At > cursor && kind != null && IsKnown(kind)) result.Add(new(cursor, entry.At, kind, recordedAt));
            cursor = entry.At; kind = entry.Kind; recordedAt = entry.At;
        }
        if (cursor < stop && kind != null && IsKnown(kind)) result.Add(new(cursor, stop, kind, recordedAt));
        return result;
    }
}

// The plugin's history, shared by every widget and agent tool and saved whole in the plugin's own encrypted store (ADR 0081).
internal sealed class ConditionRecords
{
    private sealed record StoreFile(int SchemaVersion, List<ConditionEntry>? Conditions);
    private readonly IPluginStore store;
    private List<ConditionEntry> entries;
    internal IReadOnlyList<ConditionEntry> Entries => entries;
    internal event Action? Changed;

    // An unreadable store throws, so the plugin reports it and never overwrites the file.
    internal ConditionRecords(IPluginStore store)
    {
        this.store = store;
        entries = Parse(store.Read());
    }

    private static List<ConditionEntry> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        try {
            var file = JsonSerializer.Deserialize<StoreFile>(text);
            if (file is not { SchemaVersion: 1, Conditions: { } list } || list.Any(entry => entry == null)) throw new InvalidDataException("컨디션 저장소 형식이 올바르지 않습니다.");
            return list;
        }
        catch (JsonException ex) { throw new InvalidDataException("컨디션 저장소를 읽을 수 없습니다.", ex); }
    }

    // ArgumentException from the change, or false from the save, leaves the history as it was.
    internal bool Commit(Action<List<ConditionEntry>> change)
    {
        var next = entries.ToList();
        change(next);
        if (!store.Write(JsonSerializer.Serialize(new StoreFile(1, next.OrderBy(entry => entry.At).ToList())))) return false;
        entries = next;
        Changed?.Invoke();
        return true;
    }
}
