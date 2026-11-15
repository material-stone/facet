using Facet.Core;
using System.Text.Json;

int passed = 0;
void Check(string name, Action action) { action(); Console.WriteLine("PASS " + name); passed++; }
void Assert(bool ok) { if (!ok) throw new Exception("Assertion failed"); }
DateTime T(string s) => DateTime.Parse(s);
Check("old built-in music records move once to the music plugin",()=>{
    var old=JsonSerializer.Deserialize<AppData>("{\"Music\":{\"Tracks\":[{\"VideoId\":\"dQw4w9WgXcQ\",\"Title\":\"곡\"}],\"Repeat\":true,\"VolumePercent\":35},\"Settings\":{\"MusicIndicatorVisible\":false},\"Workspace\":{\"GridVersion\":3,\"Tabs\":[{\"Id\":\"dashboard\",\"Title\":\"대시보드\",\"Widgets\":[{\"Id\":\"builtin.music\",\"WidgetId\":\"builtin.music\",\"X\":3,\"Y\":-4,\"Width\":3,\"Height\":8}]}]}}")!;
    PluginData.Normalize(old);
    Assert(PluginData.Setting(old,"facet.music","playlist")!.Contains("곡") && PluginData.Setting(old,"facet.music","indicator")=="off" && old.Workspace!.Tabs[0].Widgets[0].WidgetId=="plugin.facet.music.player");
    string saved=JsonSerializer.Serialize(old);Assert(!saved.Contains("\"Music\"") && !saved.Contains("MusicIndicatorVisible"));
    var fresh=JsonSerializer.Deserialize<AppData>("{}")!;PluginData.Normalize(fresh);Assert(fresh.PluginSettings==null && WorkspaceLayouts.Default().Tabs.SelectMany(t=>t.Widgets).All(w=>!w.WidgetId.Contains("music")));
});
Check("records under the old first-party plugin ids move to facet ids once",()=>{
    string o=PluginIds.RenamedPrefix;
    var data=JsonSerializer.Deserialize<AppData>("{\"EnabledPlugins\":[{\"Id\":\""+o+"clock\",\"Fingerprint\":\"a\"},{\"Id\":\"my.own\",\"Fingerprint\":\"b\"}],\"PluginSettings\":{\""+o+"weather\":{\"region\":\"kr-1\",\"old\":\"x\"},\"facet.weather\":{\"region\":\"kr-2\"}},\"Workspace\":{\"GridVersion\":5,\"Tabs\":[{\"Id\":\"t\",\"Title\":\"T\",\"Widgets\":[{\"Id\":\"w\",\"WidgetId\":\"plugin."+o+"clock.clock\",\"Width\":2,\"Height\":2}]}]},\"WidgetStores\":{\"w\":{\"PluginId\":\""+o+"clock\",\"WidgetId\":\"plugin."+o+"clock.clock\",\"Values\":{}}}}")!;
    PluginData.Normalize(data);
    Assert(data.EnabledPlugins!.Select(g=>g.Id).SequenceEqual(["facet.clock","my.own"]));
    Assert(!data.PluginSettings!.ContainsKey(o+"weather") && PluginData.Setting(data,"facet.weather","region")=="kr-2" && PluginData.Setting(data,"facet.weather","old")=="x");
    Assert(data.Workspace!.Tabs[0].Widgets[0].WidgetId=="plugin.facet.clock.clock" && data.WidgetStores!["w"].PluginId=="facet.clock" && data.WidgetStores["w"].WidgetId=="plugin.facet.clock.clock");
    Assert(PluginIds.Current("my."+o+"x")=="my."+o+"x" && PluginIds.CurrentWidget("plugin.my.x")=="plugin.my.x");
});
Check("launcher search engine retains legacy Google and persists each selection",()=>{
    Assert(JsonSerializer.Deserialize<Settings>("{}")!.LauncherSearchEngine==WebSearchEngine.Google);
    foreach(var engine in Enum.GetValues<WebSearchEngine>()) {
        var settings=new Settings { LauncherSearchEngine=engine };
        Assert(JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!.LauncherSearchEngine==engine);
    }
});
Check("legacy overlay settings retain previous blur and transition defaults",()=>{
    var old=JsonSerializer.Deserialize<AppData>("{\"SchemaVersion\":2,\"Settings\":{\"Opacity\":0.8}}")!;
    Assert(old.SchemaVersion==2 && old.Settings.Opacity==.8 && old.Settings.OverlayBlurRadius==18 && old.Settings.OverlayShowMilliseconds==280 && old.Settings.OverlayHideMilliseconds==280);
});
Check("overlay appearance settings preserve zero and independent times across serialization",()=>{
    var data=new AppData();data.Settings.OverlayBlurRadius=0;data.Settings.OverlayShowMilliseconds=0;data.Settings.OverlayHideMilliseconds=2000;
    var copy=JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(data))!;
    Assert(copy.Settings.OverlayBlurRadius==0 && copy.Settings.OverlayShowMilliseconds==0 && copy.Settings.OverlayHideMilliseconds==2000);
});
Check("calendar notes retain legacy defaults and detach editable copies",()=>{
    var d=JsonSerializer.Deserialize<AppData>("{\"SchemaVersion\":2}")!;Assert(d.CalendarDays.Count==0);
    var entry=new CalendarDayEntry { Date=new(2026,10,24), Note="첫 노트",Todos=[new(){Title="할 일"}] };
    CalendarNotes.Save(d,entry,T("2026-10-24"));entry.Todos[0].Title="변경";
    var copy=CalendarNotes.Get(d,entry.Date);copy.Note="초안";copy.Todos[0].Done=true;
    Assert(d.CalendarDays[0].Todos[0].Title=="할 일" && !d.CalendarDays[0].Todos[0].Done && d.CalendarDays[0].Note=="첫 노트");
    var loaded=JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(d))!;Assert(loaded.CalendarDays[0].Note=="첫 노트");
});
Check("calendar same date upsert preserves other dates and future plans",()=>{
    var d=new AppData();var a=new CalendarDayEntry{Date=new(2026,10,24),Note="A"};var b=new CalendarDayEntry{Date=new(2030,1,1),Note="미래"};
    CalendarNotes.Save(d,a,T("2026-10-24"));CalendarNotes.Save(d,b,T("2026-10-24"));a.Note="수정";CalendarNotes.Save(d,a,T("2026-10-25"));
    Assert(d.CalendarDays.Count==2 && CalendarNotes.Get(d,a.Date).Note=="수정" && CalendarNotes.Get(d,b.Date).Note=="미래");
});
Check("calendar invalid entries preserve stored data",()=>{
    var d=new AppData();var date=new DateOnly(2026,10,24);CalendarNotes.Save(d,new(){Date=date,Note="보존"},T("2026-10-24"));
    string before=JsonSerializer.Serialize(d);var id=Guid.NewGuid();
    foreach(var entry in new[]{new CalendarDayEntry{Date=date,Note=new string('x',20001)},new CalendarDayEntry{Date=date,Todos=[new(){Title=" "}]},new CalendarDayEntry{Date=date,Todos=[new(){Id=id,Title="A"},new(){Id=id,Title="B"}]}}) {
        bool rejected=false;try{CalendarNotes.Save(d,entry,T("2026-10-24"));}catch(ArgumentException){rejected=true;}Assert(rejected && JsonSerializer.Serialize(d)==before);
    }
});
Check("clearing calendar content removes only that date",()=>{
    var d=new AppData();CalendarNotes.Save(d,new(){Date=new(2026,10,24),Note="A"},T("2026-10-24"));CalendarNotes.Save(d,new(){Date=new(2026,10,25),Note="B"},T("2026-10-24"));
    CalendarNotes.Save(d,new(){Date=new(2026,10,24)},T("2026-10-24"));Assert(d.CalendarDays.Count==1 && d.CalendarDays[0].Note=="B");
});
Check("day schedules preserve legacy days and detach persisted copies",()=>{
    var old=JsonSerializer.Deserialize<CalendarDayEntry>("{\"Note\":\"기존 기록\"}")!;Assert(old.Schedules.Count==0 && old.Note=="기존 기록");
    var data=new AppData();var day=new CalendarDayEntry {Date=new(2026,10,24),Schedules=[new(){Name="집중",StartMinute=540,DurationMinutes=120,Label="작업",Color="#91BFFF",Content="세부 내용"}]};
    CalendarNotes.Save(data,day,T("2026-10-24"));Assert(data.CalendarDays.Count==1);
    day.Schedules[0].Name="원본 수정";var copy=CalendarNotes.Get(data,day.Date);copy.Schedules[0].Content="초안";
    var loaded=JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(data))!;
    Assert(loaded.CalendarDays[0].Schedules[0].Name=="집중" && loaded.CalendarDays[0].Schedules[0].Content=="세부 내용");
    CalendarNotes.Save(data,new(){Date=day.Date},T("2026-10-24"));Assert(data.CalendarDays.Count==0);
});
Check("day schedules validate half hours, overnight and full day without mutating stored data",()=>{
    var night=new DaySchedule {Name="자정 통과",StartMinute=1410,DurationMinutes=90};DaySchedule.Validate(night);
    Assert(night.ContainsSlot(47) && !night.ContainsSlot(0));var incoming=night.Copy();incoming.StartDayOffset=-1;Assert(incoming.ContainsSlot(0) && incoming.ContainsSlot(1) && !incoming.ContainsSlot(2));
    var full=new DaySchedule {Name="하루",StartMinute=540,DurationMinutes=1440};DaySchedule.Validate(full);Assert(!full.ContainsSlot(0) && full.ContainsSlot(18) && full.ContainsSlot(47));
    var data=new AppData();var date=new DateOnly(2026,10,24);CalendarNotes.Save(data,new(){Date=date,Note="보존"},T("2026-10-24"));string before=JsonSerializer.Serialize(data);
    var valid=new DaySchedule {Name="정상",StartMinute=540,DurationMinutes=30};
    var invalid=new List<DaySchedule>();
    foreach(var duration in new[]{0,15,1441}){var item=valid.Copy();item.DurationMinutes=duration;invalid.Add(item);}
    foreach(var start in new[]{-30,15,1440}){var item=valid.Copy();item.StartMinute=start;invalid.Add(item);}
    var badColor=valid.Copy();badColor.Color="#GGGGGG";invalid.Add(badColor);
    var badLabel=valid.Copy();badLabel.Label=" ";invalid.Add(badLabel);
    foreach(var item in invalid){bool rejected=false;try{CalendarNotes.Save(data,new(){Date=date,Schedules=[item]},T("2026-10-24"));}catch(ArgumentException){rejected=true;}Assert(rejected && JsonSerializer.Serialize(data)==before);}
    bool duplicate=false;try{CalendarNotes.Save(data,new(){Date=date,Schedules=[valid,valid.Copy()]},T("2026-10-24"));}catch(ArgumentException){duplicate=true;}Assert(duplicate && JsonSerializer.Serialize(data)==before);
});
Check("content-only schedules round-trip with empty names while blank or malformed records are rejected atomically",()=>{
    var data=new AppData();var date=new DateOnly(2026,10,24);
    var item=new DaySchedule {Name="",Content="이름 없이 남긴 내용",StartMinute=600,DurationMinutes=60};
    CalendarNotes.SaveSchedule(data,new(){Date=date},item,false,T("2026-10-24"));
    var loaded=JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(data))!;
    var saved=CalendarNotes.SchedulesForDate(loaded,date).Single();
    Assert(saved.Name=="" && saved.Content==item.Content && saved.Id==item.Id);
    saved.Content+=" 수정";CalendarNotes.SaveSchedule(loaded,CalendarNotes.Get(loaded,date),saved,false,T("2026-10-25"));
    Assert(CalendarNotes.Get(loaded,date).Schedules.Single().Content==saved.Content);
    string before=JsonSerializer.Serialize(loaded);
    foreach(var invalid in new[]{new DaySchedule(),new(){Name=" \t",Content="\n "},new(){Name=null!,Content="내용"},new(){Name="",Content="내용",Color="#bad"}}) {
        bool rejected=false;try{CalendarNotes.SaveSchedule(loaded,CalendarNotes.Get(loaded,date),invalid,false,T("2026-10-25"));}catch(ArgumentException){rejected=true;}
        Assert(rejected && JsonSerializer.Serialize(loaded)==before);
    }
    CalendarNotes.SaveSchedule(loaded,CalendarNotes.Get(loaded,date),saved,true,T("2026-10-25"));
    Assert(!CalendarNotes.HasContentOnDate(loaded,date));
});
Check("custom reminders validate before mutation and deduplicate agent retries",()=>{
    var data=new AppData();var at=new DateTimeOffset(2026,10,24,18,0,0,TimeSpan.FromHours(9));
    var request=new ReminderCommand{Action="add",Key="agent-42",Title="테스트 일정",At=at,Kind="schedule"};
    var first=UserReminders.Apply(data,request);var repeat=UserReminders.Apply(data,request);
    Assert(first.Changed && first.Item!.Id==repeat.Item!.Id && !repeat.Changed && data.Reminders.Count==1);
    string before=JsonSerializer.Serialize(data);request.Title="서로 다른 내용";Assert(!UserReminders.Apply(data,request).Ok && JsonSerializer.Serialize(data)==before);
    request.Key="invalid";request.Title=" ";bool rejected=false;try{UserReminders.Apply(data,request);}catch(ArgumentException){rejected=true;}
    Assert(rejected && JsonSerializer.Serialize(data)==before);
});
Check("custom reminders persist delivery completion cancellation and disabled notifications",()=>{
    var data=new AppData();var now=new DateTimeOffset(2026,10,24,18,0,0,TimeSpan.FromHours(9));
    UserReminder Add(string key)=>UserReminders.Apply(data,new(){Action="add",Key=key,Title=key,At=now}).Item!;
    var a=Add("first");Assert(UserReminders.Due(data,now.AddSeconds(-1))==null && UserReminders.Due(data,now)!.Id==a.Id);
    Assert(UserReminders.MarkDelivered(data,a.Id,now) && !UserReminders.MarkDelivered(data,a.Id,now));
    data=JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(data))!;Assert(UserReminders.Due(data,now)==null);
    var b=Add("second");UserReminders.Apply(data,new(){Action="complete",Id=b.Id});Assert(UserReminders.Due(data,now)==null);
    var c=Add("third");UserReminders.Apply(data,new(){Action="cancel",Id=c.Id});Assert(UserReminders.Due(data,now)==null);
    Add("disabled");data.Settings.Notifications=false;Assert(UserReminders.Due(data,now)==null);
});
Check("dashboard cards select today's pending reminders and date tasks without inventing times",()=>{
    var data=new AppData();var now=new DateTimeOffset(2026,10,24,10,0,0,TimeSpan.FromHours(9));
    UserReminders.Apply(data,new(){Action="add",Key="today",Title="오늘 일정",At=now.AddHours(2),Kind="schedule"});
    UserReminders.Apply(data,new(){Action="add",Key="tomorrow",Title="내일 일정",At=now.AddDays(1)});
    data.CalendarDays.Add(new(){Date=new(2026,10,24),Todos=[new(){Title="오늘 할 일"},new(){Title="완료한 할 일",Done=true}]});
    var cards=DashboardAgenda.Today(data,now);
    Assert(cards.Any(x=>x.Title=="오늘 일정" && x.Label=="일정") && cards.Any(x=>x.Title=="오늘 할 일" && x.At==null) && cards.All(x=>x.Title!="내일 일정" && x.Title!="완료한 할 일"));
    Assert(JsonSerializer.Deserialize<AppData>("{\"SchemaVersion\":2}")!.Reminders.Count==0);
});
Check("startup overlay preference preserves legacy default and explicit disabled value",()=>{
    Assert(JsonSerializer.Deserialize<Settings>("{}")!.ShowOverlayOnLaunch);
    var settings=new Settings{ShowOverlayOnLaunch=false,OverlayVisible=true};
    var loaded=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
    Assert(!loaded.ShowOverlayOnLaunch && loaded.OverlayVisible);
});
Check("weather location selection retains Windows default and a saved region across legacy serialization",()=>{
    Assert(JsonSerializer.Deserialize<Settings>("{}")!.WeatherRegionId=="");
    var settings=new Settings {WeatherRegionId="kr-1835847"};
    Assert(JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!.WeatherRegionId=="kr-1835847");
});
Check("cross-date ranges split only at date boundaries without losing previous-day signs",()=>{
    Assert(DaySchedule.SplitRange(-60,120).SequenceEqual(new[]{(-1,1380,60),(0,0,60)}));
    Assert(DaySchedule.SplitRange(1380,120).SequenceEqual(new[]{(0,1380,60),(1,0,60)}));
    Assert(DaySchedule.SplitRange(0,1440).SequenceEqual(new[]{(0,0,1440)}));
    Assert(DaySchedule.SplitRange(-720,1440).Sum(part=>part.DurationMinutes)==1440);
});
Check("previous-day schedules persist once and project only the selected date with legacy JSON",()=>{
    var date=new DateOnly(2028,3,1);var data=new AppData();var current=new CalendarDayEntry {Date=date,Note="오늘 기록",Todos=[new(){Title="오늘 할 일"}]};
    CalendarNotes.Save(data,new(){Date=date.AddDays(-1),Note="어제 기록"},T("2028-03-01"));
    var item=new DaySchedule {Name="어제23시 오늘1시",StartMinute=1380,StartDayOffset=-1,DurationMinutes=120};
    CalendarNotes.SaveSchedule(data,current,item,false,T("2028-03-01"));
    Assert(data.CalendarDays.SelectMany(day=>day.Schedules).Count()==1 && CalendarNotes.Get(data,date.AddDays(-1)).Schedules.Single().StartMinute==1380);
    var today=CalendarNotes.SchedulesForDate(data,date).Single();var yesterday=CalendarNotes.SchedulesForDate(data,date.AddDays(-1)).Single();
    Assert(today.VisibleStartMinute==0 && today.VisibleDurationMinutes==60 && today.OwnerDate==date.AddDays(-1));
    Assert(yesterday.VisibleStartMinute==1380 && yesterday.VisibleDurationMinutes==60 && today.DurationMinutes==120);
    Assert(CalendarNotes.Get(data,date).Note=="오늘 기록" && CalendarNotes.Get(data,date.AddDays(-1)).Note=="어제 기록");
    string json=JsonSerializer.Serialize(data);Assert(!json.Contains("StartDayOffset") && !json.Contains("OwnerDate") && !json.Contains("VisibleDuration"));
    var loaded=JsonSerializer.Deserialize<AppData>(json)!;Assert(CalendarNotes.SchedulesForDate(loaded,date).Single().VisibleDurationMinutes==60);
    Assert(CalendarNotes.RecordedDates(data).Contains(date));
});
Check("schedule color/label presets derive from used schedules then persist explicit adds and removals",()=>{
    var data=new AppData();var date=new DateOnly(2027,5,1);
    CalendarNotes.SaveSchedule(data,new(){Date=date},new(){Name="a",Color="#91bfff",Label="업무"},false,T("2027-05-01"));
    CalendarNotes.SaveSchedule(data,CalendarNotes.Get(data,date),new(){Name="b",StartMinute=60,Color="#91BFFF",Label="업무"},false,T("2027-05-01"));
    Assert(data.SchedulePresets==null && CalendarNotes.SchedulePresets(data).Single().Key==new SchedulePreset{Color="#91BFFF",Label="업무"}.Key);
    CalendarNotes.SaveSchedulePreset(data,new(){Color="#ff9d92",Label=" 운동 "},false);
    Assert(CalendarNotes.SchedulePresets(data).Select(p=>p.Label).SequenceEqual(new[]{"운동","업무"}) && data.SchedulePresets![0].Color=="#FF9D92");
    CalendarNotes.SaveSchedulePreset(data,new(){Color="#91BFFF",Label="업무"},true);
    Assert(CalendarNotes.SchedulePresets(data).Single().Label=="운동" && data.CalendarDays.Single().Schedules.Count==2);
    bool rejected=false;try{CalendarNotes.SaveSchedulePreset(data,new(){Color="#GGGGGG",Label="x"},false);}catch(ArgumentException){rejected=true;}
    Assert(rejected && CalendarNotes.SchedulePresets(data).Count==1);
    var loaded=JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(data))!;Assert(CalendarNotes.SchedulePresets(loaded).Single().Label=="운동");
});
Check("workspace layout rejects overlaps and keeps built-in tab rules",()=>{
    void That(bool ok,string _="")=>Assert(ok);
    bool Rejects(Action action){try{action();return false;}catch(ArgumentException){return true;}}
    That(new AppData().Workspace==null && WorkspaceLayouts.Current(new AppData()).Tabs.Select(t=>t.Id).SequenceEqual(new[]{"dashboard","settings"}) && WorkspaceLayouts.Default().Tabs.All(t=>t.Widgets.Count==0),"a fresh install has an empty dashboard and settings (ADR 0101)");
    // The rules below run on the layout a never-edited install had before its widgets became plugins.
    var data=new AppData{Workspace=WorkspaceLayouts.LegacyDefault()};
    That(WorkspaceLayouts.Current(data).Tabs.Select(t=>t.Id).SequenceEqual(new[]{"dashboard","schedule","sleep","chat","settings"}));
    var defaults=WorkspaceLayouts.LegacyDefault().Tabs[0];
    That(defaults.Widgets.All(w=>WorkspaceLayouts.CanPlace(defaults,w.Id,w.X,w.Y,w.Width,w.Height)),"default layout is valid");
    var tab=WorkspaceLayouts.AddTab(data,"  업무  ");
    That(tab.Title=="업무" && data.Workspace!.Tabs[^1].Id=="settings" && data.Workspace!.Tabs[^2].Id==tab.Id,"new tabs stay before settings");
    var first=WorkspaceLayouts.AddWidget(data,tab.Id,"plugin.a",4,2,false);var second=WorkspaceLayouts.AddWidget(data,tab.Id,"plugin.a",4,2,false);
    That(first.X==-6 && first.Y==-4 && second.X==-2 && second.Y==-4,"new widgets take the first free slot around the origin");
    That(Rejects(()=>WorkspaceLayouts.MoveWidget(data,tab.Id,second.Id,-4,-4,4,2)) && second.X==-2,"overlap is rejected without moving");
    WorkspaceLayouts.MoveWidget(data,tab.Id,second.Id,500,-300,6,3);That(second.X==500 && second.Y==-300 && second.Width==6,"the board has no practical edge");
    That(Rejects(()=>WorkspaceLayouts.MoveWidget(data,tab.Id,second.Id,WorkspaceLayouts.MaxCell+1,0,4,2)),"coordinates stay in the supported range");
    WorkspaceLayouts.MoveWidget(data,tab.Id,second.Id,-2,-3,6,3);That(second.Y==-3);
    That(Rejects(()=>WorkspaceLayouts.AddWidget(data,"dashboard",WorkspaceLayouts.PluginClockWidget,3,2,true)),"single widgets cannot be placed twice");
    var rule=WorkspaceLayouts.LegacyDefault();
    That(WorkspaceLayouts.PlacementProblem(rule,"dashboard",WorkspaceLayouts.PluginClockWidget,true,false)!=null && WorkspaceLayouts.PlacementProblem(rule,"chat",WorkspaceLayouts.PluginClockWidget,true,false)==null,"one per tab allows other tabs");
    That(WorkspaceLayouts.PlacementProblem(rule,"chat",WorkspaceLayouts.PluginClockWidget,false,true)!=null && WorkspaceLayouts.PlacementProblem(rule,"dashboard",WorkspaceLayouts.PluginClockWidget,false,true)==null,"one tab only allows more on that tab");
    That(Rejects(()=>WorkspaceLayouts.AddWidget(data,"settings","plugin.a",1,1,false)) && Rejects(()=>WorkspaceLayouts.SetView(data,"settings",0,0,1)),"settings holds no board");
    var onChat=WorkspaceLayouts.AddWidget(data,"chat","plugin.a",1,1,false);That(onChat.X==-6,"fixed pages hold widgets on a board");WorkspaceLayouts.RemoveWidget(data,"chat",onChat.Id);
    var big=WorkspaceLayouts.AddWidget(data,tab.Id,"plugin.a",12,8,false);That(big.Y>=-1,"a large widget goes below the others instead of failing");WorkspaceLayouts.RemoveWidget(data,tab.Id,big.Id);
    That(Rejects(()=>WorkspaceLayouts.AddWidget(data,tab.Id,"plugin.a",12,8,false,-6,-4,5,5)),"a widget larger than the board is rejected");
    var wrapped=WorkspaceLayouts.AddWidget(data,tab.Id,"plugin.a",1,1,false,40,40,8,8);That(wrapped.X==7 && wrapped.Y==7,"a viewed area past the edge stays on the board");WorkspaceLayouts.RemoveWidget(data,tab.Id,wrapped.Id);
    var near=WorkspaceLayouts.AddWidget(data,tab.Id,"plugin.a",1,1,false,100,50);That(near.X==100 && near.Y==50,"new widgets can start at the viewed area");WorkspaceLayouts.RemoveWidget(data,tab.Id,near.Id);
    WorkspaceLayouts.SetView(data,tab.Id,120.5,-40,9);That(data.Workspace.Tabs.Single(t=>t.Id==tab.Id).View is {X:120.5,Y:-40,Zoom:WorkspaceLayouts.MaxZoom},"views keep the vector and clamp the zoom");
    {var s=new Settings();That(WorkspaceGrid.CellSize(s)==96 && WorkspaceGrid.Style(s)==WorkspaceGrid.Dots,"the grid defaults to 96 px dots");s.GridCellSize=5;s.GridStyle="rounded";That(WorkspaceGrid.CellSize(s)==WorkspaceGrid.MinCellSize && WorkspaceGrid.Style(s)==WorkspaceGrid.Dots,"grid settings clamp the size and fall back to dots");}
    That(Rejects(()=>WorkspaceLayouts.SetHidden(data,"settings",true)) && Rejects(()=>WorkspaceLayouts.RemoveTab(data,"settings")) && Rejects(()=>WorkspaceLayouts.RenameTab(data,"settings","x")),"the settings tab keeps its rules");
    WorkspaceLayouts.MoveTab(data,tab.Id,1);That(data.Workspace.Tabs[^1].Id=="settings","moving a tab never passes settings");
    WorkspaceLayouts.MoveTab(data,tab.Id,-1);That(data.Workspace.Tabs[3].Id==tab.Id);
    data.Workspace.Tabs[^1].Hidden=true;var loaded=WorkspaceLayouts.Normalize(JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(data))!.Workspace);
    var reloadedTab=loaded.Tabs.Single(t=>t.Id==tab.Id);
    That(!loaded.Tabs[^1].Hidden && reloadedTab.Widgets.Count==2 && reloadedTab.Widgets[0].X==-6 && reloadedTab.View is {X:120.5},"reload restores settings and keeps placements and views");
    var broken=new WorkspaceLayout{Tabs=[new(){Id="settings",Title="x"},new(){Id="tab-a",Title="a",Widgets=[new(){Id="1",WidgetId="w",X=0,Y=0,Width=2,Height=2},new(){Id="2",WidgetId="w",X=1,Y=1,Width=2,Height=2},new(){Id="3",WidgetId="w",X=11,Y=0,Width=0,Height=1}]}]};
    var fixedUp=WorkspaceLayouts.Normalize(broken);
    That(fixedUp.Tabs.Count==6 && fixedUp.Tabs[^1].Id=="settings" && fixedUp.Tabs.Single(t=>t.Id=="tab-a").Widgets.Single().Id=="1","normalize restores tabs and drops only invalid placements");
    var centered=fixedUp.Tabs.Single(t=>t.Id=="tab-a").Widgets.Single();
    That(centered.X==-6 && centered.Y==-4 && fixedUp.GridVersion==WorkspaceLayouts.GridVersion,"an old grid moves once so it is centered on the origin");
    That(WorkspaceLayouts.Normalize(fixedUp).Tabs.Single(t=>t.Id=="tab-a").Widgets.Single().X==-6,"a current grid does not move again");
    That(WorkspaceLayouts.Normalize(new WorkspaceLayout{GridVersion=2,Tabs=[new(){Id="chat",View=new(){X=double.NaN}}]}).Tabs.Single(t=>t.Id=="chat").View==null,"an invalid view falls back to the default");
    That(WorkspaceLayouts.Normalize(null).Tabs.Count==5);
    var iconed=new AppData{Workspace=WorkspaceLayouts.LegacyDefault()};WorkspaceLayouts.SetIcon(iconed,"chat","Music");
    That(WorkspaceLayouts.Normalize(JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(iconed))!.Workspace).Tabs.Single(t=>t.Id=="chat").Icon=="Music" && Rejects(()=>WorkspaceLayouts.SetIcon(iconed,"settings","Music")),"a tab keeps its icon and settings keeps its own");
    var ordinary=new AppData{Workspace=WorkspaceLayouts.LegacyDefault()};WorkspaceLayouts.RemoveTab(ordinary,"sleep");WorkspaceLayouts.RenameTab(ordinary,"chat","대화");
    var ordinaryLoaded=WorkspaceLayouts.Normalize(JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(ordinary))!.Workspace);
    That(ordinaryLoaded.Tabs.All(t=>t.Id!="sleep") && ordinaryLoaded.Tabs.Single(t=>t.Id=="chat").Title=="대화","a removed first tab stays removed and a new name stays");
    That(WorkspaceLayouts.Normalize(new WorkspaceLayout{GridVersion=5,Tabs=[new(){Id="settings",Title="설정"}]}).Tabs.Count==5,"an older layout gets its first tabs once");
});
Check("editing moving and deleting a carried schedule preserves both dates notes and tasks",()=>{
    var date=new DateOnly(2027,1,1);var data=new AppData();var current=new CalendarDayEntry {Date=date,Note="현재 기록",Todos=[new(){Title="현재 할 일"}]};
    CalendarNotes.Save(data,new(){Date=date.AddDays(-1),Note="이전 기록"},T("2027-01-01"));
    CalendarNotes.SaveSchedule(data,current,new(){Name="자정 일정",StartDayOffset=-1,StartMinute=1380,DurationMinutes=120},false,T("2027-01-01"));
    var item=CalendarNotes.SchedulesForDate(data,date).Single();item.Name="수정";item.StartMinute=1350;item.DurationMinutes=180;
    CalendarNotes.SaveSchedule(data,CalendarNotes.Get(data,date),item,false,T("2027-01-01"));
    Assert(CalendarNotes.SchedulesForDate(data,date).Single().VisibleDurationMinutes==90);
    item=CalendarNotes.SchedulesForDate(data,date).Single();item.StartDayOffset=0;item.StartMinute=30;item.DurationMinutes=90;
    CalendarNotes.SaveSchedule(data,CalendarNotes.Get(data,date),item,false,T("2027-01-01"));
    Assert(CalendarNotes.Get(data,date.AddDays(-1)).Schedules.Count==0 && CalendarNotes.Get(data,date).Schedules.Count==1);
    item=CalendarNotes.SchedulesForDate(data,date).Single();CalendarNotes.SaveSchedule(data,CalendarNotes.Get(data,date),item,true,T("2027-01-01"));
    Assert(data.CalendarDays.SelectMany(day=>day.Schedules).Count()==0 && CalendarNotes.Get(data,date).Todos.Count==1 && CalendarNotes.Get(data,date.AddDays(-1)).Note=="이전 기록");
});
Check("cross-date schedule validation is atomic at limits and invalid date boundaries",()=>{
    var date=new DateOnly(2026,10,24);var data=new AppData();
    CalendarNotes.Save(data,new(){Date=date.AddDays(-1),Schedules=Enumerable.Range(0,200).Select(i=>new DaySchedule {Name=$"보존{i}"}).ToList()},T("2026-10-24"));
    string before=JsonSerializer.Serialize(data);bool rejected=false;
    try{CalendarNotes.SaveSchedule(data,new(){Date=date,Note="저장되면 안 됨"},new(){Name="초과",StartDayOffset=-1,StartMinute=1380,DurationMinutes=120},false,T("2026-10-24"));}catch(ArgumentException){rejected=true;}
    Assert(rejected && JsonSerializer.Serialize(data)==before);
    foreach(var edge in new[]{DateOnly.MinValue,DateOnly.MaxValue}) {
        rejected=false;try{CalendarNotes.SaveSchedule(data,new(){Date=edge},new(){Name="경계",StartDayOffset=edge==DateOnly.MinValue?-1:0,StartMinute=1410,DurationMinutes=90},false,T("2026-10-24"));}catch(ArgumentException){rejected=true;}
        Assert(rejected && JsonSerializer.Serialize(data)==before);
    }
});

Check("date-picker drafts support distant valid dates without overflowing day projection",()=>{
    var current=new DateOnly(2026,10,24);var target=new DateOnly(9998,12,31);var data=new AppData();
    CalendarNotes.SaveSchedule(data,new(){Date=current,Note="기록"},new(){Name="이동",StartMinute=600,DurationMinutes=60},false,T("2026-10-24"));
    var item=CalendarNotes.SchedulesForDate(data,current).Single();item.StartDayOffset=target.DayNumber-current.DayNumber;
    Assert(item.VisibleDurationMinutes==0);CalendarNotes.SaveSchedule(data,CalendarNotes.Get(data,current),item,false,T("2026-10-24"));
    Assert(CalendarNotes.Get(data,current).Schedules.Count==0 && CalendarNotes.Get(data,target).Schedules.Single().Id==item.Id && CalendarNotes.Get(data,current).Note=="기록");
});
Check("multi-day schedules persist once and project every covered day across leap month boundaries",()=>{
    var start=new DateOnly(2028,2,29);var end=new DateOnly(2028,3,3);var data=new AppData();
    long duration=(end.DayNumber-start.DayNumber)*1440L+60-1380;
    CalendarNotes.SaveSchedule(data,new(){Date=start,Note="시작 기록"},new(){Name="다일 일정",StartMinute=1380,DurationMinutes=duration},false,T("2026-10-24"));
    Assert(CalendarNotes.SchedulesForDate(data,start).Single().VisibleDurationMinutes==60);
    Assert(CalendarNotes.SchedulesForDate(data,start.AddDays(1)).Single().VisibleDurationMinutes==1440);
    Assert(CalendarNotes.SchedulesForDate(data,start.AddDays(2)).Single().VisibleDurationMinutes==1440);
    var last=CalendarNotes.SchedulesForDate(data,end).Single();Assert(last.VisibleStartMinute==0 && last.VisibleDurationMinutes==60 && last.OwnerDate==start);
    Assert(CalendarNotes.SchedulesForDate(data,end.AddDays(1)).Count==0 && CalendarNotes.RecordedDates(data).SequenceEqual(Enumerable.Range(start.DayNumber,4).Select(DateOnly.FromDayNumber)));
    last.Name="중간 날짜에서 수정";CalendarNotes.SaveSchedule(data,new(){Date=end,Note="끝 기록"},last,false,T("2026-10-24"));
    var loaded=JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(data))!;
    Assert(loaded.CalendarDays.Sum(day=>day.Schedules.Count)==1 && CalendarNotes.SchedulesForDate(loaded,start.AddDays(2)).Single().Name==last.Name);
    CalendarNotes.SaveSchedule(loaded,CalendarNotes.Get(loaded,end),last,true,T("2026-10-24"));
    Assert(loaded.CalendarDays.All(day=>day.Schedules.Count==0) && CalendarNotes.Get(loaded,start).Note=="시작 기록" && CalendarNotes.Get(loaded,end).Note=="끝 기록");
});
Check("multi-day schedules support the full date range without minute overflow and reject out of range ends atomically",()=>{
    var data=new AppData();long duration=((long)DateOnly.MaxValue.DayNumber+1)*1440;
    CalendarNotes.SaveSchedule(data,new(){Date=DateOnly.MinValue},new(){Name="긴 기간",DurationMinutes=duration},false,T("2026-10-24"));
    var loaded=JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(data))!;
    var item=CalendarNotes.SchedulesForDate(loaded,new(6000,1,1)).Single();Assert(item.DurationMinutes>int.MaxValue && item.VisibleDurationMinutes==1440);
    Assert(CalendarNotes.HasContentOnDate(loaded,DateOnly.MaxValue) && CalendarNotes.SchedulesForDate(loaded,DateOnly.MaxValue).Single().VisibleDurationMinutes==1440);
    string before=JsonSerializer.Serialize(loaded);item.DurationMinutes+=30;bool rejected=false;
    try{CalendarNotes.SaveSchedule(loaded,new(){Date=new(6000,1,1)},item,false,T("2026-10-24"));}catch(ArgumentException){rejected=true;}
    Assert(rejected && JsonSerializer.Serialize(loaded)==before);
    var midnight=new AppData();CalendarNotes.Save(midnight,new(){Date=new(2028,1,1),Schedules=[new(){Name="자정 종료",DurationMinutes=2880}]},T("2026-10-24"));
    Assert(CalendarNotes.HasContentOnDate(midnight,new(2028,1,2)) && !CalendarNotes.HasContentOnDate(midnight,new(2028,1,3)));
});
Check("legacy conditions merge into the plugin store format and the store wins at the same instant", () => {
    var at=DateTimeOffset.Parse("2026-10-24T12:00:00+09:00");
    var first=LegacyConditions.Merge(null,[new ConditionEntry{At=at,Kind="good"},new ConditionEntry{At=at.AddHours(1),Kind="future-kind"}]);
    var second=LegacyConditions.Merge(first,[new ConditionEntry{At=at.ToUniversalTime(),Kind="tired"},new ConditionEntry{At=at.AddHours(-1),Kind="okay"}]);
    var list=System.Text.Json.Nodes.JsonNode.Parse(second)!["Conditions"]!.AsArray();
    Assert(list.Count==3 && (string)list[0]!["Kind"]! =="okay" && (string)list[1]!["Kind"]! =="good" && (string)list[2]!["Kind"]! =="future-kind");
    Assert(DateTimeOffset.Parse((string)list[1]!["At"]!).Offset==at.Offset);
});
Check("agent tools add and delete schedules, without partial writes", () => {
    var data=new AppData(); var now=T("2026-10-31 14:10");
    var args=System.Text.Json.Nodes.JsonNode.Parse("{\"date\":\"2026-11-02\",\"start\":\"09:10\",\"duration_minutes\":50,\"title\":\"치과\"}")!.AsObject();
    var item=AgentToolCatalog.AddSchedule(data,args,now);
    var listed=AgentToolCatalog.Schedules(data,new DateOnly(2026,11,2));
    Assert(item.StartMinute==540 && item.DurationMinutes==60 && listed.Count==1 && listed[0]!["title"]!.ToString()=="치과");
    AgentToolCatalog.DeleteSchedule(data,System.Text.Json.Nodes.JsonNode.Parse("{\"date\":\"2026-11-02\",\"id\":\""+item.Id+"\"}")!.AsObject(),now);
    Assert(AgentToolCatalog.Schedules(data,new DateOnly(2026,11,2)).Count==0);
    bool rejected=false;try{AgentToolCatalog.AddSchedule(data,System.Text.Json.Nodes.JsonNode.Parse("{\"date\":\"2026/11/02\",\"start\":\"09:00\",\"title\":\"x\"}")!.AsObject(),now);}catch(ArgumentException){rejected=true;}Assert(rejected);
    Assert(AgentToolCatalog.Tools.Select(x=>x.Name).Distinct().Count()==AgentToolCatalog.Tools.Count && AgentToolCatalog.Tools.All(x=>System.Text.Json.JsonDocument.Parse(x.InputSchema).RootElement.GetProperty("type").GetString()=="object"));
});
Check("old chat settings move to the chat plugin once and leave the app settings empty", () => {
    var old=JsonSerializer.Deserialize<AppData>("{\"Settings\":{\"CodexChatEnabled\":true,\"ChatAgent\":\"claude\",\"ClaudeChatModel\":\"opus\",\"CodexChatFast\":false,\"VoiceHotkey\":\"Win+Alt+B\",\"WhisperModelPath\":\"D:\\\\m.bin\"}}")!;
    PluginData.Normalize(old);
    string? Moved(string key)=>PluginData.Setting(old,LegacyChat.Plugin,key);
    Assert(Moved("codexEnabled")=="true" && Moved("agent")=="claude" && Moved("claudeModel")=="opus" && Moved("codexFast")=="false" && Moved("voiceHotkey")=="Win+Alt+B" && Moved("whisperModel")=="D:\\m.bin");
    Assert(old.Settings.ChatAgent==null && old.Settings.CodexChatEnabled==null && old.Settings.VoiceHotkey==null);
    Assert(!JsonSerializer.Serialize(old.Settings).Contains("VoiceHotkey"));
    Assert(WorkspaceLayouts.Current(old).Tabs.Single(t=>t.Id==WorkspaceLayouts.ChatTab).Widgets.Single().WidgetId==LegacyChat.Widget);
    PluginData.SetSetting(old,LegacyChat.Plugin,"agent","codex");old.Settings.ChatAgent="claude";PluginData.Normalize(old);
    Assert(Moved("agent")=="codex");
    var unused=new AppData();PluginData.Normalize(unused);
    Assert(unused.Workspace==null && WorkspaceLayouts.Default().Tabs.All(t=>t.Id!=WorkspaceLayouts.ChatTab && t.Widgets.Count==0));
});
Check("the former built-in widgets move to the plugin widgets once and keep their place and store", () => {
    var data=JsonSerializer.Deserialize<AppData>("{\"Workspace\":{\"GridVersion\":6,\"Tabs\":[{\"Id\":\"dashboard\",\"Title\":\"대시보드\",\"Widgets\":[{\"Id\":\"builtin.clock\",\"WidgetId\":\"builtin.clock\",\"X\":-6,\"Y\":-4,\"Width\":3,\"Height\":1},{\"Id\":\"builtin.weather\",\"WidgetId\":\"builtin.weather\",\"X\":-3,\"Y\":-4,\"Width\":4,\"Height\":1},{\"Id\":\"builtin.agenda\",\"WidgetId\":\"builtin.agenda\",\"X\":-6,\"Y\":2,\"Width\":9,\"Height\":2}]},{\"Id\":\"schedule\",\"Title\":\"일정\",\"Widgets\":[{\"Id\":\"builtin.calendar\",\"WidgetId\":\"builtin.calendar\",\"X\":-7,\"Y\":-4,\"Width\":14,\"Height\":8}]},{\"Id\":\"settings\",\"Title\":\"설정\"}]},\"WidgetStores\":{\"builtin.clock\":{\"PluginId\":\"\",\"WidgetId\":\"builtin.clock\",\"Values\":{\"alignment\":\"center-left\"}}}}")!;
    PluginData.Normalize(data);
    var widgets=data.Workspace!.Tabs.SelectMany(t=>t.Widgets).ToArray();
    Assert(widgets.Select(w=>w.WidgetId).SequenceEqual([WorkspaceLayouts.PluginClockWidget,WorkspaceLayouts.PluginWeatherWidget,WorkspaceLayouts.PluginAgendaWidget,WorkspaceLayouts.PluginCalendarWidget]) && widgets[0].X==-6 && widgets[3].Width==14);
    Assert(PluginData.Store(data,"builtin.clock") is {PluginId:"facet.clock",WidgetId:WorkspaceLayouts.PluginClockWidget} store && store.Values["alignment"]=="center-left");
    PluginData.Normalize(data);Assert(data.Workspace.Tabs.SelectMany(t=>t.Widgets).Select(w=>w.WidgetId).SequenceEqual(widgets.Select(w=>w.WidgetId)));
    Assert(WorkspaceLayouts.LegacyDefault().Tabs.SelectMany(t=>t.Widgets).All(w=>w.WidgetId.StartsWith("plugin.facet.")));
});
Check("calendar records merge into the schedule plugin's store and the store wins per date", () => {
    var date=new DateOnly(2026,11,14);
    string stored=JsonSerializer.Serialize(new CalendarStoreFile{Days=[new(){Date=date,Note="저장소"}]});
    var calendar=new CalendarStoreFile{Days=[new(){Date=date,Note="달력"},new(){Date=date.AddDays(1),Note="달력만"}],SchedulePresets=[new(){Label="업무",Color="#91BFFF"}]};
    var merged=JsonSerializer.Deserialize<CalendarStoreFile>(LegacySchedule.Merge(stored,calendar,[new(){Date=date.AddDays(2),Note="기록"}],null))!;
    Assert(merged.Days.Select(d=>d.Note).SequenceEqual(["저장소","달력만","기록"]) && merged.SchedulePresets!.Single().Label=="업무");
    var fresh=JsonSerializer.Deserialize<CalendarStoreFile>(LegacySchedule.Merge(null,null,[new(){Date=date,Note="기록"}],null))!;
    Assert(fresh.Days.Single().Note=="기록" && fresh.SchedulePresets==null);
    bool rejected=false;try{LegacySchedule.Merge("{\"SchemaVersion\":9}",null,[],null);}catch(InvalidDataException){rejected=true;}Assert(rejected);
});Check("chat references merge into the plugin's store and the store wins per chat", () => {
    string id=Guid.NewGuid().ToString(),other=Guid.NewGuid().ToString();
    string text=LegacyChat.Merge("{\"SchemaVersion\":1,\"Extra\":7,\"Chats\":[{\"ThreadId\":\""+id+"\",\"Title\":\"저장소\"}]}",[new(){ThreadId=id,Title="기록"},new(){ThreadId=other,Title="추가"}]);
    var chats=JsonDocument.Parse(text).RootElement;
    Assert(chats.GetProperty("Extra").GetInt32()==7 && chats.GetProperty("Chats").GetArrayLength()==2 && chats.GetProperty("Chats")[0].GetProperty("Title").GetString()=="저장소");
});
Check("today's schedules project previous, current and next with the current memo line", () => {
    var today=new DateTime(2026,11,5);var data=new AppData();
    data.CalendarDays.Add(new(){Date=DateOnly.FromDateTime(today),Schedules=[
        new(){Name="운동",StartMinute=18*60,DurationMinutes=60},
        new(){Name="코드 리뷰",StartMinute=21*60,DurationMinutes=60,Content="\n  첫 줄 메모  \n둘째 줄"},
        new(){Name="",StartMinute=23*60,DurationMinutes=30,Content="이름 없는 일정\n그 메모"}]});
    var (previous,current,next)=DaySchedules.Around(data,today.AddHours(21.5));
    Assert(previous!.Title=="운동" && current!.Title=="코드 리뷰" && current.Memo=="첫 줄 메모" && next!.Title=="이름 없는 일정" && next.Memo=="그 메모");
    Assert(!current.IsEnding(today.AddHours(21.5)) && !current.IsEnding(today.AddMinutes(21*60+49)) && current.IsEnding(today.AddMinutes(21*60+50)));
    var (_,gap,after)=DaySchedules.Around(data,today.AddHours(22));
    Assert(gap==null && after!.Start==today.AddHours(23));
    data.CalendarDays.Add(new(){Date=DateOnly.FromDateTime(today.AddDays(-1)),Schedules=[new(){Name="밤샘",StartMinute=23*60,DurationMinutes=120}]});
    var (_,carried,_)=DaySchedules.Around(data,today.AddMinutes(30));
    Assert(carried!.Title=="밤샘" && carried.Start==today.AddHours(-1) && carried.End==today.AddHours(1));
});
Console.WriteLine($"Final: {passed} checks passed.");
