using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Facet;
using Facet.ConditionPlugin;
using Facet.Core;
using Facet.Design;
using Facet.MusicPlugin;
using Facet.SleepPlugin;
using Facet.Widgets;

internal static partial class Program
{
    private static bool WidgetThemeSoft()=>Facet.Plugins.WidgetTheme.IsSoftHud;
    // Manual look at every HUD widget with sample data, on the same tiles a board would give them.
    private static int RunHudPreview(string[] args)
    {
        try {
            var ui=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            foreach(string resource in new[]{"Tokens","Components","Scrolling"})ui.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri($"pack://application:,,,/Facet;component/Design/{resource}.xaml") });
            string skin=args.Length>3?args[3]:"space";Skins.Apply(ui,skin);
            string root=args[1],output=args[2];
            var store=new Store(root);var data=store.Load();data.Settings.Skin=skin;store.Save(data);
            using var controller=new Controller(root,Controller.ExitHotkey);
            var now=DateTime.Now;int slot=now.Hour*60/30*30;
            var day=new CalendarDayEntry { Date=DateOnly.FromDateTime(now),Schedules=[
                new(){Name="운동",StartMinute=Math.Max(0,slot-180),DurationMinutes=30},
                new(){Name="코드 리뷰",StartMinute=slot,DurationMinutes=60,Content="리뷰 대기 중인 항목부터 확인"},
                new(){Name="취침 알림",StartMinute=Math.Min(1410,slot+90),DurationMinutes=30}] };
            // Clock, weather and agenda are plugin widgets now (ADR 0101); the preview draws their HUD forms directly.
            var scheduleStore=PluginFileStore.For(root,LegacySchedule.Plugin);scheduleStore.Write(Facet.SchedulePlugin.ScheduleRecords.Serialize(new AppData{CalendarDays=[day]}));
            var agenda=new Facet.SchedulePlugin.AgendaView(new Facet.SchedulePlugin.ScheduleRecords(scheduleStore));
            var clockFace=new HudClockFace();clockFace.Show(now);
            var instant=DateTimeOffset.Now;var first=new DateTimeOffset(now.Date.AddDays(-6));
            var records=new (double Hours,string Kind)[]{(14.2,"good"),(30.75,"okay"),(47.5,"tired"),(55.1,"good"),(80,"stressed"),(92.25,"okay"),(104.4,"sleepy"),(112,"good"),(128.6,"tired"),(136.5,"good")}.Select(r=>new Facet.ConditionPlugin.ConditionEntry { At=first.AddHours(r.Hours),Kind=r.Kind }).ToArray();
            var condition=new ConditionHudView();condition.Refresh(records,instant);
            var history=new ConditionHistory(true);history.Refresh(records,instant);
            var sleep=new SleepDurationChart();
            var today=DateOnly.FromDateTime(now);
            var nights=new[]{(6.5),5,7.2,6,8,7,7.15}.Select((hours,i)=>new Facet.SleepPlugin.SleepEntry { Date=today.AddDays(i-6),Attempt="23:00",Latency=0,AwakeMinutes=0,FinalWake=TimeOnly.FromTimeSpan(TimeSpan.FromHours(23+hours-24)).ToString("HH:mm") }).ToArray();
            sleep.Refresh(nights,today);
            var music=new DashboardMusicPanel();
            var board=new Canvas();
            void Tile(FrameworkElement view,double left,double top,double height,bool fill=false)
            {
                var tile=new Grid { Width=368,Height=height };
                tile.Children.Add(new HudPanel());
                tile.Children.Add(new WidgetFit { FillHeight=fill,StretchChild=true,Margin=new(20,14,20,14),Child=view });
                Canvas.SetLeft(tile,left);Canvas.SetTop(tile,top);board.Children.Add(tile);
            }
            clockFace.HorizontalAlignment=HorizontalAlignment.Left;
            Tile(new ClockFitButton(clockFace,()=>null,_=>true),40,74,176,true);
            var weatherFace=new HudWeatherFace { VerticalAlignment=WidgetThemeSoft()?VerticalAlignment.Stretch:VerticalAlignment.Top };weatherFace.Show("서울","맑음","8.0°C","52%");
            Tile(weatherFace,40,266,176);
            Tile(agenda,40,458,272);
            Tile(condition,1192,74,176);
            Tile(history,1192,266,176);
            Tile(sleep,1192,458,176);
            Tile(music,1192,650,176);
            var host=new Grid { Background=new SolidColorBrush(Color.FromRgb(5,7,15)),Width=1600,Height=900 };
            if(Skins.Visuals!.CreateBackdrop() is { } backdrop)host.Children.Add(backdrop);else host.Background=new SolidColorBrush(Color.FromRgb(70,62,90));host.Children.Add(board);
            var window=new Window { Content=host,Width=1600,Height=900,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=-20000,Top=-20000,ShowActivated=false,ShowInTaskbar=false };window.Show();
            VisualTree(host).OfType<SpaceVoyage>().FirstOrDefault()?.ShowForCheck(new(PlanetKind.Rock,5,5,-18,12,-300,1,.5,.25),-30);
            void Settle() { host.UpdateLayout();var frame=new System.Windows.Threading.DispatcherFrame();ui.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,()=>frame.Continue=false);System.Windows.Threading.Dispatcher.PushFrame(frame); }
            Settle();Settle();
            var image=new System.Windows.Media.Imaging.RenderTargetBitmap(1600,900,96,96,PixelFormats.Pbgra32);image.Render(host);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using(var file=File.Create(output))encoder.Save(file);
            window.Close();music.Dispose();
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
}
