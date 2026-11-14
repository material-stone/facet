using System.Windows.Controls.Primitives;

namespace Facet.Design;

// F8 tools for staging a shot of the voyage. Session only: nothing here is saved.
internal sealed class VoyageDebugPanel : Border
{
    internal VoyageDebugPanel(SpaceVoyage voyage)
    {
        HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Top;
        Margin = new(0, 48, 24, 0); Padding = new(12); Width = 220; CornerRadius = new(8); BorderThickness = new(1);
        SetResourceReference(BackgroundProperty, "Panel"); SetResourceReference(BorderBrushProperty, "Mint");
        var spawn = new Button { Content = "행성 생성", Height = 36, HorizontalAlignment = HorizontalAlignment.Stretch };
        var body = new StackPanel();
        body.Children.Add(spawn);
        void AddSlider(double minimum, double maximum, double value, double tick, Func<double, string> describe, Action<double> apply)
        {
            var label = new TextBlock { Margin = new(0, 12, 0, 4), Text = describe(value) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, TickFrequency = tick, IsSnapToTickEnabled = true, TickPlacement = TickPlacement.BottomRight };
            slider.ValueChanged += (_, e) => { apply(e.NewValue); label.Text = describe(e.NewValue); };
            body.Children.Add(label); body.Children.Add(slider);
        }
        // The next press uses these; a planet already out there stays where it is.
        var spot = voyage.SpawnSpot;
        AddSlider(-100, 100, spot.Across * 100, 10, v => $"생성 가로 {v:+0;-0;0}%", v => voyage.SpawnSpot = voyage.SpawnSpot with { Across = v / 100 });
        AddSlider(-100, 100, spot.Up * 100, 10, v => $"생성 세로 {v:+0;-0;0}%", v => voyage.SpawnSpot = voyage.SpawnSpot with { Up = v / 100 });
        AddSlider(VoyageSpot.MinReach, VoyageSpot.MaxReach, spot.Reach, 1, v => $"생성 거리 행성 크기 x{v:0}", v => voyage.SpawnSpot = voyage.SpawnSpot with { Reach = v });
        AddSlider(1, SpaceVoyage.MaxTimeScale, voyage.TimeScale, 1, v => $"빨리감기 x{v:0}", v => voyage.TimeScale = v);
        spawn.Click += async (_, _) => {
            spawn.IsEnabled = false; spawn.Content = "만드는 중…";
            try { await voyage.SpawnNear(); spawn.Content = "행성 생성"; }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Debug planet failed: " + ex.Message); spawn.Content = "실패 · 다시 시도"; }
            spawn.IsEnabled = true;
        };
        Child = body;
        // An unhandled release on empty space would start a scan through the overlay window.
        MouseLeftButtonUp += (_, e) => e.Handled = true;
    }
}
