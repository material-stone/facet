using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Reflection;
using Facet;
using Rectangle=System.Drawing.Rectangle;

internal static partial class Program
{
    private sealed class ControlledBackdrop : IBackdropCaptureSource
    {
        internal readonly ManualResetEventSlim Entered=new(false),Continue=new(true);
        internal readonly List<long> Starts=[];
        private int calls;
        internal int Calls=>Volatile.Read(ref calls);
        internal int CaptureThread,CopyThread,DisposeThread;
        internal bool ThrowFirst,Fail;
        public int PixelWidth {get;private set;}
        public int PixelHeight {get;private set;}
        public bool Capture(Rectangle bounds) {
            CaptureThread=Environment.CurrentManagedThreadId;Interlocked.Increment(ref calls);
            lock(Starts)Starts.Add(Stopwatch.GetTimestamp());
            Entered.Set();Continue.Wait();
            if(ThrowFirst && Calls==1)throw new InvalidOperationException("Synthetic capture failure");
            double scale=Math.Min(1,1280d/Math.Max(bounds.Width,bounds.Height));
            PixelWidth=(int)Math.Ceiling(bounds.Width*scale);PixelHeight=(int)Math.Ceiling(bounds.Height*scale);return !Fail;
        }
        public void CopyPixels(byte[] destination) {CopyThread=Environment.CurrentManagedThreadId;Array.Fill(destination,(byte)Calls);}
        public void Dispose() {DisposeThread=Environment.CurrentManagedThreadId;}
    }
    private static void AwaitBackdrop(Func<bool> condition,bool pump=true)
    {
        var watch=Stopwatch.StartNew();
        while(!condition() && watch.ElapsedMilliseconds<3000) {if(pump)Pump(10);else Thread.Sleep(1);}
        if(!condition())throw new TimeoutException("Backdrop worker did not reach the expected state.");
    }
    private static void CheckBackdropCaptureSession()
    {
        var dispatcher=Application.Current.Dispatcher;int uiThread=Environment.CurrentManagedThreadId;
        var a=new Rectangle(0,0,40,20);var b=new Rectangle(80,0,30,20);
        var source=new ControlledBackdrop();source.Continue.Reset();
        var colors=new List<byte>();var buffers=new List<byte[]>();bool callbackOnUi=true;
        using(var session=new BackdropCaptureSession(dispatcher,(_,frame)=> {
            callbackOnUi&=Environment.CurrentManagedThreadId==uiThread;colors.Add(frame.Pixels![0]);buffers.Add(frame.Pixels);return true;
        },()=>source)) {
            try {
                session.Request(a);AwaitBackdrop(()=>source.Calls==1,pump:false);
                bool responsive=false;dispatcher.BeginInvoke(()=>responsive=true,DispatcherPriority.Input);Pump(20);
                Assert(responsive && source.DisposeThread==0,"UI processes input while a worker capture is deliberately blocked");
                for(int i=0;i<1000;i++)session.Request(a);
                Assert(source.Calls==1,"burst requests are coalesced while one capture is in flight");
                source.Continue.Set();AwaitBackdrop(()=>session.HasPendingFrame,pump:false);
                Assert(source.Calls==1 && colors.Count==0,"worker retains its pixel lease until the queued UI callback returns");
                AwaitBackdrop(()=>colors.Count==2);
                Assert(callbackOnUi && source.CaptureThread!=uiThread && source.CopyThread==source.CaptureThread,"GDI and buffer copy run on the owner worker and presentation runs on UI");
                Assert(colors.SequenceEqual(new byte[]{1,2}) && ReferenceEquals(buffers[0],buffers[1]) && session.BufferAllocations==1,"acknowledged frames reuse one buffer without overwriting unread pixels");
            }
            finally {source.Continue.Set();session.Dispose();}
            Assert(session.Stopped.Wait(2000) && source.DisposeThread==source.CaptureThread,"native source cleanup occurs on its worker without waiting for UI");
        }

        source=new ControlledBackdrop();source.Continue.Reset();int published=0;
        using(var session=new BackdropCaptureSession(dispatcher,(_,_)=>{published++;return true;},()=>source)) {
            try {
                session.Request(a);AwaitBackdrop(()=>source.Calls==1,pump:false);session.Dispose();
                Assert(!session.Stopped.IsCompleted && source.DisposeThread==0 && !session.Request(b),"dispose returns before blocked capture finishes and rejects new requests");
            }
            finally {source.Continue.Set();session.Dispose();}
            Assert(session.Stopped.Wait(2000) && published==0 && source.DisposeThread!=uiThread,"cancelled in-flight pixels are discarded and handles release after capture returns");
        }

        source=new ControlledBackdrop();published=0;
        using(var session=new BackdropCaptureSession(dispatcher,(_,_)=>{published++;return true;},()=>source)) {
            session.Request(a);AwaitBackdrop(()=>session.HasPendingFrame,pump:false);session.Dispose();
            Assert(session.Stopped.Wait(2000),"cancelling a pending Dispatcher delivery stops the worker without UI pumping");Pump(20);
            Assert(published==0,"a cancelled queued frame cannot mutate UI after hiding");
        }

        source=new ControlledBackdrop();var boundsReceived=new List<Rectangle>();
        using(var session=new BackdropCaptureSession(dispatcher,(_,frame)=>{boundsReceived.Add(frame.Bounds);return true;},()=>source)) {
            session.Request(a);AwaitBackdrop(()=>session.HasPendingFrame,pump:false);session.Request(b);
            AwaitBackdrop(()=>boundsReceived.Count==1);
            Assert(boundsReceived.Single()==b && source.Calls==2 && session.BufferAllocations==2,"monitor and size changes discard queued old coordinates and replace the buffer");
        }

        source=new ControlledBackdrop {ThrowFirst=true};int failures=0,successes=0;
        using(var session=new BackdropCaptureSession(dispatcher,(_,frame)=>{if(frame.Pixels==null)failures++;else successes++;return frame.Pixels!=null;},()=>source)) {
            session.Request(a);AwaitBackdrop(()=>failures==1);session.Request(a);AwaitBackdrop(()=>successes==1);
            Assert(session.LastFailure is InvalidOperationException && !session.Stopped.IsCompleted,"a capture failure leaves UI responsive and the same worker can recover");
        }

        foreach(bool fail in new[]{false,true}) {
            source=new ControlledBackdrop {Fail=fail};published=0;
            using var session=new BackdropCaptureSession(dispatcher,(_,frame)=>{published++;return frame.Pixels!=null;},()=>source,singleFrame:true);
            for(int i=0;i<1000;i++)session.Request(a);
            AwaitBackdrop(()=>session.Stopped.IsCompleted);
            Assert(published==1 && source.Calls==1 && source.DisposeThread!=uiThread,"single-frame fallback completes and releases its source exactly once, failure="+fail);
        }

        var first=new ControlledBackdrop();first.Continue.Reset();var second=new ControlledBackdrop();var third=new ControlledBackdrop();
        using var old=new BackdropCaptureSession(dispatcher,(_,_)=>true,()=>first);
        old.Request(a);AwaitBackdrop(()=>first.Calls==1,pump:false);old.Dispose();
        using var skipped=new BackdropCaptureSession(dispatcher,(_,_)=>true,()=>second,afterStop:old.Stopped);skipped.Request(a);skipped.Dispose();
        bool overlapped=false;int latestFrames=0;
        using var latest=new BackdropCaptureSession(dispatcher,(_,_)=>{latestFrames++;return true;},()=>{overlapped=first.DisposeThread==0;return third;},afterStop:skipped.Stopped);
        try {
            latest.Request(b);Pump(20);
            Assert(latest.WorkerThreadId==0 && third.Calls==0,"rapid reopen waits asynchronously for the previous native owner to stop");
        }
        finally {first.Continue.Set();}
        AwaitBackdrop(()=>latestFrames==1);
        Assert(!overlapped && second.Calls==0 && skipped.WorkerThreadId==0,"superseded reopen sessions never allocate a native source or overlap GDI owners");
        latest.Dispose();Assert(latest.Stopped.Wait(2000),"latest reopened worker stops cleanly");

        source=new ControlledBackdrop();int rateFrames=0;
        using(var session=new BackdropCaptureSession(dispatcher,(owner,_)=>{rateFrames++;if(rateFrames<5)owner.Request(a);return true;},()=>source)) {
            session.Request(a);AwaitBackdrop(()=>rateFrames==5);
            long[] starts;lock(source.Starts)starts=source.Starts.ToArray();
            var intervals=starts.Zip(starts.Skip(1),(x,y)=>(y-x)*1000d/Stopwatch.Frequency).ToArray();
            Assert(intervals.All(ms=>ms>=49),"capture start cadence stays bounded to at most twenty per second: "+string.Join(",",intervals));
        }
    }

    private static void CheckBackdropWindowFallback(Controller controller)
    {
        var overlay=controller.Overlay;var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var sessionField=typeof(OverlayWindow).GetField("capture",flags)!;
        var settings=controller.Data.Settings;
        double priorBlur=settings.OverlayBlurRadius;int priorShow=settings.OverlayShowMilliseconds,priorHide=settings.OverlayHideMilliseconds;
        var source=new ControlledBackdrop();source.Continue.Reset();
        try {
            controller.HideOverlay();
            controller.Commit(d=> {d.Settings.OverlayBlurRadius=18;d.Settings.OverlayShowMilliseconds=0;d.Settings.OverlayHideMilliseconds=0;});
            overlay.BackdropSourceFactory=()=>source;
            controller.ToggleOverlay();AwaitBackdrop(()=>source.Calls==1,pump:false);
            var pending=(BackdropCaptureSession)sessionField.GetValue(overlay)!;
            Assert(!overlay.IsVisible,"capturable overlay reads its first frame while hidden");
            controller.HideOverlay();
            Assert(!overlay.IsVisible && sessionField.GetValue(overlay)==null && !pending.Stopped.IsCompleted,"hiding before the first frame detaches capture without waiting for blocked GDI");
            source.Continue.Set();Assert(pending.Stopped.Wait(2000),"cancelled first-frame fallback releases its worker");Pump(30);
            Assert(!overlay.IsVisible,"late first-frame completion cannot reopen a cancelled overlay");
            foreach(bool fail in new[]{false,true}) {
                source=new ControlledBackdrop {Fail=fail};overlay.BackdropSourceFactory=()=>source;
                controller.ToggleOverlay();var session=(BackdropCaptureSession)sessionField.GetValue(overlay)!;
                AwaitBackdrop(()=>overlay.IsVisible && session.Stopped.IsCompleted);Pump(80);
                var image=(Image)((Grid)overlay.Content).Children[0];
                Assert(source.Calls==1 && source.DisposeThread!=Environment.CurrentManagedThreadId && image.Effect is BlurEffect {Radius:18} && (image.Source==null)==fail,
                    "fallback reveals once without self recapture and preserves blur configuration, failure="+fail);
                controller.HideOverlay();
            }
        }
        finally {
            source.Continue.Set();controller.HideOverlay();overlay.BackdropSourceFactory=null;
            controller.Commit(d=> {d.Settings.OverlayBlurRadius=priorBlur;d.Settings.OverlayShowMilliseconds=priorShow;d.Settings.OverlayHideMilliseconds=priorHide;});
        }
    }
}
