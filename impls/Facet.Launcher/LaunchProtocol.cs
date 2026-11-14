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
        return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaterialStone", "DailyDock");
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
