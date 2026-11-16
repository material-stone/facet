namespace Facet.SystemMonitorPlugin;

// An under-damped spring: a new reading overshoots a little and settles, which makes the gauge feel alive.
public sealed class Spring(double stiffness = 170, double damping = 13)
{
    public double Value { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; set; }
    public bool Settled => Math.Abs(Target - Value) < 0.05 && Math.Abs(Velocity) < 0.05;

    public void Reset(double value) { Value = value; Velocity = 0; }

    // Fixed sub-steps keep it stable when a frame comes late.
    public void Step(double seconds)
    {
        while (seconds > 0) {
            double dt = Math.Min(seconds, 1 / 120.0);
            Velocity += (stiffness * (Target - Value) - damping * Velocity) * dt;
            Value += Velocity * dt;
            seconds -= dt;
        }
    }
}

// One metric's motion state: the spring, the over-limit alert and the peak marker.
public sealed class GaugeMotion
{
    public const double AlertAt = 90, ClearAt = 85, PeakHold = 2;
    public readonly Spring Spring = new();
    private double delay, peakAge;

    public bool Alert { get; private set; }
    // Both run from 1 down to 0: the ring that spreads out and the shake, started when the alert begins.
    public double Pulse { get; private set; }
    public double Shake { get; private set; }
    // 0 to 1, how far the ring has turned toward the alert color.
    public double Heat { get; private set; }
    public double Peak { get; private set; }
    public bool Available { get; private set; } = true;

    public double Shown => Math.Clamp(Spring.Value, 0, 100);

    // The boot sequence: everything drops to zero and each gauge waits its turn before rising.
    public void Boot(double wait) { Spring.Reset(0); Peak = 0; peakAge = 0; delay = wait; Alert = false; Heat = 0; Pulse = 0; Shake = 0; }

    public void Feed(double? value)
    {
        Available = value is not null;
        Spring.Target = value ?? 0;
        if (value is not double v) { Alert = false; return; }
        // Hysteresis, so a value around the limit does not flicker.
        if (!Alert && v >= AlertAt) { Alert = true; Pulse = 1; Shake = 1; }
        else if (Alert && v < ClearAt) Alert = false;
    }

    public void Step(double seconds)
    {
        if (delay > 0) { delay -= seconds; if (delay > 0) return; seconds = -delay; delay = 0; }
        Spring.Step(seconds);
        Pulse = Math.Max(0, Pulse - seconds * 1.6);
        Shake = Math.Max(0, Shake - seconds * 3.5);
        Heat += ((Alert ? 1 : 0) - Heat) * Math.Min(1, seconds * 6);
        if (Shown >= Peak) { Peak = Shown; peakAge = 0; }
        else if ((peakAge += seconds) > PeakHold) Peak = Math.Max(Shown, Peak - seconds * 25);
    }
}

// The last readings, oldest first, for the sparkline.
public sealed class History(int capacity)
{
    private readonly Queue<double> values = new();
    public int Capacity => capacity;
    public int Count => values.Count;
    public void Add(double value) { values.Enqueue(value); while (values.Count > capacity) values.Dequeue(); }
    public double[] ToArray() => [.. values];
    public double Max => values.Count == 0 ? 0 : values.Max();
}
