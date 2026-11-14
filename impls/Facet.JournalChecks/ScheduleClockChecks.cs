using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Facet;
using Facet.Core;

internal static partial class Program
{
    private static void CheckScheduleCurrentTime(string qa)
    {
        var now=new DateTime(2026,10,24,0,0,0);
        var dial=new OverlayScheduleDial {Width=800,Height=560,TimeProvider=()=>now,ViewDate=new(2026,10,24)};
        dial.Measure(new Size(800,560));dial.Arrange(new Rect(0,0,800,560));dial.UpdateLayout();
        var full=new DaySchedule {Name="하루 전체",DurationMinutes=1440};dial.SetSchedules([full]);
        foreach(var (hour,angle) in new[]{(0,0d),(6,90d),(12,180d),(18,270d)}) {
            now=new(2026,10,24,hour,0,0);dial.RefreshCurrentTime();
            Assert(System.Math.Abs(dial.CurrentTimeAngle-angle)<.000001,"current-time bar maps the 24-hour clock at "+hour);
        }
        now=new(2026,10,24,6,30,30);dial.RefreshCurrentTime();
        Assert(System.Math.Abs(dial.CurrentTimeAngle-97.625)<.000001,"current-time rotation includes minutes and seconds");
        now=new(2026,10,24,23,59,59);dial.RefreshCurrentTime();double before=dial.CurrentTimeAngle;
        now=now.AddSeconds(1);dial.RefreshCurrentTime();
        Assert(before>359.99 && dial.CurrentTimeAngle==0,"current-time bar wraps across midnight without retaining yesterday's position");
        Color Pixel(BitmapSource image,Point at) {var bytes=new byte[4];image.CopyPixels(new Int32Rect((int)at.X,(int)at.Y,1,1),bytes,4,0);return Color.FromArgb(bytes[3],bytes[2],bytes[1],bytes[0]);}
        BitmapSource Paint(Visual visual,int width=800,int height=560) {dial.UpdateLayout();var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(visual);return image;}
        var bar=((SolidColorBrush)Application.Current.FindResource("ScheduleNow")).Color;
        now=new(2026,10,24,6,0,0);dial.RefreshCurrentTime();dial.MovePointer(dial.PointAt(12,dial.Radius*.6));var image=Paint(dial);
        Assert(Pixel(image,dial.PointAt(12,dial.Radius*.6))==bar && Pixel(image,dial.PointAt(12,dial.Radius*.9))==bar,"current-time bar is visible above saved fills and hover outlines");
        SavePlannerImage(dial,new Size(800,560),Path.Combine(qa,"schedule-current-time.png"));
        dial.ViewDate=new(2026,10,25);image=Paint(dial);
        Assert(Pixel(image,dial.PointAt(12,dial.Radius*.6))!=bar,"current-time bar is hidden on days other than today");
        dial.ViewDate=new(2026,10,24);
        dial.SetSchedules([]);dial.BeginSelection(dial.PointAt(11.2,dial.Radius*.8));dial.MovePointer(dial.PointAt(12.2,dial.Radius*.8));image=Paint(dial);
        Assert(dial.IsSelecting && Pixel(image,dial.PointAt(12,dial.Radius*.6))==bar,"current-time bar stays above an active drag preview");dial.CancelSelection();
        var window=new Window {Content=dial,Width=800,Height=560,Left=-10000,Top=-10000,WindowStyle=WindowStyle.None,ShowActivated=false,ShowInTaskbar=false};
        try {
            window.Show();Pump(80);Assert(dial.IsClockRunning,"visible day dial starts its current-time timer");
            var labels=dial.LayoutCallouts();now=now.AddSeconds(1);Pump(1150);
            Assert(System.Math.Abs(dial.CurrentTimeAngle-now.TimeOfDay.TotalSeconds/240)<.000001 && ReferenceEquals(labels,dial.LayoutCallouts()),"real clock tick rotates the bar without rebuilding schedule callouts");
            var hit=window.InputHitTest(dial.TranslatePoint(dial.PointAt(dial.CurrentTimeAngle/7.5,dial.Radius*.6),window));
            Assert(ReferenceEquals(hit,dial),"topmost time bar lets pointer input reach the schedule dial");
            window.Hide();Assert(!dial.IsClockRunning,"hidden day dial stops its timer");
            now=now.AddHours(6);window.Show();Pump(50);
            Assert(dial.IsClockRunning && System.Math.Abs(dial.CurrentTimeAngle-now.TimeOfDay.TotalSeconds/240)<.000001,"reopening immediately resynchronizes the current-time bar");
        } finally {window.Content=null;window.Close();}
        Assert(!dial.IsClockRunning,"unloaded day dial releases its timer activity");
        now=new(2026,10,24,6,0,0);var host=new Viewbox {Width=400,Height=280,Child=dial};
        host.Measure(new Size(400,280));host.Arrange(new Rect(0,0,400,280));host.UpdateLayout();dial.RefreshCurrentTime();image=Paint(host,400,280);
        var point=dial.TranslatePoint(dial.PointAt(12,dial.Radius*.6),host);int visible=0;
        for(int offset=-5;offset<=5;offset++)if(Pixel(image,new Point(point.X,point.Y+offset))==bar)visible++;
        Assert(visible>=3,"current-time bar retains a legible screen thickness when the viewport is halved");
        SavePlannerImage(host,new Size(400,280),Path.Combine(qa,"schedule-current-time-scaled.png"));host.Child=null;
    }
}
