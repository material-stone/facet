using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Facet.Deployment;
using Rectangle = System.Drawing.Rectangle;

namespace Facet;
public sealed class OverlayWindow : Window
{
    private readonly Controller app;
    private readonly SolidColorBrush backdrop;
    private OverlayTabs tabs = new() { Opacity = 0 };
    private QuickLaunchView quickLaunch;
    private FrameworkElement? themeBackdrop;
    private Design.VoyageDebugPanel? voyageDebug;
    private Design.WorldBackdrop? world;
    internal bool HasWorldForCheck => world != null;
    internal bool IsQuickLaunch { get; private set; }
    private readonly Image behind = new() { Stretch = Stretch.Fill, IsHitTestVisible = false, Opacity = 0 };
    private readonly BlurEffect blur = new() { Radius = 0, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
    private readonly DispatcherTimer captureToolTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    internal Func<bool> CaptureToolForegroundProbe { get; set; } = () => Native.IsCaptureToolForeground();
    private BackdropCaptureSession? capture;
    private WriteableBitmap? captureBitmap;
    private bool awaitingReveal;
    private System.Threading.Tasks.Task captureStopped = System.Threading.Tasks.Task.CompletedTask;
    internal Func<IBackdropCaptureSource>? BackdropSourceFactory { get; set; }
    private static readonly DependencyProperty TransitionProgressProperty = DependencyProperty.Register(
        "TransitionProgress", typeof(double), typeof(OverlayWindow), new PropertyMetadata(0d, (owner, e) => ((OverlayWindow)owner).ApplyProgress((double)e.NewValue)));
    private bool requestedVisible;
    private bool dismissingOverlay;
    private double requestedStrength;
    private double requestedBlurRadius;
    private int requestedShowMilliseconds, requestedHideMilliseconds;
    private int transition;
    public OverlayWindow(Controller app)
    {
        this.app = app; Title = $"{LaunchProtocol.DisplayName} · 전체 화면 오버레이"; FontFamily = Design.Typography.Family; Icon = Design.AppIcon.Image;
        tabs.Connect(app);
        quickLaunch = new(app) { Visibility = Visibility.Collapsed, Opacity = 0 };
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        backdrop = new SolidColorBrush(OverlayAppearance.BackgroundColor(app.Data.Settings)); Background = Brushes.Transparent;
        backdrop.Opacity = 0;
        ShowInTaskbar = false; ShowActivated = true; Topmost = true; Opacity = 1;
        behind.Effect = blur;
        var layers = new Grid { Focusable = true, FocusVisualStyle = null, ClipToBounds = true };
        layers.Children.Add(behind); layers.Children.Add(new Border { Background = backdrop, IsHitTestVisible = false });
        layers.Children.Add(tabs); layers.Children.Add(quickLaunch); Content = layers;
        // Child order is relied on elsewhere, so the theme joins last and is placed by z-order: above the tint, below the tabs.
        AddThemeBackdrop(layers, 0);
        captureToolTimer.Tick += (_, _) => UpdateCaptureToolState();
        IsVisibleChanged += (_, _) => {
            if (IsVisible) { UpdateCaptureToolState(); captureToolTimer.Start(); }
            else { captureToolTimer.Stop(); ReleaseBackdrop(); }
        };
        Closed += (_, _) => { captureToolTimer.Stop(); ReleaseBackdrop(); RemoveWorld(); };
        Deactivated += (_, _) => UpdateCaptureToolState();
        PreviewMouseDown += (_, e) => {
            if (IsQuickLaunch && quickLaunch.DismissFlyoutOutside(e.OriginalSource as DependencyObject)) { e.Handled=true; return; }
            if (IsQuickLaunch && !quickLaunch.ContainsPointerSource(e.OriginalSource as DependencyObject)) DismissOverlay();
        };
        // On release: a carousel swipe or a board pan has already handled the event by then.
        MouseLeftButtonUp += (_, e) => {
            // Boards and pages have no background outside edit mode, so a press on empty space lands on the window itself.
            if (!IsQuickLaunch && e.OriginalSource is Visual emptySpace && !tabs.IsAncestorOf(emptySpace) && ActualWidth > 0 && ActualHeight > 0)
            {
                var at = e.GetPosition(this);
                world?.Next(at.X / ActualWidth, at.Y / ActualHeight);
            }
            if (!IsQuickLaunch && e.OriginalSource is Visual source && !tabs.IsAncestorOf(source)) (themeBackdrop as Panel)?.Children.OfType<Design.SpaceVoyage>().FirstOrDefault()?.Scan();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (IsQuickLaunch) { if (quickLaunch.HandleKey(e.Key, Keyboard.Modifiers)) e.Handled = true; return; }
            if (tabs.HandleDebugKey(e.Key, Keyboard.Modifiers)) { UpdateVoyageDebug(); e.Handled = true; return; }
            if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Escape && tabs.HandleEscape()) { e.Handled = true; return; }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S && !tabs.IsWidgetModalOpen && (tabs.HasSelectedInput || tabs.IsSettingsActive)) { e.Handled = true; tabs.SaveShortcut(); return; }
            if (tabs.HandleWidgetKey(e.Key, Keyboard.Modifiers)) { e.Handled = true; return; }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.OemOpenBrackets or Key.OemCloseBrackets)
            {
                e.Handled = true; tabs.MoveTab(e.Key == Key.OemOpenBrackets ? -1 : 1); return;
            }
            if (e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None) return;
            e.Handled = true; app.HideOverlay();
        };
        Closing += (_, e) => { if (!app.Exiting) { e.Cancel = true; app.HideOverlay(); } };
    }
    private void AddThemeBackdrop(Grid layers, double opacity)
    {
        AddWorld();
        themeBackdrop = Design.Skins.Visuals?.CreateBackdrop();
        if (themeBackdrop == null) return;
        UpdateThemeBackdrop(opacity);
        Panel.SetZIndex(themeBackdrop, 1); Panel.SetZIndex(tabs, 2); Panel.SetZIndex(quickLaunch, 2);
        layers.Children.Add(themeBackdrop);
        ApplyThemeOptions();
    }
    // Made on the first F8, so an ordinary session never carries it.
    private void UpdateVoyageDebug()
    {
        bool show = tabs.DebugEnabled && !IsQuickLaunch;
        if (show && voyageDebug == null && Content is Grid layers && (themeBackdrop as Panel)?.Children.OfType<Design.SpaceVoyage>().FirstOrDefault() is { } voyage) {
            voyageDebug = new(voyage);
            Panel.SetZIndex(voyageDebug, 3);
            layers.Children.Add(voyageDebug);
        }
        if (voyageDebug != null) voyageDebug.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }
    private void AddWorld()
    {
        if (Design.Skins.Visuals is not { WorldScenes: { } scenes, WorldPageHtml: { } page }) return;
        var weather = new WeatherClient(locate: token => WeatherRegions.ResolveAsync(PluginData.WeatherRegion(app.Data), token));
        world = new Design.WorldBackdrop(page, scenes, weather.FetchAsync);
        world.Failed += () => { world = null; tabs.SetSkinWorld(false); Background = Brushes.Transparent; ApplyProgress((double)GetValue(TransitionProgressProperty)); };
        // The widget layer (ADR 0110): the tabs say where skin-drawn widgets are and the page asks for their actions.
        var current = world;
        world.ActionRequested = json => tabs.RunSkinAction(json);
        tabs.SkinFrames = json => { if (ReferenceEquals(world, current)) current.Widgets(json); };
        tabs.SkinPointerOut = json => { if (ReferenceEquals(world, current)) current.Pointer(json); };
        tabs.SetSkinWorld(true);
        // Nearly invisible, but it makes every pixel of this layered window take the click instead of the app behind.
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
    }
    private void RemoveWorld()
    {
        world?.Dispose(); world = null; Background = Brushes.Transparent;
        tabs.SetSkinWorld(false); tabs.SkinFrames = null; tabs.SkinPointerOut = null;
    }
    private void UpdateWorld() => world?.SetVisible(IsVisible && requestedVisible && !IsQuickLaunch && requestedStrength > 0, this);
    internal void ApplyGrid() => tabs.ApplyWorkspace();
    internal void ApplyThemeOptions()
    {
        if ((themeBackdrop as Panel)?.Children.OfType<Design.FlightReticle>().FirstOrDefault() is not { } reticle || Design.Skins.Visuals is not { } visuals) return;
        bool on = visuals.IsOptionOn(app.Data.Settings, Design.Skins.Resolve(app.Data.Settings.Skin), Design.ThemeVisuals.ReticleOption);
        reticle.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }
    // Follows the same transparency as the tint; a fully transparent backdrop is collapsed so its animations stop too.
    private void UpdateThemeBackdrop(double progress)
    {
        UpdateVoyageDebug();
        if (themeBackdrop == null) return;
        themeBackdrop.Opacity = requestedStrength * progress;
        themeBackdrop.Visibility = !IsQuickLaunch && requestedStrength > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    internal FrameworkElement? ThemeBackdropForCheck => themeBackdrop;
    internal void ApplyProgressForCheck(double progress) => ApplyProgress(progress);
    internal double TintOpacityForCheck => backdrop.Opacity;
    // Views hold the brushes they resolved, so a new skin needs new views; data, music and chat sessions live in the Controller.
    internal void SwitchSkin(string? skin)
    {
        if (!tabs.TrySaveEdits()) return;
        var layers = (Grid)Content;
        double progress = (double)GetValue(TransitionProgressProperty);
        bool settings = tabs.IsSettingsActive;
        double scroll = tabs.SettingsScrollOffset;
        string section = tabs.SettingsSection;
        if (themeBackdrop != null) layers.Children.Remove(themeBackdrop);
        if (voyageDebug != null) { layers.Children.Remove(voyageDebug); voyageDebug = null; }
        RemoveWorld();
        tabs.Retire();
        Design.Skins.Switch(Application.Current, skin);
        int tabsIndex = layers.Children.IndexOf(tabs), launchIndex = layers.Children.IndexOf(quickLaunch);
        layers.Children.RemoveAt(launchIndex); layers.Children.RemoveAt(tabsIndex);
        tabs = new() { Opacity = progress, Visibility = IsQuickLaunch ? Visibility.Collapsed : Visibility.Visible };
        tabs.Connect(app);
        quickLaunch = new(app) { Opacity = progress, Visibility = IsQuickLaunch ? Visibility.Visible : Visibility.Collapsed };
        layers.Children.Insert(tabsIndex, tabs); layers.Children.Insert(launchIndex, quickLaunch);
        AddThemeBackdrop(layers, progress);
        UpdateWorld();
        // Whether a world exists decides the blur and the tint, and Present returns early when its inputs are unchanged.
        app.ApplyDisplay();
        ApplyProgress((double)GetValue(TransitionProgressProperty));
        tabs.SettingsSection = section;
        if (settings) tabs.OpenSettings();
        tabs.SettingsScrollOffset = scroll;
        if (IsQuickLaunch) quickLaunch.Begin();
    }
    public bool TrySaveEdits() => tabs.TrySaveEdits();
    internal void StopWidgets() => tabs.StopWidgets();
    internal bool ShowWidget(string widgetId) => tabs.ShowWidget(widgetId);
    internal void OpenPluginSettings(string pluginId) => tabs.OpenPluginSettings(pluginId);
    internal void SetQuickLaunch(bool value)
    {
        IsQuickLaunch = value;
        tabs.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        quickLaunch.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        UpdateThemeBackdrop((double)GetValue(TransitionProgressProperty));
        UpdateWorld();
        if (value) {
            ReleaseBackdrop();
            blur.BeginAnimation(BlurEffect.RadiusProperty, null); blur.Radius = 0;
            behind.BeginAnimation(OpacityProperty, null); behind.Opacity = 0;
            backdrop.Opacity = 0;
        }
        if (value) quickLaunch.Begin(); else quickLaunch.End();
    }
    internal void FocusQuickLaunch() => quickLaunch.FocusInput();
    private void UpdateCaptureToolState() => SetTopmost(!CaptureToolForegroundProbe());
    private void SetTopmost(bool topmost)
    {
        if (Topmost != topmost) { Topmost = topmost; world?.Place(this); }
    }
    private void DismissOverlay()
    {
        if (!requestedVisible || dismissingOverlay || app.Exiting) return;
        dismissingOverlay = true;
        try {
            app.HideOverlay(restore: false);
            if (!app.Data.Settings.OverlayVisible) quickLaunch.End();
        } finally { dismissingOverlay = false; }
    }
    public void Present(bool visible, double strength, double blurRadius, int showMilliseconds, int hideMilliseconds, Color? backgroundColor = null)
    {
        if (backgroundColor is Color color) backdrop.Color = Color.FromRgb(color.R, color.G, color.B);
        strength = IsQuickLaunch ? 0 : Math.Clamp(double.IsFinite(strength) ? strength : .57, 0, 1);
        blurRadius = IsQuickLaunch ? 0 : Math.Clamp(double.IsFinite(blurRadius) ? blurRadius : 18, 0, 40);
        if (strength == 0 || world != null) blurRadius = 0;
        showMilliseconds = Math.Clamp(showMilliseconds, 0, 2000); hideMilliseconds = Math.Clamp(hideMilliseconds, 0, 2000);
        if (requestedVisible == visible && requestedStrength == strength && requestedBlurRadius == blurRadius && requestedShowMilliseconds == showMilliseconds && requestedHideMilliseconds == hideMilliseconds) return;
        bool opening = visible && !requestedVisible;
        requestedVisible = visible; requestedStrength = strength;
        requestedBlurRadius = blurRadius; requestedShowMilliseconds = showMilliseconds; requestedHideMilliseconds = hideMilliseconds;
        int version = ++transition;
        if (!visible && !IsVisible) { ReleaseBackdrop(); return; }
        if (visible && blurRadius > 0 && capture == null)
        {
            _ = new WindowInteropHelper(this).EnsureHandle();
            // Re-enabling blur (including leaving quick launch) also needs a hidden snapshot.
            if (IsVisible) Hide();
            // Capture only while hidden: the overlay itself is now included in screen captures.
            if (!IsVisible) {
                awaitingReveal = true;
                capture = new BackdropCaptureSession(Dispatcher,ApplyBackdropFrame,BackdropSourceFactory,singleFrame:true,afterStop:captureStopped);
                UpdateBackdrop();
            }
        }
        if (visible && awaitingReveal && blurRadius > 0) return;
        if (visible && awaitingReveal) ReleaseBackdrop();
        ContinuePresentation(version,opening);
    }

    private void ContinuePresentation(int version,bool opening)
    {
        if (version != transition) return;
        bool visible=requestedVisible;
        double blurRadius=requestedBlurRadius;
        if (!IsVisible)
        {
            if (!visible) return;
            BeginAnimation(TransitionProgressProperty,null);SetValue(TransitionProgressProperty,0d);
            Show();
        }
        UpdateWorld();
        // An explicit open wins over a capture tool, so the overlay can be recorded; the tool regains priority when it next takes the foreground.
        if (opening) { SetTopmost(true); Activate(); if (IsQuickLaunch) quickLaunch.FocusInput(); else if (!tabs.FocusSettingsSearch()) ((Grid)Content).Focus(); }
        double from = (double)GetValue(TransitionProgressProperty), target = visible ? 1 : 0;
        double fromBlur = blur.Radius, targetBlur = visible ? blurRadius : 0;
        double fromImage = behind.Opacity, targetImage = visible && blurRadius > 0 ? 1 : 0;
        BeginAnimation(TransitionProgressProperty, null); SetValue(TransitionProgressProperty, from); ApplyProgress(from);
        blur.BeginAnimation(BlurEffect.RadiusProperty, null); blur.Radius = fromBlur;
        behind.BeginAnimation(OpacityProperty, null); behind.Opacity = fromImage;
        int milliseconds = visible ? requestedShowMilliseconds : requestedHideMilliseconds;
        if (milliseconds == 0) { CompleteTransition(version, target, targetBlur); return; }
        var fade = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        var blurFade = new DoubleAnimation(fromBlur, targetBlur, fade.Duration) { EasingFunction = fade.EasingFunction, FillBehavior = FillBehavior.HoldEnd };
        var imageFade = new DoubleAnimation(fromImage, targetImage, fade.Duration) { EasingFunction = fade.EasingFunction, FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) => CompleteTransition(version, target, targetBlur);
        blur.BeginAnimation(BlurEffect.RadiusProperty, blurFade, HandoffBehavior.SnapshotAndReplace);
        behind.BeginAnimation(OpacityProperty, imageFade, HandoffBehavior.SnapshotAndReplace);
        BeginAnimation(TransitionProgressProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }
    private void CompleteTransition(int version, double target, double targetBlur)
    {
        if (version != transition) return;
        SetValue(TransitionProgressProperty, target); BeginAnimation(TransitionProgressProperty, null);
        blur.Radius = targetBlur; blur.BeginAnimation(BlurEffect.RadiusProperty, null);
        behind.Opacity = requestedVisible && requestedBlurRadius > 0 ? 1 : 0; behind.BeginAnimation(OpacityProperty, null);
        if (requestedVisible) { if (requestedBlurRadius == 0) ReleaseBackdrop(); return; }
        bool restore = IsActive;
        Hide(); UpdateWorld(); quickLaunch.End(); app.OverlayDismissed(restore);
    }
    private void ApplyProgress(double progress)
    {
        // The world covers the screen itself; a tint would only dim it.
        backdrop.Opacity = IsQuickLaunch || world != null ? 0 : requestedStrength * progress;
        tabs.Opacity = progress;
        UpdateThemeBackdrop(progress);
        quickLaunch.Opacity = progress;
    }
    private void UpdateBackdrop()
    {
        if (capture == null || !requestedVisible || requestedBlurRadius<=0 || IsVisible) return;
        capture.Request(CurrentCaptureBounds());
    }
    private Rectangle CurrentCaptureBounds() => System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).Bounds;

    private bool ApplyBackdropFrame(BackdropCaptureSession session,BackdropPixels frame)
    {
        if (!ReferenceEquals(capture,session) || !requestedVisible || requestedBlurRadius<=0 || IsVisible) return false;
        if (frame.Bounds!=CurrentCaptureBounds()) {UpdateBackdrop();return false;}
        bool applied=frame.Pixels!=null;
        if (frame.Pixels is {} pixels) {
            if(captureBitmap==null || captureBitmap.PixelWidth!=frame.Width || captureBitmap.PixelHeight!=frame.Height)
                captureBitmap=new WriteableBitmap(frame.Width,frame.Height,96,96,PixelFormats.Bgr32,null);
            captureBitmap.WritePixels(new Int32Rect(0,0,frame.Width,frame.Height),pixels,frame.Width*4,0);
            behind.Source=captureBitmap;
        }
        if(awaitingReveal) {
            awaitingReveal=false;
            ContinuePresentation(transition,opening:true);
        }
        return applied;
    }
    private void ReleaseBackdrop()
    {
        awaitingReveal=false;
        var previous=capture;capture=null;captureBitmap=null;behind.Source=null;
        if(previous!=null) {previous.Dispose();captureStopped=previous.Stopped;}
    }
}

public sealed class ToastWindow : Window
{
    private readonly DispatcherTimer timer;
    private bool dismissing, closed, hovered, clickDismissed;
    private int animationVersion;
    public ToastWindow(string title, string body, Action open, bool startup = false)
    {
        FontFamily = Design.Typography.Family;
        Title = $"{LaunchProtocol.DisplayName} · 알림"; Icon = Design.AppIcon.Image; Width = 380; SizeToContent = SizeToContent.Height; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        var stack = new StackPanel();
        if (startup) {
            Width = double.NaN; SizeToContent = SizeToContent.WidthAndHeight;
            stack.VerticalAlignment = VerticalAlignment.Center;
            stack.Children.Add(new TextBlock { Text = title, FontSize = 13, TextWrapping = TextWrapping.NoWrap });
        } else {
            stack.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0,0,0,10) });
            stack.Children.Add(new TextBlock { Text = body, FontSize = 13, Margin = new(0,0,0,16) });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var go = new Button { Content = "열기" }; go.Click += (_, _) => { open(); Close(); };
            var dismiss = new Button { Content = "닫기" }; dismiss.Click += (_, _) => Close(); row.Children.Add(go); row.Children.Add(dismiss); stack.Children.Add(row);
        }
        Content = new Border { Background = (Brush)Application.Current.FindResource("Panel"), Padding = startup ? new(12,7,12,7) : new(22), CornerRadius = new(startup ? 8 : 12), BorderBrush = (Brush)Application.Current.FindResource("Mint"), BorderThickness = new(1), Child = stack };
        SourceInitialized += (_, _) => Native.Passive(this, false);
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(startup ? 2 : 20) };
        timer.Tick += (_, _) => {
            timer.Stop();
            if (!startup) { Close(); return; }
            FadeOut();
        };
        if (startup) {
            Opacity = 0; Focusable = false;
            PreviewMouseDown += (_, e) => { e.Handled = true; FadeOut(clicked: true); };
            MouseEnter += (_, _) => {
                hovered = true; timer.Stop();
                if (dismissing && !closed) { dismissing = false; clickDismissed = false; FadeIn(); }
            };
            MouseLeave += (_, _) => {
                hovered = false;
                if (!dismissing && !closed) { timer.Stop(); timer.Start(); }
            };
        }
        Loaded += (_, _) => {
            if (startup) Native.AboveTaskbar(this);
            else { Left = SystemParameters.WorkArea.Right - ActualWidth - 24; Top = SystemParameters.WorkArea.Bottom - ActualHeight - 24; Native.Clamp(this); }
            if (!startup) { timer.Start(); return; }
            FadeIn();
        };
        Closed += (_, _) => { closed = true; ++animationVersion; timer.Stop(); BeginAnimation(OpacityProperty, null); };
    }
    private void FadeIn()
    {
        int version = ++animationVersion;
        var fade = new DoubleAnimation(Opacity, 1, TimeSpan.FromSeconds(.2));
        fade.Completed += (_, _) => {
            if (version == animationVersion && !dismissing && !closed && !hovered) timer.Start();
        };
        BeginAnimation(OpacityProperty, fade);
    }
    private void FadeOut(bool clicked = false)
    {
        if (closed || clickDismissed || (!clicked && (dismissing || hovered))) return;
        if (clicked) clickDismissed = true;
        if (dismissing) return;
        dismissing = true; timer.Stop();
        int version = ++animationVersion;
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromSeconds(.2));
        fade.Completed += (_, _) => { if (version == animationVersion && dismissing && !closed) Close(); };
        BeginAnimation(OpacityProperty, fade);
    }
}
