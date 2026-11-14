using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Facet;

internal static partial class Program
{
    [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
    private static extern bool PositionFocusTestWindow(IntPtr hwnd, IntPtr insert, int x, int y, int width, int height, uint flags);

    // Opt-in native focus checks: no keyboard injection, desktop capture or patterned backgrounds.
    private static void CheckOverlayFocus(Controller controller)
    {
        var overlay = controller.Overlay;
        var settings = controller.Data.Settings;
        var priorProbe = overlay.CaptureToolForegroundProbe;
        var priorFactory = overlay.BackdropSourceFactory;
        double priorBlur = settings.OverlayBlurRadius;
        int priorShow = settings.OverlayShowMilliseconds, priorHide = settings.OverlayHideMilliseconds;
        using var origin = new KeyboardHost();
        using var target = new KeyboardHost();
        origin.Window.Background = target.Window.Background = Brushes.DimGray;
        var primary = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var sourceScreen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => !s.Primary)
            ?? System.Windows.Forms.Screen.PrimaryScreen!;
        void CheckBounds(string label)
        {
            var state = JsonSerializer.SerializeToElement(Native.WindowState(overlay));
            Assert(state.GetProperty("x").GetInt32() == primary.X && state.GetProperty("y").GetInt32() == primary.Y &&
                state.GetProperty("width").GetInt32() == primary.Width && state.GetProperty("height").GetInt32() == primary.Height, label);
        }
        try {
            settings.OverlayBlurRadius = 0; settings.OverlayShowMilliseconds = 0;
            overlay.CaptureToolForegroundProbe = () => false;
            origin.Window.Show(); target.Window.Show();
            Assert(PositionFocusTestWindow(new WindowInteropHelper(origin.Window).Handle, IntPtr.Zero,
                sourceScreen.Bounds.X + 30, sourceScreen.Bounds.Y + 30, 280, 140, 0x14), "place origin on secondary monitor when available");
            Console.WriteLine($"Monitor check: {System.Windows.Forms.Screen.AllScreens.Length} monitors; origin primary={sourceScreen.Primary}");
            foreach (bool quick in new[] { false, true }) foreach (int hide in new[] { 0, 160 }) {
                settings.OverlayHideMilliseconds = hide;
                Assert(origin.Focus(), "origin owns foreground before opening overlay");
                if (quick) controller.ToggleQuickLaunch(); else controller.ResetOverlay();
                Pump(30);
                Assert(overlay.IsVisible && overlay.IsActive, "overlay opens and activates in mode " + quick);
                CheckBounds("normal and quick launch cover only primary monitor");
                Native.CoverScreen(overlay); CheckBounds("placement refresh keeps primary monitor bounds");
                Assert(target.Focus(), "new focus target receives activation");
                Pump(hide + 60);
                Assert(overlay.IsVisible && settings.OverlayVisible && !overlay.IsActive && target.Window.IsActive,
                    "focus loss keeps overlay visible and new app focused with hide duration " + hide);
                controller.Tick(false);
                Assert(overlay.IsVisible && settings.OverlayVisible && target.Window.IsActive,
                    "periodic refresh preserves overlay visibility without taking focus");
                controller.HideOverlay(restore: false); Pump(hide + 60);
                Assert(!overlay.IsVisible && !settings.OverlayVisible && target.Window.IsActive,
                    "explicit hide still dismisses overlay and preserves new app focus");
            }
            foreach (int radius in new[] { 0, 18 }) {
                settings.OverlayBlurRadius = radius;
                settings.OverlayShowMilliseconds = settings.OverlayHideMilliseconds = 280;
                overlay.BackdropSourceFactory = () => new ControlledBackdrop();
                Assert(origin.Focus(), "origin receives focus before show-time deactivation check");
                bool movedDuringShow = false;
                EventHandler moveFocus = (_, _) => {
                    if (movedDuringShow) return;
                    movedDuringShow = true;
                    target.Window.Activate();
                    Native.RestoreForeground(new WindowInteropHelper(target.Window).Handle);
                    Assert(settings.OverlayVisible, "deactivation inside Show does not cancel the opening request");
                };
                overlay.Activated += moveFocus;
                try {
                    controller.ResetOverlay();
                    AwaitBackdrop(() => overlay.IsVisible && movedDuringShow); Pump(360);
                    Assert(overlay.IsVisible && settings.OverlayVisible,
                        "show-time focus loss keeps overlay open with blur radius " + radius);
                } finally { overlay.Activated -= moveFocus; }
                controller.HideOverlay(restore: false); Pump(360);
            }
            Assert(origin.Focus(), "origin receives focus before switching presentation mode");
            settings.OverlayHideMilliseconds = 0;
            controller.ToggleQuickLaunch();
            var snapshot = new ControlledBackdrop();
            overlay.BackdropSourceFactory = () => snapshot;
            settings.OverlayBlurRadius = 18;
            controller.ToggleOverlay();
            Assert(settings.OverlayVisible, "snapshot Hide during quick-launch to normal switch preserves visibility request");
            AwaitBackdrop(() => overlay.IsVisible && snapshot.Calls == 1);
            Assert(overlay.IsActive && settings.OverlayVisible, "synthetic snapshot reveals and focuses the normal overlay");
            CheckBounds("snapshot reveal stays on primary monitor");
            controller.HideOverlay(restore: false);
            CheckCaptureToolPriority(controller);
        }
        finally {
            controller.HideOverlay(restore: false); Pump(220);
            overlay.SetQuickLaunch(false); overlay.CaptureToolForegroundProbe = priorProbe;
            overlay.BackdropSourceFactory = priorFactory;
            settings.OverlayBlurRadius = priorBlur; settings.OverlayShowMilliseconds = priorShow; settings.OverlayHideMilliseconds = priorHide;
        }
    }
}
