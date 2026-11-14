using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Facet;

internal sealed class QuickLaunchIcons
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellInfo {
        public IntPtr Icon;
        public int Index;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, out ShellInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    private readonly Dictionary<string, ImageSource?> cache = new(StringComparer.OrdinalIgnoreCase);

    internal ImageSource? Get(string path)
    {
        if (cache.TryGetValue(path, out var value)) return value;
        ShellInfo info = default;
        try {
            if (SHGetFileInfo(path, 0, out info, (uint)Marshal.SizeOf<ShellInfo>(), 0x100 | 1) != IntPtr.Zero && info.Icon != IntPtr.Zero) {
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze(); value = bitmap;
            }
        } catch (Exception ex) when (ex is ArgumentException or ExternalException or InvalidOperationException) { }
        finally { if (info.Icon != IntPtr.Zero) DestroyIcon(info.Icon); }
        if (cache.Count >= 128) cache.Clear();
        cache[path] = value; return value;
    }
}
