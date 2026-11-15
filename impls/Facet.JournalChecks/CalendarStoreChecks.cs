using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Facet;
using Facet.SchedulePlugin;
using Facet.Core;
using Facet.Plugins;

internal static partial class Program
{
    // Schedules leave the app for the schedule plugin's store once, backed up and verified (ADR 0101).
    private static void CheckCalendarStore(Controller controller)
    {
        var date=new DateOnly(2026,10,24);
        string root=Path.Combine(controller.Store.Root,"calendar-store");Directory.CreateDirectory(root);
        var legacy=new AppData{CalendarDays=[new(){Date=date,Note="이전 기록",Todos=[new(){Title="이전 할 일"}],Schedules=[new(){Name="이전 일정",StartMinute=600}]}]};
        var original=Store.EncodeRecords(legacy);
        string records=Path.Combine(root,"records.dat");File.WriteAllBytes(records,original);

        var store=new Store(root);var loaded=store.Load();
        var day=CalendarNotes.Get(ScheduleData(root),date);
        Assert(day.Note=="이전 기록" && day.Todos.Single().Title=="이전 할 일" && day.Schedules.Single().Name=="이전 일정","days in records.dat move into the schedule plugin's store");
        Assert(File.Exists(Path.Combine(root,"plugin-data",LegacySchedule.Plugin+".dat")) && !File.Exists(store.CalendarPathName),"the plugin store is created and no calendar.dat is written");
        var backup=Directory.GetFiles(store.BackupDirectory).Single();
        Assert(File.ReadAllBytes(backup).SequenceEqual(original) && Store.Decode(original).CalendarDays.Count==1,"the backup is the untouched old records file");
        Assert(Store.Decode(File.ReadAllBytes(records)).CalendarDays.Count==0 && loaded.CalendarDays.Count==0,"days leave records.dat after the verified copy");
        Assert(loaded.Workspace!.Tabs.SelectMany(t=>t.Widgets).Any(w=>w.WidgetId==WorkspaceLayouts.PluginCalendarWidget),"a never-edited layout keeps showing the calendar as the plugin's widget");

        new Store(root).Load();
        Assert(ScheduleData(root).CalendarDays.Count==1 && Directory.GetFiles(store.BackupDirectory).Length==1,"a later start moves nothing");

        // calendar.dat from the previous build moves too and stays beside as calendar.dat.moved; the plugin store wins per date.
        string fileRoot=Path.Combine(root,"from-file");Directory.CreateDirectory(fileRoot);
        var fileStore=new Store(fileRoot);File.WriteAllBytes(fileStore.PathName,Store.EncodeRecords(new AppData()));
        var calendarFile=new CalendarStoreFile{Days=[new(){Date=date,Note="달력 파일"},new(){Date=date.AddDays(1),Note="달력만"}],SchedulePresets=[new(){Color="#112233",Label="운동"}]};
        Store.WriteProtected(fileStore.CalendarPathName,Store.Protect(JsonSerializer.SerializeToUtf8Bytes(calendarFile),true));
        Assert(PluginFileStore.For(fileRoot,LegacySchedule.Plugin).Write(ScheduleRecords.Serialize(new AppData{CalendarDays=[new(){Date=date,Note="플러그인"}]})),"an existing plugin store is prepared");
        fileStore.Load();
        var moved=ScheduleData(fileRoot);
        Assert(CalendarNotes.Get(moved,date).Note=="플러그인" && CalendarNotes.Get(moved,date.AddDays(1)).Note=="달력만" && moved.SchedulePresets!.Single().Label=="운동","calendar.dat merges into the plugin store, which wins per date");
        Assert(!File.Exists(fileStore.CalendarPathName) && File.Exists(fileStore.CalendarPathName+".moved"),"calendar.dat is kept as calendar.dat.moved");
        new Store(fileRoot).Load();
        Assert(ScheduleData(fileRoot).CalendarDays.Count==2,"the move happens once");

        // A store that cannot be read stops the move and keeps everything as it was.
        string brokenRoot=Path.Combine(root,"broken");Directory.CreateDirectory(Path.Combine(brokenRoot,"plugin-data"));
        File.WriteAllBytes(Path.Combine(brokenRoot,"records.dat"),original);
        File.WriteAllBytes(Path.Combine(brokenRoot,"plugin-data",LegacySchedule.Plugin+".dat"),[1,2,3]);
        bool refused=false;try{new Store(brokenRoot).Load();}catch(InvalidDataException){refused=true;}
        Assert(refused && File.ReadAllBytes(Path.Combine(brokenRoot,"records.dat")).SequenceEqual(original),"an unreadable plugin store stops the move and keeps records.dat");

        // The plugin registers the calendar, the agenda and the schedule tools; the app's catalog no longer has them.
        var registry=new TestRegistry(PluginFileStore.For(Path.Combine(root,"registry"),LegacySchedule.Plugin));
        new SchedulePlugin().Register(registry);
        Assert(registry.Widgets.Select(w=>w.Id).SequenceEqual(["calendar","agenda"]) && registry.Tools.Select(t=>t.Name).Order().SequenceEqual(["facet_add_schedule","facet_delete_schedule","facet_list_schedules","facet_today"]),"the schedule plugin offers both widgets and the schedule tools");
        var added=registry.Tools.Single(t=>t.Name=="facet_add_schedule").Run("{\"date\":\"2026-11-02\",\"start\":\"09:10\",\"title\":\"치과\"}").Result;
        var listed=JsonDocument.Parse(registry.Tools.Single(t=>t.Name=="facet_list_schedules").Run("{\"date\":\"2026-11-02\"}").Result).RootElement;
        Assert(JsonDocument.Parse(added).RootElement.GetProperty("start").GetString()=="09:00" && listed.GetArrayLength()==1 && listed[0].GetProperty("title").GetString()=="치과","an agent adds and lists schedules through the plugin");
        Assert(CalendarNotes.SchedulesForDate(ScheduleData(Path.Combine(root,"registry")),new DateOnly(2026,11,2)).Single().Name=="치과","agent edits persist in the plugin store");
        bool rejected=false;try{registry.Tools.Single(t=>t.Name=="facet_add_schedule").Run("{\"date\":\"2026/11/02\",\"start\":\"09:00\",\"title\":\"x\"}").GetAwaiter().GetResult();}catch(ArgumentException){rejected=true;}
        Assert(rejected,"a bad date is a bad request");
        Assert(JsonDocument.Parse(registry.Tools.Single(t=>t.Name=="facet_today").Run("{}").Result).RootElement.TryGetProperty("schedules",out _),"facet_today answers from the plugin");

        // The widget hosts both screens.
        using var runtime=new Controller(Path.Combine(root,"widget"),Controller.ExitHotkey);
        var hud=new OverlayTabs();hud.Connect(runtime);var board=ScheduleBoardFor(hud,runtime.Store.Root);board.AnimateDayTransitions=false;
        var tabs=(TabControl)hud.FindName("Tabs");tabs.SelectedItem=hud.FindName("DashboardTab");
        void Layout(){hud.Measure(new Size(1920,1080));hud.Arrange(new Rect(0,0,1920,1080));hud.UpdateLayout();}
        Layout();
        var calendar=(FrameworkElement)board.FindName("CalendarHost");var page=(FrameworkElement)board.FindName("DayPage");
        Assert(calendar.ActualWidth>0 && calendar.Visibility==Visibility.Visible && page.Visibility==Visibility.Collapsed,"the calendar starts alone");
        board.OpenDay(date);Layout();
        Assert(page.Visibility==Visibility.Visible && page.ActualWidth>0 && calendar.Visibility==Visibility.Collapsed && board.IsEditingDay,"opening a day switches inside the widget");
        Assert(board.ReturnToCalendar(),"the day screen returns to the calendar");Layout();
        Assert(calendar.Visibility==Visibility.Visible && page.Visibility==Visibility.Collapsed,"the calendar returns inside the widget");
        Assert(((Grid)hud.FindName("WidgetLayers")).Children.Contains((Grid)board.FindName("ScheduleModal")),"the schedule editor lives on the overlay's widget layer");
        board.Dispose();
        Assert(!((Grid)hud.FindName("WidgetLayers")).Children.Contains((Grid)board.FindName("ScheduleModal")),"disposing the widget removes its layer");

        var saved=new WorkspaceLayout{GridVersion=2,Tabs=[new(){Id="schedule",Title="일정",Widgets=[new(){Id="a",WidgetId="plugin.a",X=-7,Y=-4,Width=2,Height=2}]}]};
        var upgraded=WorkspaceLayouts.Normalize(saved);var placed=upgraded.Tabs.Single(t=>t.Id=="schedule").Widgets;
        Assert(placed.Count==2 && placed.Count(w=>w.WidgetId==WorkspaceLayouts.PluginCalendarWidget)==1,"a saved layout from before the calendar widget gets the plugin's calendar once");
        var schedule=upgraded.Tabs.Single(t=>t.Id=="schedule");schedule.Widgets.RemoveAll(w=>w.WidgetId==WorkspaceLayouts.PluginCalendarWidget);
        Assert(WorkspaceLayouts.Normalize(upgraded).Tabs.Single(t=>t.Id=="schedule").Widgets.Count==1,"a removed widget stays removed");
    }

    private sealed class TestRegistry(IPluginStore store) : IWidgetRegistry
    {
        internal readonly List<WidgetDescriptor> Widgets = [];
        internal readonly List<PluginAgentTool> Tools = [];
        public string Directory => ""; public string DataDirectory => "";
        public IPluginSettings Settings => throw new NotSupportedException();
        public IPluginStore Store => store;
        public IPluginShell Shell => throw new NotSupportedException();
        public void AddWidget(WidgetDescriptor widget) => Widgets.Add(widget);
        public void AddSettings(PluginSettingsSection section) { }
        public void AddAgentTool(PluginAgentTool tool) => Tools.Add(tool);
        public void AddLauncherMode(LauncherMode mode) { }
        public void AddHotkey(PluginHotkey hotkey) { }
        public void AddTrayCommand(string label, Action run) { }
    }
}
