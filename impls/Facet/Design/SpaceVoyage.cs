using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Facet.Design;

internal enum PlanetKind { Rock, Gas, Ice, Lava, Ocean, Desert }

// One planet's pass. The camera stays at the origin and the planet travels along +Z on a fixed side line.
internal sealed record Encounter(PlanetKind Kind,int Seed,double Radius,double X,double Y,double StartZ,double Speed,double Spin,double Tilt)
{
    internal const double RingScale=2.2;
    internal bool HasRing=>Kind==PlanetKind.Gas;
    internal double Extent=>HasRing ? Radius*RingScale : Radius;
    internal double AxisDistance=>Math.Sqrt(X*X+Y*Y);
    internal double ZAt(double seconds)=>StartZ+Speed*seconds;
    internal bool IsPast(double seconds)=>ZAt(seconds)>Extent*2;
    internal double Duration=>(Extent*2-StartZ)/Speed;
    // Both planets may be anywhere along Z at any time, so only their side-on separation keeps them apart.
    internal bool ClearOf(Encounter other)=>Math.Sqrt((X-other.X)*(X-other.X)+(Y-other.Y)*(Y-other.Y))>=(Extent+other.Extent)*1.1;
}

// Pure and deterministic. Keeping the axis distance beyond the extent is what makes a collision impossible.
internal sealed class VoyagePlan(int seed)
{
    internal const double MinClearance=1.2;
    private readonly Random random=new(seed);
    private PlanetKind? last;

    internal Encounter Next()
    {
        PlanetKind kind;
        do kind=(PlanetKind)random.Next(Enum.GetValues<PlanetKind>().Length); while(kind==last);
        last=kind;
        double radius=1+random.NextDouble()*random.NextDouble()*6;
        double extent=kind==PlanetKind.Gas ? radius*Encounter.RingScale : radius;
        // Most pass well off the line and stay small; some skim close and fill a side of the screen.
        double clearance=random.NextDouble()<.45 ? MinClearance+random.NextDouble() : 3+random.NextDouble()*9;
        double distance=extent*clearance,angle=random.NextDouble()*Math.PI*2;
        // Starts far out to appear small; the speed is set by the nearer distance so a farther start does not hurry it.
        double start=-Math.Max(250,extent*350),speed=Math.Max(60,extent*90)/(240+random.NextDouble()*180);
        return new(kind,random.Next(),radius,Math.Cos(angle)*distance,Math.Sin(angle)*distance*.7+Math.Sign(Math.Sin(angle))*extent*.1,start,speed,
            (.4+random.NextDouble())*(random.Next(2)*2-1),(random.NextDouble()-.5)*.8);
    }
    // The sine term above shrinks vertical offset, so the guarantee is re-established here for every result.
    internal Encounter NextClear()
    {
        var item=Next();
        double scale=Math.Max(1,MinClearance*item.Extent/Math.Max(item.AxisDistance,1e-6));
        return item with { X=item.X*scale,Y=item.Y*scale };
    }
}

internal static class PlanetSurface
{
    internal const int Width=1536,Height=768;
    private const int Size=64;

    private sealed class Field
    {
        private readonly double[] grid=new double[Size*Size*Size];
        internal Field(int seed) { var random=new Random(seed);for(int i=0;i<grid.Length;i++)grid[i]=random.NextDouble(); }
        private double Noise(double x,double y,double z)
        {
            int xi=(int)Math.Floor(x),yi=(int)Math.Floor(y),zi=(int)Math.Floor(z);
            double tx=x-xi,ty=y-yi,tz=z-zi,fx=tx*tx*(3-2*tx),fy=ty*ty*(3-2*ty),fz=tz*tz*(3-2*tz);
            int m=Size-1,x0=xi&m,x1=(xi+1)&m,y0=(yi&m)*Size,y1=((yi+1)&m)*Size,z0=(zi&m)*Size*Size,z1=((zi+1)&m)*Size*Size;
            double L(double a,double b,double t)=>a+(b-a)*t;
            return L(L(L(grid[z0+y0+x0],grid[z0+y0+x1],fx),L(grid[z0+y1+x0],grid[z0+y1+x1],fx),fy),L(L(grid[z1+y0+x0],grid[z1+y0+x1],fx),L(grid[z1+y1+x0],grid[z1+y1+x1],fx),fy),fz);
        }
        internal double Fbm(double x,double y,double z,int octaves,double frequency)
        {
            double sum=0,amplitude=.5,total=0;
            x*=frequency;y*=frequency;z*=frequency;
            for(int octave=0;octave<octaves;octave++) {
                sum+=amplitude*Noise(x,y,z);total+=amplitude;amplitude*=.52;
                x=x*2.03+17;y=y*2.03+9;z=z*2.03+5;
            }
            return sum/total;
        }
    }

    private readonly record struct Rgb(double R,double G,double B)
    {
        internal static Rgb Mix(Rgb a,Rgb b,double t)=>new(a.R+(b.R-a.R)*t,a.G+(b.G-a.G)*t,a.B+(b.B-a.B)*t);
        internal Rgb Times(double v)=>new(R*v,G*v,B*v);
    }
    private static double Step(double a,double b,double x) { double t=Math.Clamp((x-a)/(b-a),0,1);return t*t*(3-2*t); }

    // Returns BGRA diffuse pixels and, for worlds that glow, emissive pixels.
    internal static (byte[] Diffuse,byte[]? Emissive) Bake(PlanetKind kind,int seed)
    {
        var field=new Field(seed);
        var diffuse=new byte[Width*Height*4];
        byte[]? emissive=kind==PlanetKind.Lava ? new byte[Width*Height*4] : null;
        var craters=Craters(seed);
        Parallel.For(0,Height,new ParallelOptions { MaxDegreeOfParallelism=Math.Max(1,Environment.ProcessorCount/2) },row=>{
            double latitude=Math.PI*(row+.5)/Height,sinLat=Math.Sin(latitude),y=Math.Cos(latitude);
            for(int column=0;column<Width;column++) {
                double longitude=2*Math.PI*(column+.5)/Width,x=sinLat*Math.Cos(longitude),z=sinLat*Math.Sin(longitude);
                var color=Surface(kind,field,craters,x,y,z,out double glow);
                int index=(row*Width+column)*4;
                diffuse[index]=Byte(color.B);diffuse[index+1]=Byte(color.G);diffuse[index+2]=Byte(color.R);diffuse[index+3]=255;
                if(emissive!=null) { emissive[index]=Byte(glow*.08);emissive[index+1]=Byte(glow*.38);emissive[index+2]=Byte(glow);emissive[index+3]=255; }
            }
        });
        return (diffuse,emissive);
    }
    private static byte Byte(double v)=>(byte)Math.Clamp(v*255,0,255);

    private static (double X,double Y,double Z,double R)[] Craters(int seed)
    {
        var random=new Random(seed^0x5a5a);
        return Enumerable.Range(0,90).Select(_=>{
            double z=random.NextDouble()*2-1,a=random.NextDouble()*Math.PI*2,s=Math.Sqrt(1-z*z),u=random.NextDouble();
            return (s*Math.Cos(a),s*Math.Sin(a),z,.012+.11*u*u*u*u);
        }).ToArray();
    }

    private static Rgb Surface(PlanetKind kind,Field f,(double X,double Y,double Z,double R)[] craters,double x,double y,double z,out double glow)
    {
        glow=0;
        switch(kind) {
            case PlanetKind.Rock: {
                double h=f.Fbm(x,y,z,8,2.4),shade=1;
                foreach(var c in craters) {
                    double dx=x-c.X,dy=y-c.Y,dz=z-c.Z,d=dx*dx+dy*dy+dz*dz;
                    if(d>c.R*c.R*2) continue;
                    d=Math.Sqrt(d)/c.R;
                    double side=dx*-.6+dy*.5;
                    shade+=d<1 ? -.16+side/c.R*.2 : Math.Exp(-(d-1)*(d-1)/.02)*.12;
                }
                return Rgb.Mix(new(.30,.20,.15),new(.68,.52,.40),Step(.34,.68,h)).Times(shade*(.8+.4*f.Fbm(x,y,z,5,14)));
            }
            case PlanetKind.Gas: {
                double warp=f.Fbm(x,y*4,z,5,3)-.5,band=y+warp*.06;
                double t=(Math.Sin(band*24+Math.Sin(band*7)*3)*.5+.5)*.65+f.Fbm(x*.5+warp,band*24,z*.5,6,1.5)*.55-.12;
                var low=Rgb.Mix(new(.42,.25,.20),new(.64,.44,.27),Step(0,.5,t));
                return Rgb.Mix(low,new(.88,.76,.58),Step(.5,1,t));
            }
            case PlanetKind.Ice: {
                double h=f.Fbm(x,y,z,8,2.1),q=f.Fbm(x,y,z,6,2.6);
                double crack=Math.Exp(-Math.Abs(q-.5)/.012)+Math.Exp(-Math.Abs(h-.5)/.007)*.6;
                return Rgb.Mix(new(.56,.72,.85),new(.92,.96,1),Step(.36,.64,h)).Times((1-.36*Math.Min(1,crack))*(.9+.25*f.Fbm(x,y,z,6,8)));
            }
            case PlanetKind.Lava: {
                double h=f.Fbm(x,y,z,8,2.1),q=f.Fbm(x+h,y-h,z,6,3);
                glow=Math.Min(1,Math.Exp(-Math.Abs(q-.5)/.016)*Step(.42,.56,h)*1.4);
                return Rgb.Mix(new(.05,.04,.04),new(.17,.13,.11),Step(.35,.7,h));
            }
            case PlanetKind.Ocean: {
                double w=f.Fbm(x,y,z,4,1.6)-.5,h=f.Fbm(x+w,y+w,z-w,9,1.6);
                double land=Step(.505,.51,h),depth=Math.Clamp((.505-h)/.1,0,1);
                var water=Rgb.Mix(new(.02,.10,.16),new(.01,.03,.10),depth);
                var ground=Rgb.Mix(new(.10,.20,.09),new(.42,.32,.20),Step(.4,.6,f.Fbm(x,y,z,6,3)));
                var color=Rgb.Mix(water,ground,land);
                color=Rgb.Mix(color,new(.88,.91,.95),Step(.72,.84,Math.Abs(y)+.12*(f.Fbm(x,y,z,5,5)-.5)));
                double q=f.Fbm(x,y*2.4,z,5,3.2)-.5,cloud=Step(.5,.68,f.Fbm(x+q,y*2.8+q,z-q,8,4.6));
                return Rgb.Mix(color,new(.95,.96,1),cloud*.95);
            }
            default: {
                double warp=f.Fbm(x,y,z,5,2)-.5,dunes=Math.Sin((y+warp*.5)*70+f.Fbm(x,y,z,5,5)*14)*.5+.5;
                double h=f.Fbm(x+warp,y,z-warp,8,2.2);
                var color=Rgb.Mix(new(.46,.20,.09),new(.82,.56,.30),Step(.36,.66,h));
                return Rgb.Mix(color,new(.90,.86,.78),Step(.86,.94,Math.Abs(y)+.1*warp)).Times(.88+.14*dunes);
            }
        }
    }

    internal static Color? Atmosphere(PlanetKind kind)=>kind switch {
        PlanetKind.Rock=>null,
        PlanetKind.Gas=>Color.FromRgb(214,170,120),
        PlanetKind.Ice=>Color.FromRgb(120,190,255),
        PlanetKind.Lava=>Color.FromRgb(255,110,40),
        PlanetKind.Ocean=>Color.FromRgb(90,160,255),
        _=>Color.FromRgb(240,150,90)
    };
}

// The timing of one scan. The expanding sphere is never drawn, so its radius is only "how far along the planet the front is":
// it takes a moment to arrive, crosses the planet in a fixed time however far away it is, then the lit lines fade.
internal readonly record struct ScanSweep(double Near,double Far)
{
    internal const double CrossSeconds=1,FadeSeconds=2;
    internal double Delay=>Math.Min(.8,Near/400);
    internal double Duration=>Delay+CrossSeconds+FadeSeconds;
    // 0 is the nearest point and 1 the farthest; past 1 the sphere has gone by and only the fade is left.
    internal double Front(double seconds)=>(seconds-Delay)/CrossSeconds;
    internal double Reach(Point3D point)=>Math.Clamp((((Vector3D)point).Length-Near)/Math.Max(Far-Near,1e-6),0,1);
    internal static ScanSweep Of(double distance,double extent)=>new(distance-extent,Math.Sqrt(Math.Max(distance*distance-extent*extent,0)));
}

// Real spheres on the GPU; only their transforms change, and only while visible and a planet is out there.
internal sealed class SpaceVoyage : Viewport3D
{
    internal const int MaxPlanets=3;
    private const double FadeInSeconds=8;

    // One planet on its way past: its own transforms, its fading brushes and its share of a scan.
    private sealed class Flight(Encounter encounter)
    {
        internal readonly Encounter Encounter=encounter;
        internal readonly ModelVisual3D Visual=new();
        internal readonly TranslateTransform3D Place=new();
        internal readonly AxisAngleRotation3D Spin=new(new Vector3D(0,1,0),0);
        internal readonly MatrixTransform3D HaloPlace=new();
        internal readonly List<Brush> Looks=[];
        internal readonly TranslateTransform3D ScanDrift=new();
        internal double Elapsed,Age;
        internal Model3DGroup Model=null!;
        internal ScanSweep Sweep;
        internal ImageBrush? ScanGlow;
        internal GeometryModel3D? ScanWire;
        internal double ScanZ;
        internal double Distance=>new Vector3D(Place.OffsetX,Place.OffsetY,Place.OffsetZ).Length;
    }

    private readonly VoyagePlan plan;
    private readonly DispatcherTimer timer=new(DispatcherPriority.Render) { Interval=TimeSpan.FromMilliseconds(33) };
    private readonly System.Diagnostics.Stopwatch clock=new();
    private readonly List<Flight> flights=[];
    private readonly ModelVisual3D scanHolder=new();
    private readonly Model3DGroup scanWires=new();
    private TimeSpan last;
    private Task<Flight>? baking;
    private double gap,scanSeconds;
    private bool first=true;

    internal bool IsSailing=>timer.IsEnabled;
    internal bool IsScanning=>flights.Any(flight=>flight.ScanGlow!=null);
    internal int PlanetCount=>flights.Count;
    internal int Passes { get; private set; }

    internal SpaceVoyage(int? seed=null)
    {
        plan=new(seed ?? Environment.TickCount);
        IsHitTestVisible=false;Focusable=false;ClipToBounds=true;
        Camera=new PerspectiveCamera(new Point3D(0,0,0),new Vector3D(0,0,-1),new Vector3D(0,1,0),55) { NearPlaneDistance=.1,FarPlaneDistance=12000 };
        var lights=new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(8,9,14)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(255,248,236),new Vector3D(.72,-.42,-.55)));
        Children.Add(new ModelVisual3D { Content=lights });
        scanHolder.Content=scanWires;
        Children.Add(scanHolder);
        timer.Tick+=(_,_)=>Advance();
        IsVisibleChanged+=(_,_)=>{ if(IsVisible) Begin(); else End(); };
    }

    private void Begin()
    {
        // Software rendering would redraw the whole screen on the CPU every frame.
        if(RenderCapability.Tier>>16<2) return;
        clock.Restart();last=TimeSpan.Zero;timer.Start();
    }
    private void End() { timer.Stop();clock.Stop();EndScan(); }

    private void Advance()
    {
        var now=clock.Elapsed;double step=(now-last).TotalSeconds;last=now;
        gap-=step;
        if(flights.Count<MaxPlanets) {
            baking ??=StartBake();
            if(baking is { IsCompletedSuccessfully:true } && gap<=0) {
                Launch(baking.Result);baking=null;
                gap=30+Random.Shared.NextDouble()*90;
            }
            else if(baking is { IsFaulted:true }) { baking=null;gap=30; }
        }
        foreach(var flight in flights.ToArray()) {
            flight.Elapsed+=step;flight.Age+=step;
            Place(flight);Fade(flight);
            if(!flight.Encounter.IsPast(flight.Elapsed)) continue;
            Remove(flight);Passes++;
        }
        if(IsScanning) SetScanTime(scanSeconds+step);
        DrawFarthestFirst();
    }

    private void Place(Flight flight)
    {
        var item=flight.Encounter;
        double x=item.X,y=item.Y,z=item.ZAt(flight.Elapsed);
        flight.Place.OffsetX=x;flight.Place.OffsetY=y;flight.Place.OffsetZ=z;
        flight.Spin.Angle=flight.Elapsed*item.Spin;
        flight.ScanDrift.OffsetZ=z-flight.ScanZ;
        // What the eye sees as the planet's edge is not its equator: it is the circle where lines from the eye
        // just touch the sphere, which moves toward the eye and shrinks as the planet nears. The glow sits on that circle.
        var eye=new Vector3D(-x,-y,-z);
        double distance=Math.Max(eye.Length,item.Radius*1.001),radius=item.Radius;
        eye.Normalize();
        double lift=radius*radius/distance,scale=radius*Math.Sqrt(1-radius*radius/(distance*distance));
        var right=Vector3D.CrossProduct(Math.Abs(eye.Y)<.9 ? new Vector3D(0,1,0) : new Vector3D(1,0,0),eye);right.Normalize();
        var up=Vector3D.CrossProduct(eye,right);
        flight.HaloPlace.Matrix=new(right.X*scale,right.Y*scale,right.Z*scale,0,up.X*scale,up.Y*scale,up.Z*scale,0,eye.X,eye.Y,eye.Z,0,eye.X*lift,eye.Y*lift,eye.Z*lift,1);
    }

    private static void Fade(Flight flight)
    {
        if(flight.Looks.Count==0) return;
        double opacity=Math.Min(1,flight.Age/FadeInSeconds);
        foreach(var look in flight.Looks) look.Opacity=opacity;
        if(opacity>=1) flight.Looks.Clear();
    }

    // Translucent parts only blend over what is already drawn, so a farther planet has to come first.
    private void DrawFarthestFirst()
    {
        int slot=1;
        foreach(var flight in flights.OrderByDescending(flight=>flight.Distance)) {
            if(!ReferenceEquals(Children[slot],flight.Visual)) { Children.Remove(flight.Visual);Children.Insert(slot,flight.Visual); }
            slot++;
        }
    }

    private void Remove(Flight flight)
    {
        if(flight.ScanWire!=null) scanWires.Children.Remove(flight.ScanWire);
        flight.ScanGlow=null;flight.ScanWire=null;
        flights.Remove(flight);Children.Remove(flight.Visual);
    }

    private void Launch(Flight flight)
    {
        // The first world is already nearby when the overlay opens, instead of an empty sky.
        if(first) flight.Elapsed=(-flight.Encounter.Extent*25-flight.Encounter.StartZ)/flight.Encounter.Speed;
        first=false;
        flights.Add(flight);Children.Insert(1,flight.Visual);
        Place(flight);Fade(flight);
    }

    // None if every candidate would share a path with a planet already out there; the next tick tries again.
    private Task<Flight>? StartBake()
    {
        for(int attempt=0;attempt<8;attempt++) {
            var item=plan.NextClear();
            if(flights.All(flight=>item.ClearOf(flight.Encounter))) {
                var dispatcher=Dispatcher;
                return Task.Run(()=>{
                    var (diffuse,emissive)=PlanetSurface.Bake(item.Kind,item.Seed);
                    return dispatcher.Invoke(()=>Build(item,diffuse,emissive));
                });
            }
        }
        return null;
    }

    internal void ShowForCheck(Encounter item,double z,bool add=false)
    {
        if(!add) foreach(var flight in flights.ToArray()) Remove(flight);
        var (diffuse,emissive)=PlanetSurface.Bake(item.Kind,item.Seed);
        var shown=Build(item,diffuse,emissive);
        shown.Elapsed=(z-item.StartZ)/item.Speed;shown.Age=FadeInSeconds;first=false;
        flights.Add(shown);Children.Insert(1,shown.Visual);
        Place(shown);Fade(shown);DrawFarthestFirst();
    }

    // One sweep at a time; a press while one is running, or with nothing out there, does nothing.
    internal bool Scan()
    {
        if(flights.Count==0 || IsScanning) return false;
        foreach(var flight in flights.OrderByDescending(flight=>flight.Distance)) AddScanWire(flight);
        SetScanTime(0);
        return true;
    }

    private void AddScanWire(Flight flight)
    {
        var item=flight.Encounter;
        var sweep=flight.Sweep=ScanSweep.Of(flight.Distance,item.Extent);
        var mesh=new MeshGeometry3D();
        var positions=new Point3DCollection();var coordinates=new PointCollection();var triangles=new Int32Collection();
        void Edge(Point3D a,Point3D b)
        {
            int start=positions.Count;
            var side=Vector3D.CrossProduct(b-a,(Vector3D)a);
            if(side.LengthSquared<1e-12) return;
            side.Normalize();
            foreach(var (point,sign) in new[]{(a,1),(a,-1),(b,1),(b,-1)}) {
                var toward=(Vector3D)point;double range=toward.Length;
                // Lifted just off the surface toward the eye, and about a pixel wide at any distance.
                positions.Add(point-toward/range*(item.Extent*.004)+side*(range*.00055*sign));
                coordinates.Add(new(sweep.Reach(point),.5));
            }
            foreach(int offset in new[]{0,1,2,2,1,3}) triangles.Add(start+offset);
        }
        var world=flight.Model.Children[0].Transform.Value*flight.Place.Value;
        const int Stacks=18,Slices=36;
        Point3D Globe(int row,int column) { double latitude=Math.PI*row/Stacks,longitude=2*Math.PI*column/Slices;return world.Transform(new Point3D(Math.Sin(latitude)*Math.Cos(longitude),Math.Cos(latitude),Math.Sin(latitude)*Math.Sin(longitude))); }
        for(int row=0;row<=Stacks;row++)
            for(int column=0;column<Slices;column++) {
                if(row is >0 and <Stacks) Edge(Globe(row,column),Globe(row,column+1));
                if(row<Stacks) Edge(Globe(row,column),Globe(row+1,column));
            }
        if(item.HasRing) {
            var ring=flight.Model.Children[1].Transform.Value*flight.Place.Value;
            double inner=item.Radius*1.32,outer=item.Radius*Encounter.RingScale;
            const int Bands=4,Spokes=72;
            Point3D Disc(int band,int spoke) { double radius=inner+(outer-inner)*band/Bands,angle=2*Math.PI*spoke/Spokes;return ring.Transform(new Point3D(Math.Cos(angle)*radius,0,Math.Sin(angle)*radius)); }
            for(int spoke=0;spoke<Spokes;spoke++)
                for(int band=0;band<=Bands;band++) {
                    Edge(Disc(band,spoke),Disc(band,spoke+1));
                    if(band<Bands && spoke%2==0) Edge(Disc(band,spoke),Disc(band+1,spoke));
                }
        }
        positions.Freeze();coordinates.Freeze();triangles.Freeze();
        mesh.Positions=positions;mesh.TextureCoordinates=coordinates;mesh.TriangleIndices=triangles;mesh.Freeze();
        // Moving this strip along the lines is the whole animation; the geometry is never touched again.
        var strip=new byte[512*4];
        for(int x=0;x<512;x++) {
            double t=x/511d,alpha=t*t*.75+(t>.97 ? .25 : 0);
            strip[x*4]=(byte)(225*alpha);strip[x*4+1]=(byte)(245*alpha);strip[x*4+2]=(byte)(130*alpha);strip[x*4+3]=(byte)(255*alpha);
        }
        var image=BitmapSource.Create(512,1,96,96,PixelFormats.Pbgra32,null,strip,512*4);image.Freeze();
        flight.ScanGlow=new ImageBrush(image) { ViewportUnits=BrushMappingMode.Absolute,TileMode=TileMode.None };
        var look=new EmissiveMaterial(flight.ScanGlow);
        flight.ScanZ=flight.Place.OffsetZ;flight.ScanDrift.OffsetZ=0;
        flight.ScanWire=new GeometryModel3D(mesh,look) { BackMaterial=look,Transform=flight.ScanDrift };
        scanWires.Children.Add(flight.ScanWire);
    }

    private void SetScanTime(double seconds)
    {
        scanSeconds=seconds;
        double width=ScanSweep.FadeSeconds/ScanSweep.CrossSeconds;
        foreach(var flight in flights) {
            if(flight.ScanGlow==null) continue;
            if(seconds>=flight.Sweep.Duration) { scanWires.Children.Remove(flight.ScanWire!);flight.ScanGlow=null;flight.ScanWire=null; }
            else flight.ScanGlow.Viewport=new Rect(flight.Sweep.Front(seconds)-width,0,width,1);
        }
    }

    private void EndScan()
    {
        scanWires.Children.Clear();
        foreach(var flight in flights) { flight.ScanGlow=null;flight.ScanWire=null; }
    }

    internal void SetScanTimeForCheck(double seconds)=>SetScanTime(seconds);

    private static ImageBrush Texture(byte[] pixels,int width,int height,List<Brush> looks)
    {
        var image=BitmapSource.Create(width,height,96,96,PixelFormats.Pbgra32,null,pixels,width*4);image.Freeze();
        var brush=new ImageBrush(image) { ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,1,1),Opacity=0 };
        RenderOptions.SetBitmapScalingMode(brush,BitmapScalingMode.Linear);
        looks.Add(brush);
        return brush;
    }

    private Flight Build(Encounter item,byte[] diffuse,byte[]? emissive)
    {
        var flight=new Flight(item);
        var material=new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(Texture(diffuse,PlanetSurface.Width,PlanetSurface.Height,flight.Looks)));
        if(emissive!=null) material.Children.Add(new EmissiveMaterial(Texture(emissive,PlanetSurface.Width,PlanetSurface.Height,flight.Looks)));
        var body=new GeometryModel3D(SphereMesh.Value,material);
        var turn=new Transform3DGroup();
        turn.Children.Add(new RotateTransform3D(flight.Spin));
        turn.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1),item.Tilt*57.3)));
        turn.Children.Add(new ScaleTransform3D(item.Radius,item.Radius,item.Radius));
        body.Transform=turn;
        var group=new Model3DGroup();group.Children.Add(body);
        if(item.HasRing) group.Children.Add(Ring(item,flight.Looks));
        if(PlanetSurface.Atmosphere(item.Kind) is { } air) group.Children.Add(Halo(air,flight));
        group.Transform=flight.Place;
        flight.Model=group;flight.Visual.Content=group;
        return flight;
    }

    private static GeometryModel3D Ring(Encounter item,List<Brush> looks)
    {
        const int Segments=128;
        double inner=item.Radius*1.32,outer=item.Radius*Encounter.RingScale;
        var mesh=new MeshGeometry3D();
        for(int i=0;i<=Segments;i++) {
            double a=2*Math.PI*i/Segments,c=Math.Cos(a),s=Math.Sin(a);
            mesh.Positions.Add(new(c*inner,0,s*inner));mesh.Positions.Add(new(c*outer,0,s*outer));
            mesh.TextureCoordinates.Add(new(0,.5));mesh.TextureCoordinates.Add(new(1,.5));
            if(i==Segments) continue;
            int b=i*2;
            foreach(int v in new[]{b,b+2,b+1,b+1,b+2,b+3}) mesh.TriangleIndices.Add(v);
        }
        mesh.Freeze();
        var random=new Random(item.Seed);
        var bands=new byte[512*4];
        for(int x=0;x<512;x++) {
            double density=.25+.75*random.NextDouble(),gapBand=x is >310 and <340 ? .08 : 1,fade=Math.Min(1,Math.Min(x,511-x)/12d);
            double alpha=density*gapBand*fade*.85;
            bands[x*4]=(byte)(150*alpha);bands[x*4+1]=(byte)(180*alpha);bands[x*4+2]=(byte)(205*alpha);bands[x*4+3]=(byte)(255*alpha);
        }
        var look=new EmissiveMaterial(Texture(bands,512,1,looks));
        var model=new GeometryModel3D(mesh,look) { BackMaterial=look,Transform=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1,0,.3),18+item.Tilt*20)) };
        return model;
    }

    // A disc facing the eye on the planet's visible edge, in units of that edge's radius; Place keeps it there.
    private static GeometryModel3D Halo(Color air,Flight flight)
    {
        const double Size=1.14;
        double edge=1/Size;
        var mesh=new MeshGeometry3D {
            Positions=[new(-Size,-Size,0),new(Size,-Size,0),new(Size,Size,0),new(-Size,Size,0)],
            TextureCoordinates=[new(0,1),new(1,1),new(1,0),new(0,0)],TriangleIndices=[0,1,2,0,2,3]
        };mesh.Freeze();
        Color At(byte alpha)=>Color.FromArgb(alpha,air.R,air.G,air.B);
        var glow=new RadialGradientBrush { GradientStops=[new(At(0),edge-.02),new(At(170),edge),new(At(60),edge+(1-edge)*.3),new(At(0),1)],Opacity=0 };
        flight.Looks.Add(glow);
        return new GeometryModel3D(mesh,new EmissiveMaterial(glow)) { Transform=flight.HaloPlace };
    }

    private static readonly Lazy<MeshGeometry3D> SphereMesh=new(()=>{
        const int Stacks=64,Slices=128;
        var mesh=new MeshGeometry3D();
        for(int row=0;row<=Stacks;row++) {
            double latitude=Math.PI*row/Stacks,y=Math.Cos(latitude),ring=Math.Sin(latitude);
            for(int column=0;column<=Slices;column++) {
                double longitude=2*Math.PI*column/Slices,x=ring*Math.Cos(longitude),z=ring*Math.Sin(longitude);
                mesh.Positions.Add(new(x,y,z));mesh.Normals.Add(new(x,y,z));
                mesh.TextureCoordinates.Add(new(column/(double)Slices,row/(double)Stacks));
            }
        }
        for(int row=0;row<Stacks;row++)
            for(int column=0;column<Slices;column++) {
                int a=row*(Slices+1)+column,b=a+Slices+1;
                foreach(int v in new[]{a,a+1,b,a+1,b+1,b}) mesh.TriangleIndices.Add(v);
            }
        mesh.Freeze();
        return mesh;
    },LazyThreadSafetyMode.ExecutionAndPublication);
}
