using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Facet;
using Facet.MusicPlugin;

internal static partial class Program
{
    private static async Task<string> CreateLocalMusicFixtureAsync(string root)
    {
        Directory.CreateDirectory(root);string wave=Path.Combine(root,"source.wav");
        using(var writer=new BinaryWriter(File.Create(wave))) {
            int length=20*8000*2;writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+length);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(8000);writer.Write(16000);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(length);writer.Write(new byte[length]);
        }
        var source=await Windows.Storage.StorageFile.GetFileFromPathAsync(wave);
        var folder=await Windows.Storage.StorageFolder.GetFolderFromPathAsync(root);
        var target=await folder.CreateFileAsync("source.m4a",Windows.Storage.CreationCollisionOption.ReplaceExisting);
        var profile=Windows.Media.MediaProperties.MediaEncodingProfile.CreateM4a(Windows.Media.MediaProperties.AudioEncodingQuality.Low);
        var prepared=await new Windows.Media.Transcoding.MediaTranscoder().PrepareFileTranscodeAsync(source,target,profile);
        if(!prepared.CanTranscode)throw new IOException("Synthetic M4A transcoding failed: "+prepared.FailureReason);
        await prepared.TranscodeAsync();return target.Path;
    }
    private static void CheckLocalMusic(string qa)
    {
        string root=Path.Combine(qa,"local-music");
        var fixture=CreateLocalMusicFixtureAsync(root);MusicAwait(fixture);string source=fixture.GetAwaiter().GetResult();
        int downloads=0;
        Task<MusicDownload> Download(string id,string destination,CancellationToken token) {
            token.ThrowIfCancellationRequested();downloads++;
            if(id=="99999999999")throw new IOException("synthetic download failure");
            File.Copy(source,destination,true);return Task.FromResult(new MusicDownload("로컬 합성 음악",20));
        }
        using(var cache=new MusicDownloadCache(root,"",Download)) {
            var first=cache.PrepareAsync("11111111111");MusicAwait(first);string path=first.GetAwaiter().GetResult().Path;
            var again=cache.PrepareAsync("11111111111");MusicAwait(again);
            Assert(downloads==1 && again.Result.Path==path && File.Exists(path),"completed music downloads are reused without network or browser playback");
            bool failed=false;try { MusicAwait(cache.PrepareAsync("99999999999")); }catch(IOException) { failed=true; }
            Assert(failed && File.Exists(path) && !File.Exists(Path.Combine(root,"MusicCache","99999999999.json")),"failed downloads never mark an incomplete song as cached or delete previous audio");
            bool invalid=false;try { MusicAwait(cache.PrepareAsync("../escape")); }catch(ArgumentException) { invalid=true; }
            Assert(invalid,"music cache rejects paths disguised as video IDs");
            var saved=cache.Library().Single();
            Assert(saved.Path==path && saved.Title=="로컬 합성 음악" && saved.Duration==20 && saved.DownloadedAt!=null,"the music store keeps each download's file, title, length and time");
            Assert(MusicDownloadCache.ParseDownload("{\"title\":\"곡\",\"duration\":12.5}") is {Title:"곡",Duration:12.5} && MusicDownloadCache.ParseDownload("{\"title\":\"곡\",\"duration\":null}") is {Duration:null} && MusicDownloadCache.ParseDownload("NA")==null,"the downloader's title and length are read and a missing length stays unknown");
            File.WriteAllText(Path.Combine(root,"MusicCache","33333333333.json"),"{\"VideoId\":\"33333333333\",\"Title\":\"예전 곡\",\"Length\":3}");File.WriteAllBytes(Path.Combine(root,"MusicCache","33333333333.m4a"),new byte[3]);
            Assert(cache.Library().Single(file=>file.Title=="예전 곡").Duration==null,"downloads saved before lengths existed still load with an unknown length");
            MusicAwait(cache.ForgetAsync("33333333333"));
            Assert(!File.Exists(Path.Combine(root,"MusicCache","33333333333.m4a")) && cache.Library().Count==1,"forgetting a song deletes its audio and details");
        }
        static byte[] Box(string type,params byte[][] children) {
            var body=children.SelectMany(child=>child).ToArray();var box=new byte[8+body.Length];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(box,box.Length);System.Text.Encoding.ASCII.GetBytes(type,box.AsSpan(4));body.CopyTo(box,8);return box;
        }
        byte[] Movie(bool fragmented)=>Box("moov",fragmented?[Box("mvex"),Box("trak",Box("edts",Box("elst")),Box("mdia"))]:[Box("trak",Box("edts",Box("elst")),Box("mdia"))]);
        string edited=Path.Combine(root,"edited.m4a");byte[] start=Box("ftyp"),end=Box("mdat",System.Text.Encoding.ASCII.GetBytes("edts"));
        File.WriteAllBytes(edited,[..start,..Movie(true),..end]);long editedLength=new FileInfo(edited).Length;
        Assert(MusicDownloadCache.DropEditList(edited),"a fragmented download's edit list is found");
        string editedText=System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(edited));
        Assert(new FileInfo(edited).Length==editedLength && !editedText[..^12].Contains("edts") && editedText.Contains("elst") && editedText.EndsWith("edts"),"only the edit list box is renamed, so Windows plays the song from its start and the saved length still matches");
        Assert(!MusicDownloadCache.DropEditList(edited),"a repaired song is left alone");
        File.WriteAllBytes(edited,[..start,..Movie(false),..end]);
        Assert(!MusicDownloadCache.DropEditList(edited) && System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(edited))[..^12].Contains("edts"),"an ordinary M4A keeps its edit list");
        string moved=Path.Combine(root,"adopt");string movedPlugin=Path.Combine(moved,"plugins","music"),movedData=Path.Combine(moved,"plugin-data","facet.music");
        Directory.CreateDirectory(Path.Combine(movedPlugin,"MusicCache"));Directory.CreateDirectory(movedData);File.WriteAllText(Path.Combine(movedPlugin,"MusicCache","a.txt"),"x");
        Directory.CreateDirectory(Path.Combine(moved,"MusicCache"));
        MusicDownloadCache.AdoptOldFolders(movedPlugin,movedData);
        Assert(File.Exists(Path.Combine(movedData,"MusicCache","a.txt")) && !Directory.Exists(Path.Combine(movedPlugin,"MusicCache")),"downloads in the plugin folder move to the data folder");
        MusicDownloadCache.AdoptOldFolders(movedPlugin,movedData);
        Assert(Directory.Exists(Path.Combine(moved,"MusicCache")) && File.Exists(Path.Combine(movedData,"MusicCache","a.txt")),"an existing data folder is never overwritten by an older one");
        Directory.Delete(moved,true);Directory.CreateDirectory(Path.Combine(moved,"MusicCache"));File.WriteAllText(Path.Combine(moved,"MusicCache","b.txt"),"x");Directory.CreateDirectory(movedData);Directory.CreateDirectory(movedPlugin);
        MusicDownloadCache.AdoptOldFolders(movedPlugin,movedData);
        Assert(File.Exists(Path.Combine(movedData,"MusicCache","b.txt")),"downloads from before the plugin move to the data folder");
        string folderName="합성 곡 #1.m4a",folderId=YouTubePlaylist.LocalPrefix+folderName,musicFolder=Path.Combine(root,"Music");
        foreach(string bad in new[]{"local:../a.mp3","local:..\\a.mp3","local:C:\\a.mp3","local:a.exe","local:.mp3","local:"})Assert(!YouTubePlaylist.ValidTrackId(bad),"a music folder track cannot name anything outside the folder: "+bad);
        using(var folderTransport=new LocalMusicTransport(new MusicDownloadCache(root,"",Download),muteForChecks:true)) {
            Assert(folderTransport.LocalFiles().Count==0,"a missing music folder lists nothing");
            Assert(folderTransport.OpenLocalFolder()==musicFolder && Directory.Exists(musicFolder),"the music folder is created when opened");
            File.Copy(source,Path.Combine(musicFolder,folderName));File.WriteAllText(Path.Combine(musicFolder,"memo.txt"),"x");File.WriteAllBytes(Path.Combine(musicFolder,"empty.mp3"),[]);
            Assert(folderTransport.LocalFiles().SequenceEqual([folderName]),"only playable audio files are listed from the music folder");
            var (folderLibrary,folderMusic)=NewMusic(folderTransport);using var folderService=folderMusic;
            var folderPanel=new DashboardMusicPanel();folderPanel.Connect(folderService,folderLibrary);
            Assert(folderPanel.UrlDialog.LocalFiles!().SequenceEqual([folderName]) && folderPanel.AddFile(folderName)==null && folderLibrary.Data.Tracks.Single() is {VideoId:var added,Title:"합성 곡 #1"} && added==folderId,"choosing a folder file adds it to the playlist under its name");
            Assert(folderPanel.UrlDialog.LocalFiles!().Count==0 && folderPanel.AddFile(folderName)!=null,"a file already in the playlist is neither offered nor added again");
            MusicAwait(folderService.PlayAsync(folderId));Pump(350);MusicAwait(folderService.PollAsync());
            Assert(folderService.Playing && folderService.Title=="합성 곡 #1" && folderService.Duration>15,"a music folder file plays through the same player");
            MusicAwait(folderService.ForgetAsync(folderId));
            Assert(File.Exists(Path.Combine(musicFolder,folderName)),"removing a folder song from the playlist never deletes the user's file");
            folderPanel.Dispose();
        }
        using var transport=new LocalMusicTransport(new MusicDownloadCache(root,"",Download));
        var (library,music)=NewMusic(transport,new("11111111111","기존 제목"),new("22222222222","다음 곡"));using var controller=music;
        MusicAwait(controller.SetVolumeAsync(35));
        MusicAwait(controller.PlayAsync("11111111111"));Pump(350);MusicAwait(controller.PollAsync());
        Assert(controller.Playing && controller.Title=="로컬 합성 음악" && controller.Duration>15,"native MediaPlayer plays a completed compatible M4A and reports the actual title and duration");
        var player=(System.Windows.Media.MediaPlayer)typeof(LocalMusicTransport).GetField("player",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(transport)!;
        Assert(Math.Abs(player.Volume-.35)<.001,"native MediaPlayer receives the chosen volume before starting cached audio");
        MusicAwait(controller.SetVolumeAsync(0));Pump(100);MusicAwait(controller.PollAsync());
        Assert(player.Volume==0 && controller.Playing,"native zero volume mutes audio without pausing its timeline");
        int cachedDownloads=downloads;
        MusicAwait(controller.PauseAsync());Assert(!controller.Playing,"local playback pauses through the existing remote");
        MusicAwait(controller.PlayAsync());Pump(100);MusicAwait(controller.PollAsync());
        Assert(controller.Playing && downloads==cachedDownloads,"resuming local playback never downloads again");
        MusicAwait(controller.SeekAsync(5));Pump(250);MusicAwait(controller.PollAsync());
        Assert(controller.Position>=4,"native local audio supports seeking without browser commands");
        MusicAwait(controller.SetVolumeAsync(100));MusicAwait(controller.PlayAsync("22222222222"));
        Assert(player.Volume==1 && controller.Playing,"native volume remains at 100 percent when a new song opens");
        bool rejected=false;try {MusicAwait(transport.SetVolumeAsync(double.NaN));}catch(ArgumentException){rejected=true;}
        Assert(rejected && player.Volume==1,"native transport rejects invalid volume without altering the current level");
        using(var panel=new DashboardMusicPanel { Width=232 }) { panel.Connect(controller,library);panel.Dispose(); }
        Pump(100);MusicAwait(controller.PollAsync());Assert(controller.Playing,"disposing the overlay remote leaves native cached audio playing");
    }
    private static void CheckLatestMusicSelection(string qa)
    {
        var blocker=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake=new DelayedMusicTransport(blocker.Task);
        var (_,latestMusic)=NewMusic(fake,new("11111111111","첫 선택"),new("22222222222","최신 선택"));using var controller=latestMusic;
        Task first=controller.PlayAsync("11111111111"),latest=controller.PlayAsync("22222222222");
        blocker.SetResult();MusicAwait(Task.WhenAll(first,latest));MusicAwait(controller.PollAsync());
        Assert(controller.SelectedId=="22222222222" && controller.Playing && fake.Opens==2,"selecting another song during loading queues the latest request instead of silently dropping it");
        var pending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var removing=new DelayedMusicTransport(pending.Task);
        var (_,deleteMusic)=NewMusic(removing,new YouTubeTrack("11111111111","준비 중 삭제"));using var deleteController=deleteMusic;
        Task loading=deleteController.PlayAsync("11111111111"),deleted=deleteController.RemoveAsync("11111111111");
        pending.SetResult();MusicAwait(Task.WhenAll(loading,deleted));
        Assert(deleteController.SelectedId==null && !deleteController.Playing && removing.Pauses==1,"deleting a song while it is preparing never leaves the removed song playing after loading completes");
    }
    private sealed class DelayedMusicTransport : IMusicTransport
    {
        private readonly Task first;private string selected="";internal int Opens,Pauses;
        internal DelayedMusicTransport(Task first)=>this.first=first;
        public bool Connected { get; private set; }
        public async Task OpenAsync(string id) { if(++Opens==1)await first;selected=id;Connected=true; }
        public Task<BrowserMusicState> ReadAsync()=>Task.FromResult(new BrowserMusicState(selected,"선택 음악",true,false,false,1,60));
        public Task PlayAsync()=>Task.CompletedTask;public Task PauseAsync() { Pauses++;return Task.CompletedTask; }public Task SeekAsync(double seconds)=>Task.CompletedTask;public void Dispose()=>Connected=false;
        public Task SetVolumeAsync(double volume)=>Task.CompletedTask;
    }
    private static void CheckMusicDownloadProbe(string qa)
    {
        var start=MusicDownloadCache.StartInfo(Path.Combine(AppContext.BaseDirectory,"Tools"),"M7lc1UVf-VE",Path.Combine(qa,"should-not-download.m4a"),metadataOnly:true);
        using var process=System.Diagnostics.Process.Start(start)!;
        var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();MusicAwait(process.WaitForExitAsync(),60000);MusicAwait(output);MusicAwait(errors);
        Assert(process.ExitCode==0 && output.Result.Contains("Embedded Web Player Customization") && !File.Exists(Path.Combine(qa,"should-not-download.m4a")),"official yt-dlp and installed Node can resolve a compatible YouTube audio format without downloading copyrighted test media: "+(process.ExitCode==0?"":errors.Result));
    }
    private static void CheckDownloadedMusicLive(string qa)
    {
        string root=Path.Combine(qa,"downloaded-test-video");
        using var cache=new MusicDownloadCache(root,Path.Combine(AppContext.BaseDirectory,"Tools"));
        var request=cache.PrepareAsync("YE7VzlLtp-4");MusicAwait(request,120000);
        var file=request.GetAwaiter().GetResult();
        Assert(File.Exists(file.Path) && new FileInfo(file.Path).Length>1000,"official downloader saves a complete M4A for the Creative Commons Blender test fixture");
        using var local=new LocalMusicTransport(cache,muteForChecks:true);MusicAwait(local.OpenAsync("YE7VzlLtp-4"));Pump(200);
        var state=local.ReadAsync().GetAwaiter().GetResult();
        Assert(state.Playing && state.Duration>1 && state.Title.Contains("Big Buck Bunny"),"the actual downloaded test audio plays natively without a browser or FFmpeg conversion");
    }
}
