namespace OpenRend.Core;

public enum VideoFadeCurve { Linear, Fast, Slow, Smooth, Sharp }

public static class VideoFadeInterpolation
{
    public static double Evaluate(VideoFadeCurve curve, double progress)
    {
        if (!double.IsFinite(progress)) throw new ArgumentOutOfRangeException(nameof(progress));
        var t = Math.Clamp(progress, 0, 1);
        return curve switch
        {
            VideoFadeCurve.Linear => t,
            VideoFadeCurve.Fast => 1 - (1 - t) * (1 - t),
            VideoFadeCurve.Slow => t * t,
            VideoFadeCurve.Smooth => t * t * (3 - 2 * t),
            VideoFadeCurve.Sharp => .5 + 4 * (t - .5) * (t - .5) * (t - .5),
            _ => throw new ArgumentOutOfRangeException(nameof(curve))
        };
    }
}
