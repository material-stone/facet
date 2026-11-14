using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Facet;
using Facet.Core;

internal static partial class Program
{
    private static void CheckOverlayAppearance(Controller controller,string qa)
    {
        var legacy=JsonSerializer.Deserialize<Settings>("{\"Opacity\":0.5}")!;
        Assert(Math.Abs(OverlayAppearance.Transparency(legacy)-70)<.001 && OverlayAppearance.BackgroundColor(legacy)==Color.FromRgb(20,26,29),"old settings retain their effective background alpha and default color");
        foreach(var invalid in new Settings[] {
            new(){OverlayTransparencyPercent=-1},new(){OverlayTransparencyPercent=101},new(){OverlayTransparencyPercent=double.NaN},
            new(){OverlayBackgroundColor="#80141A1D"},new(){OverlayBackgroundColor="red"} }) {
            bool rejected=false;try {SettingsEditing.Validate(invalid);}catch(ArgumentException){rejected=true;}
            Assert(rejected,"appearance rejects an invalid percentage or non-RGB background color");
        }
        var overlay=new OverlayWindow(controller);
        var layers=(Grid)overlay.Content;
        var tint=(SolidColorBrush)((Border)layers.Children[1]).Background;
        var progress=typeof(OverlayWindow).GetMethod("ApplyProgress",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var capture=typeof(OverlayWindow).GetField("capture",BindingFlags.Instance|BindingFlags.NonPublic)!;
        foreach(double percent in new[]{0d,25d,50d,100d}) {
            var settings=new Settings { OverlayTransparencyPercent=percent,OverlayBackgroundColor="#3265A8" };
            overlay.Present(false,OverlayAppearance.Strength(settings),18,0,0,OverlayAppearance.BackgroundColor(settings));
            progress.Invoke(overlay,[1d]);
            Assert(tint.Color==Color.FromRgb(50,101,168) && Math.Abs(tint.Opacity-(1-percent/100))<.001 && ((OverlayTabs)layers.Children[2]).Opacity==1,"background color and alpha match transparency while foreground stays opaque at "+percent+"%");
            if(percent==100)Assert(capture.GetValue(overlay)==null && (double)typeof(OverlayWindow).GetField("requestedBlurRadius",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(overlay)! == 0,"fully transparent background disables blur and never starts capture");
        }
        overlay.SetQuickLaunch(true);progress.Invoke(overlay,[1d]);
        Assert(tint.Opacity==0,"quick launch stays transparent regardless of the saved background color");

    }

    private static void CheckNativeOverlayAppearance(Controller controller)
    {
        var previous=JsonSerializer.Serialize(controller.Data.Settings);
        using var host=new KeyboardHost();
        var bounds=System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var layers=(Grid)controller.Overlay.Content;
        var tint=(SolidColorBrush)((Border)layers.Children[1]).Background;
        int x=bounds.X+10,y=bounds.Y+bounds.Height/2;
        try {
            controller.HideOverlay();Pump(400);
            host.Window.Background=Brushes.Red;host.Window.Show();Native.CoverScreen(host.Window);host.Focus();
            controller.Commit(data=> {var s=data.Settings;s.OverlayTransparencyPercent=0;s.OverlayBackgroundColor="#3265A8";s.OverlayBlurRadius=0;s.OverlayShowMilliseconds=0;s.OverlayHideMilliseconds=0;});
            controller.ToggleOverlay();Pump(100);
            var opaque=CaptureDesktopPixel(x,y);
            Assert(Math.Abs(opaque.R-50)<=1 && Math.Abs(opaque.G-101)<=1 && Math.Abs(opaque.B-168)<=1,"0% transparency renders the exact opaque palette color in desktop capture");
            controller.Commit(data=>data.Settings.OverlayTransparencyPercent=50);Pump(100);
            var red=CaptureDesktopPixel(x,y);
            host.Window.Background=Brushes.Lime;Pump(100);
            var green=CaptureDesktopPixel(x,y);
            Assert(red.R>green.R+100 && green.G>red.G+100 && ((Image)layers.Children[0]).Source==null,"simple translucency follows changing desktop content without snapshots or self capture");
            controller.Commit(data=>{data.Settings.OverlayTransparencyPercent=100;data.Settings.OverlayBlurRadius=18;});Pump(100);
            var transparent=CaptureDesktopPixel(x,y);
            Assert(transparent.G>=254 && transparent.R<=1 && transparent.B<=1 && tint.Opacity==0 && ((Image)layers.Children[0]).Source==null && ((OverlayTabs)layers.Children[2]).Opacity==1,"100% transparency reveals the live desktop, disables configured blur and keeps the foreground visible");
        } finally {
            controller.HideOverlay();Pump(50);
            controller.Commit(data=>data.Settings=JsonSerializer.Deserialize<Settings>(previous)!);
        }
    }
}
