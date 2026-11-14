using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using Facet.Plugins;

namespace Facet;

// Hosts plugin launcher modes around the unchanged centered input (ADR 0084).
public partial class QuickLaunchView : ILauncherHost
{
    private readonly Canvas flyoutLayer = new();
    private readonly Border flyout = new();
    private readonly Dictionary<LauncherMode, ILauncherSession> modes = [];
    private ILauncherSession? mode;

    private void InitializeModes()
    {
        flyout.Visibility = Visibility.Collapsed;
        flyout.Background = (Brush)FindResource("Panel"); flyout.BorderBrush = (Brush)FindResource("Edge");
        flyout.BorderThickness = new(1); flyout.CornerRadius = new(12); flyout.Padding = new(8);
        flyoutLayer.Children.Add(flyout); Grid.SetRowSpan(flyoutLayer, 3); Panel.SetZIndex(flyoutLayer, 20);
        Layout.Children.Add(flyoutLayer);
        RegisterName("LauncherFlyout", flyout);
        SizeChanged += (_, _) => CloseFlyout();
    }

    string ILauncherHost.InputText { get => Input.Text; set { Input.Text = value; Input.CaretIndex = value.Length; } }
    void ILauncherHost.FocusInput() => FocusInput();

    // Plugin code can throw anything; a failing mode must not stop the launcher.
    private ILauncherSession? ModeFor(string text)
    {
        var current = app.Plugins.LauncherModes;
        foreach (var gone in modes.Keys.Except(current).ToArray()) {
            if (modes.Remove(gone, out var session)) { Layout.Children.Remove(session.Above); Layout.Children.Remove(session.Below); if (ReferenceEquals(mode, session)) mode = null; try { session.Dispose(); } catch (Exception ex) { System.Diagnostics.Trace.TraceWarning(ex.Message); } }
        }
        foreach (var candidate in current) {
            try {
                if (!modes.TryGetValue(candidate, out var session)) {
                    modes[candidate] = session = candidate.Create(this);
                    Place(session.Above, 0); Place(session.Below, 2); session.Above.Visibility = session.Below.Visibility = Visibility.Collapsed;
                }
                if (session.Matches(text)) return session;
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Launcher mode failed: " + ex.Message); }
        }
        return null;
    }

    private void Place(FrameworkElement element, int row) { Grid.SetRow(element, row); Layout.Children.Add(element); }

    // True while a plugin mode owns the input.
    private bool RefreshMode()
    {
        var next = ModeFor(Input.Text);
        if (!ReferenceEquals(next, mode)) {
            if (mode != null) { mode.Above.Visibility = mode.Below.Visibility = Visibility.Collapsed; CloseFlyout(); try { mode.Refresh(Input.Text, active); } catch (Exception ex) { System.Diagnostics.Trace.TraceWarning(ex.Message); } }
            mode = next;
            if (mode != null) mode.Above.Visibility = mode.Below.Visibility = Visibility.Visible;
        }
        if (mode != null) { try { mode.Refresh(Input.Text, active); } catch (Exception ex) { System.Diagnostics.Trace.TraceWarning(ex.Message); } }
        return mode != null;
    }

    private async Task SubmitModeAsync()
    {
        try { await mode!.SubmitAsync(Input.Text); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    public void ShowFlyout(FrameworkElement anchor, FrameworkElement content, bool above = false)
    {
        CloseFlyout();
        double inputTop = InputFrame.TranslatePoint(new(), Layout).Y, inputBottom = inputTop + InputFrame.ActualHeight;
        double width = Math.Min(365, Math.Max(1, Layout.ActualWidth));
        Point p = anchor.TranslatePoint(new(), Layout);
        double belowTop = Math.Max(inputBottom + 8, p.Y + anchor.ActualHeight + 8);
        double aboveBottom = Math.Min(inputTop - 8, p.Y - 8);
        double belowSpace = Layout.ActualHeight - belowTop - 4, aboveSpace = aboveBottom - 4;
        if (above && aboveSpace < 60 && belowSpace > aboveSpace) above = false;
        else if (!above && belowSpace < 60 && aboveSpace > belowSpace) above = true;
        double available = Math.Max(1, above ? aboveSpace : belowSpace);
        content.Width = Math.Max(1, width - 18);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = Math.Max(1, Math.Min(300, available - 18)) };
        flyout.Child = scroll; flyout.Width = width; flyout.MaxHeight = available; flyout.Visibility = Visibility.Visible;
        flyout.Measure(new(width, double.PositiveInfinity));
        Canvas.SetLeft(flyout, Math.Max(0, Math.Min(p.X, Layout.ActualWidth - width)));
        Canvas.SetTop(flyout, above ? Math.Max(4, aboveBottom - flyout.DesiredSize.Height) : belowTop);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => {
            if (flyout.Visibility == Visibility.Visible && ReferenceEquals(scroll, flyout.Child) && IsVisible)
                content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }));
    }

    public void CloseFlyout() { flyout.Visibility = Visibility.Collapsed; flyout.Child = null; }

    internal bool DismissFlyoutOutside(DependencyObject? source)
    {
        if (flyout.Visibility != Visibility.Visible || IsUnder(source, flyout)) return false;
        CloseFlyout(); FocusInput(); return true;
    }

    private bool ContainsModeSource(DependencyObject? source) => mode != null && (IsUnder(source, mode.Above) || IsUnder(source, mode.Below) || flyout.Visibility == Visibility.Visible && IsUnder(source, flyout));

    private static bool IsUnder(DependencyObject? source, DependencyObject ancestor)
    {
        for (var node = source; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node)) if (ReferenceEquals(node, ancestor)) return true;
        return false;
    }
}
