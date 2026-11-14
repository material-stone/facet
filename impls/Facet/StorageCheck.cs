using System.Text;

namespace Facet;
internal static class StorageCheck
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        if (File.Exists(Path.Combine(root, "records.dat"))) throw new InvalidOperationException("저장 검증은 빈 디렉터리가 필요합니다.");
        var store = new Store(root);
        var data = new AppData { SchemaVersion = 1 };
        data.Settings.TherapyNote = "검증용 건강 기록";
        store.Save(data);
        if (Encoding.UTF8.GetString(File.ReadAllBytes(store.PathName)).Contains("검증용")) throw new Exception("Plaintext detected");
        if (store.Load().Settings.TherapyNote != "검증용 건강 기록") throw new Exception("Round trip failed");
        var migrated = store.Load();
        if (migrated.SchemaVersion != 2 || migrated.Plan != null) throw new Exception("V1 migration failed");
        migrated.Plan = new() { Start = new(2026,10,15), Entries = [new() { Note="계획 저장 검증" }] };
        store.Save(migrated);
        if (store.Load().Plan!.Entries.Single().Note != "계획 저장 검증") throw new Exception("Legacy plan persistence failed");
        data.Settings.TherapyNote = "두 번째 저장"; store.Save(data);
        if (!File.Exists(store.PathName + ".bak1")) throw new Exception("Backup missing");
        if (Store.Decode(File.ReadAllBytes(store.PathName + ".bak1")).Settings.OverlayTransparencyPercent != null) throw new Exception("Null changed");
        File.WriteAllText(store.PathName, "intentionally damaged test file");
        var recovered = new Store(root); var restored = recovered.Load();
        if (restored.Settings.TherapyNote != "검증용 건강 기록" || recovered.RecoveryMessage == null) throw new Exception("Recovery failed");
        var a = Native.ParseHotkey("Ctrl+Alt+S"); var b = Native.ParseHotkey("Alt+Ctrl+s");
        if (a != b) throw new Exception("Hotkey normalization failed");
        try { Native.ParseHotkey("Shift+S"); throw new Exception("Unsafe shortcut accepted"); } catch (ArgumentException) { }
        File.WriteAllText(Path.Combine(root, "check-ok.txt"), "PASS: DPAPI encryption, v1 migration, plan persistence, round trip, atomic backup, null preservation, corrupted-primary recovery, hotkey parsing.");
    }
}
