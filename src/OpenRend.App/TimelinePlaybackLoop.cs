using OpenRend.Core;
using OpenRend.Media;

namespace OpenRend.App;

internal sealed class TimelinePlaybackLoop
{
    private readonly FrameRange range;
    private readonly Rational rate;
    private readonly long frameUnits;
    private readonly Int128 cycleUnits;
    private readonly Int128 initialUnits;

    public TimelinePlaybackLoop(FrameRange range, Rational rate, long startFrame)
    {
        if (startFrame < range.StartFrame || startFrame >= range.EndFrame)
            throw new ArgumentOutOfRangeException(nameof(startFrame));
        this.range = range;
        this.rate = rate;
        // Count samples in fractions of 1 / rate.Numerator so wrap rounding never accumulates.
        frameUnits = (long)MediaService.SampleRate * rate.Denominator;
        cycleUnits = (Int128)range.FrameCount * frameUnits;
        initialUnits = (Int128)(startFrame - range.StartFrame) * frameUnits;
        SampleCount = CeilingSamples(cycleUnits);
    }

    public long SampleCount { get; }
    public double SecondsAt(double cycleSample) => rate.Seconds(range.StartFrame) + cycleSample / MediaService.SampleRate;

    public (double Offset, int Length) Window(long elapsed, int maximumLength)
    {
        var phase = PhaseAt(elapsed);
        return ((double)phase / rate.Numerator, (int)Math.Min(maximumLength, CeilingSamples(cycleUnits - phase)));
    }

    public long FrameAt(long elapsed) => range.StartFrame + (long)(PhaseAt(elapsed) / frameUnits);

    private Int128 PhaseAt(long elapsed) => (initialUnits + (Int128)elapsed * rate.Numerator) % cycleUnits;
    private long CeilingSamples(Int128 units) => checked((long)((units + rate.Numerator - 1) / rate.Numerator));
}
