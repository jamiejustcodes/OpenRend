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
        var position = (decimal)sourceFrame * rate.Denominator / rate.Numerator;
        var duration = (decimal)durationSeconds;
        if (mode == VideoSourceMode.HoldLastFrame) return (double)Math.Clamp(position, 0, duration);
        var wrapped = position % duration;
        return (double)(wrapped < 0 ? wrapped + duration : wrapped);
    }
}
