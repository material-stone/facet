using System.Runtime.InteropServices;
using System.Drawing.Text;
using Forms = System.Windows.Forms;

namespace Facet.Design;

internal static class Typography
{
    internal static FontFamily Family => (FontFamily)Application.Current.FindResource("AppFontFamily");
    internal static Typeface Regular => new(Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    internal static Forms.ContextMenuStrip CreateTrayMenu()
    {
        var face = new TrayFace();
        var menu = new Forms.ContextMenuStrip { Font = face.Font };
        menu.Disposed += (_, _) => face.Dispose();
        return menu;
    }

    // ToolStrip uses GDI while System.Drawing.Font uses GDI+: keep the same embedded face in both.
    private sealed class TrayFace : IDisposable
    {
        private readonly PrivateFontCollection collection = new();
        private IntPtr memory, resource;
        internal System.Drawing.Font Font { get; private set; } = null!;
        internal TrayFace()
        {
            try {
                using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Facet;component/Design/Fonts/Pretendard-Regular.ttf")).Stream;
                using var buffer = new MemoryStream(); stream.CopyTo(buffer); byte[] bytes = buffer.ToArray();
                memory = Marshal.AllocCoTaskMem(bytes.Length); Marshal.Copy(bytes, 0, memory, bytes.Length);
                resource = AddFontMemResourceEx(memory, (uint)bytes.Length, IntPtr.Zero, out _);
                if (resource == IntPtr.Zero) throw new InvalidDataException("트레이 글꼴을 불러오지 못했습니다.");
                collection.AddMemoryFont(memory, bytes.Length);
                Font = new System.Drawing.Font(collection.Families[0], 9, System.Drawing.FontStyle.Regular);
            } catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            Font?.Dispose(); collection.Dispose();
            if (resource != IntPtr.Zero) { RemoveFontMemResourceEx(resource); resource = IntPtr.Zero; }
            if (memory != IntPtr.Zero) { Marshal.FreeCoTaskMem(memory); memory = IntPtr.Zero; }
        }
        [DllImport("gdi32.dll")] private static extern IntPtr AddFontMemResourceEx(IntPtr data, uint length, IntPtr reserved, out uint count);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveFontMemResourceEx(IntPtr resource);
    }
}
