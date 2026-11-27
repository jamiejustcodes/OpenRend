namespace OpenRend.Core;

public sealed class TransitionProgressCurve
{
    public List<EnvelopePoint> Points { get; set; } = [new(0, 0)];
    public double EndValue { get; set; } = 1;

    public double ValueAt(long frame, long fadeFrames)
    {
        if (frame >= fadeFrames) return EndValue;
        if (frame <= 0) return Points[0].Value;

        var earlier = 0;
        var later = Points.Count;
        while (later - earlier > 1)
        {
            var middle = earlier + (later - earlier) / 2;
            if (Points[middle].Frame <= frame) earlier = middle;
            else later = middle;
        }

        var from = Points[earlier];
        // Resizing the overlap retains keys that now fall beyond the end.
        var to = later < Points.Count && Points[later].Frame < fadeFrames ? Points[later] : null;
        var toFrame = to?.Frame ?? fadeFrames;
        return from.InterpolateTo(to?.Value ?? EndValue, (double)(frame - from.Frame) / (toFrame - from.Frame));
    }

    public TransitionProgressCurve DeepClone() => new() { Points = Points.Select(point => point.DeepClone()).ToList(), EndValue = EndValue };

    public TransitionProgressCurve WithFrameRate(Func<long, long> convertFrame)
    {
        var mapped = new SortedDictionary<long, EnvelopePoint>();
        foreach (var point in Points)
        {
            var frame = convertFrame(point.Frame);
            // Keep the start anchor. At other collapsed frames the later key wins.
            if (frame != 0 || mapped.Count == 0) mapped[frame] = new EnvelopePoint(frame, point.Value, point.Curve);
        }
        return new TransitionProgressCurve { Points = [.. mapped.Values], EndValue = EndValue };
    }

    public void Validate()
    {
        if (Points is not { Count: > 0 } || Points[0]?.Frame != 0 || !double.IsFinite(EndValue) || EndValue is < 0 or > 1)
            throw new InvalidDataException("Transition progress requires a start point and an end value between 0 and 100%.");
        var previous = -1L;
        foreach (var point in Points)
        {
            if (point is null || point.Frame <= previous || point.Value is < 0 or > 1)
                throw new InvalidDataException("Transition progress points must be ordered, unique, and valued between 0 and 100%.");
            point.Validate();
            previous = point.Frame;
        }
    }
}
