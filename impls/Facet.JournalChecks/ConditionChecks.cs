using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Facet;
using Facet.ConditionPlugin;
using Facet.Core;
using PluginEntry=Facet.ConditionPlugin.ConditionEntry;

internal static partial class Program
{
    private static void CheckConditions(string qa)
    {
        CheckConditionTracking();
        CheckConditionMove(qa);
        CheckConditionWidgets(qa);
    }

    private static void CheckConditionTracking()
    {
        var start=DateTimeOffset.Parse("2026-10-18T00:00:00+09:00");var end=start.AddDays(7);
        var same=new List<PluginEntry>();ConditionTracking.Record(same,"good",start);ConditionTracking.Record(same,"tired",start.ToUniversalTime());
        Assert(same.Single().Kind=="tired","identical instants keep the latest selection");
        bool rejected=false;try{ConditionTracking.Record(same,"invalid",start);}catch(ArgumentException){rejected=true;}
        Assert(rejected && same.Single().Kind=="tired","an unknown kind changes nothing");

        var entries=new PluginEntry[]{new(){At=start.AddDays(1),Kind="okay"},new(){At=start.AddDays(2).AddHours(3),Kind="tired"},new(){At=end.AddHours(1),Kind="unwell"}};
        var now=start.AddDays(6).AddHours(12);var spans=ConditionTracking.Project(entries,start,end,now);
        Assert(spans.Count==2 && spans[0].Start==start.AddDays(1) && spans[0].End==entries[1].At && spans[1].End==now && spans[1].Kind=="tired","projection leaves the unknown start and the future transparent");
        Assert(ConditionTracking.Project([],start,end,now).Count==0 && ConditionTracking.Project(entries,start,end,start.AddSeconds(-1)).Count==0 && ConditionTracking.Current(entries,now)!.Kind=="tired","empty history and early windows project nothing");
        var carried=new PluginEntry[]{new(){At=start.AddHours(-2),Kind="good"},new(){At=start.AddDays(1),Kind="unwell"}};
        spans=ConditionTracking.Project(carried.Reverse(),start,end,end.AddHours(1));
        Assert(spans.Count==2 && spans[0].Start==start && spans[0].End==start.AddDays(1) && spans[1].End==end,"a condition carries into the window and clips at its end");
        Assert(ConditionTracking.Project([new PluginEntry{At=start,Kind="good"},new PluginEntry{At=start.AddHours(1),Kind="future-kind"}],start,end,start.AddHours(4)).Single().End==start.AddHours(1),"an unknown stored kind ends the previous span");

        var list=new List<PluginEntry>();ConditionTracking.Record(list,"good",start.AddDays(-1));ConditionTracking.Record(list,"tired",start.AddDays(1));ConditionTracking.Record(list,"okay",start.AddDays(2));
        ConditionTracking.Update(list,list[1] with{},"unwell",start.AddDays(3),start.AddDays(6));
        spans=ConditionTracking.Project(list,start,end,start.AddDays(6));
        Assert(spans.Count==3 && spans[0].RecordedAt==start.AddDays(-1) && spans[1].Kind=="okay" && spans[2].Kind=="unwell" && spans[2].Start==start.AddDays(3),"an edit reorders the spans and keeps the source time");
        var source=list[0] with{};var before=JsonSerializer.Serialize(list);
        foreach(var edit in new Action[]{()=>ConditionTracking.Update(list,source,"tired",now.AddDays(1),now),()=>ConditionTracking.Update(list,source,"tired",list[1].At,now),
            ()=>ConditionTracking.Update(list,source,"invalid",source.At,now),()=>ConditionTracking.Update(list,source with{Kind="sleepy"},"tired",now,now)}) {
            rejected=false;try{edit();}catch(ArgumentException){rejected=true;}
            Assert(rejected && JsonSerializer.Serialize(list)==before,"a future, colliding, invalid or stale edit changes nothing");
        }
        var removed=list[1] with{};ConditionTracking.Delete(list,removed);
        rejected=false;try{ConditionTracking.Delete(list,removed);}catch(ArgumentException){rejected=true;}
        Assert(rejected && list.Count==2,"deleting a record twice is refused");
    }

    // Old records move once, stay safe on failure and end up only in the plugin's store.
    private static void CheckConditionMove(string qa)
    {
        string root=Path.Combine(qa,"condition-move");Directory.CreateDirectory(root);
        var at=DateTimeOffset.Parse("2026-10-24T12:34:56+09:00");
        var old=new AppData{Conditions=[new(){At=at,Kind="good"},new(){At=at.AddHours(1),Kind="tired"}],Workspace=WorkspaceLayouts.Default()};
        old.Workspace.Tabs[0].Widgets.Add(new(){Id="builtin.condition",WidgetId="builtin.condition",X=20,Y=20,Width=2,Height=1});
        old.Workspace.Tabs[0].Widgets.Add(new(){Id="builtin.condition-history",WidgetId="builtin.condition-history",X=20,Y=22,Width=9,Height=2});
        var store=new Store(root);File.WriteAllBytes(store.PathName,Store.EncodeRecords(old));
        var pluginStore=PluginFileStore.For(root,LegacyConditions.Plugin);

        // A store that cannot be written leaves the records where they are.
        Directory.CreateDirectory(Path.GetDirectoryName(pluginStore.PathName)!);
        using(new FileStream(pluginStore.PathName+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None)) {
            bool failed=false;try{new Store(root).Load();}catch(InvalidDataException){failed=true;}
            Assert(failed && Store.Decode(File.ReadAllBytes(store.PathName)).Conditions.Count==2,"a failed move keeps the records in records.dat");
        }

        var loaded=new Store(root).Load();
        Assert(loaded.Conditions.Count==0 && Store.Decode(File.ReadAllBytes(store.PathName)).Conditions.Count==0,"moved records leave records.dat");
        Assert(Directory.GetFiles(store.BackupDirectory,"records-before-condition-store-*").Any(file=>Store.Decode(File.ReadAllBytes(file)).Conditions.Count==2),"records.dat is backed up with the records before the move");
        var records=new ConditionRecords(pluginStore);
        Assert(records.Entries.Count==2 && records.Entries[0].At==at && records.Entries[0].At.Offset==at.Offset && records.Entries[1].Kind=="tired","every record reaches the store with its offset");
        Assert(!File.ReadAllText(pluginStore.PathName,System.Text.Encoding.Latin1).Contains("tired"),"the store file is encrypted");
        var widgets=loaded.Workspace!.Tabs[0].Widgets;
        Assert(widgets.Any(w=>w.Id=="builtin.condition" && w.WidgetId=="plugin.facet.condition.current" && w.X==20) && widgets.Any(w=>w.WidgetId=="plugin.facet.condition.history" && w.Y==22) && widgets.All(w=>!w.WidgetId.StartsWith("builtin.condition")),"placed condition widgets become the plugin's widgets where they were");
        Assert(new Store(root).Load().Conditions.Count==0 && new ConditionRecords(pluginStore).Entries.Count==2,"a second load moves nothing");

        // Records an older build wrote later merge in; the store wins at the same instant.
        File.WriteAllBytes(store.PathName,Store.EncodeRecords(new AppData{Conditions=[new(){At=at,Kind="unwell"},new(){At=at.AddHours(2),Kind="okay"}]}));
        new Store(root).Load();
        var merged=new ConditionRecords(pluginStore).Entries;
        Assert(merged.Count==3 && merged[0].Kind=="good" && merged[2].Kind=="okay","later records merge and the store wins at the same instant");

        File.WriteAllBytes(pluginStore.PathName,[1,2,3]);
        foreach(string backup in Directory.GetFiles(Path.GetDirectoryName(pluginStore.PathName)!,"*.bak*"))File.Delete(backup);
        bool unreadable=false;try{_=new ConditionRecords(pluginStore);}catch(InvalidDataException){unreadable=true;}
        Assert(unreadable && File.ReadAllBytes(pluginStore.PathName).Length==3,"an unreadable store stops the plugin and is not overwritten");
    }

    private static void CheckConditionWidgets(string qa)
    {
        string root=Path.Combine(qa,"condition-widgets");Directory.CreateDirectory(root);
        var pluginStore=PluginFileStore.For(root,LegacyConditions.Plugin);var records=new ConditionRecords(pluginStore);
        var selector=new ConditionSelector{Record=(kind,at)=>records.Commit(entries=>ConditionTracking.Record(entries,kind,at))};
        selector.Live=new(selector,records,selector.Refresh);
        var timeline=new ConditionHistory{Commit=records.Commit};timeline.Live=new(timeline,records,timeline.Refresh);
        void Fit(FrameworkElement view,double width,double height){view.Measure(new Size(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();}

        // The selector fills whatever slot it gets: the button to the edge, the icon growing with it.
        Fit(selector,240,180);
        Assert(double.IsNaN(selector.Width) && selector.Toggle.ActualWidth==240 && selector.Toggle.ActualHeight==180,$"the button fills its slot ({selector.Toggle.ActualWidth}x{selector.Toggle.ActualHeight})");
        var face=(Grid)((Grid)selector.Toggle.Content).Children[0];double small=face.ActualHeight;
        Fit(selector,480,360);
        Assert(selector.Toggle.ActualWidth==480 && selector.Toggle.ActualHeight==360 && face.ActualHeight>small*1.5,"the icon grows with a larger slot");
        Fit(selector,60,40);
        Assert(selector.Toggle.ActualWidth==60 && selector.Toggle.ActualHeight==40,"a small slot still fits exactly");

        Assert(!selector.Focusable && !selector.Toggle.Focusable && !selector.Toggle.IsTabStop && selector.Options.All(option=>!option.Focusable && !option.IsTabStop),"the icon and choices stay out of Tab navigation");
        Assert(selector.Options.Count==6 && selector.Choices.Placement==PlacementMode.Bottom && !selector.Choices.StaysOpen,"six choices open below the button and dismiss outside");
        foreach(var style in ConditionIcons.All) {
            var bitmap=(BitmapSource)ConditionIcons.Load(style.Id);
            Assert(bitmap.IsFrozen && bitmap.PixelWidth==228,"the icon loads from the plugin's own resources: "+style.Name);
        }

        // One save reaches both widgets and the disk; a failed one changes nothing.
        selector.Options[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var current=records.Entries.Single();
        Assert(current.Kind=="tired" && new ConditionRecords(pluginStore).Entries.Single().Kind=="tired" && Math.Abs((DateTimeOffset.Now-current.At).TotalSeconds)<5,"choosing saves to the store at the selection time");
        Assert(timeline.Spans.Count==0 || timeline.Spans.Last().Kind=="tired","the other widget follows the shared records");
        using(new FileStream(pluginStore.PathName+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            Assert(!selector.Select("good",current.At.AddSeconds(1)) && records.Entries.Single().Kind=="tired","a failed save keeps the current state");
        Assert(new ConditionRecords(pluginStore).Entries.Single().Kind=="tired","a failed save keeps the file");

        var now=new DateTimeOffset(DateTime.SpecifyKind(new DateTime(2026,10,24,12,0,0),DateTimeKind.Local));var start=now.Date.AddDays(-6);
        Assert(records.Commit(list=>{list.Clear();list.AddRange([new(){At=new DateTimeOffset(start.AddHours(12)),Kind="good"},new(){At=new DateTimeOffset(start.AddDays(2)),Kind="tired"},new(){At=new DateTimeOffset(start.AddDays(4)),Kind="okay"},new(){At=now.AddHours(-1),Kind="sleepy"}]);}),"fixture saved");
        timeline.Refresh(records.Entries,now);Fit(timeline,800,200);
        Assert(timeline.Start.Date==start && timeline.End.Date==now.Date.AddDays(1) && timeline.Spans.Last().End==now,"the gauge spans six days ago through today and stops at now");
        Assert(!timeline.OpenAt(20,false) && !timeline.OpenAt(790,false) && timeline.OpenAt(300,false),"only recorded segments open the editor");

        var editor=timeline.Editor;var ordered=records.Entries.OrderBy(entry=>entry.At).ToArray();
        Assert(editor.DraftTimestamp==ordered[1].At,"the editor shows the actual recording time");
        var beforeEdit=JsonSerializer.Serialize(records.Entries);
        using(new FileStream(pluginStore.PathName+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            editor.Options[4].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(editor.HasPendingChanges && JsonSerializer.Serialize(records.Entries)==beforeEdit && editor.Retry.Visibility==Visibility.Visible,"a failed edit keeps the draft and the saved history");
        }
        editor.Retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(!editor.HasPendingChanges && new ConditionRecords(pluginStore).Entries.Any(item=>item.Kind=="unwell"),"retry saves the edit");
        var chosen=DateOnly.FromDateTime(editor.DraftTimestamp.LocalDateTime).AddDays(-1);editor.TimePicker.SetTimestamp(editor.DraftTimestamp);
        Fit(editor.TimePicker,320,420);
        VisualElements(editor.TimePicker.Calendar).OfType<Button>().Single(button=>System.Windows.Automation.AutomationProperties.GetName(button)==chosen.ToString("yyyy년 M월 d일")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(records.Entries.Any(item=>item.Kind=="unwell" && DateOnly.FromDateTime(item.At.LocalDateTime)==chosen),"choosing a date saves at once");
        Assert(!editor.TimePicker.Calendar.IsDateEnabled(DateOnly.FromDateTime(DateTime.Now).AddDays(1)),"future dates are disabled");
        Assert(editor.DeleteRecord() && records.Entries.All(item=>item.Kind!="unwell"),"delete removes exactly the chosen record");
        timeline.Refresh([],now);Assert(timeline.Spans.Count==0,"no record leaves the gauge empty");

        // Disposed widgets stop following the records.
        selector.Dispose();timeline.Dispose();int shown=timeline.Spans.Count;
        Assert(records.Commit(list=>list.Clear()) && timeline.Spans.Count==shown,"a disposed widget no longer refreshes");
    }
}
