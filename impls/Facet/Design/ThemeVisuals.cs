using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Facet.Design;

// A choice a theme offers in settings. The theme only names it; the code behind the slot acts on it.
internal sealed record ThemeOption(string Id,string Name,string? Description=null,bool Default=true);

internal sealed record ThemeManifest(string? Background,double BackgroundOpacity,int Stars,string? Frame,int FrameSlice,double FramePadding,double NebulaSpeed=0,double NebulaBrightness=1,double PanelMarkLength=0,double PanelCornerRadius=0,double TabCornerRadius=ThemeVisuals.DefaultTabCornerRadius,bool Voyage=false,bool HudWidgets=false,ThemeOption[]? ThemeOptions=null,string[]? WorldScenes=null,bool SoftHud=false,string? WorldPage=null,ThemeFont? BodyFont=null,ThemeFont? HeadingFont=null,string[]? Widgets=null)
{
    private sealed class Raw
    {
        public string? Background { get; set; }
        public double? BackgroundOpacity { get; set; }
        public int? Stars { get; set; }
        public string? Frame { get; set; }
        public int? FrameSlice { get; set; }
        public double? FramePadding { get; set; }
        public double? NebulaSpeed { get; set; }
        public double? NebulaBrightness { get; set; }
        public double? PanelMarkLength { get; set; }
        public double? PanelCornerRadius { get; set; }
        public double? TabCornerRadius { get; set; }
        public bool? Voyage { get; set; }
        public bool? HudWidgets { get; set; }
        public ThemeOption[]? Options { get; set; }
        public RawWorld? World { get; set; }
        public string? HudStyle { get; set; }
        public RawFonts? Fonts { get; set; }
        public string[]? Widgets { get; set; }
    }
    private sealed class RawWorld { public string? Page { get; set; } public string[]? Scenes { get; set; } }
    private sealed class RawFonts { public ThemeFont? Body { get; set; } public ThemeFont? Heading { get; set; } }
    private static bool ValidFont(ThemeFont? font)=>font==null || (font.File!=null && Regex.IsMatch(font.File,"^[A-Za-z0-9_-]{1,64}\\.(ttf|otf)$") && !string.IsNullOrWhiteSpace(font.Family) && Regex.IsMatch(font.Family,"^[\\p{L}\\p{N} ]{1,64}$"));
    // Widget state types the world page draws (ADR 0110): "<pluginId>/<widget>@<version>".
    internal const int MaxWidgetTypes=32;
    internal static bool ValidWidgetType(string? type)=>type!=null && Regex.IsMatch(type,"^[a-z0-9][a-z0-9._-]{0,63}/[a-z0-9][a-z0-9._-]{0,31}@[1-9][0-9]{0,3}$");
    private static readonly JsonSerializerOptions Options=new() { PropertyNameCaseInsensitive=true };
    private static bool ValidAsset(string? name)=>name==null || Regex.IsMatch(name,"^[a-z0-9_-]{1,64}\\.png$");

    // Null means the manifest is unusable; a theme is data, so nothing in it may reach outside its folder.
    internal static ThemeManifest? Parse(string json)
    {
        Raw? raw;
        try { raw=JsonSerializer.Deserialize<Raw>(json,Options); } catch(JsonException) { return null; }
        if(raw==null || !ValidAsset(raw.Background) || !ValidAsset(raw.Frame)) return null;
        double opacity=raw.BackgroundOpacity ?? 1,padding=raw.FramePadding ?? 0;
        int stars=raw.Stars ?? 0,slice=raw.FrameSlice ?? 0;
        if(opacity is <0 or >1 || stars is <0 or >500 || padding is <0 or >32) return null;
        if(raw.Frame!=null && slice is <1 or >128) return null;
        double nebulaSpeed=raw.NebulaSpeed ?? 0,nebulaBrightness=raw.NebulaBrightness ?? 1;
        if(nebulaSpeed is <0 or >8 || nebulaBrightness is <0 or >1) return null;
        double markLength=raw.PanelMarkLength ?? 0,cornerRadius=raw.PanelCornerRadius ?? 0;
        double tabRadius=raw.TabCornerRadius ?? ThemeVisuals.DefaultTabCornerRadius;
        if(markLength is <0 or >32 || cornerRadius is <0 or >24 || tabRadius is <0 or >24) return null;
        var options=raw.Options ?? [];
        if(options.Any(option=>option==null || option.Id==null || !Regex.IsMatch(option.Id,"^[a-z0-9-]{1,32}$") || string.IsNullOrWhiteSpace(option.Name)) || options.Select(option=>option.Id).Distinct().Count()!=options.Length) return null;
        if(raw.World is { } world && (world.Page==null || !Regex.IsMatch(world.Page,"^[a-z0-9_-]{1,64}\\.html$") || world.Scenes is not { Length:>0 } scenes || scenes.Any(scene=>scene==null || !Regex.IsMatch(scene,"^[a-z0-9-]{1,32}$")) || scenes.Distinct().Count()!=scenes.Length)) return null;
        if(!ValidFont(raw.Fonts?.Body) || !ValidFont(raw.Fonts?.Heading)) return null;
        if(raw.HudStyle is not (null or "tactical" or "soft")) return null;
        // Only a world page can draw widgets, so the list needs one.
        if(raw.Widgets is { } widgets && (raw.World==null || widgets.Length>MaxWidgetTypes || widgets.Any(type=>!ValidWidgetType(type)) || widgets.Distinct().Count()!=widgets.Length)) return null;
        return new(raw.Background,opacity,stars,raw.Frame,slice,padding,nebulaSpeed,nebulaBrightness,markLength,cornerRadius,tabRadius,raw.Voyage ?? false,raw.HudWidgets ?? false,options,raw.World?.Scenes,raw.HudStyle=="soft",raw.World?.Page,raw.Fonts?.Body,raw.Fonts?.Heading,raw.Widgets is { Length:>0 } ? raw.Widgets : null);
    }
}

// A font file inside the skin and the family name it declares.
internal sealed record ThemeFont(string File,string Family);

internal sealed record ThemeFrame(BitmapSource Image,int Slice,double Padding);

internal sealed class ThemeVisuals
{
    internal BitmapSource? Background { get; private init; }
    internal double BackgroundOpacity { get; private init; }
    internal int Stars { get; private init; }
    internal ThemeFrame? Frame { get; private init; }
    internal double NebulaSpeed { get; private init; }
    internal double NebulaBrightness { get; private init; }
    internal double PanelMarkLength { get; private init; }
    internal double PanelCornerRadius { get; private init; }
    internal bool Voyage { get; private init; }
    internal bool HudWidgets { get; private init; }
    internal string[]? WorldScenes { get; private init; }
    internal string? WorldPageHtml { get; private init; }
    // NavigateToString refuses anything larger.
    private const int MaxWorldPageBytes=1900000;
    internal bool SoftHud { get; private init; }
    // Widget state types the world page draws instead of their WPF look (ADR 0110).
    internal IReadOnlySet<string> WidgetTypes { get; private init; }=new HashSet<string>();
    internal ThemeOption[] Options { get; private init; }=[];
    internal const string ReticleOption="reticle";
    internal static string OptionKey(string skin,string option)=>skin+"."+option;
    internal bool IsOptionOn(Facet.Core.Settings settings,string skin,string id)=>
        settings.SkinOptions?.TryGetValue(OptionKey(skin,id),out bool value)==true ? value : Options.FirstOrDefault(option=>option.Id==id)?.Default ?? true;
    internal const double DefaultTabCornerRadius=12;
    internal double TabCornerRadius { get; private init; }=DefaultTabCornerRadius;

    private static Uri Asset(string folder,string name)=>new($"pack://application:,,,/Facet;component/Design/Skins/{folder}/{name}");
    // OnLoad reads everything now, so an external skin's file stays replaceable.
    private static BitmapSource Image(Func<string,Stream> open,string name)
    {
        using var stream=open(name);
        var image=new BitmapImage();
        image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();
        return image;
    }

    // A broken theme must not stop the app: it keeps its tokens and loses only the visuals.
    internal static ThemeVisuals? Load(string folder)
    {
        Stream Open(string name)=>Application.GetResourceStream(Asset(folder,name)).Stream;
        try {
            using var reader=new StreamReader(Open("theme.json"));
            return ThemeManifest.Parse(reader.ReadToEnd()) is { } manifest ? From(manifest,Open) : null;
        }
        catch(Exception ex) when(ex is IOException or NullReferenceException or NotSupportedException or ArgumentException or InvalidOperationException) { return null; }
    }

    internal static ThemeVisuals? From(ThemeManifest manifest,Func<string,Stream> open)
    {
        try {
            string? worldPage=null;
            if(manifest.WorldPage is { } page) {
                using var pageReader=new StreamReader(open(page));
                worldPage=pageReader.ReadToEnd();
                if(System.Text.Encoding.UTF8.GetByteCount(worldPage)>MaxWorldPageBytes) return null;
            }
            var frameImage=manifest.Frame is { } frame ? Image(open,frame) : null;
            // NineSliceFrame needs a center left between its corners.
            if(frameImage!=null && (frameImage.PixelWidth<=2*manifest.FrameSlice || frameImage.PixelHeight<=2*manifest.FrameSlice)) return null;
            return new() {
                Background=manifest.Background is { } background ? Image(open,background) : null,
                BackgroundOpacity=manifest.BackgroundOpacity,
                Stars=manifest.Stars,
                NebulaSpeed=manifest.NebulaSpeed,
                NebulaBrightness=manifest.NebulaBrightness,
                PanelMarkLength=manifest.PanelMarkLength,
                PanelCornerRadius=manifest.PanelCornerRadius,
                TabCornerRadius=manifest.TabCornerRadius,
                Voyage=manifest.Voyage,
                HudWidgets=manifest.HudWidgets,
                WorldScenes=manifest.WorldScenes,
                WorldPageHtml=worldPage,
                SoftHud=manifest.SoftHud,
                WidgetTypes=new HashSet<string>(manifest.Widgets ?? [],StringComparer.Ordinal),
                Options=manifest.ThemeOptions ?? [],
                Frame=frameImage!=null ? new(frameImage,manifest.FrameSlice,manifest.FramePadding) : null
            };
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NullReferenceException or NotSupportedException or ArgumentException or InvalidOperationException) { return null; }
    }

    internal FrameworkElement? CreateBackdrop()
    {
        if(Background==null && Stars==0 && NebulaSpeed==0 && !Voyage) return null;
        var layer=new Grid { IsHitTestVisible=false,Focusable=false };
        if(NebulaSpeed>0) layer.Children.Add(new NebulaField(NebulaSpeed,NebulaBrightness) { HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center });
        if(Background!=null) layer.Children.Add(new Image { Source=Background,Stretch=Stretch.UniformToFill,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Opacity=BackgroundOpacity });
        if(Stars>0) layer.Children.Add(new StarField(Stars));
        if(Voyage) { layer.Children.Add(new SpaceVoyage());layer.Children.Add(new FlightReticle()); }
        return layer;
    }
}

// The thin aiming marks at the screen's center, on the line planets never cross.
internal sealed class FlightReticle : FrameworkElement
{
    internal FlightReticle() { IsHitTestVisible=false;Focusable=false; }
    protected override void OnRender(DrawingContext context)
    {
        if(TryFindResource("Mint") is not SolidColorBrush mint) return;
        Pen Stroke(byte alpha,double width,params double[] dash)
        {
            var brush=new SolidColorBrush(Color.FromArgb(alpha,mint.Color.R,mint.Color.G,mint.Color.B));brush.Freeze();
            var pen=new Pen(brush,width);if(dash.Length>0) pen.DashStyle=new DashStyle(dash,0);pen.Freeze();
            return pen;
        }
        double x=ActualWidth/2,y=ActualHeight/2;const double R=150;
        context.DrawEllipse(null,Stroke(70,1,2,10),new Point(x,y),R,R);
        var arcs=Geometry.Parse(FormattableString.Invariant($"M{x-100},{y-112}A{R},{R} 0 0 1 {x+100},{y-112}M{x+100},{y+112}A{R},{R} 0 0 1 {x-100},{y+112}"));arcs.Freeze();
        context.DrawGeometry(null,Stroke(190,1.5),arcs);
        var cross=Stroke(255,1.5);
        foreach(var (dx,dy) in new[]{(1d,0d),(-1d,0d),(0d,1d),(0d,-1d)}) context.DrawLine(cross,new Point(x+dx*10,y+dy*10),new Point(x+dx*24,y+dy*24));
    }
}

// Corners keep their size and the edges and center stretch, so one image fits every tile size.
internal sealed class NineSliceFrame : FrameworkElement
{
    private readonly CroppedBitmap[] parts;
    private readonly int slice;

    internal NineSliceFrame(ThemeFrame frame)
    {
        IsHitTestVisible=false;Focusable=false;slice=frame.Slice;
        int width=frame.Image.PixelWidth,height=frame.Image.PixelHeight;
        int[] xs=[0,slice,width-slice],ws=[slice,Math.Max(1,width-2*slice),slice],ys=[0,slice,height-slice],hs=[slice,Math.Max(1,height-2*slice),slice];
        parts=new CroppedBitmap[9];
        for(int row=0;row<3;row++)
            for(int column=0;column<3;column++) {
                var part=new CroppedBitmap(frame.Image,new Int32Rect(xs[column],ys[row],ws[column],hs[row]));part.Freeze();
                parts[row*3+column]=part;
            }
    }

    protected override void OnRender(DrawingContext context)
    {
        double corner=Math.Min(slice,Math.Min(ActualWidth,ActualHeight)/2);
        double[] xs=[0,corner,ActualWidth-corner],ws=[corner,Math.Max(0,ActualWidth-2*corner),corner];
        double[] ys=[0,corner,ActualHeight-corner],hs=[corner,Math.Max(0,ActualHeight-2*corner),corner];
        for(int row=0;row<3;row++)
            for(int column=0;column<3;column++)
                context.DrawImage(parts[row*3+column],new Rect(xs[column],ys[row],ws[column],hs[row]));
    }
}

// Restyles every app and plugin surface that paints the Panel token, so about thirty call sites need no theme code.
// The marks are the Border's own brush, so they follow its opacity, visibility, popups and shadow without extra elements.
internal static class ThemePanels
{
    private const double MinWidth=64,MinHeight=28,MinOriginalRadius=6;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Border,object> tracked=new();
    private static bool registered;

    internal static void Register()
    {
        if(registered) return;
        registered=true;
        // Loaded skips elements without their own handler, while every laid-out element raises SizeChanged.
        EventManager.RegisterClassHandler(typeof(Border),FrameworkElement.SizeChangedEvent,new SizeChangedEventHandler((sender,_)=>Update((Border)sender)));
    }

    // For a surface the token match cannot find, such as the layer above the tab headers.
    internal static void Track(Border border)=>tracked.TryAdd(border,border);

    private static void Update(Border border)
    {
        if(Skins.Visuals is not { PanelMarkLength:>0 } visuals) return;
        // Remembered because restyling removes the radius that identified the surface.
        if(!tracked.TryGetValue(border,out _)) {
            if(!IsPanelSurface(border)) return;
            tracked.Add(border,visuals);
        }
        Style(border,visuals.PanelMarkLength,visuals.PanelCornerRadius);
    }

    // Templated parts such as the tab bar, whose shape has its own slot, and small inline surfaces such as code blocks are not panels.
    private static bool IsPanelSurface(Border border)=>border.TemplatedParent==null && border.CornerRadius.TopLeft>=MinOriginalRadius
        && border.Background is SolidColorBrush surface && border.TryFindResource("Panel") is SolidColorBrush panel && surface.Color==panel.Color;

    internal static void Style(Border border,double length,double radius)
    {
        if(border.ActualWidth<MinWidth || border.ActualHeight<MinHeight || border.TryFindResource("Mint") is not SolidColorBrush mint) return;
        border.CornerRadius=new(radius);
        if(border.BorderThickness==default) border.BorderThickness=new(1);
        border.SnapsToDevicePixels=true;border.UseLayoutRounding=true;
        border.BorderBrush=Marks(border.ActualWidth,border.ActualHeight,length,mint.Color,VisualTreeHelper.GetDpi(border));
    }

    // A one-bit image at device resolution stays sharp where a vector brush would be resampled, and costs little on full-screen panels.
    private static ImageBrush Marks(double width,double height,double length,Color mint,DpiScale dpi)
    {
        int pixelWidth=Math.Max(1,(int)Math.Round(width*dpi.DpiScaleX)),pixelHeight=Math.Max(1,(int)Math.Round(height*dpi.DpiScaleY));
        int armX=Math.Min(pixelWidth,(int)Math.Round(length*dpi.DpiScaleX)),armY=Math.Min(pixelHeight,(int)Math.Round(length*dpi.DpiScaleY));
        // Exactly the stroke's device width: a border ring straddling into the next pixel finds it transparent there and stays one crisp line.
        int thickX=Math.Min(pixelWidth,(int)Math.Ceiling(dpi.DpiScaleX)),thickY=Math.Min(pixelHeight,(int)Math.Ceiling(dpi.DpiScaleY));
        int stride=(pixelWidth+7)/8;var bits=new byte[stride*pixelHeight];
        void Fill(int left,int top,int fillWidth,int fillHeight) {
            for(int y=top;y<top+fillHeight;y++)
                for(int x=left;x<left+fillWidth;x++) bits[y*stride+x/8]|=(byte)(0x80>>(x%8));
        }
        foreach(int left in new[]{0,pixelWidth-armX})
            foreach(int top in new[]{0,pixelHeight-thickY}) Fill(left,top,armX,thickY);
        foreach(int left in new[]{0,pixelWidth-thickX})
            foreach(int top in new[]{0,pixelHeight-armY}) Fill(left,top,thickX,armY);
        var image=BitmapSource.Create(pixelWidth,pixelHeight,96,96,PixelFormats.Indexed1,new BitmapPalette([Colors.Transparent,mint]),bits,stride);image.Freeze();
        var brush=new ImageBrush(image) { Stretch=Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush,BitmapScalingMode.NearestNeighbor);brush.Freeze();
        return brush;
    }
}

// Most stars hold still and faint; the brighter ones sit in layers that fade at different speeds. Only opacity changes, and only while visible.
internal sealed class StarField : Grid
{
    private const int FramesPerSecond=12,TwinkleLayers=5;
    private static readonly Color[] Tints=[Colors.White,Color.FromRgb(207,224,255),Color.FromRgb(255,233,199),Color.FromRgb(255,210,176),Color.FromRgb(188,212,255)];

    private sealed class Layer : FrameworkElement
    {
        private readonly (double X,double Y,double Radius,SolidColorBrush Brush,RadialGradientBrush? Glow)[] stars;
        internal Layer(int count,int seed,bool bright)
        {
            var random=new Random(seed);
            stars=Enumerable.Range(0,count).Select(_=>{
                // A long-tailed brightness: many barely visible stars and very few strong ones.
                double power=Math.Pow(random.NextDouble(),bright ? 2.2 : 4);
                double radius=(bright ? .7 : .45)+power*(bright ? 1.5 : .7);
                var tint=Tints[random.Next(Tints.Length)];
                byte alpha=(byte)((bright ? 130 : 45)+power*(bright ? 125 : 150));
                var brush=new SolidColorBrush(Color.FromArgb(alpha,tint.R,tint.G,tint.B));brush.Freeze();
                RadialGradientBrush? glow=null;
                if(bright && power>.45) { glow=new(Color.FromArgb(70,tint.R,tint.G,tint.B),Color.FromArgb(0,tint.R,tint.G,tint.B));glow.Freeze(); }
                return (random.NextDouble(),random.NextDouble(),radius,brush,glow);
            }).ToArray();
        }
        protected override void OnRender(DrawingContext context)
        {
            foreach(var (x,y,radius,brush,glow) in stars) {
                var at=new Point(x*ActualWidth,y*ActualHeight);
                if(glow!=null) context.DrawEllipse(glow,null,at,radius*4,radius*4);
                context.DrawEllipse(brush,null,at,radius,radius);
            }
        }
    }

    internal StarField(int count)
    {
        IsHitTestVisible=false;Focusable=false;
        int twinkling=count/3;
        Children.Add(new Layer(count-twinkling,11,false));
        for(int index=0;index<TwinkleLayers;index++) Children.Add(new Layer(twinkling/TwinkleLayers+(index<twinkling%TwinkleLayers ? 1 : 0),index+1,true));
        IsVisibleChanged+=(_,_)=>{ if(IsVisible) Start(); else Stop(); };
    }

    internal bool IsTwinkling { get; private set; }

    private void Start()
    {
        IsTwinkling=true;
        for(int index=1;index<Children.Count;index++) {
            double seconds=1.7+index*1.9;
            // A negative begin time joins the cycle part-way, so reopening looks like a sky that never stopped instead of every layer dimming together.
            var fade=new DoubleAnimation(.2+index*.06,1,TimeSpan.FromSeconds(seconds)) { AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,BeginTime=TimeSpan.FromSeconds(-Random.Shared.NextDouble()*seconds*2),EasingFunction=new SineEase() };
            Timeline.SetDesiredFrameRate(fade,FramesPerSecond);
            Children[index].BeginAnimation(OpacityProperty,fade);
        }
    }

    private void Stop()
    {
        IsTwinkling=false;
        foreach(UIElement child in Children) child.BeginAnimation(OpacityProperty,null);
    }
}
