using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Input;
using System.Diagnostics;
using Forms = System.Windows.Forms;

namespace Facet;

public static class Native
{
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RectNative rect);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insert, int x, int y, int width, int height, uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    public static void DarkTitleBar(Window window)
    {
        int enabled = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref enabled, sizeof(int));
    }
    public static void Passive(Window w, bool through)
    {
        var h = new WindowInteropHelper(w).Handle;
        int style = GetWindowLong(h, -20) | 0x08000000 | 0x80;
        style = through ? style | 0x20 : style & ~0x20;
        SetWindowLong(h, -20, style);
    }
    public static void Clamp(Window w, bool reset = false)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero || !GetWindowRect(h, out var r)) return;
        var area = (reset ? Forms.Screen.PrimaryScreen! : Forms.Screen.FromHandle(h)).WorkingArea;
        int width = r.Right - r.Left, height = r.Bottom - r.Top;
        int x = reset ? area.Right - width - 24 : Math.Clamp(r.Left, area.Left, Math.Max(area.Left, area.Right - width));
        int y = reset ? area.Top + 24 : Math.Clamp(r.Top, area.Top, Math.Max(area.Top, area.Bottom - height));
        SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, 0x15);
    }
    public static IntPtr ForegroundWindow => GetForegroundWindow();
    internal static void AboveTaskbar(Window window, int bottomOffset = 16)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(hwnd, out var rect)) return;
        var area = Forms.Screen.PrimaryScreen!.WorkingArea;
        var scale = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice;
        int gapY = (int)Math.Round(bottomOffset * (scale?.M22 ?? 1));
        int x = area.Left + Math.Max(0, (area.Width - (rect.Right - rect.Left)) / 2);
        int y = Math.Max(area.Top, area.Bottom - (rect.Bottom - rect.Top) - gapY);
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, 0x15);
    }
    internal static bool IsCaptureToolForeground(Func<IntPtr, uint, bool>? matchesWindow = null)
    {
        // Only the foreground counts: a recording keeps Snipping Tool windows visible the whole time.
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd) ||
            DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        GetWindowThreadProcessId(hwnd, out uint id);
        if (matchesWindow != null) return matchesWindow(hwnd, id);
        if (id == Environment.ProcessId) return false;
        try {
            using var process = Process.GetProcessById((int)id);
            return new[] { "SnippingTool", "ScreenClippingHost", "ScreenSketch", "SnipAndSketch" }
                .Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { } // The window's process may have just exited.
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        return false;
    }
    public static void RestoreForeground(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero && IsWindow(hwnd)) SetForegroundWindow(hwnd);
    }
    public static void CoverScreen(Window window, bool foreground = false)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var bounds = (window is OverlayWindow ? Forms.Screen.PrimaryScreen! :
            Forms.Screen.FromHandle(foreground ? GetForegroundWindow() : hwnd)).Bounds;
        // Native pixels include the taskbar and preserve mixed-DPI/negative monitor coordinates.
        // Resizing must not restore Topmost or reorder a window yielded to Snipping Tool.
        SetWindowPos(hwnd, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, window is OverlayWindow && !window.Topmost ? 0x14u : 0x10u);
    }
    public static bool FullscreenOther()
    {
        var h = GetForegroundWindow();
        GetWindowThreadProcessId(h, out var id);
        if (id == Environment.ProcessId || h == IntPtr.Zero || !GetWindowRect(h, out var r)) return false;
        var b = Forms.Screen.FromHandle(h).Bounds;
        return Math.Abs(r.Left - b.Left) <= 2 && Math.Abs(r.Top - b.Top) <= 2 && Math.Abs(r.Right - b.Right) <= 2 && Math.Abs(r.Bottom - b.Bottom) <= 2;
    }
    public static object WindowState(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        int style = GetWindowLong(h, -20);
        GetWindowRect(h, out var r);
        return new { visible = w.IsVisible, topmost = (style & 8) != 0, noActivate = (style & 0x08000000) != 0, clickThrough = (style & 0x20) != 0,
            width = r.Right - r.Left, height = r.Bottom - r.Top, x = r.Left, y = r.Top };
    }
    public static (uint modifiers, uint key) ParseHotkey(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new ArgumentException("단축키는 Ctrl+Alt+S 또는 Win+Alt+Space처럼 보조키와 영문/숫자/F키/Space/End로 입력하세요.");
        uint modifiers = 0x4000;
        foreach (var p in parts[..^1])
            modifiers |= p.ToLowerInvariant() switch { "ctrl" => 2u, "alt" => 1u, "shift" => 4u, "win" => 8u, _ => throw new ArgumentException("보조키는 Ctrl, Alt, Shift, Win을 사용할 수 있습니다.") };
        string last = parts[^1].ToUpperInvariant();
        uint key;
        if (last.Length == 1 && char.IsAsciiLetterOrDigit(last[0])) key = last[0];
        else if (last == "SPACE") key = 0x20;
        else if (last == "END") key = 0x23;
        else if (last.StartsWith('F') && int.TryParse(last[1..], out int f) && f is >= 1 and <= 12) key = (uint)(111 + f);
        else throw new ArgumentException("단축키 마지막 키는 영문, 숫자, F1~F12, Space, End를 사용하세요.");
        if ((modifiers & 11) == 0) throw new ArgumentException("Ctrl, Alt 또는 Win을 포함해 주세요.");
        return (modifiers, key);
    }
}
