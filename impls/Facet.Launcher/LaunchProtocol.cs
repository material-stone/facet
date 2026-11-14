using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Facet.Deployment;

internal static class LaunchProtocol
{
    public const string DisplayName = "Facet";
    // Persistent identities remain compatible with earlier releases (ADR 0056).
    public const string AppId = "MaterialStone.DailyDock";
    public const string LauncherName = "Facet.Launcher.exe";
    public const string LauncherEnvironment = "DAILYDOCK_LAUNCHER";
    public static bool IsBackgroundStart(string[] args) => args.Contains("--tray") || args.Contains("--autostart");
    public static string AutostartCommand(string executable) => $"\"{executable}\" --autostart --tray";
    public static string DataRoot(string[] args)
    {
        int index = Array.IndexOf(args, "--data-dir");
        return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : DefaultRoot(LocalData);
    }
    private static string LocalData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    // Records live in MaterialStone/Facet; the folder from before the rename is used until the app moves it (ADR 0099).
    internal static string DefaultRoot(string local)
    {
        string current = Path.Combine(local, "MaterialStone", "Facet"), renamed = Path.Combine(local, "MaterialStone", "DailyDock");
        return !Directory.Exists(current) && Directory.Exists(renamed) ? renamed : current;
    }
    // Moves the old records folder once, only while no app instance uses it. A failed move keeps the old folder in use.
    public static void MoveRenamedRoot(string[] args) { if (Array.IndexOf(args, "--data-dir") < 0) MoveRenamedRoot(LocalData); }
    internal static bool MoveRenamedRoot(string local)
    {
        string current = Path.Combine(local, "MaterialStone", "Facet"), renamed = Path.Combine(local, "MaterialStone", "DailyDock");
        if (Directory.Exists(current) || !Directory.Exists(renamed)) return false;
        if (System.Threading.Mutex.TryOpenExisting(Instance(renamed), out var running)) { running.Dispose(); return false; }
        try { Directory.Move(renamed, current); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant())))[..20];
    public static string Instance(string root) => @"Local\DailyDock-" + Hash(root);
    public static string Open(string root) => @"Local\DailyDock-open-" + Hash(root);
    public static string Update(string root) => @"Local\DailyDock-update-" + Hash(root);
    public static string Cancel(string root) => @"Local\DailyDock-update-cancel-" + Hash(root);
    public static string Version(string root, string directory) => @"Local\DailyDock-version-" + Hash(root) + "-" + Hash(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar));
    public static string? FindLauncher()
    {
        string? configured = Environment.GetEnvironmentVariable(LauncherEnvironment);
        if (configured is not null && Path.IsPathFullyQualified(configured) && File.Exists(configured)
            && Path.GetFileName(configured).Equals(LauncherName, StringComparison.OrdinalIgnoreCase)) return configured;
        string besideReleases = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", LauncherName));
        return File.Exists(besideReleases) ? besideReleases : null;
    }
}
