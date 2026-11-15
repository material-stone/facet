using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Facet;
using Facet.Widgets;
using Facet.Core;
using Facet.Design;

internal static partial class Program
{
    private static void CheckSkins(Controller controller,string qa)
    {
        Assert(JsonSerializer.Deserialize<Settings>("{}")!.Skin==Skins.DefaultId,"a stored settings file without a skin keeps the default skin");
        Assert(Skins.Resolve("removed-skin")==Skins.DefaultId && Skins.Resolve(null)==Skins.DefaultId,"unknown or missing skin ids fall back to the default skin");
        var listed=new SkinOption("listed","목록",false,new ExternalSkin("listed","목록","1.0",new Dictionary<string,Color>(),null,""));
        var gone=new SkinOption("gone","빠짐",false,new ExternalSkin("gone","빠짐","1.0",new Dictionary<string,Color>(),null,""));
        RegistryEntry Entry(string id,string version)=>new(PackageKind.Skin,id,id,version,"","","","");
        RegistryEntry[] registry=[Entry("listed","1.1"),Entry("fresh","1.0")];
        Assert(SkinsPanel.StateOf(listed,registry,true)==SkinsPanel.SkinState.Update && SkinsPanel.StateOf(gone,registry,true)==SkinsPanel.SkinState.Unsupported,"installed skins show an update chip for a newer listing and an unsupported chip when unlisted");
        Assert(SkinsPanel.StateOf(gone,registry,false)==SkinsPanel.SkinState.Current && SkinsPanel.StateOf(new SkinOption(Skins.DefaultId,"기본"),registry,true)==SkinsPanel.SkinState.Current,"an unloaded registry and the built-in skin never get a chip");
        Assert(SkinsPanel.Installable([listed,gone],registry).Select(entry=>entry.Id).SequenceEqual(["fresh"]),"only registry skins without an installed folder are offered for install");
        var tokens=new ResourceDictionary { Source=new Uri("pack://application:,,,/Facet;component/Design/Tokens.xaml") };
        var colorKeys=tokens.Keys.Cast<object>().Where(key=>tokens[key] is SolidColorBrush).ToArray();
        foreach(var option in Skins.Options.Where(option=>option.Id!=Skins.DefaultId)) {
            var skin=Skins.Load(option.Id);
            Assert(colorKeys.All(skin.Contains) && skin.Keys.Cast<object>().Count(key=>skin[key] is SolidColorBrush)==colorKeys.Length,"skin defines exactly every color token: "+option.Id);
        }
        Assert(ThemeManifest.Parse("{}") is { Background:null,Frame:null,Stars:0 },"an empty theme manifest fills no visual slots");
        Assert(ThemeManifest.Parse("{\"background\":\"../secret.png\"}")==null && ThemeManifest.Parse("{\"frame\":\"C:\\\\x.png\",\"frameSlice\":8}")==null,"theme assets cannot leave the theme folder");
        Assert(ThemeManifest.Parse("{\"stars\":9999}")==null && ThemeManifest.Parse("{\"frame\":\"f.png\"}")==null && ThemeManifest.Parse("not json")==null,"out-of-range or malformed theme manifests are rejected");
        Assert(ThemeVisuals.Load("Missing")==null,"a missing theme manifest leaves tokens only");
        Assert(Skins.LoadVisuals("space") is { NebulaSpeed:>0,Frame:null,Stars:>0 },"the space theme loads its live nebula and stars without a frame image");
        Assert(Skins.LoadVisuals("space") is { PanelMarkLength:12,PanelCornerRadius:0 } && Skins.LoadVisuals("midnight")==null,"only the space theme restyles panel surfaces");
        Assert(ThemeManifest.Parse("{}") is { TabCornerRadius:12 } && Skins.LoadVisuals("space") is { TabCornerRadius:0 } && ThemeManifest.Parse("{\"tabCornerRadius\":99}")==null,"the tab bar shape is a validated theme slot that defaults to rounded ends");
        Assert(ThemeManifest.Parse("{\"panelMarkLength\":99}")==null && ThemeManifest.Parse("{\"panelCornerRadius\":-1}")==null,"out-of-range panel style values are rejected");
        var markedPanel=new Border { CornerRadius=new(12),Width=200,Height=100 };markedPanel.Measure(new Size(200,100));markedPanel.Arrange(new Rect(0,0,200,100));
        var markedBrush=new SolidColorBrush(Colors.Cyan);markedPanel.Resources["Mint"]=markedBrush;ThemePanels.Style(markedPanel,12,2);
        Assert(markedPanel.CornerRadius.TopLeft==2 && markedPanel.BorderThickness.Left==1 && markedPanel.BorderBrush is ImageBrush { ImageSource:System.Windows.Media.Imaging.BitmapSource { Format:var markFormat } } && markFormat==PixelFormats.Indexed1,"a panel surface takes the theme's radius and pixel-exact corner marks");
        var tinySurface=new Border { CornerRadius=new(12),Width=20,Height=20 };tinySurface.Measure(new Size(20,20));tinySurface.Arrange(new Rect(0,0,20,20));tinySurface.Resources["Mint"]=markedBrush;ThemePanels.Style(tinySurface,12,2);
        Assert(tinySurface.CornerRadius.TopLeft==12 && tinySurface.BorderBrush==null,"tiny surfaces keep their own shape");
        Assert(ThemeManifest.Parse("{\"nebulaSpeed\":99}")==null && ThemeManifest.Parse("{\"nebulaBrightness\":2}")==null,"out-of-range nebula values are rejected");
        var nebula=new NebulaRenderer(.5);var frameA=new byte[NebulaRenderer.Width*NebulaRenderer.Height*4];var frameB=(byte[])frameA.Clone();
        nebula.Render(frameA,0);nebula.Render(frameB,30);
        var cost=Stopwatch.StartNew();for(int run=0;run<10;run++)nebula.Render(frameB,30+run);cost.Stop();
        Console.WriteLine($"INFO nebula frame {cost.ElapsedMilliseconds/10d:0.0} ms");
        Assert(!frameA.SequenceEqual(frameB),"the nebula changes over time");
        Assert(cost.ElapsedMilliseconds/10d<83,"a nebula frame renders within its 12 fps budget");
        var field=new NebulaField(1,.5);field.Start();int shownBefore=field.FramesShown;Pump(500);
        Assert(field.IsFlowing && field.FramesShown>shownBefore,"the nebula presents frames while flowing");
        field.Stop();Pump(150);int shownAfter=field.FramesShown;Pump(300);
        Assert(!field.IsFlowing && field.FramesShown==shownAfter,"the nebula stops rendering when stopped");
        Assert(Skins.LoadVisuals(Skins.DefaultId)==null,"the default skin has no theme visuals");
        Assert(Skins.LoadVisuals("voxel") is { HudWidgets:true,WorldScenes:["market","lakeside","cave"] } && Skins.LoadVisuals("space") is { WorldScenes:null },"only the voxel skin opens the world, in the listed order");
        Assert(ThemeManifest.Parse("{\"world\":{\"page\":\"w.html\",\"scenes\":[\"cave\"]}}") is { WorldScenes:["cave"],WorldPage:"w.html" } && ThemeManifest.Parse("{\"world\":{\"scenes\":[\"cave\"]}}")==null && ThemeManifest.Parse("{\"world\":{\"page\":\"../w.html\",\"scenes\":[\"cave\"]}}")==null && ThemeManifest.Parse("{\"world\":{\"page\":\"w.html\",\"scenes\":[]}}")==null && ThemeManifest.Parse("{\"world\":{\"page\":\"w.html\",\"scenes\":[\"a\",\"a\"]}}")==null,"a world names a page in the skin and its scenes once each");
        Assert(ThemeManifest.Parse("{\"fonts\":{\"body\":{\"file\":\"a.ttf\",\"family\":\"A\"}}}") is { BodyFont:{ File:"a.ttf" } } && ThemeManifest.Parse("{\"fonts\":{\"body\":{\"file\":\"../a.ttf\",\"family\":\"A\"}}}")==null && ThemeManifest.Parse("{\"fonts\":{\"body\":{\"file\":\"a.exe\",\"family\":\"A\"}}}")==null,"skin fonts stay in the skin folder");
        Assert(Skins.LoadVisuals("voxel") is { WorldPageHtml:{ Length:>1000 } },"the voxel skin carries its own world page");
        Assert(Skins.LoadVisuals("voxel") is { SoftHud:true } && Skins.LoadVisuals("space") is { SoftHud:false } && ThemeManifest.Parse("{\"hudStyle\":\"nope\"}")==null,"only the voxel skin asks for the soft widget form and unknown styles are refused");
        Assert(typeof(Facet.Widgets.Hud).GetField("Mono") is { IsStatic:true },"Hud.Mono stays a field, or installed plugins fail with MissingFieldException");
        string worldFolder=Path.Combine(qa,"world-skin");Directory.CreateDirectory(worldFolder);
        File.WriteAllText(Path.Combine(worldFolder,"w.html"),"<canvas></canvas>");File.WriteAllBytes(Path.Combine(worldFolder,"h.ttf"),[0]);
        var worldSkin=ExternalSkins.Parse("{\"id\":\"w\",\"name\":\"W\",\"theme\":{\"world\":{\"page\":\"w.html\",\"scenes\":[\"cave\"]},\"fonts\":{\"heading\":{\"file\":\"h.ttf\",\"family\":\"H\"}}}}",worldFolder);
        Assert(worldSkin!=null && ExternalSkins.Dictionary(worldSkin).Contains("HudHeadingFont") && ExternalSkins.Visuals(worldSkin) is { WorldPageHtml:"<canvas></canvas>" },"an installed skin brings its own world page and fonts");
        File.WriteAllText(Path.Combine(worldFolder,"w.html"),new string('x',2000000));
        Assert(ExternalSkins.Visuals(worldSkin!)==null,"a world page too large to navigate to leaves tokens only");
        Assert(WorldWeather.Key(0)=="clear" && WorldWeather.Key(2)=="partly" && WorldWeather.Key(3)=="cloud" && WorldWeather.Key(48)=="fog" && WorldWeather.Key(63)=="rain" && WorldWeather.Key(96)=="rain" && WorldWeather.Key(75)=="snow" && WorldWeather.Key(null)=="partly","weather codes pick the sky and an unknown code stays calm");
        Assert(Skins.LoadVisuals("space") is { Voyage:true,HudWidgets:true } && ThemeManifest.Parse("{}") is { Voyage:false,HudWidgets:false },"the voyage and HUD slots are on only where a theme asks");
        Assert(Skins.LoadVisuals("space")!.Options.Single().Id==ThemeVisuals.ReticleOption && ThemeManifest.Parse("{\"options\":[{\"id\":\"../x\",\"name\":\"a\"}]}")==null && ThemeManifest.Parse("{\"options\":[{\"id\":\"a\"}]}")==null,"a theme declares valid options and bad ones are rejected");
        var optionSettings=new Settings();var spaceVisuals=Skins.LoadVisuals("space")!;
        bool optionDefault=spaceVisuals.IsOptionOn(optionSettings,"space",ThemeVisuals.ReticleOption);optionSettings.SkinOptions["space.reticle"]=false;
        Assert(optionDefault && !spaceVisuals.IsOptionOn(optionSettings,"space",ThemeVisuals.ReticleOption) && JsonSerializer.Deserialize<Settings>("{}")!.SkinOptions.Count==0,"a theme option keeps its default until the user changes it");
        var sweep=ScanSweep.Of(30,5);
        Assert(sweep.Front(sweep.Delay)==0 && sweep.Front(sweep.Delay+ScanSweep.CrossSeconds)==1 && sweep.Reach(new(0,0,-25))==0 && sweep.Reach(new(0,0,-sweep.Far))==1,"the scan reaches the nearest point first and crosses the planet in a fixed time");
        Assert(!new Encounter(PlanetKind.Rock,1,5,10,0,-300,1,0,0).ClearOf(new(PlanetKind.Ice,1,5,18,0,-300,1,0,0)) && new Encounter(PlanetKind.Rock,1,5,10,0,-300,1,0,0).ClearOf(new(PlanetKind.Ice,1,5,-10,0,-300,1,0,0)),"two planets flying at once cannot share a path");
        var voyagePlan=new VoyagePlan(4);var passes=Enumerable.Range(0,2000).Select(_=>voyagePlan.NextClear()).ToArray();
        Assert(passes.All(pass=>pass.AxisDistance>=pass.Extent*VoyagePlan.MinClearance-1e-9),"no planet's path can reach the camera, however close it passes");
        var corner=new VoyageSpot(-1,1,12);
        Assert(passes.All(pass=>{var placed=pass.PlacedAt(corner,16d/9);double range=pass.Extent*corner.Reach,half=Math.Tan(27.5*Math.PI/180);return Math.Abs(placed.X/range+half)<1e-9 && Math.Abs(placed.Y/range-half*9/16)<1e-9;}),"a debug planet appears at the chosen spot");
        Assert(passes.All(pass=>pass.PlacedAt(new(0,0,VoyageSpot.MinReach),16d/9).AxisDistance>=pass.Extent*VoyagePlan.MinClearance-1e-9 && pass.PlacedAt(new(.01,0,VoyageSpot.MinReach),16d/9).AxisDistance>=pass.Extent*VoyagePlan.MinClearance-1e-9),"a debug planet aimed at the middle still misses the camera");
        Assert(passes.All(pass=>pass.StartZ<0 && pass.Speed>0) && passes.Select(pass=>pass.Kind).Distinct().Count()==Enum.GetValues<PlanetKind>().Length,"planets approach from ahead and every world type appears");
        Assert(passes.Zip(passes.Skip(1)).All(pair=>pair.First.Kind!=pair.Second.Kind),"the same world type never comes twice in a row");
        Assert(passes.Any(pass=>pass.AxisDistance<pass.Extent*2) && passes.Any(pass=>pass.AxisDistance>pass.Extent*4),"some planets skim close and others stay distant");
        var bakeCost=Stopwatch.StartNew();var baked=PlanetSurface.Bake(PlanetKind.Rock,1);bakeCost.Stop();
        Console.WriteLine($"INFO planet surface {bakeCost.ElapsedMilliseconds} ms");
        Assert(baked.Diffuse.Length==PlanetSurface.Width*PlanetSurface.Height*4 && baked.Diffuse.Distinct().Count()>16,"a planet surface bakes a varied texture");
        Assert(PlanetSurface.Bake(PlanetKind.Lava,2).Emissive!=null && baked.Emissive==null && PlanetSurface.Atmosphere(PlanetKind.Rock)==null && PlanetSurface.Atmosphere(PlanetKind.Desert)!=PlanetSurface.Atmosphere(PlanetKind.Ocean),"only lava glows, rock has no air and air color differs by world");
        var ruler=new Facet.Widgets.DayRuler { Now=TimeSpan.FromHours(12) };ruler.Measure(new Size(300,44));ruler.Arrange(new Rect(0,0,300,44));
        Assert(Math.Abs(ruler.MarkerX-150)<.01,"the day ruler's marker sits at the current share of 24 hours");
        Assert(Facet.Widgets.HudWeatherFace.FormatTemperature("7.6°C")=="+08" && Facet.Widgets.HudWeatherFace.FormatTemperature("-3°C")=="-03" && Facet.Widgets.HudWeatherFace.FormatTemperature("--°C")=="--","the HUD temperature is signed and an unknown value stays unknown");
        string preview=Path.Combine(qa,"space-theme.png"),previewRoot=Path.Combine(qa,"space-store");Directory.CreateDirectory(previewRoot);
        var previewInfo=new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true };
        foreach(string arg in new[]{"--skin-preview",previewRoot,preview})previewInfo.ArgumentList.Add(arg);
        using(var previewProcess=Process.Start(previewInfo)!) {
            string previewError=previewProcess.StandardError.ReadToEnd();
            Assert(previewProcess.WaitForExit(60000) && previewProcess.ExitCode==0 && File.Exists(preview),"the space theme draws its backdrop and tile frames "+previewError);
        }
        // Resources resolve once per process, so applying a skin needs a process that has not rendered yet.
        string root=Path.Combine(qa,"skin-store");Directory.CreateDirectory(root);
        var info=new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true };
        foreach(string arg in new[]{"--skin-app",root})info.ArgumentList.Add(arg);
        using var process=Process.Start(info)!;string error=process.StandardError.ReadToEnd();
        Assert(process.WaitForExit(60000) && process.ExitCode==0,"a saved skin recolors tokens and shared styles on the next start "+error);
    }
    // Renders the real overlay layers in a window far off screen, so a run never covers the desktop but elements still load.
    // Manual look at the real backdrop and panels: writes one screen per world to the given folder.
    // Loads an unpacked skin folder as the app would: its fonts must resolve and its page must start under the lock.
    private static int RunWorldSkinCheck(string folder)
    {
        var ui=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        var skin=ExternalSkins.Read(folder);
        if(skin==null) { Console.WriteLine("skin.json refused");return 1; }
        var tokens=ExternalSkins.Dictionary(skin);
        foreach(string key in new[]{"AppFontFamily","HudHeadingFont"}) {
            bool resolved=tokens[key] is FontFamily family && family.GetTypefaces().Any(face => face.TryGetGlyphTypeface(out var glyphs) && glyphs.CharacterToGlyphMap.ContainsKey('가'));
            Console.WriteLine(key+" hangul: "+resolved);
            if(!resolved) return 1;
        }
        if(ExternalSkins.Visuals(skin) is not { WorldScenes:{ } scenes,WorldPageHtml:{ } page }) { Console.WriteLine("no world");return 1; }
        var overlay=new Window { Left=-20000,Top=-20000,Width=800,Height=500,WindowStyle=WindowStyle.None,ShowInTaskbar=false,ShowActivated=false };overlay.Show();
        var world=new WorldBackdrop(page,scenes,_=>Task.FromException<WeatherSnapshot>(new OperationCanceledException()));
        world.DataFolderForCheck=Path.Combine(Path.GetTempPath(),"facet-world-check");
        int code=1;
        world.ProbeForCheck=result => { Console.WriteLine("page: "+result);code=0; };
        world.Failed+=() => { Console.WriteLine("page failed: "+world.FailureForCheck);code=1;ui.Shutdown(); };
        var limit=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromSeconds(12) };limit.Tick+=(_,_) => { limit.Stop();ui.Shutdown(); };limit.Start();
        ui.Dispatcher.BeginInvoke(() => world.SetVisible(true,overlay));
        ui.Run();
        world.Dispose();
        return code;
    }
    // Runs a hostile page in the real locked WebView2 and prints what it could reach.
    private static int RunWorldNetCheck()
    {
        var ui=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        const string probe="<script>(async()=>{const r={};const send=()=>chrome.webview.postMessage({type:'probe',message:JSON.stringify(r)});window.onerror=m=>{r.error=m;send()};chrome.webview.postMessage({type:'probe-start',message:''});const t=async(n,f)=>{try{await Promise.race([f(),new Promise((a,b)=>setTimeout(()=>b(0),4000))]);r[n]='OPEN'}catch(e){r[n]='blocked'}};"
            +"await t('fetch',()=>fetch('https://example.com/',{mode:'no-cors'}));await t('fetchIp',()=>fetch('http://1.1.1.1/',{mode:'no-cors'}));await t('local',()=>fetch('http://127.0.0.1:9/',{mode:'no-cors'}));"
            +"await t('image',()=>new Promise((a,b)=>{const i=new Image();i.onload=a;i.onerror=b;i.src='https://example.com/favicon.ico'}));"
            +"await t('socket',()=>new Promise((a,b)=>{const s=new WebSocket('wss://echo.websocket.org');s.onopen=a;s.onerror=()=>b();}));"
            +"const m=document.createElement('meta');m.httpEquiv='Content-Security-Policy';m.content=\"default-src *\";document.head.appendChild(m);await t('afterMeta',()=>fetch('https://example.com/',{mode:'no-cors'}));"
            +"try{r.rtc=await new Promise(a=>{const p=new RTCPeerConnection({iceServers:[{urls:'stun:stun.l.google.com:19302'}]});let c='none';p.createDataChannel('x');p.onicecandidate=e=>{if(e.candidate&&e.candidate.type==='srflx')c='OPEN'};p.createOffer().then(o=>p.setLocalDescription(o));setTimeout(()=>a(c),5000)})}catch(e){r.rtc='unavailable'}"
            +"send()})().catch(e=>chrome.webview.postMessage({type:'probe',message:'error '+e}))</script>";
        var overlay=new Window { Left=-20000,Top=-20000,Width=200,Height=200,WindowStyle=WindowStyle.None,ShowInTaskbar=false,ShowActivated=false };overlay.Show();
        var world=new WorldBackdrop(probe,["x"],_=>Task.FromException<WeatherSnapshot>(new OperationCanceledException()));
        world.DataFolderForCheck=Path.Combine(Path.GetTempPath(),"facet-world-check");
        int code=1;
        world.Failed+=() => { Console.WriteLine("failed: "+world.FailureForCheck);ui.Shutdown(); };
        world.ProbeForCheck=result => { Console.WriteLine(result);code=result.Contains("OPEN") ? 1 : 0;ui.Shutdown(); };
        var limit=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromSeconds(60) };limit.Tick+=(_,_) => { Console.WriteLine("no probe result");ui.Shutdown(); };limit.Start();
        ui.Dispatcher.BeginInvoke(() => world.SetVisible(true,overlay));
        ui.Run();
        world.Dispose();
        return code;
    }
    private static int RunVoyagePreview(string[] args)
    {
        try {
            var ui=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            foreach(string resource in new[]{"Tokens","Components","Scrolling"})ui.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri($"pack://application:,,,/Facet;component/Design/{resource}.xaml") });
            string root=args[1],output=args[2];Directory.CreateDirectory(output);
            var store=new Store(root);var data=store.Load();data.Settings.Skin="space";store.Save(data);
            using var controller=new Controller(root,Controller.ExitHotkey);
            var layers=(Grid)controller.Overlay.Content;controller.Overlay.Content=null;
            foreach(UIElement layer in layers.Children){layer.Opacity=1;layer.Visibility=Visibility.Visible;}
            ((Border)layers.Children[1]).Background=Brushes.Black;layers.Children[3].Visibility=Visibility.Collapsed;
            var host=new Border { Background=Brushes.Black,Child=layers };var size=new Size(1920,1080);
            var window=new Window { Content=host,Width=1920,Height=1080,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=-20000,Top=-20000,ShowActivated=false,ShowInTaskbar=false };window.Show();
            void Settle() { host.Measure(size);host.Arrange(new Rect(size));host.UpdateLayout();var frame=new System.Windows.Threading.DispatcherFrame();ui.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,()=>frame.Continue=false);System.Windows.Threading.Dispatcher.PushFrame(frame); }
            Settle();Settle();
            var voyage=VisualTree(host).OfType<SpaceVoyage>().First();
            foreach(var kind in Enum.GetValues<PlanetKind>()) {
                var pass=new Encounter(kind,(int)kind+5,5,14,-4,-300,1,.5,.25);
                voyage.ShowForCheck(pass,-22);Settle();
                var image=new System.Windows.Media.Imaging.RenderTargetBitmap(1920,1080,96,96,PixelFormats.Pbgra32);image.Render(host);
                var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var file=File.Create(Path.Combine(output,kind+".png"));encoder.Save(file);
            }
            void Shot(string name) {
                Settle();var image=new System.Windows.Media.Imaging.RenderTargetBitmap(1920,1080,96,96,PixelFormats.Pbgra32);image.Render(host);
                var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);
            }
            var spawned=voyage.SpawnNear();
            if(voyage.SpawnNear().Result)throw new Exception("a second debug planet started while one was baking");
            while(!spawned.IsCompleted)Settle();
            if(!spawned.Result || voyage.PlanetCount!=1)throw new Exception("the debug planet did not replace the sky");
            Shot("debug-near");
            voyage.TimeScale=99;if(voyage.TimeScale!=SpaceVoyage.MaxTimeScale)throw new Exception("the time scale was not limited");voyage.TimeScale=1;
            voyage.ShowForCheck(new(PlanetKind.Ocean,8,5,7.5,0,-300,1,.5,.25),-6);Shot("close-ocean");
            voyage.ShowForCheck(new(PlanetKind.Ice,9,5,-8,3,-300,1,.5,.25),-3);Shot("close-ice");
            voyage.ShowForCheck(new(PlanetKind.Desert,3,5,16,-5,-300,1,.5,.25),-40);
            voyage.ShowForCheck(new(PlanetKind.Lava,4,3,-14,6,-300,1,.5,.25),-30,true);
            voyage.ShowForCheck(new(PlanetKind.Ocean,5,2,2,9,-300,1,.5,.25),-60,true);
            if(voyage.PlanetCount!=3)throw new Exception("three planets did not fly together");
            Shot("trio");
            if(!voyage.Scan())throw new Exception("the scan did not start for three planets");
            voyage.SetScanTimeForCheck(1);Shot("trio-scan");voyage.SetScanTimeForCheck(60);
            if(voyage.IsScanning)throw new Exception("a scan of three planets did not clear itself");
            var scanned=new Encounter(PlanetKind.Gas,5,4,14,-4,-300,1,.5,.25);
            voyage.ShowForCheck(scanned,-22);
            if(!voyage.Scan() || voyage.Scan())throw new Exception("a scan did not start once");
            double arrival=ScanSweep.Of(new System.Windows.Media.Media3D.Vector3D(14,-4,-22).Length,scanned.Extent).Delay;
            foreach(double moment in new[]{.2,.5,1,2}) {
                voyage.SetScanTimeForCheck(arrival+moment);Settle();
                var image=new System.Windows.Media.Imaging.RenderTargetBitmap(1920,1080,96,96,PixelFormats.Pbgra32);image.Render(host);
                var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var file=File.Create(Path.Combine(output,$"scan-{moment}.png"));encoder.Save(file);
            }
            voyage.SetScanTimeForCheck(60);
            if(voyage.IsScanning)throw new Exception("a finished scan did not clear itself");
            window.Close();
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }

    private static int RunSkinPreview(string[] args)
    {
        try {
            var ui=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            foreach(string resource in new[]{"Tokens","Components","Scrolling"})ui.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri($"pack://application:,,,/Facet;component/Design/{resource}.xaml") });
            var store=new Store(args[1]);var data=store.Load();data.Settings.Skin="space";data.Workspace??=WorkspaceLayouts.LegacyDefault();store.Save(data);
            using var controller=new Controller(args[1],Controller.ExitHotkey);
            var layers=(Grid)controller.Overlay.Content;
            if(layers.Children.Count!=5 || layers.Children[4] is not Grid)throw new Exception("the theme backdrop was not added");
            controller.Overlay.Content=null;
            foreach(UIElement layer in layers.Children)layer.Opacity=1;
            ((Border)layers.Children[1]).Background=Brushes.Black;layers.Children[3].Visibility=Visibility.Collapsed;
            var host=new Border { Background=Brushes.Black,Child=layers };
            var size=new Size(1920,1080);
            var offscreen=new Window { Content=host,Width=1920,Height=1080,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=-20000,Top=-20000,ShowActivated=false,ShowInTaskbar=false };offscreen.Show();
            void Pump() { host.Measure(size);host.Arrange(new Rect(size));host.UpdateLayout();var frame=new System.Windows.Threading.DispatcherFrame();ui.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,()=>frame.Continue=false);System.Windows.Threading.Dispatcher.PushFrame(frame); }
            Pump();Pump();
            var board=VisualTree(layers).OfType<OverlayWorkspaceView>().First(view=>view.ActualWidth>0);
            // A HUD tile has its panel only when the widget declares one, so the calendar and chat stay bare (ADR 0086).
            board.HasPanel=_=>false;board.Render(WorkspaceLayouts.Current(controller.Data).Tabs.First(tab=>tab.Id==board.TabId),false);Pump();
            if(VisualTree(board).OfType<Facet.Widgets.HudPanel>().Any())throw new Exception("a HUD tile without a panel drew one");
            board.HasPanel=_=>true;board.Render(WorkspaceLayouts.Current(controller.Data).Tabs.First(tab=>tab.Id==board.TabId),false);Pump();
            if(!VisualTree(board).OfType<Facet.Widgets.HudPanel>().Any())throw new Exception("a HUD tile did not draw its panel");
            if(!VisualTree(host).OfType<Border>().Any(panel=>panel.BorderBrush is ImageBrush && panel.CornerRadius.TopLeft==0))throw new Exception("no panel took the theme panel style");
            var tabs=VisualTree(host).OfType<TabControl>().First();var tabItems=tabs.Items.OfType<TabItem>().Where(item=>item.Visibility==Visibility.Visible).ToArray();
            if(((Border)tabs.Template.FindName("TabMarks",tabs)).BorderBrush is not ImageBrush)throw new Exception("the tab bar did not take the corner marks");
            if(((CachedShadow)tabs.Template.FindName("TabShadow",tabs)).CornerRadius.TopLeft!=0 || tabItems.Any(item=>item.Tag is not CornerRadius { TopLeft:0,TopRight:0,BottomRight:0,BottomLeft:0 }))throw new Exception("the space tab bar kept rounded corners");
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(1920,1080,96,96,PixelFormats.Pbgra32);bitmap.Render(host);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var output=File.Create(args[2]);encoder.Save(output);
            offscreen.Close();
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
    private static IEnumerable<DependencyObject> VisualTree(DependencyObject root)
    {
        yield return root;
        for(int index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
            foreach(var child in VisualTree(VisualTreeHelper.GetChild(root,index))) yield return child;
    }
    private static int RunSkinApp(string[] args)
    {
        try {
            var ui=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            foreach(string resource in new[]{"Tokens","Components"})ui.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri($"pack://application:,,,/Facet;component/Design/{resource}.xaml") });
            var store=new Store(args[1]);var data=store.Load();data.Settings.Skin="midnight";store.Save(data);
            using var controller=new Controller(args[1],Controller.ExitHotkey);
            var expected=((SolidColorBrush)Skins.Load("midnight")["Panel"]).Color;
            if(((SolidColorBrush)ui.FindResource("Panel")).Color!=expected)throw new Exception("token kept the default color");
            var tip=new ToolTip { Style=(Style)ui.FindResource(typeof(ToolTip)) };
            if(((SolidColorBrush)tip.Background).Color!=expected)throw new Exception("shared style kept the default color");
            // Switching while running: tokens, shared styles, the backdrop and the settings tab all follow the saved skin.
            Color Panel()=>((SolidColorBrush)ui.FindResource("Panel")).Color;
            Color Shared()=>((SolidColorBrush)new ToolTip { Style=(Style)ui.FindResource(typeof(ToolTip)) }.Background).Color;
            var layers=(Grid)controller.Overlay.Content;
            var before=layers.Children.OfType<OverlayTabs>().Single();before.OpenSettings();before.SettingsSection="스킨";
            void Save(string skin) {
                // The parent check process holds the global hotkeys, so ApplySettings cannot re-register them here.
                if(!controller.Commit(d=>d.Settings.Skin=skin))throw new Exception("saving the skin failed: "+controller.LastError);
                controller.Overlay.SwitchSkin(controller.Data.Settings.Skin);
                var frame=new System.Windows.Threading.DispatcherFrame();ui.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,()=>frame.Continue=false);System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            Save("space");
            var space=((SolidColorBrush)Skins.Load("space")["Panel"]).Color;
            if(Panel()!=space || Shared()!=space)throw new Exception("a running switch kept the previous colors");
            var after=layers.Children.OfType<OverlayTabs>().Single();
            if(ReferenceEquals(before,after) || !after.IsSettingsActive)throw new Exception("the switch did not rebuild the overlay on the settings tab");
            if(after.SettingsSection!="스킨")throw new Exception("the switch left the skin tab of the settings");
            if(Skins.Visuals==null || layers.Children.Count!=5 || layers.Children[2] is not OverlayTabs)throw new Exception("the switch did not add the theme backdrop below the tabs");
            var overlay=controller.Overlay;
            // The settings tab may not be laid out in this window, so its rows are walked logically.
            IEnumerable<DependencyObject> Logical(DependencyObject root)=>new[]{root}.Concat(LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().SelectMany(Logical));
            CheckBox? ReticleBox(OverlayTabs tabs)=>Logical((OverlaySettingsView)tabs.FindName("SettingsView")).OfType<CheckBox>().FirstOrDefault(box=>Equals(box.Content,"중앙 조준선"));
            if(ReticleBox(after) is not { IsChecked:true })throw new Exception("the space skin's settings did not offer the reticle checked");
            var reticle=((Panel)overlay.ThemeBackdropForCheck!).Children.OfType<FlightReticle>().Single();
            if(reticle.Visibility!=Visibility.Visible)throw new Exception("the reticle did not start visible");
            if(!controller.Commit(d=>d.Settings.SkinOptions["space.reticle"]=false))throw new Exception("saving the option failed");
            overlay.ApplyThemeOptions();
            if(reticle.Visibility!=Visibility.Collapsed)throw new Exception("turning the reticle off did not hide it");
            if(!controller.Commit(d=>d.Settings.SkinOptions.Remove("space.reticle")))throw new Exception("clearing the option failed");
            overlay.ApplyThemeOptions();
            overlay.Present(false,.3,0,0,0);overlay.ApplyProgressForCheck(1);
            if(Math.Abs(overlay.ThemeBackdropForCheck!.Opacity-.3)>.001 || overlay.ThemeBackdropForCheck.Visibility!=Visibility.Visible)throw new Exception("the theme backdrop ignored the overlay transparency");
            overlay.Present(false,0,0,0,0);overlay.ApplyProgressForCheck(1);
            if(overlay.ThemeBackdropForCheck.Visibility!=Visibility.Collapsed)throw new Exception("a fully transparent overlay kept drawing the theme backdrop");
            overlay.Present(false,1,0,0,0);overlay.ApplyProgressForCheck(1);
            if(overlay.ThemeBackdropForCheck.Opacity!=1)throw new Exception("an opaque overlay did not draw the full theme backdrop");
            if(overlay.TintOpacityForCheck!=1)throw new Exception("the space skin did not draw its tint");
            Save("voxel");
            if(!overlay.HasWorldForCheck)throw new Exception("the voxel skin did not create its world");
            if(overlay.TintOpacityForCheck!=0)throw new Exception("switching to a world skin kept the previous tint over the world");
            Save(Skins.DefaultId);
            var standard=((SolidColorBrush)new ResourceDictionary { Source=new Uri("pack://application:,,,/Facet;component/Design/Tokens.xaml") }["Panel"]).Color;
            if(Panel()!=standard || Shared()!=standard)throw new Exception("returning to the default skin kept the previous colors");
            if(Skins.Visuals!=null || layers.Children.Count!=4)throw new Exception("returning to the default skin kept the theme backdrop");
            if(ReticleBox(layers.Children.OfType<OverlayTabs>().Single())!=null)throw new Exception("the default skin showed the space skin's settings");
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
}
