using System.Runtime.InteropServices;
using System.Text.Json;
using System.ComponentModel;

namespace Facet;

public sealed class Store
{
    public string Root { get; }
    public string PathName => Path.Combine(Root, "records.dat");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string? RecoveryMessage { get; private set; }
    public Store(string root) { Root = root; Directory.CreateDirectory(root); }
    public string CalendarPathName => Path.Combine(Root, "calendar.dat");
    public string BackupDirectory => Path.Combine(Root, "backups");

    public AppData Load()
    {
        RenamePluginData();
        var data = LoadRecords();
        MoveCalendar(data);
        MoveConditions(data);
        MoveSleep(data);
        MoveChat(data);
        return data;
    }

    // Plugin files and folders saved under the old first-party ids take the facet.* names once; an existing new name wins (ADR 0096).
    private void RenamePluginData()
    {
        string folder = Path.Combine(Root, "plugin-data");
        if (!Directory.Exists(folder)) return;
        foreach (string entry in Directory.GetFileSystemEntries(folder, PluginIds.RenamedPrefix + "*")) {
            string target = Path.Combine(folder, PluginIds.Current(Path.GetFileName(entry)));
            if (File.Exists(target) || Directory.Exists(target)) continue;
            try { if (Directory.Exists(entry)) Directory.Move(entry, target); else File.Move(entry, target); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.TraceWarning("Plugin data rename failed: " + ex.Message); }
        }
    }

    // Schedules, todos and notes from calendar.dat (ADR 0076), and any still in records.dat, move once into the schedule
    // plugin's store. The copy is verified before calendar.dat becomes calendar.dat.moved and the records forget the days (ADR 0101).
    private void MoveCalendar(AppData data)
    {
        var stored = ReadCalendar();
        if (stored == null && data.CalendarDays.Count == 0 && data.SchedulePresets == null) return;
        try {
            BackupRecords("records-before-schedule-store-");
            var store = PluginFileStore.For(Root, LegacySchedule.Plugin);
            string text = LegacySchedule.Merge(store.Read(), stored, data.CalendarDays, data.SchedulePresets);
            if (!store.Write(text) || store.Read() != text) throw new IOException("복사한 일정 저장소가 원본과 다릅니다.");
            // A never-edited layout showed the calendar; it keeps showing it as the plugin's widget.
            data.Workspace ??= WorkspaceLayouts.LegacyDefault();
            data.CalendarDays = []; data.SchedulePresets = null;
            WriteProtected(PathName, Protect(RecordsBytes(data), true));
            if (File.Exists(CalendarPathName)) File.Move(CalendarPathName, CalendarPathName + ".moved", true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or JsonException or ArgumentException or InvalidDataException) {
            throw new InvalidDataException("기록을 일정 저장소로 옮기지 못했습니다. 기존 기록은 보존되어 있습니다. " + ex.Message, ex);
        }
    }
    // Records still in records.dat (first run, or written by an older build) are backed up, copied and verified before they leave it. ADR 0081.
    private void MoveConditions(AppData data)
    {
        if (data.Conditions.Count == 0) return;
        try {
            BackupRecords("records-before-condition-store-");
            var store = PluginFileStore.For(Root, LegacyConditions.Plugin);
            string text = LegacyConditions.Merge(store.Read(), data.Conditions);
            if (!store.Write(text) || store.Read() != text) throw new IOException("복사한 컨디션 저장소가 원본과 다릅니다.");
            data.Conditions = [];
            WriteProtected(PathName, Protect(RecordsBytes(data), true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or JsonException or ArgumentException or InvalidDataException) {
            throw new InvalidDataException("기록을 컨디션 저장소로 옮기지 못했습니다. 기존 기록은 보존되어 있습니다. " + ex.Message, ex);
        }
    }

    // The same one-time move for sleep records and their deleted archive. ADR 0083.
    private void MoveSleep(AppData data)
    {
        if (data.Sleep.Count == 0 && data.RemovedSleep.Count == 0) return;
        try {
            BackupRecords("records-before-sleep-store-");
            var store = PluginFileStore.For(Root, LegacySleep.Plugin);
            string text = LegacySleep.Merge(store.Read(), data.Sleep, data.RemovedSleep);
            if (!store.Write(text) || store.Read() != text) throw new IOException("복사한 수면 저장소가 원본과 다릅니다.");
            data.Sleep = []; data.RemovedSleep = [];
            WriteProtected(PathName, Protect(RecordsBytes(data), true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or JsonException or ArgumentException or InvalidDataException) {
            throw new InvalidDataException("기록을 수면 저장소로 옮기지 못했습니다. 기존 기록은 보존되어 있습니다. " + ex.Message, ex);
        }
    }

    // The same one-time move for chat references; the agents keep the transcripts. ADR 0084.
    private void MoveChat(AppData data)
    {
        if (data.AgentChatReferences.Count == 0) return;
        try {
            BackupRecords("records-before-chat-store-");
            var store = PluginFileStore.For(Root, LegacyChat.Plugin);
            string text = LegacyChat.Merge(store.Read(), data.AgentChatReferences);
            if (!store.Write(text) || store.Read() != text) throw new IOException("복사한 채팅 저장소가 원본과 다릅니다.");
            data.AgentChatReferences = [];
            WriteProtected(PathName, Protect(RecordsBytes(data), true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or JsonException or ArgumentException or InvalidDataException) {
            throw new InvalidDataException("기록을 채팅 저장소로 옮기지 못했습니다. 기존 기록은 보존되어 있습니다. " + ex.Message, ex);
        }
    }

    private void BackupRecords(string prefix = "records-before-calendar-store-")
    {
        if (!File.Exists(PathName)) return;
        Directory.CreateDirectory(BackupDirectory);
        string target = Path.Combine(BackupDirectory, prefix + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".dat");
        File.Copy(PathName, target, false);
        Decode(File.ReadAllBytes(target));
    }

    private CalendarStoreFile? ReadCalendar()
    {
        if (!File.Exists(CalendarPathName)) return null;
        try { return DecodeCalendar(File.ReadAllBytes(CalendarPathName)); }
        catch (Exception original) when (IsUnreadable(original))
        {
            foreach (var backup in Enumerable.Range(1, 5).Select(i => CalendarPathName + $".bak{i}"))
            {
                if (!File.Exists(backup)) continue;
                try
                {
                    var days = DecodeCalendar(File.ReadAllBytes(backup));
                    File.Move(CalendarPathName, CalendarPathName + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                    File.Copy(backup, CalendarPathName);
                    RecoveryMessage = "최근 달력 기록을 읽지 못해 이전 백업을 복구했습니다. 원본은 보존되어 있습니다.";
                    return days;
                }
                catch (Exception e) when (IsUnreadable(e)) { }
            }
            throw new InvalidDataException("달력 저장소와 백업을 읽을 수 없습니다. 기존 파일을 덮어쓰지 않았습니다. 같은 Windows 계정인지 확인해 주세요.", original);
        }
    }

    private static bool IsUnreadable(Exception e) => e is IOException or JsonException or Win32Exception or InvalidDataException or ArgumentException;

    private static CalendarStoreFile DecodeCalendar(byte[] bytes)
    {
        var file = JsonSerializer.Deserialize<CalendarStoreFile>(Protect(bytes, false), Json);
        CalendarNotes.ValidateStore(file);
        file!.SchedulePresets = CleanPresets(file.SchedulePresets);
        return file;
    }

    private static List<SchedulePreset>? CleanPresets(List<SchedulePreset>? presets) =>
        presets?.Where(p => p != null && SchedulePreset.IsValid(p)).DistinctBy(p => p.Key).Take(SchedulePreset.MaxCount).ToList();

    // Keeps the other records as they are; the days live in the schedule plugin's store.
    private static byte[] RecordsBytes(AppData data)
    {
        var node = JsonSerializer.SerializeToNode(data, Json)!;
        node[nameof(AppData.CalendarDays)] = new System.Text.Json.Nodes.JsonArray();
        node[nameof(AppData.SchedulePresets)] = null;
        return JsonSerializer.SerializeToUtf8Bytes(node, Json);
    }

    private AppData LoadRecords()
    {
        if (!File.Exists(PathName)) return new();
        try { return Decode(File.ReadAllBytes(PathName)); }
        catch (Exception original) when (original is IOException or JsonException or Win32Exception or InvalidDataException or ArgumentException)
        {
            foreach (var backup in Enumerable.Range(1, 5).Select(i => PathName + $".bak{i}"))
            {
                if (!File.Exists(backup)) continue;
                try
                {
                    var data = Decode(File.ReadAllBytes(backup));
                    File.Move(PathName, PathName + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                    RecoveryMessage = "최근 기록을 읽지 못해 이전 백업을 복구했습니다. 원본은 보존되어 있습니다.";
                    WriteProtected(PathName, EncodeRecords(data)); return data;
                }
                catch (Exception e) when (e is IOException or JsonException or Win32Exception or InvalidDataException or ArgumentException) { }
            }
            throw new InvalidDataException("기록과 백업을 읽을 수 없습니다. 기존 파일을 덮어쓰지 않았습니다. 같은 Windows 계정인지 확인해 주세요.", original);
        }
    }
    public static AppData Decode(byte[] bytes)
    {
        var d = JsonSerializer.Deserialize<AppData>(Protect(bytes, false), Json) ?? throw new InvalidDataException("빈 기록");
        if (d.SchemaVersion is not (1 or 2) || d.Settings is null || d.Sleep is null || d.Work is null || d.Projects is null || d.ProjectJournals is null || d.Delivered is null || d.Notices is null || d.RemovedSleep is null || (d.Plan != null && d.Plan.Entries == null))
            throw new InvalidDataException("지원하지 않는 기록 형식");
        d.SchemaVersion = 2;
        if (d.CalendarDays == null || d.CalendarDays.Any(e => e == null) || d.CalendarDays.Select(e => e.Date).Distinct().Count() != d.CalendarDays.Count)
            throw new InvalidDataException("날짜별 할 일과 노트 형식이 올바르지 않습니다.");
        foreach (var day in d.CalendarDays) CalendarNotes.Validate(day);
        d.SchedulePresets = CleanPresets(d.SchedulePresets);
        if (d.Workspace != null) d.Workspace = WorkspaceLayouts.Normalize(d.Workspace);
        if (d.EnabledPlugins != null) d.EnabledPlugins = d.EnabledPlugins.Where(p => p != null && !string.IsNullOrEmpty(p.Id) && !string.IsNullOrEmpty(p.Fingerprint)).DistinctBy(p => p.Id).ToList();
        PluginData.Normalize(d);
        UserReminders.ValidateAll(d.Reminders);
        if (d.Conditions == null || d.Conditions.Any(entry => entry == null))
            throw new InvalidDataException("컨디션 기록 형식이 올바르지 않습니다.");
        var s = d.Settings;
        if (d.AgentChatReferences == null || d.AgentChatReferences.Count > 40 || d.AgentChatReferences.Any(x => x == null || !Guid.TryParse(x.ThreadId, out _) || x.HostId is not ("local" or "claude")))
            throw new InvalidDataException("채팅 참조 형식이 올바르지 않습니다.");
        return d;
    }
    public void Save(AppData data)
    {
        WriteProtected(PathName, Protect(RecordsBytes(data), true));
    }

    // The whole AppData as records.dat holds it before the days move out.
    internal static byte[] EncodeRecords(AppData data) => Protect(JsonSerializer.SerializeToUtf8Bytes(data, Json), true);

    internal static void WriteProtected(string path, byte[] bytes)
    {
        string tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { stream.Write(bytes); stream.Flush(true); }
        if (File.Exists(path))
        {
            for (int i = 4; i >= 1; i--)
                if (File.Exists(path + $".bak{i}")) File.Copy(path + $".bak{i}", path + $".bak{i + 1}", true);
            File.Replace(tmp, path, path + ".bak1", true);
        }
        else File.Move(tmp, path);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    internal static byte[] Protect(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool ok = encrypt ? CryptProtectData(ref input, "Facet", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, output.Size); return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
    }
}
