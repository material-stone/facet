#if PLUGIN_CHECKS
using System.IO;
using Facet.SystemMonitorPlugin;

internal static partial class Program
{
    // The system monitor: sampling arithmetic, gauge motion and the face's text (ADR 0106).
    private static void SystemMonitorChecks()
    {
        Assert(Math.Abs(SystemMath.CpuPercent(250, 1000, 0) - 75) < 1e-9, "CPU use is the non-idle share of kernel and user time");
        Assert(SystemMath.CpuPercent(0, 0, 0) == 0, "no elapsed CPU time reads as zero");
        Assert(SystemMath.Rate(100, 50, 1) == 0 && SystemMath.Rate(0, 2048, 2) == 1024, "a reset counter reads as no traffic and rates divide by elapsed time");
        Assert(SystemMath.FormatRate(500) == "500 B/s" && SystemMath.FormatRate(2048) == "2 KB/s" && SystemMath.FormatRate(3.5 * 1024 * 1024) == "3.5 MB/s", "rates are shown in B, KB and MB per second");

        var spring = new Spring { Target = 50 };
        double highest = 0;
        for (int n = 0; n < 240; n++) { spring.Step(1 / 60.0); highest = Math.Max(highest, spring.Value); }
        Assert(highest > 50 && Math.Abs(spring.Value - 50) < 0.5, "the gauge spring overshoots its reading and then settles on it");

        var gauge = new GaugeMotion();
        gauge.Feed(95);
        Assert(gauge.Alert && gauge.Pulse == 1 && gauge.Shake == 1, "crossing the limit starts the alert, the pulse and the shake");
        gauge.Feed(88); Assert(gauge.Alert, "the alert holds until the value falls clearly below the limit");
        gauge.Feed(80); Assert(!gauge.Alert, "the alert clears below the release level");
        gauge.Feed(95); for (int n = 0; n < 120; n++) gauge.Step(1 / 60.0);
        Assert(gauge.Pulse == 0 && gauge.Shake == 0 && gauge.Heat > 0.9, "the pulse and shake fade while the ring stays hot");
        var peak = new GaugeMotion();
        peak.Feed(95); for (int n = 0; n < 30; n++) peak.Step(1 / 60.0);
        peak.Feed(20); for (int n = 0; n < 60; n++) peak.Step(1 / 60.0);
        Assert(peak.Peak > 90, "the peak marker holds the recent high");
        for (int n = 0; n < 300; n++) peak.Step(1 / 60.0);
        Assert(peak.Peak < 30, "the peak marker falls back after holding");
        gauge.Boot(0.5); gauge.Feed(60); gauge.Step(0.3);
        Assert(gauge.Shown == 0, "a booting gauge waits its turn before rising");
        gauge.Feed(null); Assert(!gauge.Available && !gauge.Alert, "a missing reading shows the gauge as unavailable");

        var face = new MonitorFace();
        face.Boot();
        face.Feed(new SystemSample(42, null, 61, 9.76, 16, 1.5 * 1024 * 1024, 3000));
        for (int n = 0; n < 120; n++) face.Advance(1 / 60.0);
        Assert(face.MemoryLine == "9.8/16G" && face.NetworkLine == "↓ 1.5 MB/s  ↑ 3 KB/s", "the face shows memory and network lines");
        Assert(Math.Abs(face.Gauges[0].Shown - 42) < 2 && !face.Gauges[1].Available, "the CPU gauge reaches its reading and a missing GPU shows unavailable");

        using var sampler = new SystemSampler();
        sampler.Read(); Thread.Sleep(300);
        var live = sampler.Read();
        Assert(live is { Cpu: >= 0 and <= 100, Ram: > 0 and <= 100, RamTotalGb: > 0 }, "the sampler reads this machine's CPU and memory");
    }

    // Manual look at the monitor in a skin after a burst of load: --sysmon-preview <output.png> [skin].
    private static int RunSystemMonitorPreview(string[] args)
    {
        var ui = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        foreach (string resource in new[] { "Tokens", "Components" }) ui.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri($"pack://application:,,,/Facet;component/Design/{resource}.xaml") });
        Facet.Design.Skins.Apply(ui, args.Length > 2 ? args[2] : "dark");
        var face = new MonitorFace();
        var view = new System.Windows.Controls.Viewbox { Child = face };
        face.Boot();
        var random = new Random(7);
        for (int n = 0; n < 40; n++) {
            face.Feed(new SystemSample(30 + random.Next(30), 20 + random.Next(40), 58, 18.6, 32, random.Next(4 * 1024 * 1024), random.Next(400 * 1024)));
            for (int f = 0; f < 60; f++) face.Advance(1 / 60.0);
        }
        face.Feed(new SystemSample(97, 64, 58, 18.6, 32, 5.2 * 1024 * 1024, 220 * 1024));
        for (int f = 0; f < 9; f++) face.Advance(1 / 60.0);
        var tile = new System.Windows.Controls.Border { Width = 480, Height = 270, Padding = new System.Windows.Thickness(12), CornerRadius = new System.Windows.CornerRadius(12), Child = view };
        tile.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Panel");
        var host = new System.Windows.Controls.Border { Child = tile, Padding = new System.Windows.Thickness(10) };
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
