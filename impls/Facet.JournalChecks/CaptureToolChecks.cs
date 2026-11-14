using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Facet;

internal static partial class Program
{
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);

    private static System.Drawing.Color CaptureDesktopPixel(int x,int y)
    {
        using var bitmap=new System.Drawing.Bitmap(1,1);
        using(var graphics=System.Drawing.Graphics.FromImage(bitmap)) {
            var source=GetDC(IntPtr.Zero);var target=graphics.GetHdc();
            try {Assert(BitBlt(target,0,0,1,1,source,x,y,0x40CC0020),"native synthetic desktop pixel capture succeeds including layered windows");}
            finally {graphics.ReleaseHdc(target);ReleaseDC(IntPtr.Zero,source);}
        }
        return bitmap.GetPixel(0,0);
    }

    private static void CheckCaptureToolPriority(Controller controller)
    {
        var overlay=controller.Overlay;
        var priorProbe=overlay.CaptureToolForegroundProbe;
        var settings=controller.Data.Settings;
        double priorBlur=settings.OverlayBlurRadius;
        int priorShow=settings.OverlayShowMilliseconds,priorHide=settings.OverlayHideMilliseconds;
        var tool=new Window {Title="Synthetic capture tool",Width=220,Height=120,ShowInTaskbar=false};
        var focusTarget=new Window {Title="Synthetic focus target",Width=220,Height=120,ShowInTaskbar=false};
        var hwnd=new WindowInteropHelper(tool).EnsureHandle();int probes=0;
        bool Visible()=>Native.IsCaptureToolForeground((candidate,_)=>candidate==hwnd);
        bool NativeTopmost()=>System.Text.Json.JsonSerializer.SerializeToElement(Native.WindowState(overlay)).GetProperty("topmost").GetBoolean();
        try {
            controller.HideOverlay();
            controller.Commit(d=>{d.Settings.OverlayBlurRadius=0;d.Settings.OverlayShowMilliseconds=0;d.Settings.OverlayHideMilliseconds=0;});
            overlay.CaptureToolForegroundProbe=()=>{probes++;return Visible();};
            Assert(!Visible(),"native capture-tool detection ignores a window that is not foreground");
            controller.ToggleOverlay();Pump(180);
            Assert(overlay.Topmost,"overlay remains topmost before capture UI appears");
            tool.Show();tool.Activate();Pump(220);
            Assert(Visible() && !overlay.Topmost && !NativeTopmost() && tool.IsActive,"foreground capture UI lowers overlay without stealing tool focus");
            Native.CoverScreen(overlay);Pump(30);
            Assert(!overlay.Topmost && !NativeTopmost() && tool.IsActive,"monitor resize preserves yielded topmost and capture-tool focus");
            tool.WindowState=WindowState.Minimized;Pump(220);
            Assert(!Visible() && overlay.Topmost && NativeTopmost(),"minimizing the capture tool restores overlay topmost");
            tool.WindowState=WindowState.Normal;tool.Activate();Pump(220);
            Assert(!overlay.Topmost,"restored capture UI lowers overlay again");
            tool.Hide();Pump(220);
            Assert(!Visible() && overlay.Topmost,"hiding capture UI restores overlay topmost");
            controller.HideOverlay();Pump(30);int hiddenProbes=probes;Pump(220);
            Assert(probes==hiddenProbes,"hidden overlay stops polling for capture tools");
            tool.Show();tool.Activate();Pump(30);controller.ToggleOverlay();Pump(220);
            Assert(overlay.IsVisible && overlay.Topmost && NativeTopmost() && overlay.IsActive && tool.IsVisible,"explicit open comes above a visible capture tool and stays there, so it can be recorded");
            tool.Activate();Pump(220);
            Assert(!overlay.Topmost && tool.IsActive,"capture tool taking the foreground lowers the opened overlay");
            focusTarget.Show();focusTarget.Activate();Pump(220);
            Assert(overlay.Topmost && focusTarget.IsActive && tool.IsVisible,"a visible background capture tool does not hold the overlay down");
            tool.Close();Pump(220);
            Assert(overlay.Topmost && focusTarget.IsActive,"closing capture tool restores topmost without activating overlay");
        }
        finally {
            controller.HideOverlay();tool.Close();focusTarget.Close();overlay.CaptureToolForegroundProbe=priorProbe;
            controller.Commit(d=>{d.Settings.OverlayBlurRadius=priorBlur;d.Settings.OverlayShowMilliseconds=priorShow;d.Settings.OverlayHideMilliseconds=priorHide;});
        }
    }
}
