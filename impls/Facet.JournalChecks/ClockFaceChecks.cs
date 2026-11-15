#if PLUGIN_CHECKS
using Facet.ClockPlugin;
using System.IO;
using Facet.Plugins;

internal static partial class Program
{
    private sealed class MemoryData(bool accept = true) : IWidgetData
    {
        public readonly Dictionary<string, string> Values = [];
        public bool Accept = accept;
        public IReadOnlyCollection<string> Keys => Values.Keys;
        public string? Get(string key) => Values.GetValueOrDefault(key);
        public bool Set(string key, string value) { if (Accept) Values[key] = value; return Accept; }
        public bool Remove(string key) => Values.Remove(key);
    }

    // The weekday and time widgets keep alignment, mode and style per placement (ADR 0102).
    private static void ClockFaceChecks()
    {
        var sunday = new DateTime(2026, 11, 15, 15, 30, 45);
        var first = new MemoryData(); var second = new MemoryData();
        var a = new ClockFaceView(new WidgetContext("a", "") { Data = first }, "시계", FaceChoices.Time);
        var b = new ClockFaceView(new WidgetContext("b", "") { Data = second }, "시계", FaceChoices.Time);
        Assert(a.Choice.Style == "classic" && a.Face is AnalogClockFace { Minimal: false }, "a new time widget starts on the classic analog face");
        Assert(a.Select(FaceChoices.Find(FaceChoices.Time, "digital", "plain")) && first.Values["mode"] == "digital" && first.Values["style"] == "plain", "choosing a face saves mode and style");
        Assert(a.Face is DigitalClockFace && b.Face is AnalogClockFace, "each placement keeps its own face");
        a.ShowTime(sunday); Assert(a.Face.Text == "15:30:45", "the digital face shows 24-hour time");
        var reopened = new ClockFaceView(new WidgetContext("a", "") { Data = first }, "시계", FaceChoices.Time);
        Assert(reopened.Face is DigitalClockFace, "the saved face comes back");
        first.Accept = false;
        Assert(!reopened.Select(FaceChoices.Time[1]) && reopened.Face is DigitalClockFace, "a failed save keeps the old face");
        second.Values["mode"] = "analog"; second.Values["style"] = "nonsense";
        Assert(new ClockFaceView(new WidgetContext("b", "") { Data = second }, "시계", FaceChoices.Time).Choice == FaceChoices.Time[0], "an unknown style falls back to the first face");

        var day = new ClockFaceView(new WidgetContext("d", "") { Data = new MemoryData() }, "요일", FaceChoices.Weekday);
        day.ShowTime(sunday);
        Assert(day.Face is WeekdayStripFace && day.Face.Text == "일요일", "the weekday widget starts on the strip and shows today");
        Assert(day.Select(FaceChoices.Find(FaceChoices.Weekday, "analog", "dial")) && day.Face is WeekdayDialFace, "the weekday widget has an analog dial");
        var dial = (WeekdayDialFace)day.Face;
        dial.Show(new DateTime(2026, 11, 15, 12, 0, 0)); Assert(Math.Abs(dial.HandDegrees) < 1e-9, "at noon the hand points at the middle of today's slice");
        dial.Show(new DateTime(2026, 11, 16, 0, 0, 0)); Assert(Math.Abs(dial.HandDegrees - 360.0 / 14) < 1e-9, "at midnight the hand is at the start of the new day's slice");
        Assert(day.BuildFacePicker() is System.Windows.Controls.StackPanel { Children.Count: 3 }, "the picker lists analog and digital rows");
        foreach (var v in new[] { a, b, reopened, day }) v.Dispose();
    }

    // Manual look at every weekday and time face on board-sized tiles: --clock-face-preview <output.png> [skin].
    private static int RunClockFacePreview(string[] args)
    {
        var ui = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        foreach (string resource in new[] { "Tokens", "Components" }) ui.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri($"pack://application:,,,/Facet;component/Design/{resource}.xaml") });
        Facet.Design.Skins.Apply(ui, args.Length > 2 ? args[2] : "dark");
        var panel = new System.Windows.Controls.WrapPanel { Width = 900 };
        var now = new DateTime(2026, 11, 15, 10, 9, 42);
        foreach (var (list, w, h) in new[] { (FaceChoices.Weekday, 280.0, 130.0), (FaceChoices.Time, 280.0, 200.0) })
            foreach (var choice in list) {
                var data = new MemoryData(); data.Set("mode", choice.Mode); data.Set("style", choice.Style); data.Set("alignment", "center-center");
                var view = new ClockFaceView(new WidgetContext("p", "") { Data = data }, "미리보기", list); view.ShowTime(now);
                var tile = new System.Windows.Controls.Border { Width = w, Height = h, Margin = new System.Windows.Thickness(10), Padding = new System.Windows.Thickness(12), CornerRadius = new System.Windows.CornerRadius(12), Child = view };
                tile.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Panel");
                panel.Children.Add(tile);
            }
        var host = new System.Windows.Controls.Border { Child = panel, Padding = new System.Windows.Thickness(10) };
        host.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Bg");
        var window = new System.Windows.Window { Content = host, SizeToContent = System.Windows.SizeToContent.WidthAndHeight, WindowStyle = System.Windows.WindowStyle.None, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        window.Show(); host.UpdateLayout();
        var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)host.ActualWidth, (int)host.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); image.Render(host);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using (var file = File.Create(args[1])) encoder.Save(file);
        window.Close();
        return 0;
    }
}
#endif
