using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Facet.Widgets;

// An empty background sibling only: text and controls never enter this bitmap. Shared by the app and plugins (ADR 0101).
public sealed class CachedShadow : Border
{
    public static readonly DependencyProperty ShadowProperty=DependencyProperty.Register(nameof(Shadow),typeof(DropShadowEffect),typeof(CachedShadow),
        new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public DropShadowEffect? Shadow {get=>(DropShadowEffect?)GetValue(ShadowProperty);set=>SetValue(ShadowProperty,value);}
    private BitmapSource? bitmap;
    private Rect bitmapBounds;
    private readonly record struct Tile(BitmapSource? Image,Rect Bounds);
    private Tile[] tiles=[];
    private DpiScale bitmapDpi;
    private Size bitmapSize;
    private bool useLiveEffect;
    public int CacheBuildCount {get;private set;}
    public double CacheBuildMilliseconds {get;private set;}
    public long CacheBytes=>bitmap==null?0:(long)bitmap.PixelWidth*bitmap.PixelHeight*4;

    // Same element and geometry, with the original effect path, for visual/performance A/B checks.
    public bool UseLiveEffect {
        get=>useLiveEffect;
        set {if(useLiveEffect==value)return;useLiveEffect=value;Effect=value?Shadow:null;InvalidateVisual();}
    }

    static CachedShadow()
    {
        IsHitTestVisibleProperty.OverrideMetadata(typeof(CachedShadow),new FrameworkPropertyMetadata(false));
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if(e.Property==BackgroundProperty || e.Property==BorderBrushProperty || e.Property==BorderThicknessProperty ||
            e.Property==CornerRadiusProperty || e.Property==ShadowProperty) {
            bitmap=null;tiles=[];
            if(useLiveEffect && e.Property==ShadowProperty) Effect=Shadow;
            InvalidateVisual();
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi,DpiScale newDpi)
    {
        bitmap=null;tiles=[];base.OnDpiChanged(oldDpi,newDpi);InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if(useLiveEffect || Shadow==null || Shadow.Opacity==0 || RenderSize.Width<=0 || RenderSize.Height<=0) {
            base.OnRender(drawingContext);return;
        }
        var dpi=VisualTreeHelper.GetDpi(this);
        if(bitmap==null || bitmapSize!=RenderSize || bitmapDpi.DpiScaleX!=dpi.DpiScaleX || bitmapDpi.DpiScaleY!=dpi.DpiScaleY) {
            long started=Stopwatch.GetTimestamp();
            BuildTiles(RenderSize,dpi);bitmapDpi=dpi;bitmapSize=RenderSize;CacheBuildCount++;
            CacheBuildMilliseconds+=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        foreach(var tile in tiles)
            if(tile.Image!=null) drawingContext.DrawImage(tile.Image,tile.Bounds);
            else drawingContext.DrawRectangle(Background,null,tile.Bounds);
    }

    private void BuildTiles(Size size,DpiScale dpi)
    {
        var shadow=Shadow!;
        double padding=Math.Ceiling(shadow.BlurRadius+Math.Abs(shadow.ShadowDepth))+2;
        int px=(int)Math.Ceiling(padding*dpi.DpiScaleX),py=(int)Math.Ceiling(padding*dpi.DpiScaleY);
        double corner=Math.Max(Math.Max(CornerRadius.TopLeft,CornerRadius.TopRight),Math.Max(CornerRadius.BottomLeft,CornerRadius.BottomRight));
        int capX=(int)Math.Ceiling((corner+shadow.BlurRadius+Math.Abs(shadow.ShadowDepth)+2)*dpi.DpiScaleX);
        int capY=(int)Math.Ceiling((corner+shadow.BlurRadius+Math.Abs(shadow.ShadowDepth)+2)*dpi.DpiScaleY);
        double width=size.Width*dpi.DpiScaleX,height=size.Height*dpi.DpiScaleY;
        bool compatible=Background is SolidColorBrush && BorderThickness==new Thickness(0);
        bool sliceX=compatible && width>capX*2+1,sliceY=compatible && height>capY*2+1;
        // Keep the original fractional pixel phase at the right/bottom edges.
        var stencil=new Size((sliceX?capX*2+1+width-Math.Floor(width):width)/dpi.DpiScaleX,
            (sliceY?capY*2+1+height-Math.Floor(height):height)/dpi.DpiScaleY);
        bitmap=RenderBackground(stencil,dpi,out _);
        bitmapBounds=new Rect(-px/dpi.DpiScaleX,-py/dpi.DpiScaleY,
            (Math.Ceiling(width)+px*2)/dpi.DpiScaleX,(Math.Ceiling(height)+py*2)/dpi.DpiScaleY);
        int[] sx=sliceX?[0,px+capX,bitmap.PixelWidth-px-capX,bitmap.PixelWidth]:[0,bitmap.PixelWidth];
        int[] sy=sliceY?[0,py+capY,bitmap.PixelHeight-py-capY,bitmap.PixelHeight]:[0,bitmap.PixelHeight];
        double[] tx=sliceX?[bitmapBounds.Left,capX/dpi.DpiScaleX,bitmapBounds.Right-(px+capX)/dpi.DpiScaleX,bitmapBounds.Right]:[bitmapBounds.Left,bitmapBounds.Right];
        double[] ty=sliceY?[bitmapBounds.Top,capY/dpi.DpiScaleY,bitmapBounds.Bottom-(py+capY)/dpi.DpiScaleY,bitmapBounds.Bottom]:[bitmapBounds.Top,bitmapBounds.Bottom];
        var result=new List<Tile>();
        for(int y=0;y<sy.Length-1;y++)for(int x=0;x<sx.Length-1;x++) {
            BitmapSource? image=null;
            bool solidCenter=sliceX && sliceY && x==1 && y==1 && Background is SolidColorBrush {Opacity:1,Color.A:255};
            if(!solidCenter) {image=new CroppedBitmap(bitmap,new Int32Rect(sx[x],sy[y],sx[x+1]-sx[x],sy[y+1]-sy[y]));image.Freeze();}
            result.Add(new(image,new Rect(tx[x],ty[y],tx[x+1]-tx[x],ty[y+1]-ty[y])));
        }
        tiles=result.ToArray();
    }

    public BitmapSource RenderBackground(Size size,DpiScale dpi,out Rect bounds)
    {
        var shadow=Shadow??throw new InvalidOperationException("A shadow is required for prerendering.");
        double padding=Math.Ceiling(shadow.BlurRadius+Math.Abs(shadow.ShadowDepth))+2;
        double paddingX=Math.Ceiling(padding*dpi.DpiScaleX)/dpi.DpiScaleX,paddingY=Math.Ceiling(padding*dpi.DpiScaleY)/dpi.DpiScaleY;
        int width=(int)Math.Ceiling(size.Width*dpi.DpiScaleX)+2*(int)Math.Ceiling(padding*dpi.DpiScaleX);
        int height=(int)Math.Ceiling(size.Height*dpi.DpiScaleY)+2*(int)Math.Ceiling(padding*dpi.DpiScaleY);
        var source=new Border {Width=size.Width,Height=size.Height,Background=Background?.CloneCurrentValue(),
            BorderBrush=BorderBrush?.CloneCurrentValue(),BorderThickness=BorderThickness,CornerRadius=CornerRadius,
            Effect=shadow.CloneCurrentValue(),SnapsToDevicePixels=SnapsToDevicePixels};
        var canvas=new Canvas {Width=width/dpi.DpiScaleX,Height=height/dpi.DpiScaleY,ClipToBounds=false};
        Canvas.SetLeft(source,paddingX);Canvas.SetTop(source,paddingY);canvas.Children.Add(source);
        canvas.Measure(new Size(canvas.Width,canvas.Height));canvas.Arrange(new Rect(0,0,canvas.Width,canvas.Height));
        var rendered=new RenderTargetBitmap(width,height,96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
        rendered.Render(canvas);rendered.Freeze();
        bounds=new Rect(-paddingX,-paddingY,width/dpi.DpiScaleX,height/dpi.DpiScaleY);
        return rendered;
    }
}
