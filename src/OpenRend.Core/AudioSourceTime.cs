namespace OpenRend.Core;

public static class AudioSourceTime
{
    public const int MixSampleRate = 48_000;

    public static double SampleAt(double seconds) => Math.Round(seconds * MixSampleRate, MidpointRounding.AwayFromZero);

    public static long SampleCount(Int128 ticks, long ticksPerSecond) =>
        checked((long)((ticks * MixSampleRate + ticksPerSecond - 1) / ticksPerSecond));

    public static double SourceSampleCount(MediaAsset asset) => asset.SourceRange is { } range
        ? SampleCount(range.TickCount, range.TicksPerSecond) : SampleAt(asset.DurationSeconds);
}
