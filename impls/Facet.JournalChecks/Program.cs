using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Input;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Facet;
#if PLUGIN_CHECKS
using Facet.SchedulePlugin;
#endif
using Facet.Core;

internal static partial class Program
{
    private static int passed;
    private static void Assert(bool ok,string label) { if(!ok)throw new Exception(label); Console.WriteLine("PASS "+label); passed++; }
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int count);
    // For effects with a start but no fixed arrival: a busy machine delays animation ticks, not their outcome.
    private static void PumpUntil(Func<bool> done,int timeoutMilliseconds=3000)
    {
        var watch=Stopwatch.StartNew();
        while(!done() && watch.ElapsedMilliseconds<timeoutMilliseconds) Pump(20);
    }
    private static void Pump(int milliseconds=150)
    {
        var frame=new DispatcherFrame();
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
    private static void Chord(params byte[] keys)
    {
        foreach(byte key in keys)keybd_event(key,0,0,0);
        foreach(byte key in keys.Reverse())keybd_event(key,0,2,0);
        Pump();
    }
    private static string TestExitShortcut()
    {
        foreach(string candidate in new[]{Controller.ExitHotkey,"Ctrl+Alt+Shift+F12","Ctrl+Alt+Shift+F11"}) {
            var parsed=Native.ParseHotkey(candidate);
            if(!Native.RegisterHotKey(IntPtr.Zero,199,parsed.modifiers,parsed.key))continue;
            Native.UnregisterHotKey(IntPtr.Zero,199);
            Console.WriteLine("Native exit check uses "+candidate+"; running app shortcuts are preserved.");
            return candidate;
        }
        throw new Exception("No isolated exit shortcut is available for native checks.");
    }
    private static byte[] ExitKeys(Controller controller)
    {
        var parsed=Native.ParseHotkey(controller.ExitShortcut);var keys=new List<byte>();
        if((parsed.modifiers&8)!=0)keys.Add(0x5B);
        if((parsed.modifiers&2)!=0)keys.Add(0x11);
        if((parsed.modifiers&1)!=0)keys.Add(0x12);
        if((parsed.modifiers&4)!=0)keys.Add(0x10);
        keys.Add((byte)parsed.key);return keys.ToArray();
    }
    private static string TestQuickLaunchShortcut()
    {
        foreach(string candidate in new[]{"Ctrl+Alt+Shift+F7","Ctrl+Alt+Shift+F6","Ctrl+Alt+Shift+F5"}) {
            var parsed=Native.ParseHotkey(candidate);
            if(!Native.RegisterHotKey(IntPtr.Zero,198,parsed.modifiers,parsed.key))continue;
            Native.UnregisterHotKey(IntPtr.Zero,198);return candidate;
        }
        throw new Exception("No isolated quick-launch shortcut is available for native checks.");
    }
    private static void CheckOverlay(Controller controller, string qa)
    {
        var settings=controller.Data.Settings;
        settings.OverlayHotkey="Ctrl+Alt+Shift+F10";
        settings.HideFullscreen=true;settings.HideAtNight=true;settings.ClickThrough=true;settings.OverlayTopmost=false;
        settings.OverlayWidth=300;settings.OverlayLeft=-99999;settings.OverlayTop=-99999;
        Assert(Native.ParseHotkey("Win+Alt+G")==((uint)0x4009,(uint)'G'),"Win modifier is supported without changing saved shortcuts");
        Assert(Native.ParseHotkey(" win + alt + space ")==((uint)0x4009,(uint)0x20),"Win Alt Space accepts case and spacing variations");
        Assert(Native.ParseHotkey(" win + alt + end ")==((uint)0x4009,(uint)0x23),"fixed Windows exit shortcut parses End with case and spacing variations");
        using var host=new KeyboardHost();
        host.Window.Show();
        Assert(host.Focus(),"synthetic keyboard target owns focus before native shortcut checks");
        Native.CoverScreen(host.Window, true);Pump();
        controller.Start(false);Pump();
        Assert(!controller.HotkeyStatus.Contains("실패"),"native global shortcuts register: "+controller.HotkeyStatus);
        var reserved=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
        reserved.OverlayHotkey=controller.ExitShortcut;
        bool rejected=false;
        try { controller.RegisterKeys(reserved); } catch(ArgumentException ex) { rejected=ex.Message.Contains("종료 전용"); }
        Assert(rejected,"reserved exit shortcut cannot be assigned to another action before unregistering working shortcuts");
        Assert(!controller.Overlay.IsVisible,"full-screen overlay starts hidden");
        var bounds=System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        Assert(host.Focus(),"test host is native foreground before opening overlay");
        Chord(0x11,0x12,0x10,0x79);
        Assert(controller.Overlay.IsVisible && controller.Overlay.IsActive,"real global shortcut opens and focuses full-screen overlay");
        var state=JsonSerializer.SerializeToElement(Native.WindowState(controller.Overlay));
        Assert(state.GetProperty("x").GetInt32()==bounds.X && state.GetProperty("y").GetInt32()==bounds.Y && state.GetProperty("width").GetInt32()==bounds.Width && state.GetProperty("height").GetInt32()==bounds.Height,"overlay covers primary monitor including taskbar regardless of foreground monitor and legacy position and width");
        Assert(state.GetProperty("topmost").GetBoolean() && !state.GetProperty("noActivate").GetBoolean() && !state.GetProperty("clickThrough").GetBoolean(),"full-screen overlay accepts input despite legacy passive settings");
        var layers=(Grid)controller.Overlay.Content;
        var backgroundImage=(Image)layers.Children[0];
        var blur=(BlurEffect)backgroundImage.Effect;
        var backdrop=(SolidColorBrush)((Border)layers.Children[1]).Background;
        Assert(backdrop.Opacity>0 && backdrop.Opacity<settings.Opacity,"opening passes through an intermediate background opacity");
        Assert(blur.Radius>0 && blur.Radius<18,"opening gradually increases actual Gaussian blur radius");
        Pump(400);
        Assert(controller.Overlay.Opacity==1 && backdrop.Color.A==255 && Math.Abs(backdrop.Opacity-OverlayAppearance.Strength(settings))<.001,"fade in settles at configured backdrop strength");
        Assert(layers.Children.Count==4 && layers.Children[2] is OverlayTabs { Opacity:1 } && layers.Children[3] is QuickLaunchView {Visibility:Visibility.Collapsed},"overlay layers contain crisp calendar HUD above background image and tint with quick launch hidden");
#if PLUGIN_CHECKS
        CheckOverlayCalendar((OverlayTabs)layers.Children[2],qa);
#endif
        Assert(Math.Abs(blur.Radius-18)<.001,$"blur settles at intended radius (radius={blur.Radius}, visible={controller.Overlay.IsVisible}, requested={controller.Data.Settings.OverlayVisible}, configured={controller.Data.Settings.OverlayBlurRadius})");
        var frame=(WriteableBitmap)backgroundImage.Source;
        Assert(frame.PixelWidth<=1280 && frame.PixelHeight<=1280,"background snapshot is limited to bounded resolution");
        var initialPixel=new byte[4];frame.CopyPixels(new Int32Rect(frame.PixelWidth/2,frame.PixelHeight/2,1,1),initialPixel,4,0);
        host.Window.Background=Brushes.Red;Pump(250);
        var livePixel=new byte[4];frame.CopyPixels(new Int32Rect(frame.PixelWidth/2,frame.PixelHeight/2,1,1),livePixel,4,0);
        Assert(livePixel.SequenceEqual(initialPixel),"visible overlay keeps its original background instead of recapturing itself or changed desktop content");
        Assert(ReferenceEquals(frame,backgroundImage.Source),"fixed background keeps the same capture bitmap");
        Assert(GetWindowDisplayAffinity(new WindowInteropHelper(controller.Overlay).Handle,out uint affinity) && affinity==0,"overlay has no Windows capture exclusion");
        var color=CaptureDesktopPixel(bounds.X+10,bounds.Y+10);
        Assert(color.R<245 || color.G>10 || color.B>10,"actual desktop capture includes overlay above the red synthetic background");
        host.RestorePattern();Pump(250);
        controller.Overlay.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(controller.Overlay.ActualWidth),(int)Math.Ceiling(controller.Overlay.ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(controller.Overlay);
        var pixel=new byte[4];bitmap.CopyPixels(new Int32Rect(0,0,1,1),pixel,4,0);
        Assert(pixel[3]>0,"processed desktop background renders into overlay");
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(qa,"fullscreen-overlay.png")))png.Save(output);
        Chord(0x10,0x1B);
        Assert(controller.Overlay.IsVisible,"modified Escape leaves overlay visible");
        int previousEscapes=host.Escapes;
        Chord(0x1B);
        Assert(controller.Overlay.IsVisible && !controller.Data.Settings.OverlayVisible && backdrop.Opacity>0 && backdrop.Opacity<settings.Opacity,"Escape fades out before actually hiding the window");
        Assert(blur.Radius>0 && blur.Radius<18,"closing gradually restores sharp background");
        Pump(400);
        Assert(!controller.Overlay.IsVisible && !controller.Data.Settings.OverlayVisible,"bare Escape dismisses full-screen overlay");
        Assert(blur.Radius==0 && backgroundImage.Source==null,"hidden overlay clears blur and releases captured pixels");
        for(int attempt=0;attempt<5 && !host.Window.IsActive;attempt++)Pump();
        Assert(host.Window.IsActive,"dismissal restores foreground app");
        Assert(host.Escapes==previousEscapes,"dismissal does not forward Escape to previous app");
        controller.Tick(false);Assert(!controller.Overlay.IsVisible,"timer preserves Escape dismissal");
        Chord(0x11,0x12,0x10,0x79);Assert(controller.Overlay.IsVisible,"global shortcut reopens after Escape");
        Chord(0x11,0x12,0x10,0x79);Pump(400);Assert(!controller.Overlay.IsVisible,"global shortcut toggles visible overlay off");
        controller.ToggleOverlay();
        Assert(!controller.Overlay.IsVisible && backdrop.Opacity==0,"reopening waits transparently for a new hidden background snapshot");
        AwaitBackdrop(()=>controller.Overlay.IsVisible);
        Pump(90);double rising=backdrop.Opacity, risingBlur=blur.Radius;
        controller.HideOverlay();
        Assert(Math.Abs(backdrop.Opacity-rising)<.03,"closing during fade in continues from current opacity without a jump");
        Assert(Math.Abs(blur.Radius-risingBlur)<.6,"blur reversal preserves current radius without a jump");
        Pump(70);double falling=backdrop.Opacity;
        Assert(falling<rising && controller.Overlay.IsVisible,"interrupted fade in reverses toward transparency");
        controller.ToggleOverlay();
        Assert(controller.Data.Settings.OverlayVisible && Math.Abs(backdrop.Opacity-falling)<.03,"shortcut reverses a pending fade out from its current opacity");
        controller.Tick(false);Pump(400);
        Assert(controller.Overlay.IsVisible && Math.Abs(backdrop.Opacity-OverlayAppearance.Strength(settings))<.001,"old completion and periodic refresh cannot hide or restart a reopened overlay");
        Assert(Math.Abs(blur.Radius-18)<.001,"interrupted blur resumes to the full radius");
        controller.HideOverlay();Pump(400);
        Assert(!controller.Overlay.IsVisible && backdrop.Opacity==0,"fade out completes fully transparent before hiding");
        settings.Bed=DateTime.Now.ToString("HH:mm");settings.Wake=DateTime.Now.AddHours(2).ToString("HH:mm");
        Chord(0x11,0x12,0x10,0x79);controller.Tick(false);
        Assert(controller.Overlay.IsVisible,"explicit overlay remains open through night and fullscreen policies");
        controller.HideOverlay();Pump(400);
        var custom=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(controller.Data.Settings))!;
        custom.OverlayHotkey="Win+Alt+Shift+Space";
        byte[] customChord=[0x5B,0x12,0x10,0x20];
        bool registered=controller.ApplySettings(custom);
        if(!registered && controller.HotkeyStatus.Contains("1409")) {
            Assert(controller.Data.Settings.OverlayHotkey==settings.OverlayHotkey,"occupied custom shortcut preserves existing setting");
            Console.WriteLine("Win+Alt+Shift+Space is already in use; checking Win+Ctrl+Shift+Space instead.");
            custom.OverlayHotkey="Win+Ctrl+Shift+Space";customChord=[0x5B,0x11,0x10,0x20];
            registered=controller.ApplySettings(custom);
        }
        Assert(registered,"custom Windows shortcut registers through settings: "+controller.HotkeyStatus);
        Assert(new Store(controller.Store.Root).Load().Settings.OverlayHotkey==custom.OverlayHotkey,"custom Windows shortcut persists to encrypted settings");
        Assert(host.Focus(),"test host owns native foreground before custom shortcut input");
        Chord(customChord);
        Assert(controller.Overlay.IsVisible && controller.Overlay.IsActive,"real custom Windows shortcut opens full-screen overlay from another window");
        Chord(customChord);
        Pump(400);
        Assert(!controller.Overlay.IsVisible,"real custom Windows shortcut toggles overlay closed");
        Chord(customChord);
        controller.HideOverlay();Pump(400);
        Assert(!new Store(controller.Store.Root).Load().Settings.OverlayVisible,"Escape dismissal persists to encrypted settings");
        CheckAppearanceSettings(controller,qa);
        CheckBackdropWindowFallback(controller);
        CheckNativeOverlayAppearance(controller);
        CheckCaptureToolPriority(controller);
    }
#if PLUGIN_CHECKS
    private static void CheckOverlayCalendar(OverlayTabs hud,string qa,bool nativeInput=true)
    {
        var tabs=(TabControl)hud.FindName("Tabs");
        var board=ScheduleBoardFor(hud,Path.Combine(qa,"overlay-calendar-"+Guid.NewGuid().ToString("N")));
        var calendar=(OverlayCalendarView)board.FindName("Calendar");
        void Input(params byte[] keys)
        {
            if(nativeInput) { Chord(keys);return; }
            if(keys.SequenceEqual(new byte[]{0x11,0xDB}))hud.MoveTab(-1);
            else if(keys.SequenceEqual(new byte[]{0x11,0xDD}))hud.MoveTab(1);
            else if(keys.SequenceEqual(new byte[]{0x21}))board.MoveCalendarMonth(-1);
            else if(keys.SequenceEqual(new byte[]{0x22}))board.MoveCalendarMonth(1);
            else if(keys.SequenceEqual(new byte[]{0x24}))board.GoToCalendarToday();
            else throw new Exception("Unsupported calendar test input.");
        }
        Assert(tabs.Items.Count>=1 && tabs.SelectedIndex==0 && hud.FindName("SettingsTab")==null,"dashboard opens first and settings is not a tab");
        var dashboard=(TabItem)tabs.Items[0];
        Assert(System.Windows.Automation.AutomationProperties.GetName(dashboard)=="대시보드","dashboard has an accessible header");
        // The calendar widget sits on the dashboard here; another tab does not get its keys.
        hud.OpenSettings();Assert(!board.IsEditingDay && !hud.HandleWidgetKey(System.Windows.Input.Key.PageDown,System.Windows.Input.ModifierKeys.None) && !hud.HandleWidgetKey(System.Windows.Input.Key.Home,System.Windows.Input.ModifierKeys.None),"the settings modal takes no calendar keys");hud.CloseSettings();tabs.SelectedIndex=0;
        Input(0x11,0xDB);Assert(tabs.SelectedItem is TabItem{Visibility:Visibility.Visible},"previous tab wraps among the visible tabs");
        Input(0x11,0xDD);Assert(tabs.SelectedIndex==0 && !board.IsEditingDay,"next tab wraps to the dashboard without opening a day");
        Assert(hud.HandleWidgetKey(System.Windows.Input.Key.Home,System.Windows.Input.ModifierKeys.None),"the selected tab routes calendar keys to its widget");


        var body=(StackPanel)calendar.Child;
        var calendarHeader=(StackPanel)body.Children[0];
        var navigation=(Grid)calendarHeader.Children[0];
        var heading=(TextBlock)navigation.Children[1];
        var previous=(Button)navigation.Children[0];var next=(Button)navigation.Children[2];
        var todayButton=(Button)calendarHeader.Children[1];
        var weekdays=(System.Windows.Controls.Primitives.UniformGrid)body.Children[2];
        var dates=(System.Windows.Controls.Primitives.UniformGrid)body.Children[3];
        foreach(var today in new[]{new DateOnly(2024,2,29),new DateOnly(2026,2,1),new DateOnly(2026,8,31),new DateOnly(2027,1,1)}) {
            calendar.Refresh(today);
            calendar.SetSelectedDate(today);
            var numbered=dates.Children.OfType<Border>().Where(b=>b.Child is TextBlock).ToArray();
            int offset=(int)new DateOnly(today.Year,today.Month,1).DayOfWeek;
            Assert(numbered.Length==DateTime.DaysInMonth(today.Year,today.Month) && ((TextBlock)((Border)dates.Children[offset]).Child).Text=="1" && ((TextBlock)numbered[^1].Child).Text==numbered.Length.ToString(),"calendar aligns month start and end for "+today);
            Assert(numbered.Count(b=>b.BorderThickness.Left==2)==1 && ((TextBlock)numbered.Single(b=>b.BorderThickness.Left==2).Child).Text==today.Day.ToString(),"calendar outlines the selected date for "+today);
            Assert(heading.Text==$"{today.Year:0000}년 {today.Month}월","calendar renders Korean year and month for "+today);
            Assert(numbered.Where(b=>dates.Children.IndexOf(b)%7==0).All(b=>((TextBlock)b.Child).Foreground==((TextBlock)weekdays.Children[0]).Foreground) && numbered.Where(b=>dates.Children.IndexOf(b)%7==6).All(b=>((TextBlock)b.Child).Foreground==((TextBlock)weekdays.Children[6]).Foreground),"weekend date colors match weekday headers for "+today);
        }
        Assert(((SolidColorBrush)((TextBlock)weekdays.Children[0]).Foreground).Color==Color.FromRgb(255,157,146) && ((SolidColorBrush)((TextBlock)weekdays.Children[6]).Foreground).Color==Color.FromRgb(145,191,255),"Sunday is red and Saturday is blue");
        board.RefreshToday();
        calendar.GoToToday();
        Assert(heading.Text==$"{DateTime.Now.Year:0000}년 {DateTime.Now.Month}월","calendar refresh restores current local month");
        var localToday=DateOnly.FromDateTime(DateTime.Now);
        string MonthLabel(DateOnly value)=>$"{value.Year:0000}년 {value.Month}월";
        Input(0x21);Assert(heading.Text==MonthLabel(localToday.AddMonths(-1)),"previous month command moves calendar back");
        Assert(dates.Children.OfType<Border>().All(b=>b.BorderThickness.Left!=2),"browsing another month never highlights the same day number as today");
        board.RefreshToday();Assert(heading.Text==MonthLabel(localToday.AddMonths(-1)),"periodic refresh preserves browsed month");
        Input(0x22);Assert(heading.Text==MonthLabel(localToday),"next month command moves calendar forward");
        previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(heading.Text==MonthLabel(localToday.AddMonths(-1)),"previous month arrow invokes navigation");
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(heading.Text==MonthLabel(localToday),"next month arrow invokes navigation");
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        todayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(heading.Text==MonthLabel(localToday) && dates.Children.OfType<Border>().Count(b=>b.BorderThickness.Left==2)==1,"Today button restores current month and today outline");
        Input(0x21);Input(0x24);
        Assert(heading.Text==MonthLabel(localToday) && dates.Children.OfType<Border>().Count(b=>b.BorderThickness.Left==2)==1,"today command restores current month and today outline");
        Assert(todayButton.ToolTip==null && System.Windows.Automation.AutomationProperties.GetAcceleratorKey(todayButton)=="Home","Today has no tooltip and preserves accessible Home shortcut");
        var tooltip=new ToolTip { Content="다크 테마 도움말" };
        tooltip.Measure(new Size(600,200));tooltip.Arrange(new Rect(tooltip.DesiredSize));tooltip.UpdateLayout();
        IEnumerable<DependencyObject> Descendants(DependencyObject parent) {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) {
                var child=VisualTreeHelper.GetChild(parent,i);yield return child;
                foreach(var descendant in Descendants(child))yield return descendant;
            }
        }
        var tooltipText=Descendants(tooltip).OfType<TextBlock>().First();
        var tooltipBackground=((SolidColorBrush)tooltip.Background).Color;
        var tooltipForeground=((SolidColorBrush)tooltipText.Foreground).Color;
        double Luminance(Color color) {
            double Linear(byte channel) { double value=channel/255d;return value<=.04045?value/12.92:Math.Pow((value+.055)/1.055,2.4); }
            return .2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);
        }
        Assert(tooltipBackground==((SolidColorBrush)Application.Current.FindResource("Panel")).Color && tooltipForeground==((SolidColorBrush)Application.Current.FindResource("Ink")).Color && tooltipText.Text.Contains("도움말"),"shared tooltip theme retains dark panel and readable light text");
        Assert((Luminance(tooltipForeground)+.05)/(Luminance(tooltipBackground)+.05)>=7,"tooltip rendered text contrast exceeds 7 to 1");
        var tooltipBitmap=new RenderTargetBitmap((int)Math.Ceiling(tooltip.ActualWidth),(int)Math.Ceiling(tooltip.ActualHeight),96,96,PixelFormats.Pbgra32);tooltipBitmap.Render(tooltip);
        var tooltipPng=new PngBitmapEncoder();tooltipPng.Frames.Add(BitmapFrame.Create(tooltipBitmap));
        using(var tooltipOutput=File.Create(Path.Combine(qa,"today-tooltip-dark.png")))tooltipPng.Save(tooltipOutput);
        calendar.Refresh(new DateOnly(2026,1,15));calendar.MoveMonth(-1);Assert(heading.Text=="2025년 12월","previous month crosses year boundary");
        calendar.MoveMonth(1);Assert(heading.Text=="2026년 1월","next month crosses year boundary");
        calendar.Refresh(new DateOnly(1,1,1));calendar.MoveMonth(-1);Assert(heading.Text=="0001년 1월" && !previous.IsEnabled,"previous month safely stops at minimum year");
        calendar.Refresh(new DateOnly(9999,12,31));calendar.MoveMonth(1);Assert(heading.Text=="9999년 12월" && !next.IsEnabled,"next month safely stops at maximum year");
        calendar.GoToToday();
        Assert(dates.IsHitTestVisible && dates.Children.OfType<Border>().Where(b=>b.Child is TextBlock).All(b=>b.Focusable && b.ToolTip==null),"date grid accepts selection without hover tooltips");
        Assert(navigation.Children.Count==3 && previous.ToolTip==null && next.ToolTip==null,"month arrows have no hover tooltips or persistent labels");
        // Measure a detached HUD: a live Window restores its child's monitor-sized layout.
        var preview=new OverlayTabs();var previewBoard=ScheduleBoardFor(preview,Path.Combine(qa,"overlay-calendar-preview-"+Guid.NewGuid().ToString("N")));
        var previewCalendar=(OverlayCalendarView)previewBoard.FindName("Calendar");
        var previewTabs=(TabControl)preview.FindName("Tabs");
        previewTabs.SelectedIndex=0;
        foreach(var size in new[]{new Size(1440,900),new Size(800,600),new Size(480,360)}) {
            preview.Measure(size);preview.Arrange(new Rect(size));preview.UpdateLayout();
            var previewEditor=(OverlayDayEditor)previewBoard.FindName("DayEditor");
            var calendarCenter=previewCalendar.TranslatePoint(new Point(previewCalendar.ActualWidth/2,previewCalendar.ActualHeight/2),preview);
            var header=(FrameworkElement)previewTabs.Template.FindName("TabStrip",previewTabs);
            var headerCenter=header.TranslatePoint(new Point(header.ActualWidth/2,header.ActualHeight/2),preview);
            Assert(((Grid)previewBoard.FindName("DayPage")).Visibility==Visibility.Collapsed && Math.Abs(calendarCenter.X-size.Width/2)<1 && Math.Abs(calendarCenter.Y-size.Height/2)<1 && Math.Abs(headerCenter.X-size.Width/2)<1 && headerCenter.Y<80,"calendar alone is centered under floating top tabs at "+size);
            var previewHeading=(TextBlock)((Grid)((StackPanel)((StackPanel)previewCalendar.Child).Children[0]).Children[0]).Children[1];
            var titleCenter=previewHeading.TranslatePoint(new Point(previewHeading.ActualWidth/2,0),previewCalendar);
            Assert(Math.Abs(titleCenter.X-previewCalendar.ActualWidth/2)<1,"year month label stays horizontally centered at "+size);
            var bmp=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bmp.Render(preview);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using var output=File.Create(Path.Combine(qa,$"overlay-calendar-{size.Width}.png"));png.Save(output);
        }
    }
#endif
    private static void CheckAppearanceSettings(Controller controller,string qa)
    {
        controller.Commit(d=> { d.Settings.OverlayBlurRadius=32;d.Settings.OverlayShowMilliseconds=600;d.Settings.OverlayHideMilliseconds=1000; });
        var saved=new Store(controller.Store.Root).Load().Settings;
        Assert(saved.OverlayBlurRadius==32 && saved.OverlayShowMilliseconds==600 && saved.OverlayHideMilliseconds==1000,"overlay appearance settings survive encrypted reload");
        var layers=(Grid)controller.Overlay.Content;
        var background=(Image)layers.Children[0];var blur=(BlurEffect)background.Effect;
        controller.ToggleOverlay();Pump(350);
        Assert(controller.Overlay.IsVisible && blur.Radius>0 && blur.Radius<32,"configured show time keeps transition running beyond old 280ms default");
        Pump(400);Assert(Math.Abs(blur.Radius-32)<.001,"configured blur strength reaches requested value");
        controller.Commit(d=>d.Settings.OverlayBlurRadius=8);
        Assert(Math.Abs(blur.Radius-32)<.01,"changing blur setting mid display preserves current radius");
        Pump(300);Assert(blur.Radius>8 && blur.Radius<32,"new blur strength changes gradually while visible");
        Pump(400);Assert(Math.Abs(blur.Radius-8)<.001,"new blur strength settles without replaying visibility");
        controller.HideOverlay();Pump(700);
        Assert(controller.Overlay.IsVisible && blur.Radius>0 && blur.Radius<8,"separate hide time keeps fade running beyond configured show time");
        Pump(450);Assert(!controller.Overlay.IsVisible,"configured hide time completes dismissal");
        controller.Commit(d=> { d.Settings.OverlayBlurRadius=0;d.Settings.OverlayShowMilliseconds=0;d.Settings.OverlayHideMilliseconds=0; });
        controller.ToggleOverlay();
        Assert(controller.Overlay.IsVisible && blur.Radius==0 && background.Source==null && background.Opacity==0,"zero blur disables capture and zero show time opens immediately");
        controller.Commit(d=>d.Settings.OverlayBlurRadius=12);
        Assert(!controller.Overlay.IsVisible && background.Source==null,"enabling blur hides overlay until a safe background snapshot is ready");
        AwaitBackdrop(()=>controller.Overlay.IsVisible && background.Source!=null);
        Assert(blur.Radius==12,"enabling blur reveals overlay with a fixed background snapshot");
        controller.Commit(d=>d.Settings.OverlayBlurRadius=0);
        Assert(controller.Overlay.IsVisible && blur.Radius==0 && background.Source==null,"disabling blur while visible releases capture without closing overlay");
        controller.HideOverlay();Assert(!controller.Overlay.IsVisible,"zero hide time dismisses immediately");
    }
    private static void CheckExitCancellation(Controller controller)
    {
        var tabs=(OverlayTabs)((Grid)controller.Overlay.Content).Children[2];
        var settings=(OverlaySettingsView)tabs.FindName("SettingsView");
        var key=SettingEditor<TextBox>(settings,"오버레이 열기·닫기");
        key.Text="invalid shortcut";
        Chord(ExitKeys(controller));
        Assert(!controller.Exiting && !controller.ExitPending && settings.HasChanges && key.Text=="invalid shortcut","exit shortcut retains an invalid overlay draft and cancels shutdown without a manager");
        ReloadSettings(settings,controller.Data.Settings);
    }
    private sealed class KeyboardHost : IDisposable
    {
        public Window Window { get; }=new() { Title="Facet synthetic keyboard checks",Width=320,Height=160,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize };
        private readonly TextBox input=new() { Width=20,Height=20,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top };
        private readonly DrawingBrush pattern;
        public int Escapes { get; private set; }
        public KeyboardHost()
        {
            var drawing=new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(Brushes.White,null,new RectangleGeometry(new Rect(0,0,64,64))));
            drawing.Children.Add(new GeometryDrawing(Brushes.Black,null,new RectangleGeometry(new Rect(0,0,32,32))));
            drawing.Children.Add(new GeometryDrawing(Brushes.Black,null,new RectangleGeometry(new Rect(32,32,32,32))));
            pattern=new DrawingBrush(drawing) { TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,64,64) };
            RestorePattern();Window.Content=input;Window.Loaded+=(_,_)=>input.Focus();
            Window.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)Escapes++;};
        }
        public void RestorePattern()=>Window.Background=pattern;
        public bool Focus()
        {
            var hwnd=new WindowInteropHelper(Window).Handle;
            for(int attempt=0;attempt<10;attempt++) {
                uint foregroundThread=GetWindowThreadProcessId(Native.ForegroundWindow,out _), currentThread=GetCurrentThreadId();
                bool attached=foregroundThread!=0 && foregroundThread!=currentThread && AttachThreadInput(currentThread,foregroundThread,true);
                try { Window.Activate();Native.RestoreForeground(hwnd); }
                finally { if(attached)AttachThreadInput(currentThread,foregroundThread,false); }
                input.Focus();Pump();
                if(Native.ForegroundWindow==hwnd && Window.IsKeyboardFocusWithin)return true;
            }
            Console.WriteLine($"Keyboard target focus unavailable: foreground={Native.ForegroundWindow}, target={hwnd}, active={Window.IsActive}, keyboard={Window.IsKeyboardFocusWithin}");
            return false;
        }
        public void Dispose()=>Window.Close();
    }
    [STAThread] private static int Main(string[] args)
    {
        Facet.Design.Skins.SharedFolders.Add(Path.Combine(AppContext.BaseDirectory,"registry-skins"));
        if(args.Contains("--launcher-echo")) { File.WriteAllText(args[1],JsonSerializer.Serialize(new { executable=Environment.ProcessPath,args,launcher=Environment.GetEnvironmentVariable("DAILYDOCK_LAUNCHER") }));return 0; }
        if(args.Contains("--launcher-app")) return RunLauncherApp(args);
        if(args.Contains("--skin-app")) return RunSkinApp(args);
        if(args.Contains("--skin-preview")) return RunSkinPreview(args);
        if(args.Contains("--voyage-preview")) return RunVoyagePreview(args);
        if(args.Contains("--world-net-check")) return RunWorldNetCheck();
        if(args.Contains("--world-widgets-check")) return RunWorldWidgetsCheck();
        if(args.Contains("--world-skin-check")) return RunWorldSkinCheck(args[1]);
        // Manual look at a skin in the real app: prepares a throwaway data folder for Facet --data-dir.
        if(args.Contains("--seed-skin")) { var seeded=new Store(args[1]);var seedData=seeded.Load();seedData.Settings.Skin=args[2];seeded.Save(seedData);return 0; }
#if PLUGIN_CHECKS
        if(args.Contains("--hud-preview")) return RunHudPreview(args);
#if PLUGIN_CHECKS
        if(args.Contains("--clock-face-preview")) return RunClockFacePreview(args);
#endif
#endif
        string qa=Path.GetFullPath(Path.Combine(".test-data","journal-"+Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(qa);
        try {
            CheckStartupRegistration();
            if(args.Contains("--launcher")) CheckLauncher(qa);
            string storeRoot=Path.Combine(qa,"store");
            var ui=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            foreach(string resource in new[]{"Tokens","Components"})ui.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri($"pack://application:,,,/Facet;component/Design/{resource}.xaml") });
            using var controller=new Controller(storeRoot,args.Contains("--overlay-visuals") ? TestExitShortcut() : Controller.ExitHotkey,
                args.Contains("--overlay-visuals") ? TestQuickLaunchShortcut() : null);var window=controller.Overlay;
            if(args.Contains("--overlay-ui") || args.Contains("--overlay-visuals") || args.Contains("--backdrop-async-check")) CheckBackdropCaptureSession();
            if(args.Contains("--overlay-visuals")) {
                var exitTimeout=new DispatcherTimer { Interval=TimeSpan.FromSeconds(5) };
                exitTimeout.Tick+=(_,_)=> { exitTimeout.Stop();ui.Shutdown(); };
                ui.Dispatcher.BeginInvoke(()=> {
                    try {
                        CheckOverlay(controller,qa);
                        CheckExitCancellation(controller);
                        controller.ToggleOverlay();Pump();
                        Assert(controller.Overlay.IsVisible,"overlay is open before final global exit check");
                        exitTimeout.Start();
                        var exitKeys=ExitKeys(controller);
                        foreach(byte key in exitKeys)keybd_event(key,0,0,0);
                        foreach(byte key in exitKeys.Reverse())keybd_event(key,0,2,0);
                    }
                    catch(Exception ex) { File.WriteAllText(Path.Combine(qa,"error.txt"),ex.ToString());Console.WriteLine(ex);Environment.ExitCode=1;ui.Shutdown(); }
                });
                ui.Run();
                exitTimeout.Stop();
                if(Environment.ExitCode==0) {
                    Assert(controller.Exiting && !window.IsVisible && !controller.Overlay.IsVisible,"real exit shortcut shuts down application and closes overlay: "+controller.ExitShortcut);
                    controller.Dispose();
                    var exitKey=Native.ParseHotkey(controller.ExitShortcut);
                    bool released=Native.RegisterHotKey(IntPtr.Zero,104,exitKey.modifiers,exitKey.key);
                    if(released)Native.UnregisterHotKey(IntPtr.Zero,104);
                    Assert(released,"shutdown releases fixed global exit shortcut");
                    File.WriteAllText(Path.Combine(qa,"result.txt"),$"PASS {passed} journal checks.");
                }
            }
            else {
                CheckOverlayOnly(controller,qa);
                CheckSkins(controller,qa);
                CheckSkinWidgets(controller,qa);
#if PLUGIN_CHECKS
                CheckWidgetStates();
#endif
                CheckPortablePlugins(qa);
        CheckDataBackup(qa);
                CheckPackages(qa);
                CheckWorkspace(controller);
#if PLUGIN_CHECKS
                CheckConditions(qa);
#endif
#if PLUGIN_CHECKS
                CheckSleep(qa);
#endif
                if(args.Contains("--hotkeys")) CheckOverlayOnlyHotkeys(qa);
                if(args.Contains("--overlay-ui") || args.Contains("--launcher")) CheckStartupSettings(controller);
#if PLUGIN_CHECKS
                if(args.Contains("--overlay-ui")) { CheckDashboard(qa); CheckDisplayCursors(controller); CheckWeather(qa); CheckOverlayCalendar(new OverlayTabs(),qa,nativeInput:false); CheckCalendarDayEditor(controller,qa); CheckDashboardRemindersAndDebug(qa); }
#else
        if(args.Contains("--overlay-ui")) { CheckDashboard(qa); CheckWeather(qa); }
#endif
                if(args.Contains("--overlay-ui") || args.Contains("--overlay-settings")) { CheckOverlaySettings(qa); CheckOverlayAppearance(controller,qa); }
            if(args.Contains("--overlay-ui") || args.Contains("--quick-launch")) { CheckQuickLaunch(controller,qa); QuickLaunchCatalog.Extras=false; }
#if PLUGIN_CHECKS
            if(args.Contains("--overlay-ui") || args.Contains("--typography")) CheckTypography(controller);
#endif
                if(args.Contains("--overlay-focus")) CheckOverlayFocus(controller);
#if PLUGIN_CHECKS
                if(args.Contains("--schedule-native-click")) CheckScheduleNativeClicks(controller);
#endif
#if PLUGIN_CHECKS
                if(args.Contains("--schedule-native-drag")) CheckNativeScheduleDragging(controller);
#endif
#if PLUGIN_CHECKS
                if(args.Contains("--overlay-ui") || args.Contains("--shadow-check")) CheckCachedShadows(qa);
#endif
#if PLUGIN_CHECKS
                if(args.Contains("--shadow-profile")) ProfileShadows(qa);
#endif
                if(args.Contains("--overlay-ui") || args.Contains("--scrollbars")) CheckSharedScrolling(qa);
#if PLUGIN_CHECKS
                if(args.Contains("--overlay-profile")) ProfileOverlay(controller,qa);
#endif
#if PLUGIN_CHECKS
                if(args.Contains("--backdrop-profile")) ProfileBackdrop(qa);
#endif
#if PLUGIN_CHECKS
                if(args.Contains("--async-backdrop-profile")) ProfileAsyncBackdrop(controller,qa);
#endif
                if(args.Contains("--weather-live")) CheckLiveWeather(qa);
#if PLUGIN_CHECKS
                if(args.Contains("--reminder-cli"))
#endif
                
#if PLUGIN_CHECKS
                if(args.Contains("--condition-input")) CheckConditionInput();
#endif
                string mode=args.Contains("--launcher") ? "launcher checks enabled" : args.Contains("--condition-input") ? "condition native pointer checks enabled" : "no visible test windows";
                File.WriteAllText(Path.Combine(qa,"result.txt"),$"PASS {passed} journal checks ({mode}).");
                ui.Shutdown();
            }
            Console.WriteLine("QA output: "+qa);Console.WriteLine($"Final: {passed} journal checks passed.");return Environment.ExitCode;
        } catch(Exception ex) { File.WriteAllText(Path.Combine(qa,"error.txt"),ex.ToString());Console.WriteLine(ex);return 1; }
    }
}
