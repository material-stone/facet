using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Facet;
using Facet.MusicPlugin;
using Facet.Plugins;

// The music player lives in Facet.MusicPlugin; these checks run its parts against in-memory host services.
internal static partial class Program
{
    private sealed class MemorySettings : IPluginSettings
    {
        internal readonly Dictionary<string,string> Values=[];
        internal bool Fail;
        public string Get(string key)=>Values.GetValueOrDefault(key,"");
        public double GetNumber(string key)=>0;
        public IReadOnlyList<string> GetList(string key)=>[];
        public bool Save(string key,string value) { if(Fail)return false;Values[key]=value;return true; }
        public event Action<string>? Changed { add { } remove { } }
    }
    private sealed class FakeModal(System.Windows.Controls.Panel target) : IWidgetModal,IModalHandle
    {
        private ModalOptions options=new();private FrameworkElement? shown;
        public bool IsOpen=>shown!=null;
        public event Action? Closed;
        public IModalHandle? Show(FrameworkElement content,ModalOptions? value=null) { if(shown!=null)return null;options=value ?? new();shown=content;target.Children.Add(content);return this; }
        public void Close() { if(shown==null)return;target.Children.Remove(shown);shown=null;var closed=Closed;Closed=null;closed?.Invoke(); }
        internal void Escape() { if(options.HandleEscape?.Invoke()!=true)Close(); }
    }
    private sealed class FakeMusicTransport : IMusicTransport
    {
        public bool Connected { get; private set; }
        internal BrowserMusicState State=new("","",false,false,false,0,0);
        internal int Opens,Pauses,Plays,Seeks;
        internal bool FailRead;
        internal double Volume=.5,OpenVolume;
        public System.Threading.Tasks.Task OpenAsync(string id) { Connected=true;Opens++;OpenVolume=Volume;State=new(id,"합성 음악 "+Opens,true,false,false,10,120);return System.Threading.Tasks.Task.CompletedTask; }
        public System.Threading.Tasks.Task<BrowserMusicState> ReadAsync() { if(FailRead)throw new IOException("closed");return System.Threading.Tasks.Task.FromResult(State); }
        public System.Threading.Tasks.Task PlayAsync() { Plays++;State=State with { Playing=true,Ended=false };return System.Threading.Tasks.Task.CompletedTask; }
        public System.Threading.Tasks.Task PauseAsync() { Pauses++;State=State with { Playing=false };return System.Threading.Tasks.Task.CompletedTask; }
        public System.Threading.Tasks.Task SeekAsync(double seconds) { Seeks++;State=State with { Position=seconds,Ended=false };return System.Threading.Tasks.Task.CompletedTask; }
        public System.Threading.Tasks.Task SetVolumeAsync(double volume) { Volume=volume;return System.Threading.Tasks.Task.CompletedTask; }
        public void Dispose()=>Connected=false;
    }
    private static (MusicLibrary Library,MusicPlaybackService Music) NewMusic(IMusicTransport transport,params YouTubeTrack[] tracks)
    {
        var library=new MusicLibrary(new MemorySettings());library.Commit(data=>data.Tracks=[..tracks]);
        return (library,new MusicPlaybackService(library,transport));
    }
    private static void MusicAwait(System.Threading.Tasks.Task task,int milliseconds=30000)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        while(!task.IsCompleted && watch.ElapsedMilliseconds<milliseconds)Pump(25);
        if(!task.IsCompleted)throw new TimeoutException("Music check timed out.");
        task.GetAwaiter().GetResult();
    }
    private static void CheckMusicRules()
    {
        foreach(string url in new[]{"https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=10","https://youtu.be/dQw4w9WgXcQ?si=test","https://music.youtube.com/watch?v=dQw4w9WgXcQ","https://www.youtube.com/shorts/dQw4w9WgXcQ","https://m.youtube.com/live/dQw4w9WgXcQ"})
            Assert(YouTubePlaylist.ParseUrl(url)=="dQw4w9WgXcQ","a YouTube link is accepted: "+url);
        foreach(string url in new[]{"https://youtube.com.evil.org/watch?v=dQw4w9WgXcQ","https://youtube.com@evil.org/watch?v=dQw4w9WgXcQ","javascript:alert(1)","https://youtube.com/playlist?list=test","https://youtube.com/watch?v=bad","https://youtube.com/watch?v=dQw4w9WgXcQ&v=aaaaaaaaaaa","https://youtu.be/dQw4w9WgXcQ/extra","https://youtube.com:444/watch?v=dQw4w9WgXcQ"}) {
            bool failed=false;try { YouTubePlaylist.ParseUrl(url); }catch(ArgumentException) { failed=true; }
            Assert(failed,"an unrelated link is rejected: "+url);
        }
        var list=new YouTubePlaylist();list.Add("https://youtu.be/dQw4w9WgXcQ");
        bool duplicate=false;try { list.Add("https://youtube.com/watch?v=dQw4w9WgXcQ"); }catch(ArgumentException) { duplicate=true; }
        Assert(duplicate && list.Copy().Tracks.Count==1,"a playlist rejects duplicates and copies its entries");
        Assert(JsonSerializer.Deserialize<YouTubePlaylist>("{\"Tracks\":[],\"Repeat\":true}")!.VolumePercent==50,"records without a volume keep the old level");
        foreach(int percent in new[]{-1,101}) { bool rejected=false;try { YouTubePlaylist.Validate(new() { VolumePercent=percent }); }catch(ArgumentException) { rejected=true; }Assert(rejected,"an out-of-range volume is rejected"); }
        var settings=new MemorySettings();var library=new MusicLibrary(settings);
        Assert(library.Commit(data=>data.Repeat=true) && new MusicLibrary(settings).Data.Repeat,"the playlist reloads from the plugin's saved settings");
        settings.Fail=true;Assert(!library.Commit(data=>data.Repeat=false) && library.Data.Repeat,"a failed save keeps the previous playlist");
        settings.Fail=false;settings.Values[MusicLibrary.PlaylistKey]="not json";Assert(new MusicLibrary(settings).Data.Tracks.Count==0,"a damaged record starts empty instead of failing the plugin");
    }
    private static void CheckMusicLifetime(string qa)
    {
        var fake=new FakeMusicTransport();
        var (library,music)=NewMusic(fake,new("11111111111","첫 곡"),new("22222222222","다음 곡"));using var service=music;
        using var panel=new DashboardMusicPanel { Width=232 };panel.Connect(service,library);
        var tabs=new TabControl();tabs.Items.Add(new TabItem { Header="music",Content=panel });tabs.Items.Add(new TabItem { Header="other",Content=new TextBlock { Text="다른 화면" } });
        var host=new Window { Content=tabs,Width=260,Height=360,Left=-20000,Top=-20000,ShowActivated=false,ShowInTaskbar=false };
        try {
            host.Show();Pump(60);MusicAwait(service.PlayAsync("11111111111"));
            Assert(service.Playing && fake.Opens==1,"music service plays independently of the widget");
            // The real pointer decides the value, and the window sits far off-screen, so a press lands away from these starting places.
            void Press(Slider slider) { slider.UpdateLayout();slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left) { RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent }); }
            void Release(Slider slider) { slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left) { RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent });slider.ReleaseMouseCapture(); }
            var pressedVolume=panel.Part<Slider>("Volume");pressedVolume.Value=35;Press(pressedVolume);Release(pressedVolume);
            Assert(pressedVolume.Value!=35 && fake.Volume==pressedVolume.Value/100,"pressing the volume bar applies the pointer's place at once");
            pressedVolume.Value=50;
            var seek=panel.Part<Slider>("Seek");fake.State=fake.State with { Duration=100 };MusicAwait(service.PollAsync());seek.Value=37.5;int seeks=fake.Seeks;
            Press(seek);
            double pressedAt=seek.Value;
            Assert(seek.IsEnabled && pressedAt!=37.5 && fake.Seeks==seeks,"pressing the timeline moves only its bar until the pointer lifts");
            Release(seek);
            Assert(fake.Seeks==seeks+1 && fake.State.Position==pressedAt,"lifting the pointer moves playback to the bar's place");
            tabs.SelectedIndex=1;Pump(40);
            Assert(!panel.IsVisible && service.Playing && fake.Pauses==0,"moving to another tab never pauses music");
            host.Hide();panel.Dispose();MusicAwait(service.PollAsync());
            Assert(service.Playing && fake.Pauses==0,"hiding the overlay and disposing its remote leave music playing");
            MusicAwait(service.PauseAsync());Assert(!service.Playing && fake.Pauses==1,"explicit pause alone pauses playback");
            MusicAwait(service.PlayAsync());Assert(service.Playing && fake.Plays==1 && fake.Opens==1,"resume continues the opened song");
            MusicAwait(service.SeekAsync(45));Assert(fake.State.Position==45,"music remote seeks the timeline");
            fake.State=fake.State with { Playing=false,Ended=true,Position=120 };MusicAwait(service.PollAsync());MusicAwait(service.PollAsync());
            Assert(service.SelectedId=="22222222222" && fake.Opens==2 && service.Playing,"the queue advances once after the current song ends");
            library.Commit(data=>data.Repeat=true);
            fake.State=fake.State with { Playing=false,Ended=true,Position=120 };int opens=fake.Opens;MusicAwait(service.PollAsync());MusicAwait(service.PollAsync());
            Assert(fake.Opens==opens && fake.State.Position==0 && fake.State.Playing,"one-song repeat seeks and resumes the same song");
            fake.FailRead=true;MusicAwait(service.PollAsync());Assert(!service.Playing && service.Error.Length>0,"a failed transport clears confirmed playback state");
            fake.FailRead=false;MusicAwait(service.PlayAsync());Assert(service.Playing && fake.Opens==opens+1,"retry reopens the selected song after failure");
            library.Commit(data=>data.Mode=YouTubePlaylist.ListMode);
            string current=service.SelectedId!;MusicAwait(service.NextAsync());Assert(service.SelectedId!=current,"next moves to the other song");
            MusicAwait(service.NextAsync());Assert(service.SelectedId==current,"next wraps at the end of the list");
            MusicAwait(service.SeekAsync(1));MusicAwait(service.PreviousAsync());Assert(service.SelectedId!=current,"previous early in a song goes to the song before");
            MusicAwait(service.SeekAsync(30));string kept=service.SelectedId!;MusicAwait(service.PreviousAsync());Assert(service.SelectedId==kept && fake.State.Position==0,"previous later in a song restarts it");
            library.Commit(data=>data.Mode=YouTubePlaylist.ShuffleMode);string from=service.SelectedId!;MusicAwait(service.NextAsync());Assert(service.SelectedId!=from,"shuffle picks another song");
            MusicAwait(service.SeekAsync(1));MusicAwait(service.PreviousAsync());Assert(service.SelectedId==from,"previous in shuffle returns to the song it came from");
            service.Dispose();Assert(!fake.Connected,"stopping the plugin disposes its playback transport");
        } finally { host.Content=null;host.Close(); }
        var indicator=new MusicPlaybackIndicator();
        var startup=new ToastWindow("실행 알림","",()=>{},startup:true);
        try {
            indicator.UpdateTitle("현재 곡 제목");
            var frame=(Border)indicator.Content;var line=(MusicTitleLine)frame.Child;
            var startupFrame=(Border)startup.Content;
            Assert(System.Windows.Automation.AutomationProperties.GetName(indicator).Contains("현재 곡 제목") && frame.Padding==startupFrame.Padding && frame.CornerRadius==startupFrame.CornerRadius && frame.BorderThickness==startupFrame.BorderThickness,"music indicator keeps the current title with the startup toast's frame");
            new System.Windows.Interop.WindowInteropHelper(indicator).EnsureHandle();
            Assert(!indicator.ShowInTaskbar && !indicator.ShowActivated && (indicator.ExtendedStyle&0x08000020)==0x08000020,"music indicator is passive, click-through and absent from taskbar items");
            frame.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            int height=(int)Math.Ceiling(frame.DesiredSize.Height);
            indicator.Content=null;
            var titleHost=new Window { Content=frame,Width=280,SizeToContent=SizeToContent.Height,Left=-20000,Top=-20000,ShowActivated=false,ShowInTaskbar=false };
            try {
                string longTitle="이것은 끝부분까지 읽을 수 있어야 하는 아주 긴 음악 제목입니다 · 마지막 구절";
                indicator.UpdateTitle(longTitle);titleHost.Show();Pump(100);
                var scroll=(System.Windows.Media.TranslateTransform)typeof(MusicTitleLine).GetField("scroll",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(line)!;
                Assert(frame.ActualWidth<=280 && line.ActualWidth<=254 && scroll.X==0,"music toast caps total width at 280 DIP and holds the beginning of an overflowing title");
                Pump(1700);Assert(scroll.X< -5,"an overflowing music title scrolls horizontally after its reading pause");
                double offset=scroll.X;indicator.UpdateTitle(longTitle);
                Assert(Math.Abs(scroll.X-offset)<1,"the periodic same-title update never restarts the scrolling animation");
                titleHost.Hide();Pump(50);Assert(!scroll.HasAnimatedProperties && scroll.X==0,"hiding the music toast stops its scrolling clock");
                titleHost.Show();Pump(1600);Assert(scroll.X< -2,"showing the music toast resumes scrolling from the beginning");
                indicator.UpdateTitle("짧은 곡");Pump(100);
                Assert(!scroll.HasAnimatedProperties && scroll.X==0,"a short or changed music title resets the offset and stays still");
            } finally { titleHost.Content=null;titleHost.Close(); }
        } finally { indicator.Close();startup.Close(); }
    }
    private static void CheckMusic()
    {
        var fake=new FakeMusicTransport();
        var settings=new MemorySettings();var library=new MusicLibrary(settings);using var service=new MusicPlaybackService(library,fake);
        var layer=new Grid();var modal=new FakeModal(layer);
        var panel=new DashboardMusicPanel { Modal=modal };panel.Connect(service,library);
        var dialog=panel.UrlDialog;var add=panel.Part<Button>("AddTrack");var listButton=panel.Part<Button>("ListButton");
        var keyWindow=new Window { ShowActivated=false,ShowInTaskbar=false };
        var source=System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(keyWindow).EnsureHandle())!;
        YouTubePlaylist Saved()=>new MusicLibrary(settings).Data;
        try {
            var input=dialog.Input;
            var rows=panel.Part<StackPanel>("TrackRows");
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void Key(UIElement target,Key key)=>target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,key) { RoutedEvent=Keyboard.PreviewKeyDownEvent });
            void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            void Add(string value) { if(!dialog.IsOpen)Click(add);input.Text=value;Key(input,System.Windows.Input.Key.Enter); }
            Assert(!modal.IsOpen && panel.Part<Border>("PlaylistHost").Visibility==Visibility.Collapsed,"the list stays out of the tile until asked for");
            Click(listButton);
            Assert(modal.IsOpen && layer.Children.Count==1 && !dialog.IsOpen,"the list button opens the list in the widget modal");
            add.ApplyTemplate();add.Measure(new Size(400,40));add.Arrange(new Rect(0,0,400,40));add.UpdateLayout();
            Assert(Equals(add.Content,"＋ 곡 추가") && VisualElements(add).OfType<TextBlock>().Any(t=>t.Text=="＋ 곡 추가"),"the add button shows its label, not a type name");
            Add("https://example.com/watch?v=dQw4w9WgXcQ");
            Assert(dialog.IsOpen && dialog.Error.Length>0 && library.Data.Tracks.Count==0 && input.Text.Contains("example.com"),"an invalid URL keeps the input open without changing the playlist");
            Add("https://youtu.be/dQw4w9WgXcQ");
            Assert(!dialog.IsOpen && modal.IsOpen && input.Text=="" && rows.Children.Count==1 && Saved().Tracks.Count==1,"Enter saves the URL and returns to the list");
            Assert(System.Windows.Automation.AutomationProperties.GetName(panel.Part<Border>("TrackTitle")).Contains("dQw4w9WgXcQ"),"the selected title is available before any download");
            Add("https://youtube.com/watch?v=dQw4w9WgXcQ");
            Assert(library.Data.Tracks.Count==1 && dialog.IsOpen && dialog.Error.Length>0,"duplicate URLs keep the input for correction");
            settings.Fail=true;input.Text="https://youtu.be/aaaaaaaaaaa";Key(input,System.Windows.Input.Key.Enter);
            Assert(library.Data.Tracks.Count==1 && rows.Children.Count==1 && input.Text.Contains("aaaaaaaaaaa") && dialog.IsOpen && dialog.Error.Length>0 && panel.Part<Button>("RetrySave").Visibility==Visibility.Collapsed,"a save failure keeps only the input and its error for Enter retry");
            settings.Fail=false;Key(input,System.Windows.Input.Key.Enter);
            Assert(library.Data.Tracks.Count==2 && input.Text=="" && rows.Children.Count==2 && !dialog.IsOpen,"Enter retry commits the kept URL once and closes the input");
            Click(add);typeof(MusicUrlDialog).GetField("composing",flags)!.SetValue(dialog,true);input.Text="https://youtu.be/bbbbbbbbbbb";Key(input,System.Windows.Input.Key.Enter);
            Assert(dialog.IsOpen && library.Data.Tracks.Count==2,"IME confirmation Enter never adds music");
            typeof(MusicUrlDialog).GetField("composing",flags)!.SetValue(dialog,false);
            Assert(KeyboardNavigation.GetTabNavigation(dialog)==KeyboardNavigationMode.Cycle,"the input keeps keyboard tab navigation inside");
            modal.Escape();
            Assert(!dialog.IsOpen && modal.IsOpen && input.Text.Contains("bbbbbbbbbbb"),"Escape closes the input first and keeps its draft");
            modal.Escape();Assert(!modal.IsOpen && rows.Children.Count==2,"Escape then closes the list and keeps saved entries");
            Click(listButton);Assert(modal.IsOpen && layer.Children.Count==1,"the list opens again");
            var mode=panel.Part<Button>("ModeButton");Assert(library.Data.EffectiveMode==YouTubePlaylist.ListMode,"a new player goes through the list");Click(mode);
            Assert(Saved().EffectiveMode==YouTubePlaylist.OneMode && (string)mode.ToolTip=="한 곡 반복","the mode button goes to one-song repeat and survives a reload");Click(mode);Assert(Saved().EffectiveMode==YouTubePlaylist.ShuffleMode && (string)mode.ToolTip=="랜덤 재생","then to shuffle");Click(mode);Assert(Saved().EffectiveMode==YouTubePlaylist.ListMode && mode.Content is System.Windows.Shapes.Path,"then back to the list, shown as an icon");Click(mode);
            var row=(Border)rows.Children[0];var content=(Grid)((Grid)row.Child).Children[1];var remove=content.Children.OfType<Button>().Single();
            Assert(row.CornerRadius==new CornerRadius(6) && row.BorderThickness==new Thickness(0) && content.Margin==new Thickness(10,4,10,4),"music rows keep the todo item design");
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(50);
            Assert(library.Data.Tracks.Count==1 && library.Data.Tracks[0].VideoId=="aaaaaaaaaaa","removal deletes only the chosen entry");
            Assert(fake.Opens==0 && !service.Connected,"editing the list never starts playback");
            row=(Border)rows.Children[0];row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonUpEvent });
            Assert(fake.Opens==1 && service.SelectedId=="aaaaaaaaaaa","clicking a whole row plays that song");
            var play=panel.Part<Button>("Play");Click(play);
            Assert(!service.Playing && play.IsEnabled,"pause leaves the play button enabled for resuming");
            Click(play);Assert(service.Playing && fake.Opens==1,"play resumes without opening the song again");
            var volume=panel.Part<Slider>("Volume");volume.Value=35;
            Assert(fake.Volume==.35 && service.Playing && fake.Seeks==0 && panel.Part<TextBlock>("VolumeText").Text=="35%","the volume slider changes only the loudness and shows its percentage");
            Pump(450);Assert(Saved().VolumePercent==35,"volume is saved after the drag debounce");
            settings.Fail=true;volume.Value=0;Pump(450);
            Assert(fake.Volume==0 && service.Playing && library.Data.VolumePercent==35 && service.VolumePercent==0 && panel.Part<Button>("RetrySave").Visibility==Visibility.Visible,"a failed volume save keeps the sound and offers a retry");
            settings.Fail=false;Click(panel.Part<Button>("RetrySave"));
            Assert(Saved().VolumePercent==0 && service.VolumeError.Length==0 && service.Playing,"the retry saves the level without interrupting playback");
            volume.Value=100;Assert(fake.Volume==1 && service.TrySaveVolume() && Saved().VolumePercent==100,"saving flushes the latest volume before the debounce ends");
            Click(mode);
            Assert(Saved().VolumePercent==100,"playlist commits preserve the current volume");
            Key((Border)rows.Children[0],System.Windows.Input.Key.Delete);Pump(50);
            Assert(library.Data.Tracks.Count==0 && fake.Opens==1 && !service.Playing && play.IsEnabled,"Delete on a row removes it without starting another song");
            modal.Close();Click(play);
            Assert(modal.IsOpen && dialog.IsOpen,"play on an empty player asks for a URL");
            modal.Escape();Assert(!modal.IsOpen,"closing that input closes the modal it opened alone");
            panel.Dispose();Assert(!modal.IsOpen,"disposing the widget closes its modal");
            var reopened=new FakeMusicTransport();
            var saved=new MusicLibrary(settings);saved.Commit(data=>data.Tracks=[new("11111111111","다시 재생")]);
            using var fresh=new MusicPlaybackService(saved,reopened);MusicAwait(fresh.PlayAsync());
            Assert(fresh.VolumePercent==100 && reopened.OpenVolume==1,"a fresh service restores the saved volume before its first song");
        }
        finally { panel.Dispose();keyWindow.Close(); }
    }
}
