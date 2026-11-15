using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Facet;
using Facet.Core;
using Facet.Design;

internal static partial class Program
{
    // Today's agenda is the schedule plugin's widget (ADR 0101): schedules and open todos from its store, not the app's reminders.
    private static void CheckDashboardRemindersAndDebug(string qa)
    {
        var now = new DateTimeOffset(2026,10,24,10,0,0,TimeSpan.FromHours(9)); var data = new AppData();
        UserReminders.Apply(data,new(){Action="add",Key="ui-reminder",Title="테스트 결과 살펴보기",Body="놓치지 않도록 알림을 받아요",At=now.AddHours(5)});
        data.CalendarDays.Add(new(){Date=new(2026,10,24),Todos=[new(){Title="오늘의 첫 할 일"}],Schedules=[new(){Name="오늘 주요 일정 확인",Content="진행할 내용을 정리해요",StartMinute=780,DurationMinutes=60}]});
        string root=Path.Combine(qa,"agenda-"+Guid.NewGuid().ToString("N"));var store=PluginFileStore.For(root,LegacySchedule.Plugin);
        Assert(store.Write(Facet.SchedulePlugin.ScheduleRecords.Serialize(new AppData{CalendarDays=data.CalendarDays})),"agenda fixture saves to the plugin store");
        var records=new Facet.SchedulePlugin.ScheduleRecords(store);
        var agenda=new Facet.SchedulePlugin.AgendaView(records);agenda.Show(DashboardAgenda.Today(records.Data,now));
        var hud=new OverlayTabs();var tabs=(TabControl)hud.FindName("Tabs");((Grid)hud.FindName("DashboardViewport")).Children.Add(agenda);hud.Measure(new Size(1440,900));hud.Arrange(new Rect(0,0,1440,900));hud.UpdateLayout();
        var cards=VisualElements(agenda).OfType<Border>().Where(card=>card.Width==236).ToArray();
        Assert(cards.Length==1 && System.Windows.Automation.AutomationProperties.GetName(cards[0]).Contains("오늘의 첫 할 일") && cards.All(card=>card.CornerRadius==new CornerRadius(16)),"the agenda shows the plugin's open todos as cards without the app's reminders");
        agenda.Show(DashboardAgenda.Today(new AppData(),now));hud.UpdateLayout();
        Assert(VisualElements(agenda).OfType<TextBlock>().Any(text=>text.Text=="오늘 등록된 일정이 없습니다"),"an empty day says so");
        foreach(var size in new[]{new Size(1440,900),new Size(480,360)}) {
            hud.Measure(size);hud.Arrange(new Rect(size));hud.UpdateLayout();
            SaveWeatherImage(hud,size,Path.Combine(qa,$"dashboard-agenda-cards-{size.Width}.png"));
        }
        Assert(!hud.DebugEnabled && !hud.HandleDebugKey(Key.F8,ModifierKeys.Control),"layout debug is off by default and ignores modified F8");
        Assert(hud.HandleDebugKey(Key.F8,ModifierKeys.None) && hud.DebugEnabled,"plain F8 enables layout inspection");
        var target=(FrameworkElement)agenda.Children[0];hud.Inspector!.Inspect(target);hud.UpdateLayout();
        var expected=target.TransformToAncestor(tabs).TransformBounds(new Rect(0,0,target.ActualWidth,target.ActualHeight));
        Assert(hud.Inspector.HoverBounds==expected && !hud.Inspector.IsHitTestVisible,"hover outline follows scaled element bounds without intercepting input");
        SaveWeatherImage(hud,new Size(480,360),Path.Combine(qa,"dashboard-debug-hover.png"));
        hud.HandleDebugKey(Key.F8,ModifierKeys.None);
        Assert(!hud.DebugEnabled && !(AdornerLayer.GetAdornerLayer(tabs).GetAdorners(tabs)?.OfType<LayoutInspector>().Any()??false),"second F8 removes debug outlines and preserves layout");
        Assert(KoreanPlaces.Display(new(37,127,"Unknown-city","KR","41"))=="경기도" && KoreanPlaces.Display(new(0,0,"Testtown"))=="현재 위치","unsupported place names keep a Korean region or neutral label without another external provider");
        agenda.Dispose();
    }
}
