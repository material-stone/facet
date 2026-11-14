using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Facet;

internal static partial class Program
{
    private static void CheckCachedShadows(string qa)
    {
        var hud=new OverlayTabs {Opacity=1};var tabs=(TabControl)hud.FindName("Tabs");tabs.SelectedItem=hud.FindName("ManagementTab");
        var carousel=(OverlayCarousel)hud.FindName("ManagementCarousel");carousel.AnimateTransitions=false;
        var comparisons=new List<object>();hud.AnimateDayTransitions=false;
        // Sizes where the default tile clears the tab strip; a smaller window puts the strip's shadow over the tile.
        foreach(var size in new[]{new Size(1440,900),new Size(1920,1080)}) {
            hud.Measure(size);hud.Arrange(new Rect(size));hud.UpdateLayout();
            var shadows=VisualElements(hud).OfType<CachedShadow>().ToArray();
            Assert(shadows.Length==5 && shadows.All(s=>s.Child==null && !s.IsHitTestVisible && s.CacheMode==null),"five cached shadows contain only noninteractive empty background siblings at "+size+" (found "+string.Join(",",shadows.Select(s=>s.Name))+"; selected "+tabs.SelectedIndex+")");
            foreach(int selected in new[]{0,1}) {
                if(selected==0)hud.ReturnToCalendar();else hud.OpenDay(DateOnly.FromDateTime(DateTime.Now));
                hud.UpdateLayout();
                BitmapSource Render() {hud.UpdateLayout();var result=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);result.Render(hud);return result;}
                foreach(var shadow in shadows)shadow.UseLiveEffect=true;
                var live=Render();foreach(var shadow in shadows)shadow.UseLiveEffect=false;
                var cached=Render();int stride=(int)size.Width*4;var a=new byte[stride*(int)size.Height];var b=new byte[a.Length];live.CopyPixels(a,stride,0);cached.CopyPixels(b,stride,0);
                long delta=0;int max=0,changed=0;
                for(int i=0;i<a.Length;i++) {int difference=Math.Abs(a[i]-b[i]);delta+=difference;max=Math.Max(max,difference);if(difference!=0)changed++;}
                var boxes=shadows.Select(s=> {var box=s.TransformToAncestor(hud).TransformBounds(new Rect(s.RenderSize));box.Inflate(2,2);return box;}).ToArray();
                var shadowDeltas=new List<int>();
                for(int y=0;y<(int)size.Height;y++)for(int x=0;x<(int)size.Width;x++) {
                    int alpha=(y*(int)size.Width+x)*4+3;
                    if((a[alpha]!=0 || b[alpha]!=0) && !boxes.Any(box=>box.Contains(new Point(x+.5,y+.5))))shadowDeltas.Add(Math.Abs(a[alpha]-b[alpha]));
                }
                comparisons.Add(new {size=$"{size}",selected,meanChannelDelta=(double)delta/a.Length,maxChannelDelta=max,changedChannels=changed,
                    shadowPixels=shadowDeltas.Count,shadowMeanAlphaDelta=shadowDeltas.DefaultIfEmpty().Average(),shadowMaxAlphaDelta=shadowDeltas.DefaultIfEmpty().Max()});
                Assert((double)delta/a.Length<.2 && shadowDeltas.Count>0 && shadowDeltas.Average()<2 && shadowDeltas.Max()<=20,
                    "shadow appearance stays within subpixel resampling tolerance at "+size+" item "+selected);
                foreach(var content in new FrameworkElement[]{(FrameworkElement)hud.FindName("Calendar"),(FrameworkElement)hud.FindName("DayEditor"),(FrameworkElement)tabs.Template.FindName("TabStrip",tabs)}) {
                    bool visible=true;
                    for(DependencyObject? node=content;node!=null && !ReferenceEquals(node,hud);node=VisualTreeHelper.GetParent(node))
                        if(node is UIElement element && element.Visibility!=Visibility.Visible) {visible=false;break;}
                    if(!visible)continue;
                    if(content.ActualWidth<=0 || content.ActualHeight<=0)continue;
                    var first=content.TranslatePoint(new Point(),hud);var last=content.TranslatePoint(new Point(content.ActualWidth,content.ActualHeight),hud);
                    int inset=Math.Min(24,(int)Math.Floor(Math.Min(last.X-first.X,last.Y-first.Y)/4));
                    var area=new Int32Rect((int)Math.Ceiling(first.X)+inset,(int)Math.Ceiling(first.Y)+inset,(int)Math.Floor(last.X)-(int)Math.Ceiling(first.X)-inset*2,(int)Math.Floor(last.Y)-(int)Math.Ceiling(first.Y)-inset*2);
                    int right=Math.Min((int)size.Width,area.X+area.Width),bottom=Math.Min((int)size.Height,area.Y+area.Height);
                    int left=Math.Max(0,area.X),top=Math.Max(0,area.Y);
                    if(right<=left || bottom<=top)continue;
                    area=new Int32Rect(left,top,right-left,bottom-top);
                    var liveText=new byte[area.Width*area.Height*4];var cachedText=new byte[liveText.Length];
                    live.CopyPixels(area,liveText,area.Width*4,0);cached.CopyPixels(area,cachedText,area.Width*4,0);
                    Assert(liveText.SequenceEqual(cachedText),"prerendering preserves exact foreground pixels in "+content.GetType().Name+" at "+size+" item "+selected);
                }
                void Save(BitmapSource bitmap,string name) {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(qa,name));encoder.Save(file);}
                Save(live,$"shadow-live-{size.Width}-{selected}.png");Save(cached,$"shadow-cached-{size.Width}-{selected}.png");
                if(size.Width==1440 && selected==0) {
                    BitmapSource Gray(BitmapSource image) {var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(93,104,113)),null,new Rect(size));dc.DrawImage(image,new Rect(size));}var result=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);result.Render(visual);return result;}
                    Save(Gray(live),"shadow-live-gray.png");Save(Gray(cached),"shadow-cached-gray.png");
                }
                int builds=shadows.Sum(s=>s.CacheBuildCount);
                for(int i=0;i<8;i++)Render();
                Assert(shadows.Sum(s=>s.CacheBuildCount)==builds,"steady calendar/day rendering reuses every shadow bitmap at "+size+" item "+selected);
            }
            Assert(VisualElements(hud).Where(e=>e is TextBlock or TextBox or OverlayCalendarView or OverlayDayEditor).All(e=>e.CacheMode==null && e.Effect==null),
                "foreground text and controls keep direct rendering without bitmap cache or effects at "+size);
        }
        File.WriteAllText(Path.Combine(qa,"shadow-comparison.json"),JsonSerializer.Serialize(comparisons,new JsonSerializerOptions{WriteIndented=true}));

        var brush=new SolidColorBrush(Color.FromRgb(30,39,43));
        var sample=new CachedShadow {Width=200,Height=120,CornerRadius=new CornerRadius(20),Background=brush,
            Shadow=new DropShadowEffect {Color=Colors.Black,Opacity=.42,BlurRadius=24,ShadowDepth=6,Direction=270}};
        var root=new Canvas {Width=320,Height=240};Canvas.SetLeft(sample,40);Canvas.SetTop(sample,40);root.Children.Add(sample);
        void Paint(){root.Measure(new Size(320,240));root.Arrange(new Rect(0,0,320,240));root.UpdateLayout();var image=new RenderTargetBitmap(320,240,96,96,PixelFormats.Pbgra32);image.Render(root);}
        Paint();int count=sample.CacheBuildCount;for(int i=0;i<5;i++)Paint();
        Assert(count==1 && sample.CacheBuildCount==count && sample.CacheBytes>0,$"steady rendering retains one frozen shadow image (initial={count}, current={sample.CacheBuildCount}, bytes={sample.CacheBytes})");
        sample.Width=220;Paint();Assert(sample.CacheBuildCount==++count,"shadow cache rebuilds after geometry changes");
        brush.Color=Colors.DarkSlateGray;Paint();Assert(sample.CacheBuildCount==++count,"shadow cache rebuilds after a mutable background color change");
        sample.CornerRadius=new CornerRadius(12);Paint();Assert(sample.CacheBuildCount==++count,"shadow cache rebuilds after corner changes");
        sample.Shadow!.Opacity=.5;Paint();Assert(sample.CacheBuildCount==++count,"shadow cache rebuilds after mutable effect changes");
        foreach(double scale in new[]{1,1.25,2}) {
            var image=sample.RenderBackground(new Size(220,120),new DpiScale(scale,scale),out var bounds);
            Assert(image.IsFrozen && Math.Abs(image.PixelWidth/scale-bounds.Width)<.001 && bounds.X<0 && bounds.Y<0,"shadow image includes effect padding at DPI scale "+scale);
        }
    }

    private static void ProfileShadows(string qa)
    {
        var hud=new OverlayTabs {Opacity=1};((TabControl)hud.FindName("Tabs")).SelectedItem=hud.FindName("ManagementTab");
        var carousel=(OverlayCarousel)hud.FindName("ManagementCarousel");carousel.AnimateTransitions=false;
        var watch=Stopwatch.StartNew();hud.Measure(new Size(1440,900));hud.Arrange(new Rect(0,0,1440,900));hud.UpdateLayout();
        var shadows=VisualElements(hud).OfType<CachedShadow>().ToArray();
        var bitmap=new RenderTargetBitmap(1440,900,96,96,PixelFormats.Pbgra32);var pixel=new byte[4];
        void Render(){bitmap.Clear();bitmap.Render(hud);bitmap.CopyPixels(new Int32Rect(720,450,1,1),pixel,4,0);}
        Render();double coldMs=watch.Elapsed.TotalMilliseconds;
        var runs=new Dictionary<string,Cost>();
        foreach(bool live in new[]{true,false}) {
            foreach(var shadow in shadows)shadow.UseLiveEffect=live;
            hud.UpdateLayout();
            var samples=new List<double>();
            for(int i=-5;i<60;i++) {watch.Restart();Render();if(i>=0)samples.Add(watch.Elapsed.TotalMilliseconds);}
            runs[live?"liveEffect":"prerendered"]=Costs(samples);
        }
        var result=new {kind="1440x900 offscreen software CPU, same HUD and shadow parameters, not GPU FPS",runs,coldFirstLayoutAndRenderMs=coldMs,
            shadowBakeMs=shadows.Sum(s=>s.CacheBuildMilliseconds),cacheBytes=shadows.Sum(s=>s.CacheBytes),cacheBuilds=shadows.Sum(s=>s.CacheBuildCount)};
        File.WriteAllText(Path.Combine(qa,"shadow-profile.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
