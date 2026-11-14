extern alias Launcher;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Facet;
using Facet.Core;
using Facet.Deployment;
using ReleaseManifest = Launcher::Facet.Deployment.ReleaseManifest;

internal static partial class Program
{
    private delegate bool WindowCallback(IntPtr hwnd,IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread,WindowCallback callback,IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    private static string ReportPath(string directory,string release) => Path.Combine(directory,release+".json");
    private static bool WaitUntil(Func<bool> check,int milliseconds=10000)
    {
        var timer=Stopwatch.StartNew();
        while(timer.ElapsedMilliseconds<milliseconds) { if(check())return true;Thread.Sleep(50); }
        return check();
    }
    private static void SelectRelease(string directory,string relative,int schema=1)
    {
        string temporary=Path.Combine(directory,"latest.tmp"),path=Path.Combine(directory,"latest.json");
        File.WriteAllText(temporary,JsonSerializer.Serialize(new { schemaVersion=schema,executable=relative }));
        if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
    }
    private static void CheckLauncher(string qa)
    {
        string deployment=Path.Combine(qa,"deployment with spaces"),reports=Path.Combine(qa,"launch reports"),root=Path.Combine(qa,"launch store");
        Directory.CreateDirectory(deployment);Directory.CreateDirectory(reports);
        foreach(string file in Directory.GetFiles(AppContext.BaseDirectory))File.Copy(file,Path.Combine(deployment,Path.GetFileName(file)));
        string launcher=Path.Combine(deployment,LaunchProtocol.LauncherName);
        foreach(string release in new[]{"first","second"}) {
            string folder=Path.Combine(deployment,"releases",release);Directory.CreateDirectory(folder);
            foreach(string file in Directory.GetFiles(AppContext.BaseDirectory))File.Copy(file,Path.Combine(folder,Path.GetFileName(file)));
            File.Copy(Path.Combine(AppContext.BaseDirectory,"Facet.JournalChecks.exe"),Path.Combine(folder,"Facet.exe"),true);
        }
        void Run(params string[] arguments) {
            var info=new ProcessStartInfo(launcher) { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=qa };
            foreach(string argument in arguments)info.ArgumentList.Add(argument);
            using var process=Process.Start(info)!;
            if(!process.WaitForExit(10000)) { process.Kill();throw new Exception("Synthetic launcher timed out."); }
            Assert(process.ExitCode==0,"fixed launcher completes without a console or error");
        }
        bool Reject(string executable,int schema=1) {
            SelectRelease(deployment,executable,schema);
            try { ReleaseManifest.Resolve(deployment);return false; } catch(Exception ex) when(ex is IOException or InvalidDataException or JsonException) { return true; }
        }
        Assert(Reject("../Facet.exe") && Reject("C:/Facet.exe") && Reject("releases/../Facet.exe"),"manifest rejects paths outside a complete release");
        Assert(Reject("releases/missing/Facet.exe") && Reject("releases/first/Facet.exe",2),"missing and unsupported releases cannot replace a working target");
        string runtime=Path.Combine(deployment,"releases","second","Facet.runtimeconfig.json");File.Move(runtime,runtime+".hold");
        Assert(Reject("releases/second/Facet.exe"),"incomplete release is rejected before launch");File.Move(runtime+".hold",runtime);
        foreach(string release in new[]{"first","second"}) {
            SelectRelease(deployment,$"releases/{release}/Facet.exe");
            string report=ReportPath(reports,"echo-"+release);
            string[] arguments=["--launcher-echo",report,"spaces and 한글","quoted \"value\"","tail\\","--data-dir",root];
            Run(arguments);
            Assert(WaitUntil(()=>File.Exists(report)),"child starts through stable launcher for "+release);
            var result=JsonDocument.Parse(File.ReadAllText(report)).RootElement;
            Assert(result.GetProperty("args").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(arguments),"launcher preserves Unicode quoting spaces and all command arguments");
            Assert(result.GetProperty("executable").GetString()==Path.Combine(deployment,"releases",release,"Facet.exe") && result.GetProperty("launcher").GetString()==launcher,"same fixed launcher follows committed latest manifest for "+release);
        }
        var data=new AppData();data.Settings.Notifications=false;data.Settings.OverlayVisible=false;
        data.Settings.OverlayBlurRadius=0;data.Settings.OverlayShowMilliseconds=0;data.Settings.OverlayHideMilliseconds=0;
        data.Settings.OverlayHotkey="Ctrl+Alt+Shift+F7";
        new Store(root).Save(data);
        using var stop=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\Facet-qa-stop-"+LaunchProtocol.Hash(root));
        var children=new List<Process>();
        string[] appArgs=["--launcher-app",reports,"--tray","--data-dir",root];
        string[] normalArgs=appArgs.Where(arg=>arg!="--tray").ToArray();
        string[] loginArgs=[..normalArgs,"--autostart"];
        Process WaitForApp(string release) {
            string report=ReportPath(reports,release);
            Assert(WaitUntil(()=>File.Exists(report)),"real Desktop startup completes in synthetic release "+release);
            var result=JsonDocument.Parse(File.ReadAllText(report)).RootElement;
            var process=Process.GetProcessById(result.GetProperty("pid").GetInt32());_ = process.Handle;children.Add(process);
            Assert(result.GetProperty("id").GetString()==LaunchProtocol.AppId && result.GetProperty("command").GetString()==$"\"{launcher}\"" && result.GetProperty("name").GetString()=="Facet" && result.GetProperty("icon").GetString()==launcher+",0","native taskbar window properties use Facet and relaunch fixed launcher with stable identity");
            return process;
        }
        try {
            File.WriteAllText(Path.Combine(reports,"cancel-next"),"");
            SelectRelease(deployment,"releases/first/Facet.exe");Run(appArgs);var first=WaitForApp("first");
            Run(appArgs);
            Thread.Sleep(200);Assert(!File.Exists(Path.Combine(reports,"activated")),"background relaunch does not open current tray instance");
            Run(normalArgs);
            Assert(WaitUntil(()=>File.Exists(Path.Combine(reports,"activated"))) && !first.HasExited,"launching current release opens overlay without exposing manager or restarting");
            File.WriteAllText(Path.Combine(reports,"prepare-cancel"),"");
            Assert(WaitUntil(()=>File.Exists(Path.Combine(reports,"cancel-ready")) || File.Exists(Path.Combine(reports,"cancel-error.txt"))),"overlay draft is prepared before requesting an update");
            if(File.Exists(Path.Combine(reports,"cancel-error.txt")))throw new Exception(File.ReadAllText(Path.Combine(reports,"cancel-error.txt")));
            SelectRelease(deployment,"releases/second/Facet.exe");Run(normalArgs);
            Assert(WaitUntil(()=>File.Exists(Path.Combine(reports,"cancelled"))) && !first.HasExited && !File.Exists(ReportPath(reports,"second")),"cancelled update preserves running old version and unsaved draft");
            stop.Set();Assert(first.WaitForExit(5000) && first.ExitCode==0,"synthetic first app exits normally for test cleanup");
            File.Delete(Path.Combine(reports,"cancel-next"));File.Delete(ReportPath(reports,"first"));
            SelectRelease(deployment,"releases/first/Facet.exe");Run(appArgs);first=WaitForApp("first");
            SelectRelease(deployment,"releases/second/Facet.exe");Run(loginArgs);Thread.Sleep(200);
            Assert(!first.HasExited && !File.Exists(ReportPath(reports,"second")),"login launch leaves an older running app quiet without upgrade prompts");
            Run(normalArgs);var second=WaitForApp("second");
            Assert(first.WaitForExit(5000) && first.ExitCode==0 && !second.HasExited,"launcher waits for graceful old app exit then starts latest release");
            stop.Set();Assert(second.WaitForExit(5000) && second.ExitCode==0,"latest synthetic app exits normally");
            File.Delete(ReportPath(reports,"first"));File.Delete(ReportPath(reports,"second"));
            SelectRelease(deployment,"releases/first/Facet.exe");Run(loginArgs);first=WaitForApp("first");
            SelectRelease(deployment,"releases/second/Facet.exe");
            using(var open=EventWaitHandle.OpenExisting(LaunchProtocol.Open(root)))open.Set();
            second=WaitForApp("second");
            Assert(first.WaitForExit(5000) && first.ExitCode==0 && !second.HasExited,"activating an old taskbar window discovers latest release and upgrades through fixed launcher");
            var upgraded=JsonDocument.Parse(File.ReadAllText(ReportPath(reports,"second"))).RootElement;
            Assert(upgraded.GetProperty("overlay").GetBoolean() && !upgraded.GetProperty("manager").GetBoolean(),"explicit reopen after upgrading tray instance shows overlay in new version");
            stop.Set();Assert(second.WaitForExit(5000) && second.ExitCode==0,"activation upgrade leaves only latest app and exits normally");
            File.Delete(ReportPath(reports,"first"));
            SelectRelease(deployment,"releases/first/Facet.exe");
            Run(normalArgs);first=WaitForApp("first");
            var startup=JsonDocument.Parse(File.ReadAllText(ReportPath(reports,"first"))).RootElement;
            Assert(startup.GetProperty("overlay").GetBoolean() && !startup.GetProperty("manager").GetBoolean(),"normal cold startup shows overlay and keeps management window hidden");
            Assert(!startup.GetProperty("toast").GetBoolean(),"enabled startup overlay suppresses launch toast");
            File.WriteAllText(Path.Combine(reports,"hide-overlay"),"");
            Assert(WaitUntil(()=>File.Exists(Path.Combine(reports,"hidden"))),"synthetic startup overlay dismisses before relaunch");
            Run(loginArgs);Thread.Sleep(200);
            Assert(!File.Exists(Path.Combine(reports,"activated")) && !first.HasExited,"login relaunch never reopens an existing hidden overlay");
            Run(normalArgs);
            Assert(WaitUntil(()=>File.Exists(Path.Combine(reports,"activated"))) && !first.HasExited,"relaunch reopens dismissed overlay in the same process");
            Run(normalArgs);Thread.Sleep(200);
            Assert(File.Exists(Path.Combine(reports,"activated")) && !first.HasExited,"relaunching visible overlay keeps it open instead of toggling it off");
            Assert(!File.Exists(Path.Combine(reports,"startup-toast-created")),"enabled startup overlay suppresses toast on existing-instance relaunch");
            stop.Set();Assert(first.WaitForExit(5000) && first.ExitCode==0,"normal startup app exits gracefully");
            File.Delete(ReportPath(reports,"first"));
            Run(appArgs);first=WaitForApp("first");
            startup=JsonDocument.Parse(File.ReadAllText(ReportPath(reports,"first"))).RootElement;
            Assert(!startup.GetProperty("overlay").GetBoolean() && !startup.GetProperty("manager").GetBoolean(),"tray autostart stays silent despite previously saved visible overlay");
            Assert(!startup.GetProperty("toast").GetBoolean(),"tray startup does not show a launch toast");
            stop.Set();Assert(first.WaitForExit(5000) && first.ExitCode==0,"silent startup app exits gracefully");
            File.Delete(ReportPath(reports,"first"));File.Delete(Path.Combine(reports,"activated"));
            Run(loginArgs);first=WaitForApp("first");
            startup=JsonDocument.Parse(File.ReadAllText(ReportPath(reports,"first"))).RootElement;
            Assert(!startup.GetProperty("overlay").GetBoolean() && !startup.GetProperty("manager").GetBoolean(),"autostart marker alone forces quiet login startup even when direct launch overlay is enabled");
            Assert(!startup.GetProperty("toast").GetBoolean(),"login startup does not show a launch toast");
            stop.Set();Assert(first.WaitForExit(5000) && first.ExitCode==0,"login test app exits gracefully");
            var quiet=new Store(root).Load();quiet.Settings.ShowOverlayOnLaunch=false;quiet.Settings.OverlayVisible=true;new Store(root).Save(quiet);
            File.Delete(ReportPath(reports,"first"));File.Delete(Path.Combine(reports,"activated"));
            Run(normalArgs);first=WaitForApp("first");
            startup=JsonDocument.Parse(File.ReadAllText(ReportPath(reports,"first"))).RootElement;
            Assert(!startup.GetProperty("overlay").GetBoolean() && !startup.GetProperty("manager").GetBoolean(),"direct cold launch respects disabled overlay preference independently of saved current visibility");
            Assert(startup.GetProperty("toast").GetBoolean(),"direct cold launch with overlay disabled shows launch toast");
            var toastState=startup.GetProperty("toastState");var workArea=System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            Assert(toastState.GetProperty("noActivate").GetBoolean() && !toastState.GetProperty("clickThrough").GetBoolean() && toastState.GetProperty("topmost").GetBoolean(),"launch toast receives clicks without activation and stays topmost");
            Assert(toastState.GetProperty("x").GetInt32()>=workArea.Left && toastState.GetProperty("x").GetInt32()+toastState.GetProperty("width").GetInt32()<=workArea.Right && toastState.GetProperty("y").GetInt32()+toastState.GetProperty("height").GetInt32()<workArea.Bottom,"launch toast sits inside primary work area above taskbar");
            Assert(Math.Abs(2*toastState.GetProperty("x").GetInt32()+toastState.GetProperty("width").GetInt32()-workArea.Left-workArea.Right)<=1,"launch toast is horizontally centered above taskbar");
            Assert(WaitUntil(()=>File.Exists(Path.Combine(reports,"startup-toast-held"))),"launch toast reaches full opacity and holds");
            Assert(WaitUntil(()=>File.Exists(Path.Combine(reports,"startup-toast-closed"))),"launch toast automatically closes after fades and hold");
            Assert(File.Exists(Path.Combine(reports,"startup-toast-fade-in")) && File.Exists(Path.Combine(reports,"startup-toast-fade-out")),"launch toast passes through intermediate opacity in both fades");
            long toastLifetime=long.Parse(File.ReadAllText(Path.Combine(reports,"startup-toast-closed")));
            Assert(toastLifetime>=2300 && toastLifetime<4500,"launch toast lasts approximately 0.2s fade-in + 2s hold + 0.2s fade-out");
            File.Delete(Path.Combine(reports,"startup-toast-created"));
            Run(loginArgs);Thread.Sleep(200);
            Assert(!File.Exists(Path.Combine(reports,"startup-toast-created")),"login relaunch stays silent even when startup overlay preference is disabled");
            Run(normalArgs);Thread.Sleep(200);
            Assert(!File.Exists(Path.Combine(reports,"activated")) && !first.HasExited,"direct relaunch respects disabled overlay preference in running app");
            Assert(WaitUntil(()=>File.Exists(Path.Combine(reports,"startup-toast-created"))),"disabled startup overlay shows launch toast on existing-instance relaunch");
            File.WriteAllText(Path.Combine(reports,"hover-startup-toast"),"");
            Assert(WaitUntil(()=>File.Exists(Path.Combine(reports,"startup-toast-hover-held"))),"hover keeps launch toast fully visible beyond its two-second hold");
            File.WriteAllText(Path.Combine(reports,"rescue-startup-toast"),"");
            File.WriteAllText(Path.Combine(reports,"leave-startup-toast"),"");
            Assert(WaitUntil(()=>File.Exists(Path.Combine(reports,"startup-toast-auto-restored"))),"entering during automatic fade-out fades back in and keeps toast visible");
            var restored=JsonDocument.Parse(File.ReadAllText(Path.Combine(reports,"startup-toast-auto-reentered"))).RootElement;
            Assert(Math.Abs(restored.GetProperty("before").GetDouble()-restored.GetProperty("after").GetDouble())<.02 && !File.Exists(Path.Combine(reports,"startup-toast-hover-closed")),"automatic fade reversal preserves current opacity and invalidates the previous close");
            File.WriteAllText(Path.Combine(reports,"leave-startup-toast"),"");
            Assert(WaitUntil(()=>File.Exists(Path.Combine(reports,"startup-toast-hover-closed"))),"launch toast closes after pointer leaves");
            long leaveLifetime=long.Parse(File.ReadAllText(Path.Combine(reports,"startup-toast-hover-closed")));
            Assert(leaveLifetime>=2100 && leaveLifetime<3500 && !File.Exists(Path.Combine(reports,"startup-toast-hover-early-fade")),"pointer leave starts a fresh two-second hold before the 200ms fade");
            File.Delete(Path.Combine(reports,"startup-toast-created"));
            Run(normalArgs);
            Assert(WaitUntil(()=>File.Exists(Path.Combine(reports,"startup-toast-created"))),"another launch can show a fresh toast after hover dismissal");
            File.WriteAllText(Path.Combine(reports,"click-startup-toast"),"");
            Assert(WaitUntil(()=>File.Exists(Path.Combine(reports,"startup-toast-click-closed"))),"click dismisses launch toast before the two-second hold finishes");
            var clicked=JsonDocument.Parse(File.ReadAllText(Path.Combine(reports,"startup-toast-click-closed"))).RootElement;
            Assert(clicked.GetProperty("milliseconds").GetInt64()>=100 && clicked.GetProperty("milliseconds").GetInt64()<800 && File.Exists(Path.Combine(reports,"startup-toast-click-fading")),"click while hovered starts a 200ms fade and repeated clicks do not restart it");
            Assert(clicked.GetProperty("focusPreserved").GetBoolean() && !clicked.GetProperty("overlay").GetBoolean() && !clicked.GetProperty("manager").GetBoolean(),"dismissing launch toast preserves foreground focus without opening app screens");
            File.Delete(Path.Combine(reports,"startup-toast-created"));Run(normalArgs);
            Assert(WaitUntil(()=>File.Exists(Path.Combine(reports,"startup-toast-created"))),"fresh toast is available for click fade reversal");
            File.WriteAllText(Path.Combine(reports,"rescue-click-startup-toast"),"");
            Assert(WaitUntil(()=>File.Exists(Path.Combine(reports,"startup-toast-click-restored"))),"reentering during click fade-out fades back in and holds beyond two seconds");
            var clickRestored=JsonDocument.Parse(File.ReadAllText(Path.Combine(reports,"startup-toast-click-reentered"))).RootElement;
            Assert(Math.Abs(clickRestored.GetProperty("before").GetDouble()-clickRestored.GetProperty("after").GetDouble())<.02,"click fade reversal continues smoothly from current opacity");
            stop.Set();Assert(first.WaitForExit(5000) && first.ExitCode==0,"quiet direct launch app exits gracefully");
        }
        finally {
            stop.Set();
            foreach(var child in children) { if(!child.HasExited && !child.WaitForExit(2000))child.Kill();child.Dispose(); }
        }
    }
    private static int RunLauncherApp(string[] args)
    {
        static void ClickToast(ToastWindow target)
        {
            // Route through the content without moving the user's pointer or activating a window.
            var click=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left)
                { RoutedEvent=System.Windows.Input.Mouse.PreviewMouseDownEvent };
            ((UIElement)target.Content).RaiseEvent(click);
        }
        static void HoverToast(ToastWindow target,bool entered)
        {
            target.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount)
                { RoutedEvent=entered ? System.Windows.Input.Mouse.MouseEnterEvent : System.Windows.Input.Mouse.MouseLeaveEvent });
        }
        string reports=args[1],root=LaunchProtocol.DataRoot(args),release=new DirectoryInfo(AppContext.BaseDirectory).Name;
        var app=new Facet.App();app.InitializeComponent();
        using var stop=EventWaitHandle.OpenExisting(@"Local\Facet-qa-stop-"+LaunchProtocol.Hash(root));
        bool cancel=File.Exists(Path.Combine(reports,"cancel-next")),stopPending=false;
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(50) };var lifetime=Stopwatch.StartNew();
        // Measure from the actual reveal, before the fade starts, rather than a later startup callback.
        var toastLifetime=new Stopwatch();
        ToastWindow? clickedToast=null;Stopwatch? clickLifetime=null;IntPtr clickForeground=IntPtr.Zero;
        ToastWindow? hoveredToast=null;Stopwatch? hoverLifetime=null,leaveLifetime=null;
        Stopwatch? autoRestoreLifetime=null,clickRestoreLifetime=null;
        ToastWindow? clickRestoreToast=null;
        EventManager.RegisterClassHandler(typeof(ToastWindow),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=> {
            toastLifetime.Restart();
            var target=(ToastWindow)sender;
            var opacity=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Window.OpacityProperty,typeof(ToastWindow));
            EventHandler changed=(_,_)=> {
                if(target.Opacity>0 && target.Opacity<1)
                    File.WriteAllText(Path.Combine(reports,toastLifetime.ElapsedMilliseconds<500 ? "startup-toast-fade-in" : "startup-toast-fade-out"),"");
            };
            opacity.AddValueChanged(target,changed);
            target.Closed+=(_,_)=>opacity.RemoveValueChanged(target,changed);
        }));
        app.Startup+=(_,_)=>app.Dispatcher.BeginInvoke(()=> {
            var controller=app.Controller!;
            var settings=(OverlaySettingsView)((OverlayTabs)((Grid)controller.Overlay.Content).Children[2]).FindName("SettingsView");
            var hwnd=new WindowInteropHelper(controller.Overlay).Handle;
            var toast=app.Windows.OfType<ToastWindow>().FirstOrDefault();
            if(toast!=null) {
                File.WriteAllText(Path.Combine(reports,"startup-toast-created"),"");
                toast.Closed+=(_,_)=>File.WriteAllText(Path.Combine(reports,"startup-toast-closed"),toastLifetime.ElapsedMilliseconds.ToString());
            }
            File.WriteAllText(ReportPath(reports,release),JsonSerializer.Serialize(new { pid=Environment.ProcessId,id=TaskbarIdentity.Read(hwnd,5),command=TaskbarIdentity.Read(hwnd,2),name=TaskbarIdentity.Read(hwnd,4),icon=TaskbarIdentity.Read(hwnd,3),overlay=controller.Overlay.IsVisible,manager=app.Windows.Cast<Window>().Any(w=>w is not OverlayWindow && w is not ToastWindow),toast=toast?.IsVisible==true,toastState=toast==null ? null : Native.WindowState(toast) }));
            timer.Tick+=(_,_)=> {
                if(File.Exists(Path.Combine(reports,"prepare-cancel"))) {
                    File.Delete(Path.Combine(reports,"prepare-cancel"));
                    try {
                        cancel=true;
                        SettingEditor<TextBox>(settings,"오버레이 단축키").Text="invalid shortcut";
                        File.WriteAllText(Path.Combine(reports,"cancel-ready"),"");
                    } catch(Exception ex) { File.WriteAllText(Path.Combine(reports,"cancel-error.txt"),ex.ToString()); }
                }
                if(File.Exists(Path.Combine(reports,"hover-startup-toast")) && app.Windows.OfType<ToastWindow>().FirstOrDefault(t=>t.IsVisible && t.Opacity==1) is {} hoverTarget) {
                    File.Delete(Path.Combine(reports,"hover-startup-toast"));
                    hoveredToast=hoverTarget;hoverLifetime=Stopwatch.StartNew();HoverToast(hoverTarget,true);
                    hoverTarget.Closed+=(_,_)=>File.WriteAllText(Path.Combine(reports,"startup-toast-hover-closed"),(leaveLifetime?.ElapsedMilliseconds ?? -1).ToString());
                }
                if(hoveredToast is { IsVisible:true, Opacity:1 } && hoverLifetime?.ElapsedMilliseconds>=2400)
                    File.WriteAllText(Path.Combine(reports,"startup-toast-hover-held"),"");
                if(File.Exists(Path.Combine(reports,"leave-startup-toast")) && hoveredToast is { IsVisible:true }) {
                    File.Delete(Path.Combine(reports,"leave-startup-toast"));
                    leaveLifetime=Stopwatch.StartNew();HoverToast(hoveredToast,false);
                }
                if(File.Exists(Path.Combine(reports,"rescue-startup-toast")) && hoveredToast is { IsVisible:true } && hoveredToast.Opacity>0 && hoveredToast.Opacity<1) {
                    File.Delete(Path.Combine(reports,"rescue-startup-toast"));
                    double before=hoveredToast.Opacity;HoverToast(hoveredToast,true);leaveLifetime=null;autoRestoreLifetime=Stopwatch.StartNew();
                    File.WriteAllText(Path.Combine(reports,"startup-toast-auto-reentered"),JsonSerializer.Serialize(new { before,after=hoveredToast.Opacity }));
                }
                if(hoveredToast is { IsVisible:true, Opacity:1 } && autoRestoreLifetime?.ElapsedMilliseconds>=2400)
                    File.WriteAllText(Path.Combine(reports,"startup-toast-auto-restored"),"");
                if(hoveredToast is { IsVisible:true } && hoveredToast.Opacity<1 && leaveLifetime?.ElapsedMilliseconds<1900)
                    File.WriteAllText(Path.Combine(reports,"startup-toast-hover-early-fade"),"");
                if(File.Exists(Path.Combine(reports,"click-startup-toast")) && app.Windows.OfType<ToastWindow>().FirstOrDefault(t=>t.IsVisible && t.Opacity==1) is {} target) {
                    File.Delete(Path.Combine(reports,"click-startup-toast"));
                    clickedToast=target;clickLifetime=Stopwatch.StartNew();clickForeground=Native.ForegroundWindow;
                    target.Closed+=(_,_)=>File.WriteAllText(Path.Combine(reports,"startup-toast-click-closed"),JsonSerializer.Serialize(new { milliseconds=clickLifetime.ElapsedMilliseconds,focusPreserved=Native.ForegroundWindow==clickForeground,overlay=controller.Overlay.IsVisible,manager=app.Windows.Cast<Window>().Any(w=>w is not OverlayWindow && w is not ToastWindow) }));
                    HoverToast(target,true);ClickToast(target);
                }
                if(clickedToast is { IsVisible:true } && clickedToast.Opacity>0 && clickedToast.Opacity<1) {
                    File.WriteAllText(Path.Combine(reports,"startup-toast-click-fading"),"");
                    ClickToast(clickedToast);
                }
                if(File.Exists(Path.Combine(reports,"rescue-click-startup-toast")) && app.Windows.OfType<ToastWindow>().FirstOrDefault(t=>t.IsVisible && t.Opacity==1) is {} restoreTarget) {
                    File.Delete(Path.Combine(reports,"rescue-click-startup-toast"));
                    clickRestoreToast=restoreTarget;HoverToast(restoreTarget,true);ClickToast(restoreTarget);HoverToast(restoreTarget,false);
                }
                if(clickRestoreToast is { IsVisible:true } && clickRestoreLifetime==null && clickRestoreToast.Opacity>0 && clickRestoreToast.Opacity<1) {
                    double before=clickRestoreToast.Opacity;HoverToast(clickRestoreToast,true);clickRestoreLifetime=Stopwatch.StartNew();
                    File.WriteAllText(Path.Combine(reports,"startup-toast-click-reentered"),JsonSerializer.Serialize(new { before,after=clickRestoreToast.Opacity }));
                }
                if(clickRestoreToast is { IsVisible:true, Opacity:1 } && clickRestoreLifetime?.ElapsedMilliseconds>=2400)
                    File.WriteAllText(Path.Combine(reports,"startup-toast-click-restored"),"");
                if(app.Windows.OfType<ToastWindow>().Any(t=>t.IsVisible)) File.WriteAllText(Path.Combine(reports,"startup-toast-created"),"");
                if(toast is { IsVisible:true } && toast.Opacity>0 && toast.Opacity<1)
                    File.WriteAllText(Path.Combine(reports,toastLifetime.ElapsedMilliseconds<500 ? "startup-toast-fade-in" : "startup-toast-fade-out"),"");
                if(toast is { IsVisible:true, Opacity:1 } && toastLifetime.ElapsedMilliseconds>=350 && !File.Exists(Path.Combine(reports,"startup-toast-held"))) {
                    var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(toast.ActualWidth),(int)Math.Ceiling(toast.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render((System.Windows.Media.Visual)toast.Content);
                    var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using(var output=File.Create(Path.Combine(reports,"startup-toast.png")))png.Save(output);
                    File.WriteAllText(Path.Combine(reports,"startup-toast-held"),"");
                }
                if(File.Exists(Path.Combine(reports,"hide-overlay"))) {
                    controller.HideOverlay();File.Delete(Path.Combine(reports,"hide-overlay"));
                    File.Delete(Path.Combine(reports,"activated"));File.WriteAllText(Path.Combine(reports,"hidden"),"");
                }
                if(controller.Overlay.IsVisible && controller.Data.Settings.OverlayVisible)File.WriteAllText(Path.Combine(reports,"activated"),"");
                else File.Delete(Path.Combine(reports,"activated"));
                stopPending|=stop.WaitOne(0) || lifetime.ElapsedMilliseconds>30000;
                if(stopPending) { ReloadSettings(settings,controller.Data.Settings);controller.Exit();return; }
                if(cancel && settings.HasChanges && !controller.Exiting && !string.IsNullOrEmpty(((TextBlock)settings.FindName("Status")).Text))
                    File.WriteAllText(Path.Combine(reports,"cancelled"),"");
            };
            timer.Start();
        });
        try { return app.Run(); } finally { timer.Stop(); }
    }
}
