using System.Text.Json.Serialization;

namespace OpenRend.Core;

public readonly record struct MediaSourceRange(long StartTick, long TickCount, long TicksPerSecond,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IsReversed = false)
{
    [JsonIgnore] public double StartSeconds => (double)StartTick / TicksPerSecond;
    [JsonIgnore] public double EndSeconds => (double)checked(StartTick + TickCount) / TicksPerSecond;
    [JsonIgnore] public double DurationSeconds => (double)TickCount / TicksPerSecond;

    public FrameRange OnFrameGrid(Rational rate)
    {
        var divisor = (Int128)TicksPerSecond * rate.Denominator;
        long FrameAt(long tick) => checked((long)(((Int128)tick * rate.Numerator + divisor / 2) / divisor));
        var start = FrameAt(StartTick);
        return new(start, FrameAt(checked(StartTick + TickCount)) - start);
    }

    public static MediaSourceRange FromFrames(FrameRange frames, Rational rate, MediaSourceRange? parent = null, bool reverse = false)
    {
        var parentRate = parent?.TicksPerSecond ?? 1;
        // A shared integer timebase preserves both the parent offset and the new frame grid.
        var scale = checked(parentRate / Gcd(parentRate, rate.Numerator) * rate.Numerator);
        var projectFrameTicks = (Int128)rate.Denominator * (scale / rate.Numerator);
        var parentScale = scale / parentRate;
        var first = frames.StartFrame * projectFrameTicks;
        var last = first + frames.FrameCount * projectFrameTicks;
        var origin = (Int128)(parent?.StartTick ?? 0) * parentScale;
        if (parent is { } bounded)
        {
            var parentLength = (Int128)bounded.TickCount * parentScale;
            last = Int128.Min(last, parentLength);
            if (bounded.IsReversed)
            {
                // Convert a selection on the reversed local grid to the original file's forward interval.
                (first, last) = (parentLength - last, parentLength - first);
                reverse = !reverse;
            }
        }

        var start = checked((long)(origin + first));
        var count = checked((long)(last - first));
        _ = checked(start + count);
        if (start < 0 || count <= 0) throw new ArgumentOutOfRangeException(nameof(frames));
        var divisor = Gcd(Gcd(start, count), scale);
        return new(start / divisor, count / divisor, scale / divisor, reverse);
    }

    internal void Validate()
    {
        if (StartTick < 0 || TickCount <= 0 || TicksPerSecond <= 0 || StartTick > long.MaxValue - TickCount)
            throw new InvalidDataException("Media source range is invalid.");
    }

    private static long Gcd(long left, long right)
    {
        while (right != 0) (left, right) = (right, left % right);
        return left;
    }
}
