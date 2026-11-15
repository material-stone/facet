using System.Globalization;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Shapes;

namespace Facet.SchedulePlugin;

internal sealed class OverlayScheduleColorPicker : Border
{
    // Embedded and parsed here because pack URIs may not resolve in a plugin load context.
    private static readonly Lazy<ResourceDictionary> Styles=new(()=>{using var stream=typeof(OverlayScheduleColorPicker).Assembly.GetManifestResourceStream("ScheduleColorPickerStyles.xaml")!;return (ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream);});
    internal readonly TextBox HexInput=new() {MaxLength=7,Margin=new(0)};
    internal readonly Slider HueInput=new() {Minimum=0,Maximum=360,SmallChange=1,LargeChange=30,IsMoveToPointEnabled=true,Margin=new(0,12,0,12)};
    internal readonly Grid ColorArea=new() {Height=150,ClipToBounds=true};
    private readonly Canvas markerLayer=new() {IsHitTestVisible=false};
    private readonly Ellipse marker=new() {Width=12,Height=12,Stroke=Brushes.White,StrokeThickness=2,Fill=Brushes.Transparent};
    private readonly Border preview=new() {Width=36,Height=36,CornerRadius=new(6),Margin=new(0,0,12,0)};
    private bool updating;
    private double saturation,value;
    internal event Action<string>? ColorChanged;

    internal OverlayScheduleColorPicker()
    {
        Padding=new(16);CornerRadius=new(12);BorderThickness=new(1);
        Background=(Brush)FindResource("Panel");BorderBrush=(Brush)FindResource("Edge");
        KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.Cycle);
        var body=new StackPanel();
        body.Children.Add(new TextBlock {Text="색상 선택",FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,12)});
        ColorArea.Children.Add(new Border {Background=new LinearGradientBrush(Colors.White,Color.FromArgb(0,255,255,255),0),IsHitTestVisible=false});
        ColorArea.Children.Add(new Border {Background=new LinearGradientBrush(Color.FromArgb(0,0,0,0),Colors.Black,90),IsHitTestVisible=false});
        markerLayer.Children.Add(marker);ColorArea.Children.Add(markerLayer);body.Children.Add(ColorArea);
        var styles=Styles.Value;
        HueInput.Style=(Style)styles["HueSlider"];body.Children.Add(HueInput);
        HueInput.SizeChanged+=(_,_)=> {
            if(HueInput.Template.FindName("HueBand",HueInput) is not Border band || band.Background is not LinearGradientBrush original)return;
            // Extend the end colors to the edges while keeping hue aligned with the thumb's center.
            var gradient=original.Clone();gradient.MappingMode=BrushMappingMode.Absolute;
            gradient.StartPoint=new(7,0);gradient.EndPoint=new(Math.Max(8,HueInput.ActualWidth-7),0);band.Background=gradient;
        };
        var code=new DockPanel();DockPanel.SetDock(preview,Dock.Left);code.Children.Add(preview);code.Children.Add(HexInput);body.Children.Add(code);
        Child=new ScrollViewer {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        AutomationProperties.SetName(ColorArea,"채도와 밝기 선택");AutomationProperties.SetName(HueInput,"색조");AutomationProperties.SetName(HexInput,"선택 색상 #RRGGBB");
        HueInput.ValueChanged+=(_,_)=>{if(!updating)RefreshColor(true);};
        ColorArea.MouseLeftButtonDown+=(_,e)=>{ColorArea.CaptureMouse();Choose(e.GetPosition(ColorArea));e.Handled=true;};
        ColorArea.MouseMove+=(_,e)=>{if(ColorArea.IsMouseCaptured)Choose(e.GetPosition(ColorArea));};
        ColorArea.MouseLeftButtonUp+=(_,e)=>{if(ColorArea.IsMouseCaptured){Choose(e.GetPosition(ColorArea));ColorArea.ReleaseMouseCapture();e.Handled=true;}};
        ColorArea.SizeChanged+=(_,_)=>PositionMarker();
        HexInput.TextChanged+=(_,_)=> {
            if(updating || !TryColor(HexInput.Text,out var color))return;
            SetColor(color);ColorChanged?.Invoke(Code(color));
        };
        IsVisibleChanged+=(_,_)=>{if(!IsVisible && ColorArea.IsMouseCaptured)ColorArea.ReleaseMouseCapture();};
    }

    internal void SetColor(string code)=>SetColor(TryColor(code,out var color)?color:Color.FromRgb(130,207,173));
    private void SetColor(Color color)
    {
        double r=color.R/255d,g=color.G/255d,b=color.B/255d,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
        double hue=delta==0?0:max==r?60*((g-b)/delta%6):max==g?60*((b-r)/delta+2):60*((r-g)/delta+4);
        updating=true;HueInput.Value=(hue+360)%360;saturation=max==0?0:delta/max;value=max;updating=false;RefreshColor(false);
    }
    private void Choose(Point point)
    {
        if(ColorArea.ActualWidth<=0 || ColorArea.ActualHeight<=0)return;
        saturation=Math.Clamp(point.X/ColorArea.ActualWidth,0,1);value=1-Math.Clamp(point.Y/ColorArea.ActualHeight,0,1);RefreshColor(true);
    }
    private void RefreshColor(bool notify)
    {
        var color=FromHsv(HueInput.Value,saturation,value);string code=Code(color);
        updating=true;ColorArea.Background=new SolidColorBrush(FromHsv(HueInput.Value,1,1));preview.Background=new SolidColorBrush(color);HexInput.Text=code;updating=false;
        PositionMarker();if(notify)ColorChanged?.Invoke(code);
    }
    private void PositionMarker()
    {
        Canvas.SetLeft(marker,Math.Clamp(saturation*ColorArea.ActualWidth-6,0,Math.Max(0,ColorArea.ActualWidth-12)));
        Canvas.SetTop(marker,Math.Clamp((1-value)*ColorArea.ActualHeight-6,0,Math.Max(0,ColorArea.ActualHeight-12)));
    }
    private static Color FromHsv(double hue,double saturation,double value)
    {
        double c=value*saturation,x=c*(1-Math.Abs(hue/60%2-1)),m=value-c;
        var (r,g,b)=((int)(hue/60)%6) switch {0=>(c,x,0d),1=>(x,c,0d),2=>(0d,c,x),3=>(0d,x,c),4=>(x,0d,c),_=>(c,0d,x)};
        return Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));
    }
    private static string Code(Color color)=>$"#{color.R:X2}{color.G:X2}{color.B:X2}";
    internal static bool TryColor(string code,out Color color)
    {
        color=default;
        if(code.Length!=7 || code[0]!='#' || !int.TryParse(code.AsSpan(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out int rgb))return false;
        color=Color.FromRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb);return true;
    }
}
