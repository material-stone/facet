using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Facet;
internal static class Diagnostics
{
    public static void Capture(Controller app)
    {
        // Opt-in QA only. The regular app never writes screenshots or plaintext health records.
        var report = new { at = DateTime.Now, hotkeys = app.HotkeyStatus,
            overlay = Native.WindowState(app.Overlay) };
        File.WriteAllText(Path.Combine(app.Store.Root, "runtime-check.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        var view = (FrameworkElement)app.Overlay.Content;
        double width = app.Overlay.ActualWidth > 0 ? app.Overlay.ActualWidth : SystemParameters.PrimaryScreenWidth;
        double height = app.Overlay.ActualHeight > 0 ? app.Overlay.ActualHeight : SystemParameters.PrimaryScreenHeight;
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        // Export only the tint, including in opt-in diagnostics; never captured desktop pixels.
        var tint = (view as Grid)?.Children.OfType<Border>().FirstOrDefault()?.Background ?? Brushes.Transparent;
        var safe = new DrawingVisual();
        using (var drawing = safe.RenderOpen()) drawing.DrawRectangle(tint, null, new Rect(0, 0, width, height));
        bitmap.Render(safe);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(app.Store.Root, "overlay-render.png")); png.Save(file);
    }
}
