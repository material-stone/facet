using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Facet;

internal static partial class Program
{
    private static void CheckDashboard(string qa)
    {
        var hud = new OverlayTabs();
        var dashboard = (OverlayDashboardView)hud.FindName("Dashboard");
        var viewport = (Grid)hud.FindName("DashboardViewport");
        var tabs = (TabControl)hud.FindName("Tabs");
        var date = (TextBlock)dashboard.FindName("DateText");
        var time = (TextBlock)dashboard.FindName("TimeText");
        var timeHost = (Viewbox)dashboard.FindName("TimeHost");
        var agenda = (FrameworkElement)dashboard.FindName("AgendaSection");
        var weather = (StackPanel)dashboard.FindName("WeatherGroup");
        var group = (StackPanel)dashboard.FindName("ClockGroup");
        foreach (var pair in new[] { (new DateTime(2026,10,24,0,0,0),"오전 12:00:00"),
            (new DateTime(2026,10,24,12,0,0),"오후 12:00:00"), (new DateTime(2026,10,24,9,8,7),"오전 09:08:07") }) {
            dashboard.UpdateClock(pair.Item1);
            Assert(date.Text == "2026년 10월 24일 토요일" && time.Text == pair.Item2,"dashboard formats local date weekday and AM PM clock at " + pair.Item1);
        }
        dashboard.UpdateClock(new DateTime(2027,1,1,0,0,0));
        Assert(date.Text == "2027년 1월 1일 금요일","dashboard clock changes date and weekday across year boundary");
        dashboard.UpdateClock(new DateTime(2026,10,24,9,8,7));
        Assert(((TextBlock)dashboard.FindName("TemperatureText")).Text=="--°C" && ((TextBlock)dashboard.FindName("HumidityText")).Text=="--%" && ((System.Windows.Documents.Run)dashboard.FindName("WeatherStatus")).Text.Contains("대기"),"pending weather stays unknown instead of inventing values");
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(hud).TransformBounds(new Rect(0,0,element.ActualWidth,element.ActualHeight));
        // The clock face keeps its 300x108 ratio at the largest fit and sits at the chosen one of nine spots.
        var face=new Border{Width=300,Height=108};string saved="";
        var fit=new Facet.Widgets.ClockFitButton(face,()=>hud.WidgetModalFor("check"),value=>{saved=value;return true;});
        Rect Fit(Size size){fit.Measure(size);fit.Arrange(new Rect(size));fit.UpdateLayout();return face.TransformToAncestor(fit).TransformBounds(new Rect(0,0,300,108));}
        fit.Alignment="center-center";var shortTile=Fit(new Size(600,108));
        Assert(Math.Abs(shortTile.Width-300)<.5 && Math.Abs(shortTile.Height-108)<.5 && Math.Abs(shortTile.Left-150)<.5,"a short wide tile fits the height and centers horizontally: "+shortTile);
        var narrowTile=Fit(new Size(150,200));
        Assert(Math.Abs(narrowTile.Width-150)<.5 && Math.Abs(narrowTile.Height-54)<.5 && Math.Abs(narrowTile.Top-73)<.5,"a narrow tall tile fits the width and keeps the ratio: "+narrowTile);
        fit.Alignment="bottom-right";var corner=Fit(new Size(600,300));
        Assert(Math.Abs(corner.Right-600)<.5 && Math.Abs(corner.Bottom-300)<.5,"bottom right reaches both edges: "+corner);
        var hoverChrome=(FrameworkElement)fit.Template.FindName("HoverChrome",fit);
        Assert(hoverChrome.Opacity==0 && ((Border)System.Windows.Media.VisualTreeHelper.GetChild(hoverChrome,0)).Opacity==.4,"the clock button's hover starts hidden and uses the weather widget's 40%");
        var chooser=(StackPanel)fit.BuildChooser();var spots=((System.Windows.Controls.Primitives.UniformGrid)chooser.Children[1]).Children.OfType<Button>().ToArray();
        Assert(spots.Length==9 && spots.Select(b=>(string)b.Tag).Distinct().Count()==9,"the chooser offers nine alignments");
        var chooserHost=new Border{Background=System.Windows.Media.Brushes.Black,Padding=new Thickness(16),Child=chooser};
        chooserHost.Measure(new Size(344,300));chooserHost.Arrange(new Rect(0,0,344,chooserHost.DesiredSize.Height));chooserHost.UpdateLayout();
        foreach(var spot in spots) {
            var label=(TextBlock)spot.Content;var inner=label.TransformToAncestor(spot).TransformBounds(new Rect(label.RenderSize));
            Assert(label.Text.Length>0 && inner.Left>=0 && inner.Top>=0 && inner.Right<=spot.ActualWidth && inner.Bottom<=spot.ActualHeight && label.ActualWidth>=label.DesiredSize.Width-.5,"the alignment button shows its whole label: "+label.Text+" "+inner);
        }
        var chooserShot=new RenderTargetBitmap(344,(int)chooserHost.ActualHeight,96,96,PixelFormats.Pbgra32);chooserShot.Render(chooserHost);
        var chooserPng=new PngBitmapEncoder();chooserPng.Frames.Add(BitmapFrame.Create(chooserShot));
        using(var chooserFile=File.Create(Path.Combine(qa,"clock-alignment-chooser.png")))chooserPng.Save(chooserFile);
        chooserHost.Child=null;
        spots[3].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(saved=="center-left" && fit.Alignment=="center-left","choosing a spot applies and saves it");
        var failing=new Facet.Widgets.ClockFitButton(new Border{Width=300,Height=108},()=>hud.WidgetModalFor("check"),_=>false);
        ((System.Windows.Controls.Primitives.UniformGrid)((StackPanel)failing.BuildChooser()).Children[1]).Children.OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(failing.Alignment==Facet.Widgets.ClockFitButton.DefaultAlignment,"a failed save keeps the old alignment");
        fit.Alignment="nonsense-value";Assert(fit.Alignment==Facet.Widgets.ClockFitButton.DefaultAlignment,"unknown saved alignment falls back");
        foreach (var size in new[] {new Size(1440,900),new Size(1366,768),new Size(1920,1080),new Size(800,600),new Size(480,360)}) {
            hud.Measure(size); hud.Arrange(new Rect(size)); hud.UpdateLayout();
            var strip=(FrameworkElement)tabs.Template.FindName("TabStrip",tabs);
            double inset=strip.Margin.Top+strip.ActualHeight;
            var viewportBounds=Bounds(viewport);
            Assert(viewportBounds==new Rect(size),"the board fills the window behind the floating menus at " + size);
            // The default layout shows at 100%, so only windows tall enough hold all of it.
            bool fits=size.Height>=900;
            Assert(!fits || Bounds(group).Top>=inset,"default widgets start below the floating tab menu at " + size);
            var dateBounds=Bounds(date);var timeBounds=Bounds(time);
            Assert(Math.Abs(dateBounds.Left-timeBounds.Left)<.1 && Math.Abs(dateBounds.Width-timeBounds.Width)<1 && dateBounds.Bottom<timeBounds.Top,"date and time occupy matching left aligned single line widths at " + size);
            var fixedTime=Bounds(timeHost);var fixedText=Bounds(time);var fixedClock=Bounds(group);var fixedWeather=Bounds(weather);var fixedAgenda=Bounds(agenda);
            foreach(var stamp in new[]{new DateTime(2026,10,24,0,0,0),new DateTime(2026,10,24,1,11,11),new DateTime(2026,10,24,8,8,8),
                new DateTime(2026,10,24,9,59,59),new DateTime(2026,10,24,10,0,0),new DateTime(2026,10,24,11,59,59),new DateTime(2026,10,24,12,0,0),new DateTime(2026,10,24,23,59,59)}) {
                dashboard.UpdateClock(stamp);hud.Measure(size);hud.Arrange(new Rect(size));hud.UpdateLayout();
                Assert(Math.Abs(Bounds(timeHost).Height-fixedTime.Height)<.01 && Math.Abs(Bounds(time).Height-fixedText.Height)<.01 &&
                    Math.Abs(Bounds(group).Height-fixedClock.Height)<.01 && Math.Abs(Bounds(weather).Top-fixedWeather.Top)<.01 && Math.Abs(Bounds(agenda).Top-fixedAgenda.Top)<.01,
                    "time frame and glyph height stay fixed across digits and AM PM without moving weather or agenda at "+size+" "+stamp.ToString("HH:mm:ss"));
            }
            dashboard.UpdateClock(new DateTime(2026,10,24,9,8,7));hud.Measure(size);hud.Arrange(new Rect(size));hud.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(hud);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
            using var output=File.Create(Path.Combine(qa,$"overlay-dashboard-{size.Width}.png"));png.Save(output);
        }
    }
}
