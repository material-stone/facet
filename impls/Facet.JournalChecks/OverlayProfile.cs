using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Facet;
using Facet.Core;

internal static partial class Program
{
    private sealed record Cost(int Samples,double MeanMs,double P50Ms,double P95Ms,double MaxMs);
    private static Cost Costs(IEnumerable<double> values) {
        var sorted=values.Order().ToArray();return new(sorted.Length,sorted.Average(),sorted[(sorted.Length-1)/2],sorted[(int)Math.Ceiling(sorted.Length*.95)-1],sorted[^1]);
    }
    private static IEnumerable<UIElement> VisualElements(DependencyObject parent) {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is UIElement element)yield return element;
            foreach(var nested in VisualElements(child))yield return nested;
        }
    }
    private static BitmapSource SyntheticBackdrop(int width,int height) {
        var pixels=new byte[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++) {
            byte value=(byte)(((x/32+y/32)%2)*180+40);int offset=(y*width+x)*4;
            pixels[offset]=value;pixels[offset+1]=(byte)(value*.8);pixels[offset+2]=(byte)(value*.6);pixels[offset+3]=255;
        }
        var image=BitmapSource.Create(width,height,96,96,PixelFormats.Pbgra32,null,pixels,width*4);image.Freeze();return image;
    }

    private static void ProfileOverlay(Controller controller,string qa)
    {
        var layers=(Grid)controller.Overlay.Content;var hud=(OverlayTabs)layers.Children[2];
        ((TabControl)hud.FindName("Tabs")).SelectedItem=hud.FindName("ManagementTab");hud.Opacity=1;
        var carousel=(OverlayCarousel)hud.FindName("ManagementCarousel");carousel.AnimateTransitions=false;carousel.Select(0,false);
        var size=new Size(1440,900);layers.Measure(size);layers.Arrange(new Rect(size));layers.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1440,900,96,96,PixelFormats.Pbgra32);var pixel=new byte[4];
        void Render(Visual visual) {bitmap.Clear();bitmap.Render(visual);bitmap.CopyPixels(new Int32Rect(720,450,1,1),pixel,4,0);}
        var runs=new Dictionary<string,object>();
        void Run(string name,Visual visual) {
            carousel.Select(0,false);carousel.BeginDrag(new Point(400,200),carousel.SelectedContent);
            var input=new List<double>();var transform=new List<double>();var layout=new List<double>();var render=new List<double>();
            for(int i=-5;i<40;i++) {
                long start=Stopwatch.GetTimestamp();carousel.ContinueDrag(new Point(360-Math.Max(0,i)%50,200));
                double inputMs=Stopwatch.GetElapsedTime(start).TotalMilliseconds;start=Stopwatch.GetTimestamp();
                carousel.RenderPendingDrag();double transformMs=Stopwatch.GetElapsedTime(start).TotalMilliseconds;start=Stopwatch.GetTimestamp();
                layers.UpdateLayout();double layoutMs=Stopwatch.GetElapsedTime(start).TotalMilliseconds;start=Stopwatch.GetTimestamp();
                Render(visual);double renderMs=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if(i>=0) {input.Add(inputMs);transform.Add(transformMs);layout.Add(layoutMs);render.Add(renderMs);}
            }
            carousel.CompleteDrag(new Point(400,200));
            runs[name]=new {input=Costs(input),transforms=Costs(transform),steadyLayout=Costs(layout),render=Costs(render)};
        }
        var shadows=VisualElements(hud).OfType<CachedShadow>().ToArray();
        Run("hudPrerenderedShadows",hud);
        foreach(var shadow in shadows)shadow.UseLiveEffect=true;
        Run("hudOriginalLiveShadows",hud);
        foreach(var shadow in shadows)shadow.UseLiveEffect=false;
        var shadowEffects=shadows.Select(s=>s.Shadow).ToArray();
        foreach(var shadow in shadows)shadow.Shadow=null;
        Run("hudWithoutShadows",hud);
        for(int i=0;i<shadows.Length;i++)shadows[i].Shadow=shadowEffects[i];

        var behind=(Image)layers.Children[0];var tint=(SolidColorBrush)((Border)layers.Children[1]).Background;
        var priorSource=behind.Source;var priorEffect=behind.Effect;double priorOpacity=behind.Opacity,priorTint=tint.Opacity;
        behind.Source=SyntheticBackdrop(1280,800);behind.Opacity=1;tint.Opacity=.78;behind.Effect=null;
        Run("sceneUnblurred",layers);
        behind.Effect=new BlurEffect {Radius=18,RenderingBias=RenderingBias.Performance};
        Run("sceneBlur18",layers);
        // Same apparent blur radius, bounded intermediate surface before upscaling.
        layers.Children.Remove(behind);behind.Width=640;behind.Height=400;behind.Source=SyntheticBackdrop(640,400);
        behind.Effect=new BlurEffect {Radius=8,RenderingBias=RenderingBias.Performance};
        var blurHost=new Viewbox {Stretch=Stretch.Fill,Child=behind};layers.Children.Insert(0,blurHost);layers.UpdateLayout();
        Run("experimentalBlur640",layers);
        layers.Children.Remove(blurHost);blurHost.Child=null;behind.ClearValue(FrameworkElement.WidthProperty);behind.ClearValue(FrameworkElement.HeightProperty);
        layers.Children.Insert(0,behind);behind.Source=priorSource;behind.Effect=priorEffect;behind.Opacity=priorOpacity;tint.Opacity=priorTint;
        layers.UpdateLayout();

        var forced=new List<double>();
        for(int i=0;i<40;i++) {var watch=Stopwatch.StartNew();hud.InvalidateMeasure();hud.Measure(size);hud.Arrange(new Rect(size));hud.UpdateLayout();forced.Add(watch.Elapsed.TotalMilliseconds);}
        string markdown="# 오늘의 기록\n\n"+string.Concat(Enumerable.Repeat("- **할 일**: 마크다운 미리보기와 [링크](https://example.invalid)\n",20));
        var markdownCosts=new List<double>();
        OverlayNoteMarkdown.Render(markdown);
        for(int i=0;i<40;i++) {var watch=Stopwatch.StartNew();_ = OverlayNoteMarkdown.Render(markdown);markdownCosts.Add(watch.Elapsed.TotalMilliseconds);}
        var saveCosts=new List<double>();var data=new AppData();
        for(int i=0;i<20;i++) {
            CalendarNotes.Save(data,new CalendarDayEntry {Date=new DateOnly(2026,10,24),Note=markdown+i},DateTime.Now);
            var watch=Stopwatch.StartNew();controller.Store.Save(data);saveCosts.Add(watch.Elapsed.TotalMilliseconds);
        }
        var result=new {kind="Offscreen WPF CPU; synthetic data; not GPU/presentation FPS",size="1440x900",renderTier=RenderCapability.Tier>>16,
            shadowCount=shadows.Length,runs,forcedLayout=Costs(forced),markdownChars=markdown.Length,markdownBuild=Costs(markdownCosts),encryptedSave=Costs(saveCosts)};
        File.WriteAllText(Path.Combine(qa,"overlay-components.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(result));
    }

    private static void ProfileBackdrop(string qa)
    {
        using var host=new KeyboardHost();host.Window.Show();Native.CoverScreen(host.Window,true);Pump(250);
        var bounds=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(host.Window).Handle).Bounds;
        using var capture=new BackdropCapture {MeasureUpdates=true};
        var runs=new Dictionary<string,object>();
        foreach(int mode in new[]{4,3}) {
            capture.Update(bounds);
            var flags=BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
            var memory=(IntPtr)typeof(BackdropCapture).GetField("memory",flags)!.GetValue(capture)!;
            typeof(BackdropCapture).GetMethod("SetStretchBltMode",flags)!.Invoke(null,[memory,mode]);
            var samples=new List<BackdropCapture.Timings>();
            for(int i=-3;i<40;i++) {
                Assert(capture.Update(bounds) && capture.LastTimings.HasValue,"synthetic backdrop capture produces opt-in timing sample");
                if(i>=0)samples.Add(capture.LastTimings!.Value);Pump(45);
            }
            runs[mode==4?"halftone":"colorOnColor"]=new {
                acquire=Costs(samples.Select(s=>s.AcquireMs)),setup=Costs(samples.Select(s=>s.SetupMs)),stretch=Costs(samples.Select(s=>s.StretchMs)),
                flush=Costs(samples.Select(s=>s.FlushMs)),write=Costs(samples.Select(s=>s.WriteMs)),release=Costs(samples.Select(s=>s.ReleaseMs)),total=Costs(samples.Select(s=>s.TotalMs))};
        }
        var result=new {kind="Native GDI capture of synthetic pattern only; no pixels saved",screen=new{bounds.Width,bounds.Height},
            frame=new{capture.Frame!.PixelWidth,capture.Frame.PixelHeight},runs};
        File.WriteAllText(Path.Combine(qa,"backdrop-components.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(result));
    }

    private static void ProfileAsyncBackdrop(Controller controller,string qa)
    {
        using var host=new KeyboardHost();host.Window.Show();Native.CoverScreen(host.Window,true);Pump(250);
        var overlay=controller.Overlay;var layers=(Grid)overlay.Content;var hud=(OverlayTabs)layers.Children[2];
        ((TabControl)hud.FindName("Tabs")).SelectedItem=hud.FindName("ManagementTab");
        controller.Commit(data=> {data.Settings.OverlayBlurRadius=18;data.Settings.OverlayShowMilliseconds=0;data.Settings.OverlayHideMilliseconds=0;});
        var captures=new List<BackdropCapture.Timings>();var revealCosts=new List<double>();var workers=new List<int>();
        var priorFactory=overlay.BackdropSourceFactory;
        BackdropCaptureSession? session=null;
        try {
            // Each opening owns one frame; never request more frames from a visible overlay.
            for(int i=0;i<12;i++) {
                var source=new BackdropCapture {MeasureUpdates=true};overlay.BackdropSourceFactory=()=>source;
                var reveal=Stopwatch.StartNew();
                controller.ToggleOverlay();
                session=(BackdropCaptureSession)typeof(OverlayWindow).GetField("capture",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(overlay)!;
                AwaitBackdrop(()=>overlay.IsVisible && session.Stopped.IsCompleted);
                Assert(session.CapturedFrames==1 && session.PresentedFrames==1 && session.BufferAllocations==1,"one native background frame and buffer per opening");
                captures.Add(source.LastTimings!.Value);revealCosts.Add(reveal.Elapsed.TotalMilliseconds);workers.Add(session.WorkerThreadId);
                if(i<11)controller.HideOverlay();
            }
            var measured=captures.Skip(2).ToArray();
            Assert(measured.Length==10,"async native snapshot profile receives two warmup plus ten opening samples");
            Assert(workers.All(worker=>worker!=Environment.CurrentManagedThreadId),"native snapshot source runs on its owner worker while UI applies pixels");
            var bounds=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(overlay).Handle).Bounds;
            var image=(Image)layers.Children[0];var bitmap=(WriteableBitmap)image.Source;
            var watch=Stopwatch.StartNew();session!.Dispose();double disposeMs=watch.Elapsed.TotalMilliseconds;
            var result=new {kind="One async GDI snapshot per opening on synthetic background; not streaming or GPU FPS",
                screen=new{bounds.Width,bounds.Height},frame=new{bitmap.PixelWidth,bitmap.PixelHeight},
                captureWorker=Costs(measured.Select(s=>s.TotalMs)),stretchWorker=Costs(measured.Select(s=>s.StretchMs)),
                openingToReveal=Costs(revealCosts.Skip(2)),
                bufferAllocations=session.BufferAllocations,session.CapturedFrames,session.PresentedFrames,disposeMs,
                workerThreads=workers,uiThread=Environment.CurrentManagedThreadId,blur=((BlurEffect)image.Effect).Radius};
            File.WriteAllText(Path.Combine(qa,"async-backdrop-components.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine(JsonSerializer.Serialize(result));
        }
        finally {controller.HideOverlay();if(session!=null)AwaitBackdrop(()=>session.Stopped.IsCompleted);overlay.BackdropSourceFactory=priorFactory;}
    }
}
