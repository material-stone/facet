using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Facet;
using Facet.Core;
using Facet.Plugins;
using Facet.SleepPlugin;
using PluginEntry=Facet.SleepPlugin.SleepEntry;
using Tracking=Facet.SleepPlugin.SleepTracking;

internal static partial class Program
{
    private static void CheckSleep(string qa)
    {
        CheckSleepTracking();
        CheckSleepMove(qa);
        CheckSleepPlugin(qa);
        CheckSleepWidgets(qa);
    }

    private static bool Rejected(Action change) { try { change(); return false; } catch(ArgumentException) { return true; } }

    private static void CheckSleepTracking()
    {
        var today=new DateOnly(2026,10,24);var entries=new List<PluginEntry>{new(){Date=today,Attempt="23:00",FinalWake="07:00"},new(){Date=today.AddDays(-2),FinalWake="12:00"}};
        var week=Tracking.Week(entries,today);
        Assert(week.Count==7 && week[0].Date==today.AddDays(-6) && week[^1].Span==new SleepTimeSpan(-60,420) && week[^1].Entry!.EstimatedSleepMinutes==null && week[4].Span==null && week[3].Entry==null,"the week keeps wake dates, midnight spans, gaps and partial values");
        Assert(Tracking.TimeSpan(new(){Attempt="02:00",FinalWake="12:00"})==new SleepTimeSpan(120,720) && Tracking.TimeSpan(new(){Attempt="12:00",FinalWake="12:00"})==new SleepTimeSpan(-720,720),"equal clocks are a full day");
        Assert(Tracking.Week(entries,today.AddDays(1))[0].Date==today.AddDays(-5),"the window follows the date");

        var now=new DateTime(2026,10,24,12,0,0);
        var old=new PluginEntry{Date=today,Attempt="02:00",FinalWake="12:00",Bed="01:50",OutOfBed="12:20",Awakenings=3,Sleepiness=2,NapMinutes=15,LastCaffeine="18:00",Treatment="사용",Note="이전"};
        var book=new SleepBook{Sleep=[old]};var original=Tracking.Get(book.Sleep,today)!;var draft=original.Copy();
        draft.Latency=30;draft.AwakeMinutes=0;draft.Note="변경";Tracking.Save(book,draft,original,now);draft.Note="저장 후 초안";
        var saved=book.Sleep.Single();
        Assert(saved.EstimatedSleepMinutes==570 && saved.AwakeMinutes==0 && saved.Note=="변경" && saved.Bed=="01:50" && saved.OutOfBed=="12:20" && saved.Awakenings==3 && saved.Sleepiness==2 && saved.NapMinutes==15 && saved.LastCaffeine=="18:00" && saved.Treatment=="사용","a save detaches the draft, keeps unedited fields and tells zero from unknown");
        Assert(Tracking.ParseTime("2:00","시각")=="02:00" && Tracking.ParseMinutes(" ","분")==null && Tracking.ParseMinutes("0","분")==0,"inputs parse with blanks as unknown");
        Tracking.Save(book,new(){Date=today.AddDays(-2)},null,now);Assert(book.Sleep.Count==1,"an empty new record creates nothing");

        var current=Tracking.Get(book.Sleep,today)!;string before=JsonSerializer.Serialize(book);
        foreach(var bad in new[]{new PluginEntry{Date=today,Attempt="25:00"},new PluginEntry{Date=today.AddDays(1)},new PluginEntry{Date=today,Latency=-1},new PluginEntry{Date=today,AwakeMinutes=1441},new PluginEntry{Date=today,Attempt="02:00",FinalWake="03:00",Latency=50,AwakeMinutes=50}})
            Assert(Rejected(()=>Tracking.Save(book,bad,current,now)) && JsonSerializer.Serialize(book)==before,"an invalid record changes nothing");
        book.Sleep[0].Note="다른 변경";before=JsonSerializer.Serialize(book);
        Assert(Rejected(()=>Tracking.Save(book,current,current,now)) && Rejected(()=>Tracking.Delete(book,current)) && JsonSerializer.Serialize(book)==before,"a concurrent change is refused");
        current=Tracking.Get(book.Sleep,today)!;Tracking.Delete(book,current);
        Assert(book.Sleep.Count==0 && book.Removed.Single().Note=="다른 변경" && Rejected(()=>Tracking.Delete(book,current)),"delete archives the original once");

        Assert(Tracking.FormatDuration(45)=="45분" && Tracking.FormatDuration(120)=="2시간" && Tracking.FormatDuration(125)=="2시간 5분","durations omit zero units");
        var mixed=new List<PluginEntry>{
            new(){Date=today.AddDays(-6),Attempt="23:00",FinalWake="07:00"},new(){Date=today.AddDays(-5),Attempt="02:00",FinalWake="12:00",Latency=30},
            new(){Date=today.AddDays(-4),Attempt="02:00",FinalWake="12:00",AwakeMinutes=45},new(){Date=today.AddDays(-3),Attempt="02:00",FinalWake="12:00",Latency=30,AwakeMinutes=45},
            new(){Date=today.AddDays(-2),FinalWake="12:00"},new(){Date=today.AddDays(-1),Attempt="12:00",FinalWake="12:00"},new(){Date=today,Attempt="02:00",FinalWake="10:00",Latency=480,AwakeMinutes=0}};
        var days=Tracking.Week(mixed,today);
        Assert(days.Select(Tracking.Minutes).SequenceEqual(new double?[]{480,600,600,525,null,1440,0}) && Tracking.Average(days)=="평균 수면 시간 10시간 8분","the shown value falls back to the span, the mean includes real zero and skips unknown");
    }

    private static void CheckSleepMove(string qa)
    {
        string root=Path.Combine(qa,"sleep-move");Directory.CreateDirectory(root);
        var date=new DateOnly(2026,10,24);
        var old=new AppData{Sleep=[new(){Date=date,Attempt="02:00",FinalWake="10:00",Bed="01:50",Note="이전 기록"},new(){Date=date.AddDays(-1),FinalWake="09:00",AwakeMinutes=null}],RemovedSleep=[new(){Date=date.AddDays(-9),Note="지운 기록"}],Workspace=WorkspaceLayouts.Default()};
        old.Workspace.GridVersion=3;old.Workspace.Tabs[0].Widgets.Add(new(){Id="builtin.sleep",WidgetId="builtin.sleep",X=20,Y=20,Width=9,Height=3});
        old.Settings.SleepGraphTransparencyPercent=40;
        var store=new Store(root);File.WriteAllBytes(store.PathName,Store.EncodeRecords(old));
        var pluginStore=PluginFileStore.For(root,LegacySleep.Plugin);

        Directory.CreateDirectory(Path.GetDirectoryName(pluginStore.PathName)!);
        using(new FileStream(pluginStore.PathName+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None)) {
            bool failed=false;try{new Store(root).Load();}catch(InvalidDataException){failed=true;}
            Assert(failed && Store.Decode(File.ReadAllBytes(store.PathName)).Sleep.Count==2,"a failed move keeps the sleep records in records.dat");
        }

        var loaded=new Store(root).Load();
        var onDisk=Store.Decode(File.ReadAllBytes(store.PathName));
        Assert(loaded.Sleep.Count==0 && loaded.RemovedSleep.Count==0 && onDisk.Sleep.Count==0 && onDisk.RemovedSleep.Count==0,"moved records leave records.dat");
        Assert(Directory.GetFiles(store.BackupDirectory,"records-before-sleep-store-*").Any(file=>Store.Decode(File.ReadAllBytes(file)).Sleep.Count==2),"records.dat is backed up before the move");
        var records=new SleepRecords(pluginStore);
        Assert(records.Entries.Count==2 && records.Entries[1].Note=="이전 기록" && records.Entries[1].Bed=="01:50" && records.Entries[0].AwakeMinutes==null && records.Removed.Single().Note=="지운 기록","every record, the archive, old fields and nulls reach the store");
        Assert(!File.ReadAllText(pluginStore.PathName,System.Text.Encoding.Latin1).Contains("FinalWake"),"the sleep store is encrypted");
        var tabs=loaded.Workspace!.Tabs;
        Assert(tabs[0].Widgets.Any(w=>w.Id=="builtin.sleep" && w.WidgetId==LegacySleep.DurationWidget && w.X==20) && tabs.Single(t=>t.Id==WorkspaceLayouts.SleepTab).Widgets.Single().WidgetId==LegacySleep.PatternWidget,"the graph keeps its place and the sleep tab gets the pattern widget");
        Assert(loaded.Settings.SleepGraphTransparencyPercent==null && PluginData.Setting(loaded,LegacySleep.Plugin,LegacySleep.TransparencyKey)=="40","the background setting moves to the plugin");
        var again=new Store(root).Load();
        Assert(new SleepRecords(pluginStore).Entries.Count==2 && again.Workspace!.Tabs.Single(t=>t.Id==WorkspaceLayouts.SleepTab).Widgets.Count==1,"a second load moves nothing");

        File.WriteAllBytes(store.PathName,Store.EncodeRecords(new AppData{Sleep=[new(){Date=date,Note="늦은 쓰기"},new(){Date=date.AddDays(-3),Note="새 날짜"}]}));
        new Store(root).Load();
        var merged=new SleepRecords(pluginStore).Entries;
        Assert(merged.Count==3 && merged.Single(e=>e.Date==date).Note=="이전 기록" && merged.Any(e=>e.Note=="새 날짜"),"later records merge and the store wins per wake date");

        string fresh=Path.Combine(qa,"sleep-default");Directory.CreateDirectory(fresh);
        File.WriteAllBytes(new Store(fresh).PathName,Store.EncodeRecords(new AppData{Sleep=[new(){Date=date,Note="기본 배치"}]}));
        var layout=new Store(fresh).Load().Workspace;
        Assert(layout!=null && layout.Tabs[0].Widgets.Any(w=>w.WidgetId==LegacySleep.DurationWidget),"a never-edited layout with records keeps the graph");
        Assert(WorkspaceLayouts.Default().Tabs.SelectMany(t=>t.Widgets).All(w=>!w.WidgetId.Contains("sleep")),"a new layout holds no plugin widgets");

        File.WriteAllBytes(pluginStore.PathName,[1,2,3]);
        foreach(string backup in Directory.GetFiles(Path.GetDirectoryName(pluginStore.PathName)!,"facet.sleep.dat.bak*"))File.Delete(backup);
        bool unreadable=false;try{_=new SleepRecords(pluginStore);}catch(InvalidDataException){unreadable=true;}
        Assert(unreadable && File.ReadAllBytes(pluginStore.PathName).Length==3,"an unreadable store stops the plugin and is not overwritten");
    }

    private sealed class SleepRegistry(IPluginStore store) : IWidgetRegistry
    {
        internal readonly List<WidgetDescriptor> Widgets=[];internal readonly List<PluginAgentTool> Tools=[];internal PluginSettingsSection? Section;
        public string Directory=>"";public string DataDirectory=>"";public IPluginSettings Settings=>new WidgetContext("","").Settings;public IPluginStore Store=>store;public IPluginShell Shell=>throw new NotSupportedException();public void AddLauncherMode(LauncherMode mode){}public void AddHotkey(PluginHotkey hotkey){}public void AddTrayCommand(string label,Action run){}
        public void AddWidget(WidgetDescriptor widget)=>Widgets.Add(widget);public void AddAgentTool(PluginAgentTool tool)=>Tools.Add(tool);public void AddSettings(PluginSettingsSection section)=>Section=section;
    }

    private static void CheckSleepPlugin(string qa)
    {
        string root=Path.Combine(qa,"sleep-plugin");Directory.CreateDirectory(root);
        var registry=new SleepRegistry(PluginFileStore.For(root,LegacySleep.Plugin));
        new Facet.SleepPlugin.SleepPlugin().Register(registry);
        Assert(registry.Widgets.Select(w=>w.Id).SequenceEqual(["pattern","duration"]) && registry.Widgets.All(w=>WidgetIds.Of(LegacySleep.Plugin,w.Id) is LegacySleep.PatternWidget or LegacySleep.DurationWidget),"one plugin holds both widgets under the ids the move writes");
        Assert(registry.Section?.Fields.Single().Key==LegacySleep.TransparencyKey,"the plugin's setting uses the key the move writes");
        string Run(string name,string args)=>registry.Tools.Single(t=>t.Name==name).Run(args).Result;
        Run("facet_record_sleep","""{"attempt":"01:30","final_wake":"09:00"}""");
        var merged=JsonDocument.Parse(Run("facet_record_sleep","""{"latency_minutes":20,"awake_minutes":10}""")).RootElement;
        Assert(merged.GetProperty("attempt").GetString()=="01:30" && merged.GetProperty("estimated_sleep_minutes").GetDouble()==420,"an agent's partial record merges into the existing one");
        Assert(JsonDocument.Parse(Run("facet_list_sleep","{}")).RootElement.GetArrayLength()==1,"an agent reads the record back");
        Assert(Rejected(()=>Run("facet_record_sleep","""{"date":"2026/10/24"}""")),"a bad date is a bad request");
    }

    private static void CheckSleepWidgets(string qa)
    {
        string root=Path.Combine(qa,"sleep-widgets");Directory.CreateDirectory(root);
        var pluginStore=PluginFileStore.For(root,LegacySleep.Plugin);var records=new SleepRecords(pluginStore);
        var layer=new Grid();var frame=new Border();var host=new WidgetModalHost(layer,frame);var modal=host.For("sleep");
        double transparency=0;var today=DateOnly.FromDateTime(DateTime.Now);var yesterday=today.AddDays(-1);
        var chartEditing=new SleepEditing(records,modal);var patternEditing=new SleepEditing(records,modal);
        var chart=new SleepDurationChart{Editing=chartEditing};chart.Live=new(chart,records,chart.Refresh,null);
        var pattern=new SleepPatternView{Editing=patternEditing,Transparency=()=>transparency};pattern.Live=new(pattern,records,pattern.Refresh,null);
        void Fit(FrameworkElement view,double width){view.Measure(new Size(width,double.PositiveInfinity));view.Arrange(new Rect(0,0,width,view.DesiredSize.Height));view.UpdateLayout();}
        Fit(chart,800);Fit(pattern,800);
        Assert(chart.AverageText=="평균 수면 시간 —" && pattern.Days.Count==7,"empty widgets show unknown");

        // The modal lives in the plugin: open, cancel, save, Escape, failure and retry.
        chart.DayButtons[^1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor=chartEditing.Current!;
        Assert(host.IsOpen && editor.Date==today && editor.DeleteButton.Visibility==Visibility.Collapsed && ReferenceEquals(frame.Child,editor),"a chart day opens the plugin's editor over the overlay");
        Assert(!patternEditing.Open(today) && patternEditing.Current==null,"only one modal opens at a time");
        host.HandleEscape();Assert(!host.IsOpen && records.Entries.Count==0,"dismissing an empty draft records nothing");
        Assert(!patternEditing.Open(today.AddDays(1)) && !host.IsOpen,"a future date cannot be opened");

        pattern.TodayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));editor=patternEditing.Current!;
        editor.Attempt.Value="02:20";editor.Wake.Value="11:40";
        Assert(editor.TrySave() && !host.IsOpen && !editor.IsOpen,"saving closes the modal");
        var saved=new SleepRecords(pluginStore).Entries.Single();
        Assert(saved.Date==today && saved.Attempt=="02:20" && saved.Latency==null && saved.AwakeMinutes==null,"quick input stores partial data without invented zeroes");
        Assert(Tracking.Minutes(chart.Days[^1])==560 && chart.AverageText=="평균 수면 시간 9시간 20분" && pattern.AverageText==chart.AverageText,"both widgets follow one save");
        Assert(AutomationProperties.GetName(pattern.DateRows[^1]).Contains("수면 시간 9시간 20분"),"the pattern row shows the duration");

        Assert(records.Commit(book=>book.Sleep.Add(new(){Date=yesterday,Attempt="23:00",FinalWake="07:00",Latency=10,AwakeMinutes=30,Bed="22:50",NapMinutes=15,Treatment="기존",Note="원본"})),"fixture saves");
        Fit(pattern,800);
        pattern.DateRows.Single(b=>Equals(b.Tag,yesterday)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));editor=patternEditing.Current!;
        Assert(editor.Attempt.Value=="23:00" && editor.Details.IsExpanded && editor.DeleteButton.Visibility==Visibility.Visible,"a date row opens its saved record");
        editor.Note.Text="수정";editor.Awake.Text="0";string before=JsonSerializer.Serialize(records.Entries);
        using(new FileStream(pluginStore.PathName+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            host.HandleEscape();
            Assert(host.IsOpen && editor.IsOpen && editor.Note.Text=="수정" && editor.Error.Length>0 && JsonSerializer.Serialize(records.Entries)==before,"a failed save keeps the modal, draft and records");
            Assert(!host.TryClose() && host.IsOpen,"the app's close path (hiding, exit) stops on a failed save");
        }
        Assert(host.TryClose() && !host.IsOpen,"retry saves and closes");
        saved=new SleepRecords(pluginStore).Entries.Single(e=>e.Date==yesterday);
        Assert(saved.Note=="수정" && saved.AwakeMinutes==0 && saved.EstimatedSleepMinutes==470 && saved.Bed=="22:50" && saved.NapMinutes==15 && saved.Treatment=="기존","editing keeps unedited old values");

        var row=pattern.DateRows[0];pattern.Refresh(records.Entries,today);Assert(ReferenceEquals(row,pattern.DateRows[0]),"a routine refresh keeps row identity and focus");
        row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));editor=patternEditing.Current!;
        editor.Wake.Minute.SelectedIndex=16;
        Assert(!editor.TrySave() && editor.IsOpen && editor.Error.Contains("시와 분"),"an incomplete time keeps the draft with an error");
        editor.Wake.Value=null;editor.Latency.Text="invalid";host.HandleEscape();
        Assert(host.IsOpen,"Escape cannot discard invalid input");
        editor.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(!host.IsOpen && records.Entries.Count==2,"cancel discards only the draft");

        // Deletion and its failure.
        pattern.DateRows.Single(b=>Equals(b.Tag,yesterday)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));editor=patternEditing.Current!;
        using(new FileStream(pluginStore.PathName+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            editor.DeleteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(editor.IsOpen && records.Entries.Count==2 && records.Removed.Count==0,"a failed delete changes nothing");
        }
        editor.DeleteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var reread=new SleepRecords(pluginStore);
        Assert(!host.IsOpen && reread.Entries.Single().Date==today && reread.Removed.Single().Note=="수정","retry archives the original and closes");
        Assert(chart.Days.Single(d=>d.Date==yesterday).Entry==null && pattern.Days.Single(d=>d.Date==yesterday).Entry==null && chart.AverageText=="평균 수면 시간 9시간 20분","deletion refreshes both widgets");
        chart.DayButtons.Single(b=>Equals(b.Tag,yesterday)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(host.IsOpen && chartEditing.Current!.DeleteButton.Visibility==Visibility.Collapsed,"a deleted date reopens blank");
        host.CloseAll();

        // Transparency touches only the pattern's background; the graph breaks across unknown days.
        foreach(double value in new[]{0d,50d,100d}) {
            transparency=value;pattern.Refresh(records.Entries,today);
            Assert(Math.Abs(pattern.ChartBackground.Background.Opacity-(1-value/100))<.001 && pattern.Opacity==1,"transparency changes only the background at "+value);
        }
        Assert(chart.Background==null,"the graph has no background of its own");
        pattern.Refresh(records.Entries,today.AddDays(1));Assert(pattern.Days[0].Date==today.AddDays(-5),"the date rollover advances the pattern");
        var gaps=new List<PluginEntry>();foreach(int day in new[]{-6,-5,-2,-1,0})gaps.Add(new(){Date=today.AddDays(day),Attempt="02:00",FinalWake="10:00",Latency=0,AwakeMinutes=0});
        gaps.Add(new(){Date=today.AddDays(-3),FinalWake="10:00"});gaps.Single(e=>e.Date==yesterday).Latency=480;
        var line=new SleepDurationChart{Width=800};line.Refresh(gaps,today);Fit(line,800);
        Assert(line.Connections.Count==3 && line.PointLocations[5].Y<line.PointLocations[2].Y && line.PointLocations[2].Y==line.PointLocations[3].Y,"the line breaks across gaps and separates real zero from unknown");
        DateOnly? chosen=null;line.EditRequested+=date=>chosen=date;
        line.DayButtons[5].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(chosen==yesterday,"a zero-hour point keeps its date");
        Assert(line.PlotCanvas.Children.OfType<Button>().Count()==7,"one button per day");

        // A tile smaller than the view shrinks it instead of clipping.
        var fit=new SleepFit{Child=new SleepDurationChart()};fit.Measure(new Size(400,160));fit.Arrange(new Rect(0,0,400,160));
        var shrunk=((FrameworkElement)fit.Child).RenderTransform as ScaleTransform;
        Assert(shrunk!=null && shrunk.ScaleX<1 && ((FrameworkElement)fit.Child).DesiredSize.Height*shrunk.ScaleY<=160.5,"the widget fits a small tile");

        chart.Dispose();pattern.Dispose();var shown=chart.Days;
        Assert(records.Commit(book=>book.Sleep.Clear()) && ReferenceEquals(shown,chart.Days),"a disposed widget stops following the records");
    }
}
