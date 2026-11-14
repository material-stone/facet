using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Facet.Design;

// Domain-warped value noise whose sample points drift with time, so the clouds keep flowing and never loop.
internal sealed class NebulaRenderer(double brightness)
{
    internal const int Width=288,Height=162;
    private const int Size=256;
    private readonly double[] grid=CreateGrid();

    private static double[] CreateGrid()
    {
        var random=new Random(7);
        return Enumerable.Range(0,Size*Size).Select(_=>random.NextDouble()).ToArray();
    }
    private double Noise(double x,double y)
    {
        int xi=(int)Math.Floor(x),yi=(int)Math.Floor(y);
        double tx=x-xi,ty=y-yi,fx=tx*tx*(3-2*tx),fy=ty*ty*(3-2*ty);
        int x0=xi&(Size-1),x1=(xi+1)&(Size-1),y0=(yi&(Size-1))*Size,y1=((yi+1)&(Size-1))*Size;
        double a=grid[y0+x0],b=grid[y0+x1],c=grid[y1+x0],d=grid[y1+x1];
        double top=a+(b-a)*fx;
        return top+(c+(d-c)*fx-top)*fy;
    }
    private double Fbm(double x,double y,int octaves)
    {
        double sum=0,amplitude=.5;
        for(int octave=0;octave<octaves;octave++) { sum+=amplitude*Noise(x,y);x=x*2.03+17;y=y*2.03+9;amplitude*=.5; }
        return sum;
    }
    private static byte Channel(double value)=>(byte)Math.Clamp(value,0,255);

    // Pixels are Bgr32. Time is in seconds of flow.
    internal void Render(byte[] pixels,double time)
    {
        double driftX=time*.021,driftY=time*.013,morph=time*.034;
        int index=0;
        for(int y=0;y<Height;y++) {
            double v=y/51d,dy=y/(double)Height-.5;
            for(int x=0;x<Width;x++) {
                double u=x/51d,dx=x/(double)Width-.5;
                double warp=Fbm(u+3.1+driftX,v+1.7-morph,3);
                double violet=Math.Max(0,Fbm(u+warp*1.6-driftX,v-warp+driftY,4)-.42)*3.2;
                double cyan=Math.Max(0,Fbm(u*.8-warp+9+morph,v*.8+warp+4-driftY,4)-.46)*3.4;
                violet*=violet;cyan*=cyan;
                double light=brightness*(1-Math.Min(1,(dx*dx+dy*dy)*1.5));
                pixels[index++]=Channel((18+(violet*150+cyan*150)*brightness)*(.45+.55*light));
                pixels[index++]=Channel((8+(violet*30+cyan*110)*brightness)*(.45+.55*light));
                pixels[index++]=Channel((6+(violet*110+cyan*25)*brightness)*(.45+.55*light));
                pixels[index++]=255;
            }
        }
    }
}

// Rendering stays off the UI thread and runs only while visible; the next frame waits until the last one is shown.
internal sealed class NebulaField : Image
{
    private const int FramesPerSecond=12;
    private readonly NebulaRenderer renderer;
    private readonly double speed;
    private readonly WriteableBitmap bitmap=new(NebulaRenderer.Width,NebulaRenderer.Height,96,96,PixelFormats.Bgr32,null);
    private CancellationTokenSource? session;
    private double time;

    internal int FramesShown { get; private set; }
    internal bool IsFlowing=>session!=null;

    internal NebulaField(double speed,double brightness)
    {
        this.speed=speed;renderer=new(brightness);
        IsHitTestVisible=false;Focusable=false;Stretch=Stretch.UniformToFill;Source=bitmap;
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.Linear);
        var first=new byte[NebulaRenderer.Width*NebulaRenderer.Height*4];
        renderer.Render(first,0);Show(first);
        IsVisibleChanged+=(_,_)=>{ if(IsVisible) Start(); else Stop(); };
    }

    private void Show(byte[] pixels)
    {
        bitmap.WritePixels(new Int32Rect(0,0,NebulaRenderer.Width,NebulaRenderer.Height),pixels,NebulaRenderer.Width*4,0);
        FramesShown++;
    }

    internal void Start()
    {
        if(session!=null) return;
        var current=session=new CancellationTokenSource();
        new Thread(()=>Run(current.Token)) { IsBackground=true,Priority=ThreadPriority.BelowNormal,Name="Facet nebula" }.Start();
    }

    internal void Stop()
    {
        session?.Cancel();session?.Dispose();session=null;
    }

    private void Run(CancellationToken stop)
    {
        var pixels=new byte[NebulaRenderer.Width*NebulaRenderer.Height*4];
        var interval=TimeSpan.FromSeconds(1d/FramesPerSecond);
        var clock=System.Diagnostics.Stopwatch.StartNew();
        var last=TimeSpan.Zero;
        try {
            while(!stop.IsCancellationRequested) {
                var now=clock.Elapsed;
                time+=(now-last).TotalSeconds*speed;last=now;
                renderer.Render(pixels,time);
                var shown=Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(()=>{ if(!stop.IsCancellationRequested) Show(pixels); }));
                shown.Wait(TimeSpan.FromSeconds(1));
                var rest=interval-(clock.Elapsed-now);
                if(rest>TimeSpan.Zero && stop.WaitHandle.WaitOne(rest)) return;
            }
        }
        catch(ObjectDisposedException) { }
        catch(OperationCanceledException) { }
    }
}
