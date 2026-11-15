using System.Text.Json;
using Facet.Plugins;

namespace Facet.SchedulePlugin;

// Schedules, todos and notes by date plus presets, shared by every widget and agent tool and saved whole in the
// plugin's own encrypted store in the calendar file format (CalendarStoreFile). The rules stay in Core CalendarNotes (ADR 0101).
internal sealed class ScheduleRecords
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly IPluginStore store;
    // Only CalendarDays and SchedulePresets are used; the rules in Core work on AppData.
    internal AppData Data { get; private set; }
    internal event Action? Changed;

    // An unreadable store throws, so the plugin reports it and never overwrites the file.
    internal ScheduleRecords(IPluginStore store)
    {
        this.store = store;
        Data = Parse(store.Read());
    }

    internal static AppData Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new();
        try {
            var file = JsonSerializer.Deserialize<CalendarStoreFile>(text);
            CalendarNotes.ValidateStore(file);
            return new() { CalendarDays = file!.Days, SchedulePresets = file.SchedulePresets };
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { throw new InvalidDataException("일정 저장소를 읽을 수 없습니다.", ex); }
    }

    internal static string Serialize(AppData data) =>
        JsonSerializer.Serialize(new CalendarStoreFile { Days = data.CalendarDays.OrderBy(day => day.Date).ToList(), SchedulePresets = data.SchedulePresets }, Json);

    // ArgumentException from the change, or false from the save, leaves the records as they were.
    internal bool Commit(Action<AppData> change)
    {
        var next = new AppData { CalendarDays = Data.CalendarDays.Select(CalendarNotes.Copy).ToList(), SchedulePresets = Data.SchedulePresets?.Select(p => p.Copy()).ToList() };
        change(next);
        if (!store.Write(Serialize(next))) return false;
        Data = next;
        Changed?.Invoke();
        return true;
    }
}
