using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Facet;

internal static partial class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void mouse_event(uint flags,uint dx,uint dy,uint data,nuint extra);

    private static void CheckCarouselBackdropPause(Controller controller)
    {
        var layers=(Grid)controller.Overlay.Content;var hud=(OverlayTabs)layers.Children[2];
        ((TabControl)hud.FindName("Tabs")).SelectedItem=hud.FindName("ManagementTab");
        var host=(OverlayCarousel)hud.FindName("ManagementCarousel");
        var capture=(BackdropCaptureSession)typeof(OverlayWindow).GetField("capture",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(controller.Overlay)!;
        capture.MeasureUpdates=true;
        try {
            Pump(180);int before=capture.PresentedFrames;var frame=((Image)layers.Children[0]).Source;
            Assert(before==1 && capture.Stopped.IsCompleted,"single calendar keeps one background frame with its capture worker stopped");
            Assert(!host.BeginDrag(new Point(400,200),host.SelectedContent) && !host.ContinueDrag(new Point(200,200)),"calendar background does not start a carousel gesture");
            Pump(180);
            Assert(!hud.IsCarouselMoving && capture.PresentedFrames==before && ReferenceEquals(frame,((Image)layers.Children[0]).Source)
                && ((Image)layers.Children[0]).Effect is System.Windows.Media.Effects.BlurEffect {Radius:18},"calendar keeps its frozen blurred background without gesture capture or recapture");
        } finally {capture.MeasureUpdates=false;}
    }

    private static void CheckManagementCarousel(Controller controller,string qa)
    {
        var hud=(OverlayTabs)((Grid)controller.Overlay.Content).Children[2];
        var tabs=(TabControl)hud.FindName("Tabs");tabs.SelectedItem=hud.FindName("ManagementTab");
        var host=(OverlayCarousel)hud.FindName("ManagementCarousel");
        Assert(AutomationProperties.GetName((TabItem)tabs.SelectedItem)=="일정" && host.Children.Count==1 && host.SelectedIndex==0,"schedule tab contains only its calendar/day content");
        Assert(!host.Select(-1,false) && !host.Select(1,false),"removed neighboring management items cannot be selected");
        double opacity=hud.Opacity;hud.Opacity=1;
        foreach(var size in new[]{new Size(1440,900),new Size(800,600),new Size(480,360)}) {
            hud.Measure(size);hud.Arrange(new Rect(size));hud.UpdateLayout();
            var content=(FrameworkElement)host.Children[0];
            var box=content.TransformToAncestor(hud).TransformBounds(new Rect(content.RenderSize));
            Assert(System.Math.Abs(box.X+box.Width/2-size.Width/2)<1 && System.Math.Abs(box.Y+box.Height/2-size.Height/2)<1,"single management content remains centered at "+size);
            var tile=(FrameworkElement)hud.FindName("CalendarWidgetView");var tileBox=tile.TransformToAncestor(hud).TransformBounds(new Rect(tile.RenderSize));
            Assert(box.Left>=tileBox.Left-1 && box.Right<=tileBox.Right+1 && box.Top>=tileBox.Top-1 && box.Bottom<=tileBox.Bottom+1,"single management content fits its widget tile at "+size);
            int frames=host.DragFrameCount;
            foreach(double dx in new[]{-300d,300d}) {
                Assert(!host.BeginDrag(new Point(400,200),host.SelectedContent) && !host.ContinueDrag(new Point(400+dx,200)) && !host.CompleteDrag(new Point(400+dx,200)),"horizontal pointer motion is not consumed at "+size+" offset "+dx);
                host.RenderPendingDrag();
            }
            var after=content.TransformToAncestor(hud).TransformBounds(new Rect(content.RenderSize));
            Assert(box==after && host.DragFrameCount==frames && !host.IsMotionActive && !host.IsMouseCaptured,"single calendar never moves or captures mouse for carousel navigation at "+size);
            SavePlannerImage(hud,size,Path.Combine(qa,$"management-calendar-{size.Width}.png"));
        }
        Assert(hud.MoveCalendarMonth(1) && hud.GoToCalendarToday(),"single calendar keeps month and today shortcuts");
        hud.Opacity=opacity;
    }
}
