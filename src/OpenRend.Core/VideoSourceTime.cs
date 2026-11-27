namespace OpenRend.Core;

public enum VideoSourceMode { Bounded, Loop, HoldLastFrame }

public static class VideoSourceTime
{
    public static double Seconds(Rational rate, long sourceFrame, double durationSeconds, VideoSourceMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == VideoSourceMode.Bounded) return rate.Seconds(sourceFrame);
        if (!double.IsFinite(durationSeconds) || durationSeconds < 1e-28 || durationSeconds > (double)decimal.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        // Reduce the continuous source phase against time, not a rounded project-frame loop length.
        var position = Position(rate, sourceFrame);
        var duration = (decimal)durationSeconds;
        if (mode == VideoSourceMode.HoldLastFrame) return (double)Math.Clamp(position, 0, duration);
        var wrapped = position % duration;
        return (double)(wrapped < 0 ? wrapped + duration : wrapped);
    }

    public static double Region(Rational rate, long sourceFrame, double durationSeconds, VideoSourceMode mode)
    {
        var position = Position(rate, sourceFrame);
        var duration = (decimal)durationSeconds;
        if (mode == VideoSourceMode.HoldLastFrame) return position < 0 ? -1 : position >= duration ? 1 : 0;

        // Classify the same decimal wrap used by Seconds, including negative source offsets.
        var remainder = position % duration;
        return Math.Round((double)(position - remainder) / (double)duration) - (remainder < 0 ? 1 : 0);
    }

    private static decimal Position(Rational rate, long sourceFrame) => (decimal)sourceFrame * rate.Denominator / rate.Numerator;
}
