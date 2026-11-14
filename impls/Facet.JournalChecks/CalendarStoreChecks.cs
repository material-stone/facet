using System.IO;
using System.Windows;
using System.Windows.Controls;
using Facet;
using Facet.Core;

internal static partial class Program
{
    private static void CheckCalendarStore(Controller controller)
    {
        var date=new DateOnly(2026,10,24);
        string root=Path.Combine(controller.Store.Root,"calendar-store");Directory.CreateDirectory(root);
        var legacy=new AppData{CalendarDays=[new(){Date=date,Note="이전 기록",Todos=[new(){Title="이전 할 일"}],Schedules=[new(){Name="이전 일정",StartMinute=600}]}]};
        var original=Store.EncodeRecords(legacy);
        string records=Path.Combine(root,"records.dat");File.WriteAllBytes(records,original);

        var store=new Store(root);var loaded=store.Load();
        var day=CalendarNotes.Get(loaded,date);
        Assert(day.Note=="이전 기록" && day.Todos.Single().Title=="이전 할 일" && day.Schedules.Single().Name=="이전 일정","old days are copied into the calendar store");
        Assert(File.Exists(store.CalendarPathName),"the calendar store is created");
        var backup=Directory.GetFiles(store.BackupDirectory).Single();
        Assert(File.ReadAllBytes(backup).SequenceEqual(original) && Store.Decode(original).CalendarDays.Count==1,"the backup is the untouched old records file");
        Assert(Store.Decode(File.ReadAllBytes(records)).CalendarDays.Count==0,"days leave records.dat after the verified copy");

        var again=new Store(root).Load();
        Assert(again.CalendarDays.Count==1 && Directory.GetFiles(store.BackupDirectory).Length==1,"a later start migrates nothing");

        // An edit lands in the store; a save that changes no day leaves its backups alone.
        CalendarNotes.Save(loaded,new(){Date=date.AddDays(1),Note="새 기록"},DateTime.Now);store.Save(loaded);
        Assert(CalendarNotes.Get(new Store(root).Load(),date.AddDays(1)).Note=="새 기록" && Store.Decode(File.ReadAllBytes(records)).CalendarDays.Count==0,"edits save to the calendar store only");
        var stamp=File.GetLastWriteTimeUtc(store.CalendarPathName);
        loaded.Settings.Privacy=!loaded.Settings.Privacy;store.Save(loaded);
        Assert(File.GetLastWriteTimeUtc(store.CalendarPathName)==stamp,"other saves do not rewrite the calendar store");

        // Days an older build wrote later only fill in dates the store lacks.
        var older=new AppData{CalendarDays=[new(){Date=date,Note="겹친 날짜"},new(){Date=date.AddDays(2),Note="늦은 기록"}]};
        File.WriteAllBytes(records,Store.EncodeRecords(older));
        var merged=new Store(root).Load();
        Assert(CalendarNotes.Get(merged,date).Note=="이전 기록" && CalendarNotes.Get(merged,date.AddDays(2)).Note=="늦은 기록" && Directory.GetFiles(store.BackupDirectory).Length==2,"later old days merge with a new backup and the store wins");

        // A damaged store recovers from its backup and nothing overwrites an unreadable one.
        File.WriteAllBytes(store.CalendarPathName,[1,2,3]);
        var recovering=new Store(root);var recovered=recovering.Load();
        Assert(recovered.CalendarDays.Count>0 && recovering.RecoveryMessage!=null && Directory.GetFiles(root,"calendar.dat.unreadable-*").Length==1,"a damaged calendar store recovers from its backup");

        // Presets move the same way; the store's list wins and an emptied list stays empty.
        string presetRoot=Path.Combine(root,"presets");Directory.CreateDirectory(presetRoot);
        var presetStore=new Store(presetRoot);
        File.WriteAllBytes(presetStore.PathName,Store.EncodeRecords(new AppData{SchedulePresets=[new(){Color="#112233",Label="운동"}]}));
        var withPresets=presetStore.Load();
        Assert(withPresets.SchedulePresets!.Single().Label=="운동" && File.Exists(presetStore.CalendarPathName) && Directory.GetFiles(presetStore.BackupDirectory).Length==1,"presets alone are backed up and copied into the store");
        Assert(Store.Decode(File.ReadAllBytes(presetStore.PathName)).SchedulePresets==null,"presets leave records.dat");
        CalendarNotes.SaveSchedulePreset(withPresets,new(){Color="#112233",Label="운동"},true);presetStore.Save(withPresets);
        Assert(new Store(presetRoot).Load().SchedulePresets is {Count:0},"preset edits persist in the store");
        File.WriteAllBytes(presetStore.PathName,Store.EncodeRecords(new AppData{SchedulePresets=[new(){Color="#445566",Label="구버전"}]}));
        Assert(new Store(presetRoot).Load().SchedulePresets is {Count:0},"the store's presets win over later old ones");

        // The widget hosts both screens and moves with the board.
        using var runtime=new Controller(Path.Combine(root,"widget"),Controller.ExitHotkey);
        var hud=new OverlayTabs{AnimateDayTransitions=false};hud.Connect(runtime);
        var tabs=(TabControl)hud.FindName("Tabs");tabs.SelectedItem=hud.FindName("ManagementTab");
        void Layout(){hud.Measure(new Size(1920,1080));hud.Arrange(new Rect(0,0,1920,1080));hud.UpdateLayout();}
        Layout();
        var view=(FrameworkElement)hud.FindName("CalendarWidgetView");var calendar=(FrameworkElement)hud.FindName("CalendarHost");var page=(FrameworkElement)hud.FindName("DayPage");
        Assert(view.Parent is WidgetFit && view.ActualWidth==WorkspaceLayouts.CalendarColumns*OverlayWorkspaceView.Pitch-OverlayWorkspaceView.Gap && calendar.ActualWidth>0 && calendar.Visibility==Visibility.Visible && page.Visibility==Visibility.Collapsed,"the schedule tab shows the widget's calendar");
        hud.OpenDay(date);Layout();
        Assert(page.Visibility==Visibility.Visible && page.ActualWidth>0 && calendar.Visibility==Visibility.Collapsed && hud.IsEditingDay && view.Parent is WidgetFit,"opening a day switches inside the widget");
        Assert(hud.ReturnToCalendar(),"the day screen returns to the calendar");Layout();
        Assert(calendar.Visibility==Visibility.Visible && page.Visibility==Visibility.Collapsed,"the calendar returns inside the widget");

        var saved=new WorkspaceLayout{GridVersion=2,Tabs=[new(){Id="schedule",Title="일정",Widgets=[new(){Id="a",WidgetId="plugin.a",X=-7,Y=-4,Width=2,Height=2}]}]};
        var upgraded=WorkspaceLayouts.Normalize(saved);var placed=upgraded.Tabs.Single(t=>t.Id=="schedule").Widgets;
        Assert(placed.Count==2 && placed.Count(w=>w.WidgetId==WorkspaceLayouts.CalendarWidget)==1,"a saved layout gets the widget once");
        var schedule=upgraded.Tabs.Single(t=>t.Id=="schedule");schedule.Widgets.RemoveAll(w=>w.WidgetId==WorkspaceLayouts.CalendarWidget);
        Assert(WorkspaceLayouts.Normalize(upgraded).Tabs.Single(t=>t.Id=="schedule").Widgets.Count==1,"a removed widget stays removed");
    }
}
