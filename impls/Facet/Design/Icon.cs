using System.Windows.Automation;

namespace Facet.Design;

public enum IconKind { Dashboard, Calendar, Moon, Sun, Work, Settings, Bell, Sparkles, Save, Add, Edit, Check, Close, Left, Right, Clock, Help, Upload, Archive, Play, Stop, Link, Refresh, Chart, Download, Shield, Folder, Target, Chevron, Droplet, Trash, Home, Star, Bookmark, Note, Book, Chat, Heart, Dumbbell, Coffee, Music, Gamepad, Code, Palette, Globe }
public sealed class Icon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty=DependencyProperty.Register(nameof(Kind),typeof(IconKind),typeof(Icon),new FrameworkPropertyMetadata(IconKind.Dashboard,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty=System.Windows.Documents.TextElement.ForegroundProperty.AddOwner(typeof(Icon),new FrameworkPropertyMetadata(Brushes.White,FrameworkPropertyMetadataOptions.Inherits|FrameworkPropertyMetadataOptions.AffectsRender));
    public IconKind Kind { get => (IconKind)GetValue(KindProperty); set => SetValue(KindProperty,value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty,value); }
    private static readonly string[] Paths=[
        "M3,3H10V10H3Z M14,3H21V10H14Z M3,14H10V21H3Z M14,14H21V21H14Z",
        "M5,5H19Q21,5 21,7V20Q21,21 19,21H5Q3,21 3,19V7Q3,5 5,5Z M3,10H21 M7,3V7 M17,3V7 M7,14H10 M14,14H17 M7,18H10",
        "M20,15A9,9 0 1 1 9,3A7,7 0 0 0 20,15Z",
        "M16,12A4,4 0 1 1 8,12A4,4 0 1 1 16,12 M12,1V4 M12,20V23 M1,12H4 M20,12H23 M4,4L6,6 M18,18L20,20 M4,20L6,18 M18,6L20,4",
        "M3,7H21V21H3Z M8,7V3H16V7 M3,12Q12,17 21,12 M12,12V16",
        "M12,8A4,4 0 1 1 12,16A4,4 0 1 1 12,8 M9,3H15L16,6L20,7L22,12L20,17L16,18L15,21H9L8,18L4,17L2,12L4,7L8,6Z",
        "M5,16V10A7,7 0 0 1 19,10V16L21,18H3Z M9,21H15",
        "M12,2L15,9L22,12L15,15L12,22L9,15L2,12L9,9Z M20,2V6 M18,4H22",
        "M4,3H18L21,6V21H3V3Z M7,3V9H17V3 M7,21V14H17V21",
        "M12,4V20 M4,12H20",
        "M4,16L15,5L19,9L8,20L3,21Z M14,6L18,10 M16,4L18,2L22,6L20,8",
        "M4,12L9,17L20,6",
        "M5,5L19,19 M19,5L5,19",
        "M15,5L8,12L15,19",
        "M9,5L16,12L9,19",
        "M21,12A9,9 0 1 1 3,12A9,9 0 1 1 21,12 M12,6V12L16,14",
        "M21,12A9,9 0 1 1 3,12A9,9 0 1 1 21,12 M9,8Q9,5 12,5Q17,5 15,10L12,12V14 M12,17V18",
        "M12,17V3 M7,8L12,3L17,8 M4,15V21H20V15",
        "M3,3H21V8H3Z M5,8V21H19V8 M9,12H15",
        "M7,3L21,12L7,21Z",
        "M5,5H19V19H5Z",
        "M10,14L14,10 M8,16L6,18A4,4 0 0 1 1,13L6,8A4,4 0 0 1 11,8 M13,16A4,4 0 0 0 18,16L23,11A4,4 0 0 0 18,6L16,8",
        "M20,8A8,8 0 1 0 20,16 M20,3V8H15",
        "M3,3V21H22 M7,16L12,10L16,13L21,5",
        "M12,3V17 M7,12L12,17L17,12 M4,17V21H20V17",
        "M12,2L21,6V12Q20,19 12,22Q4,19 3,12V6Z M7,12L11,16L17,9",
        "M3,6H10L12,9H21V21H3Z",
        "M21,12A9,9 0 1 1 3,12A9,9 0 1 1 21,12 M17,12A5,5 0 1 1 7,12A5,5 0 1 1 17,12 M12,12L21,3",
        "M6,9L12,15L18,9",
        "M12,2C10,6 5,10 5,15A7,7 0 0 0 19,15C19,10 14,6 12,2Z M8,15Q8,19 12,19",
        "M3,6H21 M9,6V3H15V6 M5,6L6,20Q6,21 7,21H17Q18,21 18,20L19,6 M10,10V17 M14,10V17",
        "M3,11L12,3L21,11 M5,9V21H19V9 M10,21V14H14V21",
        "M12,3L14.8,9L21,9.8L16.5,14.2L17.6,21L12,17.8L6.4,21L7.5,14.2L3,9.8L9.2,9Z",
        "M6,3H18V21L12,16L6,21Z",
        "M5,3H15L19,7V21H5Z M15,3V7H19 M8,12H16 M8,16H16",
        "M12,6Q8,3 3,4V19Q8,18 12,21Q16,18 21,19V4Q16,3 12,6Z M12,6V21",
        "M4,4H20Q21,4 21,5V16Q21,17 20,17H10L5,21V17H4Q3,17 3,16V5Q3,4 4,4Z",
        "M12,21C5,15 2,12 2,8A5,5 0 0 1 12,6A5,5 0 0 1 22,8C22,12 19,15 12,21Z",
        "M8,12H16 M8,6V18 M16,6V18 M4,9V15 M20,9V15 M2,12H4 M20,12H22",
        "M4,9H17V16A4,4 0 0 1 13,20H8A4,4 0 0 1 4,16Z M17,11H19A2,2 0 0 1 19,16H17 M8,3V6 M12,3V6",
        "M9,18V5L20,3V16 M9,18A3,3 0 1 1 3,18A3,3 0 1 1 9,18 M20,16A3,3 0 1 1 14,16A3,3 0 1 1 20,16",
        "M7,7H17A5,5 0 0 1 22,12V15A3,3 0 0 1 16.5,17L15,15H9L7.5,17A3,3 0 0 1 2,15V12A5,5 0 0 1 7,7Z M8,9V13 M6,11H10 M15,10V10.5 M18,12V12.5",
        "M8,6L2,12L8,18 M16,6L22,12L16,18 M14,4L10,20",
        "M12,3A9,9 0 1 0 12,21Q15,21 14,18Q13.5,15 16,15H19Q21,15 21,12A9,9 0 0 0 12,3Z M8,10V10.5 M12,7V7.5 M16,10V10.5 M7,14V14.5",
        "M21,12A9,9 0 1 1 3,12A9,9 0 1 1 21,12 M3,12H21 M12,3Q17,8 17,12Q17,16 12,21Q7,16 7,12Q7,8 12,3Z"
    ];
    protected override Size MeasureOverride(Size availableSize) => new(Math.Min(20,availableSize.Width),Math.Min(20,availableSize.Height));
    protected override void OnRender(DrawingContext dc)
    {
        double scale=Math.Min(ActualWidth,ActualHeight)/24;
        dc.PushTransform(new TranslateTransform((ActualWidth-24*scale)/2,(ActualHeight-24*scale)/2)); dc.PushTransform(new ScaleTransform(scale,scale));
        dc.DrawGeometry(null,new Pen(Foreground,1.7) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round },Geometry.Parse(Paths[(int)Kind])); dc.Pop(); dc.Pop();
    }
}

public static class IconUi
{
    public static StackPanel Label(IconKind kind,string text,Brush? brush=null,int size=14)
    {
        var row=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
        var icon=new Icon { Kind=kind,Width=19,Height=19,Margin=new(0,0,8,0) }; if(brush!=null) icon.Foreground=brush; row.Children.Add(icon);
        var label=new TextBlock { Text=text,VerticalAlignment=VerticalAlignment.Center,FontSize=size }; if(brush!=null) label.Foreground=brush; row.Children.Add(label); return row;
    }
    public static Button Button(IconKind kind,string label,Action action,bool primary=false)
    {
        var button=new Button { ContentTemplate=null, ToolTip=label };
        if(primary) button.Style=(Style)Application.Current.FindResource("Primary");
        button.Content=Label(kind,label,(Brush)Application.Current.FindResource(primary?"OnAccent":"Ink"));
        AutomationProperties.SetName(button,label); button.Click+=(_,_)=>action(); return button;
    }
}
