using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Facet.Plugins;

namespace Facet.SleepPlugin;

// Fields the app no longer edits stay on the record so a save never drops them.
internal sealed class SleepEntry
{
    public SleepEntry Copy() => (SleepEntry)MemberwiseClone();
    public DateOnly Date { get; set; }
    public string? Bed { get; set; }
    public string? Attempt { get; set; }
    public int? Latency { get; set; }
    public int? Awakenings { get; set; }
    public int? AwakeMinutes { get; set; }
    public string? FinalWake { get; set; }
    public string? OutOfBed { get; set; }
    public int? Sleepiness { get; set; }
    public int? NapMinutes { get; set; }
    public string? LastCaffeine { get; set; }
    public string Treatment { get; set; } = "미기록";
    public string Note { get; set; } = "";
    public DateTime Updated { get; set; }
    [JsonIgnore]
    public double? EstimatedSleepMinutes
    {
        get {
            if (!TimeOnly.TryParseExact(Attempt, "HH:mm", out var start) || !TimeOnly.TryParseExact(FinalWake, "HH:mm", out var end) || Latency is null || AwakeMinutes is null) return null;
            double interval = (end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
            if (interval <= 0) interval += 1440;
            double total = interval - Latency.Value - AwakeMinutes.Value;
            return total >= 0 ? total : null;
        }
    }
}

// Minutes are relative to midnight of the wake date; negative values are the previous day.
internal sealed record SleepTimeSpan(int StartMinute, int EndMinute);
internal sealed record SleepDay(DateOnly Date, SleepEntry? Entry, SleepTimeSpan? Span);

internal sealed class SleepBook
{
    public List<SleepEntry> Sleep { get; set; } = [];
    public List<SleepEntry> Removed { get; set; } = [];
    internal SleepBook Copy() => new() { Sleep = Sleep.Select(entry => entry.Copy()).ToList(), Removed = Removed.Select(entry => entry.Copy()).ToList() };
}

internal static class SleepTracking
{
    internal static string? ParseTime(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!TimeOnly.TryParseExact(value.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new ArgumentException($"{label}: 24시간 형식으로 입력하세요. 예: 02:00, 12:15");
        return time.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    internal static int? ParseMinutes(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!int.TryParse(value, out int minutes) || minutes < 0 || minutes > 1440)
            throw new ArgumentException($"{label}: 0~1440분 또는 공란으로 입력하세요.");
        return minutes;
    }

    internal static SleepEntry? Get(IEnumerable<SleepEntry> entries, DateOnly date) => entries.FirstOrDefault(entry => entry.Date == date)?.Copy();

    internal static SleepTimeSpan? TimeSpan(SleepEntry? entry)
    {
        if (entry == null || !TimeOnly.TryParseExact(entry.Attempt, "HH:mm", out var start) || !TimeOnly.TryParseExact(entry.FinalWake, "HH:mm", out var end)) return null;
        int from = start.Hour * 60 + start.Minute, to = end.Hour * 60 + end.Minute;
        if (from >= to) from -= 1440;
        return new(from, to);
    }

    internal static IReadOnlyList<SleepDay> Week(IEnumerable<SleepEntry> entries, DateOnly today) => Enumerable.Range(0, 7)
        .Select(i => today.AddDays(i - 6)).Select(date => { var entry = Get(entries, date); return new SleepDay(date, entry, TimeSpan(entry)); }).ToArray();

    internal static void Save(SleepBook book, SleepEntry draft, SleepEntry? expected, DateTime now)
    {
        var next = draft.Copy();
        if (next.Date > DateOnly.FromDateTime(now)) throw new ArgumentException("기상일은 미래 날짜로 기록할 수 없습니다.");
        if (expected != null && expected.Date != next.Date) throw new ArgumentException("기상일은 다른 날짜의 행에서 선택해 주세요.");
        next.Attempt = ParseTime(next.Attempt, "잠 시도");
        next.FinalWake = ParseTime(next.FinalWake, "최종 기상");
        if (next.Latency is < 0 or > 1440 || next.AwakeMinutes is < 0 or > 1440)
            throw new ArgumentException("잠드는 시간과 각성 시간은 각각 0~1440분 또는 공란으로 입력하세요.");
        if (next.Note.Length > 4000) throw new ArgumentException("메모는 4000자까지 입력할 수 있습니다.");
        if (next.Attempt != null && next.FinalWake != null && next.Latency != null && next.AwakeMinutes != null && next.EstimatedSleepMinutes == null)
            throw new ArgumentException("잠드는 시간과 각성 시간의 합이 잠 시도~기상 구간보다 깁니다.");
        var matches = book.Sleep.Where(entry => entry.Date == next.Date).ToArray();
        if (matches.Length > 1 || JsonSerializer.Serialize(matches.FirstOrDefault()) != JsonSerializer.Serialize(expected))
            throw new ArgumentException("이 날짜의 기록이 변경되었습니다. 입력을 복사한 뒤 취소하고 다시 열어 주세요.");
        if (expected == null && next.Attempt == null && next.FinalWake == null && next.Latency == null && next.AwakeMinutes == null && string.IsNullOrWhiteSpace(next.Note)) return;
        next.Updated = now;
        book.Sleep.RemoveAll(entry => entry.Date == next.Date);
        book.Sleep.Add(next);
    }

    internal static void Delete(SleepBook book, SleepEntry expected)
    {
        var matches = book.Sleep.Where(entry => entry.Date == expected.Date).ToArray();
        if (matches.Length != 1 || JsonSerializer.Serialize(matches[0]) != JsonSerializer.Serialize(expected))
            throw new ArgumentException("이 날짜의 기록이 변경되었습니다. 창을 다시 열어 확인해 주세요.");
        book.Removed.Add(matches[0].Copy());
        book.Sleep.Remove(matches[0]);
    }

    // Both clocks give the recorded span; both optional durations refine it into an estimate. Unknown stays null.
    internal static double? Minutes(SleepDay day)
    {
        if (day.Entry is { Latency: not null, AwakeMinutes: not null }) return day.Entry.EstimatedSleepMinutes;
        return day.Span is { } span ? span.EndMinute - span.StartMinute : null;
    }

    internal static string FormatDuration(double minutes)
    {
        int total = (int)minutes;
        return total < 60 ? $"{total}분" : total % 60 == 0 ? $"{total / 60}시간" : $"{total / 60}시간 {total % 60}분";
    }

    internal static string Average(IEnumerable<SleepDay> days)
    {
        var known = days.Select(Minutes).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return $"평균 수면 시간 {(known.Length == 0 ? "—" : FormatDuration(Math.Round(known.Average())))}";
    }
}

// The plugin's records, shared by every widget and agent tool and saved whole in the plugin's own encrypted store (ADR 0083).
internal sealed class SleepRecords
{
    private sealed record StoreFile(int SchemaVersion, List<SleepEntry>? Sleep, List<SleepEntry>? Removed);
    private readonly IPluginStore store;
    private SleepBook book;
    internal IReadOnlyList<SleepEntry> Entries => book.Sleep;
    internal IReadOnlyList<SleepEntry> Removed => book.Removed;
    internal event Action? Changed;

    // An unreadable store throws, so the plugin reports it and never overwrites the file.
    internal SleepRecords(IPluginStore store)
    {
        this.store = store;
        book = Parse(store.Read());
    }

    private static SleepBook Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new();
        try {
            var file = JsonSerializer.Deserialize<StoreFile>(text);
            if (file is not { SchemaVersion: 1, Sleep: { } sleep, Removed: { } removed } || sleep.Concat(removed).Any(entry => entry == null)) throw new InvalidDataException("수면 저장소 형식이 올바르지 않습니다.");
            return new() { Sleep = sleep, Removed = removed };
        }
        catch (JsonException ex) { throw new InvalidDataException("수면 저장소를 읽을 수 없습니다.", ex); }
    }

    // ArgumentException from the change, or false from the save, leaves the records as they were.
    internal bool Commit(Action<SleepBook> change)
    {
        var next = book.Copy();
        change(next);
        if (!store.Write(JsonSerializer.Serialize(new StoreFile(1, next.Sleep.OrderBy(entry => entry.Date).ToList(), next.Removed)))) return false;
        book = next;
        Changed?.Invoke();
        return true;
    }
}
