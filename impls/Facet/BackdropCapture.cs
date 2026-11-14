using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Media.Imaging;

namespace Facet;

// Feature-owned, in-memory capture. No screenshots are saved or passed to an Agent.
internal sealed class BackdropCapture : IBackdropCaptureSource
{
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, ImageSize;
        public int XPels, YPels;
        public uint Used, Important, Color;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, uint operation);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    private IntPtr memory, bitmap, previous, pixels;
    private int width, height;
    public int PixelWidth => width;
    public int PixelHeight => height;
    public WriteableBitmap? Frame { get; private set; }
    // Opt-in diagnostics records durations only; pixels never leave the capture owner.
    internal bool MeasureUpdates { get; set; }
    internal readonly record struct Timings(double AcquireMs, double SetupMs, double StretchMs, double FlushMs, double WriteMs, double ReleaseMs, double TotalMs);
    internal Timings? LastTimings { get; private set; }
    internal int MeasuredUpdateCount { get; private set; }

    public bool Update(System.Drawing.Rectangle bounds)
    {
        if(!Capture(bounds)) return false;
        long started=MeasureUpdates?Stopwatch.GetTimestamp():0;
        if(Frame==null || Frame.PixelWidth!=width || Frame.PixelHeight!=height)
            Frame=new WriteableBitmap(width,height,96,96,PixelFormats.Bgr32,null);
        Frame.WritePixels(new Int32Rect(0,0,width,height),pixels,width*height*4,width*4);
        if(MeasureUpdates && LastTimings is {} timing) {
            double write=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            LastTimings=timing with {WriteMs=write,TotalMs=timing.TotalMs+write};
        }
        return true;
    }

    public void CopyPixels(byte[] destination)
    {
        int length=checked(width*height*4);
        if(pixels==IntPtr.Zero || destination.Length<length) throw new ArgumentException("Capture buffer is unavailable or too small.");
        Marshal.Copy(pixels,destination,0,length);
    }

    public bool Capture(System.Drawing.Rectangle bounds)
    {
        long stamp=MeasureUpdates ? Stopwatch.GetTimestamp() : 0, started=stamp;
        double acquire=0,setup=0,stretch=0,flush=0,write=0;
        if (MeasureUpdates) LastTimings=null;
        var screen = GetDC(IntPtr.Zero);
        acquire=Elapsed(ref stamp);
        if (screen == IntPtr.Zero) return false;
        try
        {
            double scale = Math.Min(1, 1280d / Math.Max(bounds.Width, bounds.Height));
            int nextWidth = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
            int nextHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
            if (width != nextWidth || height != nextHeight || memory == IntPtr.Zero)
            {
                Dispose(); width = nextWidth; height = nextHeight;
                memory = CreateCompatibleDC(screen);
                var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
                bitmap = CreateDIBSection(screen, ref info, 0, out pixels, IntPtr.Zero, 0);
                if (memory == IntPtr.Zero || bitmap == IntPtr.Zero || pixels == IntPtr.Zero) { Dispose(); return false; }
                previous = SelectObject(memory, bitmap);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1)) { previous = IntPtr.Zero; Dispose(); return false; }
                SetStretchBltMode(memory, 4);
            }
            // SRCCOPY only: CAPTUREBLT can briefly hide the system cursor on every capture.
            // OverlayWindow reads its backdrop once while hidden, then keeps that frame.
            setup=Elapsed(ref stamp);
            if (!StretchBlt(memory, 0, 0, width, height, screen, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x00CC0020)) return false;
            stretch=Elapsed(ref stamp);
            GdiFlush();
            flush=Elapsed(ref stamp);
            return true;
        }
        finally {
            ReleaseDC(IntPtr.Zero, screen);
            if (MeasureUpdates) {
                MeasuredUpdateCount++;
                LastTimings=new(acquire,setup,stretch,flush,write,Elapsed(ref stamp),Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }
    }
    private static double Elapsed(ref long stamp)
    {
        if (stamp==0) return 0;
        long now=Stopwatch.GetTimestamp();double milliseconds=(now-stamp)*1000d/Stopwatch.Frequency;stamp=now;return milliseconds;
    }
    public void Dispose()
    {
        if (previous != IntPtr.Zero && memory != IntPtr.Zero) SelectObject(memory, previous);
        if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
        if (memory != IntPtr.Zero) DeleteDC(memory);
        previous = bitmap = memory = pixels = IntPtr.Zero;
        width = height = 0; Frame = null;
    }
}
