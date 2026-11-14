using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Facet.MusicPlugin;

internal static partial class Program
{
    private static void CheckMusicPlaylistHover(string qa)
    {
        var fake=new FakeMusicTransport();
        var (library,music)=NewMusic(fake,new("11111111111","작업할 때 듣는 아주 긴 재생 목록 제목 · 끝부분까지 확인할 수 있는 음악"),new("22222222222","짧은 곡"));using var service=music;
        var stack=new StackPanel();var modal=new FakeModal(stack);
        using var panel=new DashboardMusicPanel { Modal=modal,Width=232,VerticalAlignment=VerticalAlignment.Top };panel.Connect(service,library);
        stack.Children.Add(panel);
        panel.Part<Button>("ListButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var list=(FrameworkElement)stack.Children[1];list.Width=232;
        var rows=panel.Part<StackPanel>("TrackRows");
        var host=new Window { Content=stack,Width=232,SizeToContent=SizeToContent.Height,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=-20000,Top=-20000,ShowActivated=false,ShowInTaskbar=false };
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static MusicTitleLine Label(Border row)=>(MusicTitleLine)((Grid)((Grid)row.Child).Children[1]).Children[0];
        TranslateTransform Offset(Border row)=>(TranslateTransform)typeof(MusicTitleLine).GetField("scroll",flags)!.GetValue(Label(row))!;
        static void Hover(Border row,bool active)=>row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent=active?Mouse.MouseEnterEvent:Mouse.MouseLeaveEvent });
        try {
            host.Show();Pump(120);
            var headingFrame=panel.Part<Border>("TrackTitle");
            var heading=(MusicTitleLine)headingFrame.Child;
            var headingScroll=(TranslateTransform)typeof(MusicTitleLine).GetField("scroll",flags)!.GetValue(heading)!;
            Assert(!headingScroll.HasAnimatedProperties && headingScroll.X==0,"the player heading stays still until the pointer enters its title area");
            Hover(headingFrame,true);PumpUntil(()=>headingScroll.X< -3);
            Assert(headingScroll.X< -3,"hovering the player heading starts scrolling immediately without a reading delay");
            double startOffset=headingScroll.X;var watch=System.Diagnostics.Stopwatch.StartNew();Pump(600);
            double speed=(startOffset-headingScroll.X)/watch.Elapsed.TotalSeconds;
            Assert(speed>30 && speed<43,"hover title scrolling runs at the faster 36 DIP per second: "+speed.ToString("F1"));
            double headingOffset=headingScroll.X;typeof(DashboardMusicPanel).GetMethod("UpdateTitle",flags)!.Invoke(panel,null);
            Assert(Math.Abs(headingScroll.X-headingOffset)<1,"refreshing the same current title preserves its scroll position");
            Hover(headingFrame,false);
            Assert(!headingScroll.HasAnimatedProperties && headingScroll.X==0,"leaving the player heading stops its scroll and restores the beginning");
            Hover(headingFrame,true);
            var original=library.Data.Tracks[0];library.Commit(data=>data.Tracks[0]=original with { Title="짧은 제목" });panel.Connect(service,library);Pump(80);
            Assert(!headingScroll.HasAnimatedProperties && headingScroll.X==0,"changing the current heading to a short title resets and stops scrolling");
            library.Commit(data=>data.Tracks[0]=original);panel.Connect(service,library);Pump(80);host.Hide();Pump(50);
            Assert(!headingScroll.HasAnimatedProperties && headingScroll.X==0,"hiding the player stops its current-title animation");
            host.Show();Pump(120);Assert(!headingScroll.HasAnimatedProperties && headingScroll.X==0,"showing the player waits for another title hover");
            Hover(headingFrame,true);Pump(1700);Assert(headingScroll.X< -5,"hovering again restarts the player heading from its beginning");
            var row=(Border)rows.Children[0];var scroll=Offset(row);
            Assert(Math.Abs(row.ActualHeight-34)<.1 && row.Margin.Bottom==4,"compact music rows are 34 DIP with 4 DIP gaps");
            Assert(!scroll.HasAnimatedProperties && scroll.X==0,"playlist titles stay still before their row is hovered");
            Hover(row,true);Pump(200);
            Assert(scroll.X< -3,"hovering a long playlist row starts its title scrolling immediately");Pump(600);
            Hover(row,false);
            Assert(!scroll.HasAnimatedProperties && scroll.X==0,"leaving a playlist row resets its title to the beginning and removes the animation clock");
            var shortRow=(Border)rows.Children[1];Hover(shortRow,true);Pump(80);
            Assert(!Offset(shortRow).HasAnimatedProperties && Offset(shortRow).X==0,"short playlist titles never scroll even while hovered");Hover(shortRow,false);
            Hover(row,true);Pump(1600);list.Visibility=Visibility.Collapsed;Pump(50);
            Assert(!scroll.HasAnimatedProperties && scroll.X==0,"hiding the list stops and resets an actively scrolling title");
            list.Visibility=Visibility.Visible;Pump(50);
            Assert(!scroll.HasAnimatedProperties,"showing the list again waits for a fresh row hover");
            Hover(row,true);Pump(1600);
            typeof(DashboardMusicPanel).GetMethod("RenderList",flags)!.Invoke(panel,null);Pump(50);
            Assert(!scroll.HasAnimatedProperties,"rebuilding the playlist releases the old row title animation");
            row=(Border)rows.Children[0];scroll=Offset(row);Hover(row,true);Pump(1600);panel.Dispose();panel.InvalidateMeasure();panel.UpdateLayout();Pump(100);
            Assert(!scroll.HasAnimatedProperties && !headingScroll.HasAnimatedProperties && fake.Opens==0 && library.Data.Tracks.Count==2,"disposing the player clears heading and row clocks without playing audio or editing the playlist");
        }
        finally {host.Content=null;host.Close();}
    }
}
