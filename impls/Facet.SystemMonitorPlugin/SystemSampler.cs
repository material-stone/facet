using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Facet.SystemMonitorPlugin;

// One reading. Gpu is null where the GPU counters are unavailable (no driver support, remote session).
public sealed record SystemSample(double Cpu, double? Gpu, double Ram, double RamUsedGb, double RamTotalGb, double DownBytesPerSecond, double UpBytesPerSecond);

// Pure arithmetic, kept apart from the OS calls so it can be checked.
public static class SystemMath
{
    // Kernel time includes idle time, as GetSystemTimes reports it.
    public static double CpuPercent(long idleDelta, long kernelDelta, long userDelta)
    {
        long total = kernelDelta + userDelta;
        if (total <= 0) return 0;
        return Math.Clamp((1 - (double)idleDelta / total) * 100, 0, 100);
    }

    // A counter that went backwards (adapter reset, interface removed) reads as no traffic, not a negative rate.
    public static double Rate(long previous, long current, double seconds) =>
        seconds <= 0 || current < previous ? 0 : (current - previous) / seconds;

    public static string FormatRate(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024) return $"{bytesPerSecond / (1024 * 1024):0.0} MB/s";
        if (bytesPerSecond >= 1024) return $"{bytesPerSecond / 1024:0} KB/s";
        return $"{bytesPerSecond:0} B/s";
    }
}

// Reads the machine once per second on a pool thread while anyone listens, and stops when nobody does.
public sealed class SystemSampler : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private readonly object gate = new();
    private readonly List<Action<SystemSample>> listeners = [];
    private Timer? timer;
    private GpuCounter? gpu;
    private bool gpuTried;
    private long idle, kernel, user, down, up;
    private DateTime readAt;
    private bool primed;
    private int busy;

    public SystemSample? Last { get; private set; }

    public IDisposable Subscribe(Action<SystemSample> listener)
    {
        lock (gate) {
            listeners.Add(listener);
            timer ??= new Timer(_ => Tick(), null, TimeSpan.Zero, Interval);
        }
        return new Subscription(this, listener);
    }

    private void Unsubscribe(Action<SystemSample> listener)
    {
        lock (gate) {
            listeners.Remove(listener);
            if (listeners.Count > 0) return;
            timer?.Dispose(); timer = null; primed = false;
        }
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref busy, 1) == 1) return;
        try {
            var sample = Read();
            if (sample is null) return;
            Last = sample;
            Action<SystemSample>[] copy;
            lock (gate) copy = [.. listeners];
            foreach (var listener in copy) listener(sample);
        } catch { } finally { Volatile.Write(ref busy, 0); }
    }

    // The first call only primes the deltas, so it returns null.
    public SystemSample? Read()
    {
        var now = DateTime.UtcNow;
        Native.GetSystemTimes(out long i, out long k, out long u);
        var (d, t) = NetworkBytes();
        if (!gpuTried) { gpuTried = true; gpu = GpuCounter.TryCreate(); }
        double? gpuPercent = gpu?.Read();
        bool ready = primed;
        double seconds = (now - readAt).TotalSeconds;
        var result = ready ? new SystemSample(
            SystemMath.CpuPercent(i - idle, k - kernel, u - user), gpuPercent, 0, 0, 0,
            SystemMath.Rate(down, d, seconds), SystemMath.Rate(up, t, seconds)) : null;
        idle = i; kernel = k; user = u; down = d; up = t; readAt = now; primed = true;
        if (result is null) return null;
        var memory = new Native.MemoryStatus { Length = (uint)Marshal.SizeOf<Native.MemoryStatus>() };
        if (!Native.GlobalMemoryStatusEx(ref memory)) return result;
        const double Gb = 1024d * 1024 * 1024;
        double total = memory.TotalPhys / Gb, used = (memory.TotalPhys - memory.AvailPhys) / Gb;
        return result with { Ram = total > 0 ? Math.Clamp(used / total * 100, 0, 100) : 0, RamUsedGb = used, RamTotalGb = total };
    }

    private static (long Down, long Up) NetworkBytes()
    {
        long d = 0, u = 0;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()) {
            if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try { var stats = adapter.GetIPStatistics(); d += stats.BytesReceived; u += stats.BytesSent; } catch (NetworkInformationException) { }
        }
        return (d, u);
    }

    public void Dispose()
    {
        lock (gate) { listeners.Clear(); timer?.Dispose(); timer = null; }
        gpu?.Dispose(); gpu = null;
    }

    private sealed class Subscription(SystemSampler owner, Action<SystemSample> listener) : IDisposable
    {
        private int done;
        public void Dispose() { if (Interlocked.Exchange(ref done, 1) == 0) owner.Unsubscribe(listener); }
    }

    // Sums the 3D engine utilisation of every process, as Task Manager's GPU column does.
    private sealed class GpuCounter : IDisposable
    {
        private const uint MoreData = 0x800007D2, FormatDouble = 0x200, NoCap = 0x8000;
        private IntPtr query, counter;

        public static GpuCounter? TryCreate()
        {
            var g = new GpuCounter();
            if (Native.PdhOpenQuery(null, IntPtr.Zero, out g.query) != 0) return null;
            if (Native.PdhAddEnglishCounter(g.query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out g.counter) != 0 || Native.PdhCollectQueryData(g.query) != 0) { g.Dispose(); return null; }
            return g;
        }

        public unsafe double? Read()
        {
            if (Native.PdhCollectQueryData(query) != 0) return null;
            uint size = 0, count = 0;
            uint status = Native.PdhGetFormattedCounterArray(counter, FormatDouble | NoCap, ref size, ref count, IntPtr.Zero);
            if (status == 0) return 0;
            if (status != MoreData || size == 0) return null;
            var buffer = Marshal.AllocHGlobal((int)size);
            try {
                if (Native.PdhGetFormattedCounterArray(counter, FormatDouble | NoCap, ref size, ref count, buffer) != 0) return null;
                double sum = 0;
                var items = (Native.CounterItem*)buffer;
                for (int n = 0; n < count; n++) if (items[n].Status == 0) sum += items[n].Value;
                return Math.Clamp(sum, 0, 100);
            } finally { Marshal.FreeHGlobal(buffer); }
        }

        public void Dispose() { if (query != IntPtr.Zero) Native.PdhCloseQuery(query); query = IntPtr.Zero; }
    }

    private static class Native
    {
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] public static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, ref uint count, IntPtr items);
        [DllImport("pdh.dll")] public static extern uint PdhCloseQuery(IntPtr query);

        [StructLayout(LayoutKind.Sequential)]
        public struct MemoryStatus
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        // PDH_FMT_COUNTERVALUE_ITEM_W with a double value: name, status, padding, value.
        [StructLayout(LayoutKind.Sequential)]
        public struct CounterItem { public IntPtr Name; public uint Status; public double Value; }
    }
}
