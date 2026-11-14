using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using Facet;
using Facet.Core;

internal static partial class Program
{
    private static T SettingEditor<T>(OverlaySettingsView view,string name) where T : FrameworkElement =>
        ((StackPanel)view.FindName("Groups")).Children.OfType<StackPanel>()
        .SelectMany(group=>group.Children.OfType<Grid>())
        .Select(row=>(FrameworkElement)((StackPanel)row.Children[1]).Children[0])
        .OfType<T>().Single(editor=>AutomationProperties.GetName(editor)==name);

    private static void ReloadSettings(OverlaySettingsView view,Settings settings) =>
        typeof(OverlaySettingsView).GetMethod("Reload",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,[settings]);

    private static void CheckOverlayOnly(Controller controller,string qa)
    {
        Assert(controller.Data.Plan==null && controller.Data.Work.Count==0 && !File.Exists(controller.Store.PathName),
            "fresh controller does not generate retired plans, work or stores");
        var assembly=typeof(Controller).Assembly;
        Assert(new[]{"MainWindow","DashboardView","TodayView","AgentRoadmapView","ProjectJournalView","ProjectActivityCollector","CodexRoadmapAgent"}
            .All(name=>assembly.GetType("Facet."+name)==null),"retired windows and feature executors are absent from the application assembly");
        var menu=((System.Windows.Forms.NotifyIcon)typeof(Controller).GetField("tray",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(controller)!).ContextMenuStrip!;
        Assert(menu.Items.Cast<System.Windows.Forms.ToolStripItem>().All(item=>!new[]{"대시보드 열기","오늘 할 일 열기","수면 기록 열기","게임 제작 열기","설정 열기"}.Contains(item.Text)),
            "tray contains no route to the removed manager");
        var tabs=(OverlayTabs)((Grid)controller.Overlay.Content).Children[2];
        var settings=(OverlaySettingsView)tabs.FindName("SettingsView");
        var names=((StackPanel)settings.FindName("Groups")).Children.OfType<StackPanel>().SelectMany(group=>group.Children.OfType<Grid>())
            .Select(row=>AutomationProperties.GetName((FrameworkElement)((StackPanel)row.Children[1]).Children[0])).ToArray();
        Assert(!names.Any(name=>name.Contains("수면 단축키") || name.Contains("수면 알림") || name.Contains("제작 화면") || name.Contains("기상") || name.Contains("취침")) && !names.Contains("사용자 알림 사용") && !names.Contains("블러 강도") && !names.Contains("검색 엔진") && !names.Contains("탐색기 alias") && names.Contains("빠른 실행") && names.Contains("Facet 종료") && names.Contains("백업") && names.Contains("복원"),
            "overlay settings omit retired and hidden rows and offer every app shortcut and backup");
        Assert(DashboardAgenda.Today(new AppData(),DateTimeOffset.Now).Count==0,"legacy scheduled sleep alerts never populate the dashboard");

        var root=Path.Combine(qa,"legacy-roundtrip");var store=new Store(root);
        var legacy=JsonSerializer.Deserialize<AppData>("""
            {"SchemaVersion":1,"RoadmapGoal":"old goal","Settings":{"SleepHotkey":"retired invalid key","WorkHotkey":"retired invalid key","Wake":"retired time"},
             "Sleep":[{"Date":"2026-10-17","Note":"old sleep","AwakeMinutes":null}],"RemovedSleep":[{"Date":"2026-10-16","Note":"deleted"}],
             "Work":[{"Title":"old work","Evidence":"evidence","Focus":true}],"Projects":[{"Name":"old project","Root":"D:/synthetic"}],
             "ProjectJournals":[{"Date":"2026-10-17","Comment":"old comment"}],"Plan":{"Start":"2026-10-17","Entries":[{"Note":"old plan"}]},
             "Notices":[{"Text":"old notice"}],"Delivered":{"old-key":"2026-10-17T00:00:00"}}
            """)!;
        store.Save(legacy);string before=JsonSerializer.Serialize(store.Load());
        using var restored=new Controller(root);
        var expected=JsonSerializer.Deserialize<AppData>(before)!;expected.Settings.OverlayVisible=false;
        Assert(JsonSerializer.Serialize(restored.Data)==JsonSerializer.Serialize(expected),
            "startup retains all retired records and settings without seeding or executing them");
        Assert(restored.Commit(data=>data.CalendarDays.Add(new(){Date=new(2026,10,25),Note="new note"})),"overlay edits persist beside legacy records");
        var loaded=store.Load();loaded.CalendarDays.Clear();loaded.Settings.OverlayVisible=true;
        Assert(JsonSerializer.Serialize(loaded)==before,"saving active overlay content preserves every legacy field in the encrypted store");
        var view=(OverlaySettingsView)((OverlayTabs)((Grid)restored.Overlay.Content).Children[2]).FindName("SettingsView");
        view.Configure(()=>restored.Data.Settings,_=>false);
        SettingEditor<TextBox>(view,"오버레이 열기·닫기").Text="Ctrl+Alt+F2";
        restored.Exit();
        Assert(!restored.Exiting && !restored.ExitPending && view.HasChanges,"failed overlay save cancels shutdown and retains the draft without a manager");
        ReloadSettings(view,restored.Data.Settings);

    }

    private static void CheckOverlayOnlyHotkeys(string qa)
    {
        var root=Path.Combine(qa,"native-hotkeys");
        using var controller=new Controller(root,TestExitShortcut(),TestQuickLaunchShortcut());
        var settings=controller.Data.Settings;
        string[] free=Enumerable.Range(1,12).Reverse().Select(i=>"Ctrl+Alt+Shift+F"+i)
            .Where(key=>key!=controller.ExitShortcut && key!=controller.QuickLaunchShortcut)
            .Where(key=> {var p=Native.ParseHotkey(key);bool ok=Native.RegisterHotKey(IntPtr.Zero,197,p.modifiers,p.key);if(ok)Native.UnregisterHotKey(IntPtr.Zero,197);return ok;}).Take(3).ToArray();
        Assert(free.Length==3,"isolated overlay and retired shortcuts are available");
        settings.OverlayHotkey=free[0];settings.SleepHotkey=free[1];settings.WorkHotkey=free[2];
        settings.OverlayBlurRadius=0;settings.OverlayShowMilliseconds=0;settings.OverlayHideMilliseconds=0;
        controller.Start(false);
        Assert(!controller.HotkeyStatus.Contains("실패") && controller.HotkeyStatus.Contains("3개") && !controller.Overlay.IsVisible,
            "three native hotkeys register on the hidden overlay HWND");
        foreach(string key in free.Skip(1)) {
            var p=Native.ParseHotkey(key);bool available=Native.RegisterHotKey(IntPtr.Zero,196,p.modifiers,p.key);
            if(available)Native.UnregisterHotKey(IntPtr.Zero,196);
            Assert(available,"retired shortcut remains available to other apps: "+key);
        }
        var hwnd=new WindowInteropHelper(controller.Overlay).Handle;
        IntPtr foreground=Native.ForegroundWindow;
        try {
            PostMessage(hwnd,0x0312,new IntPtr(101),IntPtr.Zero);Pump(30);
            Assert(!controller.Overlay.IsVisible,"retired hotkey messages cannot expose a window");
            PostMessage(hwnd,0x0312,new IntPtr(103),IntPtr.Zero);Pump(40);
            Assert(controller.Overlay.IsVisible && !controller.Overlay.IsQuickLaunch,"overlay shortcut route works without a manager");
            controller.HideOverlay(restore:false);
            PostMessage(hwnd,0x0312,new IntPtr(105),IntPtr.Zero);Pump(40);
            Assert(controller.Overlay.IsVisible && controller.Overlay.IsQuickLaunch,"quick-launch shortcut route works after hiding the overlay HWND");
            controller.HideOverlay(restore:false);
            UserReminders.Apply(controller.Data,new(){Action="add",Key="overlay-only-reminder",Title="Synthetic reminder",At=DateTimeOffset.Now.AddSeconds(-1)});
            settings.HideFullscreen=false;
            controller.Tick();
            var toast=(ToastWindow)typeof(Controller).GetField("toast",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(controller)!;
            Assert(toast.IsVisible && new Store(root).Load().Reminders.Single().DeliveredAt!=null,"user reminder is durably delivered without a manager");
            var buttons=(StackPanel)((StackPanel)((Border)toast.Content).Child).Children[2];
            ((Button)buttons.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(controller.Overlay.IsVisible && !controller.Overlay.IsQuickLaunch && !toast.IsVisible,"reminder open button opens the normal overlay and closes its toast");
        } finally { controller.HideOverlay(restore:false);controller.Dispose();Native.RestoreForeground(foreground); }
        foreach(string key in new[]{settings.OverlayHotkey,controller.ExitShortcut,controller.QuickLaunchShortcut}) {
            var p=Native.ParseHotkey(key);bool available=Native.RegisterHotKey(IntPtr.Zero,195,p.modifiers,p.key);
            if(available)Native.UnregisterHotKey(IntPtr.Zero,195);
            Assert(available,"dispose releases native shortcut: "+key);
        }
    }
}
