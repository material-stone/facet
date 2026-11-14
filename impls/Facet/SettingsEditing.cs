using Microsoft.Win32;
using Facet.Deployment;

namespace Facet;

internal static class SettingsEditing
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Keep the existing Run value so renaming does not create a second startup entry.
    private const string StartupValue = "MaterialStoneDailyDock";

    internal static void Validate(Settings settings)
    {
        OverlayAppearance.Validate(settings);
        if (!Enum.IsDefined(settings.LauncherSearchEngine)) throw new ArgumentException("검색 엔진을 선택해 주세요.");
        Native.ParseHotkey(settings.OverlayHotkey);
    }

    internal static bool? StartupEnabled()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(StartupValue) is string; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { return null; }
    }

    internal static void SetStartup(bool enabled)
    {
        string? launcher = LaunchProtocol.FindLauncher();
        if (enabled && launcher is null) throw new InvalidOperationException("고정 실행기를 찾을 수 없습니다. Start-Facet.cmd로 실행한 뒤 자동 실행을 켜 주세요.");
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        SetStartup(key, enabled, launcher);
    }

    internal static void SetStartup(RegistryKey key, bool enabled, string? launcher)
    {
        if (!enabled) { key.DeleteValue(StartupValue, false); return; }
        if (launcher is null) throw new InvalidOperationException("고정 실행기를 찾을 수 없습니다. Start-Facet.cmd로 실행한 뒤 자동 실행을 켜 주세요.");
        string command = LaunchProtocol.AutostartCommand(launcher);
        if (key.GetValue(StartupValue) is string current && current == command) return;
        key.SetValue(StartupValue, command, RegistryValueKind.String);
    }

    internal static void RepairStartup(string[] args)
    {
        // Synthetic data instances must never change the user's login registration.
        if (args.Contains("--data-dir") || args.Contains("--storage-check")) return;
        string? launcher = LaunchProtocol.FindLauncher();
        if (launcher is null) return;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        RepairStartup(key, launcher);
    }

    internal static void RepairStartup(RegistryKey? key, string? launcher)
    {
        if (launcher is not null && key?.GetValue(StartupValue) is string) SetStartup(key, true, launcher);
    }
}
