using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Rectangle = System.Drawing.Rectangle;

namespace Facet;

internal interface IBackdropCaptureSource : IDisposable
{
    int PixelWidth { get; }
    int PixelHeight { get; }
    bool Capture(Rectangle bounds);
    void CopyPixels(byte[] destination);
}

// Pixels are a lease: the worker cannot reuse them until the UI callback returns.
internal readonly record struct BackdropPixels(Rectangle Bounds,int Width,int Height,byte[]? Pixels);

internal sealed class BackdropCaptureSession : IDisposable
{
    private readonly object gate = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly CancellationTokenSource stop = new();
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dispatcher dispatcher;
    private readonly Func<BackdropCaptureSession,BackdropPixels,bool> publish;
    private readonly Func<IBackdropCaptureSource> sourceFactory;
    private readonly bool singleFrame;
    private Rectangle latestBounds;
    private long revision;
    private bool disposed;
    private int capturedFrames,presentedFrames,pendingFrame,bufferAllocations,workerThreadId;
    internal bool MeasureUpdates { get; set; }
    internal int CapturedFrames => Volatile.Read(ref capturedFrames);
    internal int PresentedFrames => Volatile.Read(ref presentedFrames);
    internal bool HasPendingFrame => Volatile.Read(ref pendingFrame)!=0;
    internal int BufferAllocations => Volatile.Read(ref bufferAllocations);
    internal int WorkerThreadId => Volatile.Read(ref workerThreadId);
    internal Task Stopped => stopped.Task;
    internal Exception? LastFailure { get; private set; }
    internal readonly record struct Timings(double CaptureMs,double BufferCopyMs,double DispatchWaitMs,double UiApplyMs,int WorkerThreadId,int UiThreadId,bool Applied);
    internal Timings? LastTimings { get; private set; } // UI-thread diagnostic only.
    internal event Action<Timings>? Measured;

    internal BackdropCaptureSession(Dispatcher dispatcher,Func<BackdropCaptureSession,BackdropPixels,bool> publish,
        Func<IBackdropCaptureSource>? sourceFactory = null,bool singleFrame = false,Task? afterStop = null)
    {
        this.dispatcher=dispatcher;this.publish=publish;this.sourceFactory=sourceFactory??(()=>new BackdropCapture());this.singleFrame=singleFrame;
        if(afterStop==null || afterStop.IsCompleted) StartWorker();
        else _=afterStop.ContinueWith(_=>StartWorker(),CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
    }

    private void StartWorker()
    {
        lock(gate) {
            if(disposed) {wake.Dispose();stop.Dispose();stopped.TrySetResult();return;}
            new Thread(Run) {IsBackground=true,Name="Facet backdrop capture"}.Start();
        }
    }

    internal bool Request(Rectangle bounds)
    {
        lock(gate) {
            if(disposed) return false;
            if(latestBounds!=bounds) {latestBounds=bounds;revision++;}
            wake.Set();return true;
        }
    }

    private void Run()
    {
        Volatile.Write(ref workerThreadId,Environment.CurrentManagedThreadId);
        try {
            using var source=sourceFactory();
            var waits=new WaitHandle[]{wake,stop.Token.WaitHandle};
            byte[] buffer=[];long previousStart=0;
            while(WaitHandle.WaitAny(waits)==0) {
                if(previousStart!=0) {
                    double delay=50-Stopwatch.GetElapsedTime(previousStart).TotalMilliseconds;
                    while(delay>0) {
                        if(stop.Token.WaitHandle.WaitOne((int)Math.Ceiling(delay))) return;
                        delay=50-Stopwatch.GetElapsedTime(previousStart).TotalMilliseconds;
                    }
                }
                Rectangle bounds;long generation;
                lock(gate) {if(disposed) break;bounds=latestBounds;generation=revision;}
                previousStart=Stopwatch.GetTimestamp();
                bool measure=MeasureUpdates;double captureMs=0,copyMs=0;
                BackdropPixels frame=new(bounds,0,0,null);
                try {
                    long stamp=measure?Stopwatch.GetTimestamp():0;
                    bool success=source.Capture(bounds);
                    if(measure) {captureMs=Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;stamp=Stopwatch.GetTimestamp();}
                    if(success) {
                        if(stop.IsCancellationRequested) break;
                        int length=checked(source.PixelWidth*source.PixelHeight*4);
                        if(buffer.Length!=length) {buffer=new byte[length];Interlocked.Increment(ref bufferAllocations);}
                        source.CopyPixels(buffer);
                        if(measure) copyMs=Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
                        frame=new(bounds,source.PixelWidth,source.PixelHeight,buffer);
                        Interlocked.Increment(ref capturedFrames);
                    }
                }
                catch(Exception ex) {LastFailure=ex;}
                if(stop.IsCancellationRequested || dispatcher.HasShutdownStarted) break;
                try {
                    long queued=measure?Stopwatch.GetTimestamp():0;
                    bool delivered=false;
                    Volatile.Write(ref pendingFrame,1);
                    var operation=dispatcher.InvokeAsync(()=> {
                        lock(gate) {if(disposed || generation!=revision) return;}
                        double waitMs=measure?Stopwatch.GetElapsedTime(queued).TotalMilliseconds:0;
                        long stamp=measure?Stopwatch.GetTimestamp():0;
                        bool applied=publish(this,frame);
                        lock(gate) delivered=!disposed && generation==revision;
                        if(applied) Interlocked.Increment(ref presentedFrames);
                        if(measure) {
                            var timing=new Timings(captureMs,copyMs,waitMs,Stopwatch.GetElapsedTime(stamp).TotalMilliseconds,WorkerThreadId,Environment.CurrentManagedThreadId,applied);
                            LastTimings=timing;Measured?.Invoke(timing);
                        }
                    },DispatcherPriority.Background,stop.Token);
                    // Only this background thread waits; it retains the pixel lease until UI acknowledgement.
                    operation.Task.GetAwaiter().GetResult();
                    if(singleFrame && delivered) break;
                }
                catch(OperationCanceledException) when(stop.IsCancellationRequested || dispatcher.HasShutdownStarted) {break;}
                catch(Exception ex) {LastFailure=ex;}
                finally {Volatile.Write(ref pendingFrame,0);}
            }
        }
        catch(Exception ex) {LastFailure=ex;}
        finally {
            lock(gate) {disposed=true;wake.Dispose();stop.Dispose();}
            stopped.TrySetResult();
        }
    }

    public void Dispose()
    {
        lock(gate) {
            if(disposed) return;
            disposed=true;stop.Cancel();
        }
    }
}
