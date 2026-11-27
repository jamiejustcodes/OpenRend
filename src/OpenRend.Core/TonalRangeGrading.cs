namespace OpenRend.Core;

public readonly record struct LuminanceThresholds(double Lower, double Upper);

public static class TonalRangeGrading
{
    public const string Shadows = "RangeShadows";
    public const string Midtones = "RangeMidtones";
    public const string Highlights = "RangeHighlights";
    public const string Offset = "RangeOffset";
    public const string LowerKey = "RangeLower";
    public const string UpperKey = "RangeUpper";
    public const double DefaultLower = .3;
    public const double DefaultUpper = .7;

    private static readonly VideoEffectParameterDefinition[] WheelParameters =
        new[] { (Shadows, "Shadows"), (Midtones, "Midtones"), (Highlights, "Highlights"), (Offset, "Range Offset") }
            .SelectMany(wheel => new[] { ("R", "Red"), ("G", "Green"), ("B", "Blue"), ("Y", "Luma") }
                .Select(channel => new VideoEffectParameterDefinition(
                    $"{wheel.Item1}_{channel.Item1}", $"{wheel.Item2} {channel.Item2}", -1, 1, 0)))
            .ToArray();

    internal static IEnumerable<VideoEffectParameterDefinition> Parameters =>
        [.. WheelParameters, new(LowerKey, "Lower Threshold", 0, 1, DefaultLower, .001),
            new(UpperKey, "Upper Threshold", 0, 1, DefaultUpper, .001)];

    public static bool HasAdjustments(VideoEffect effect) =>
        WheelParameters.Any(parameter => effect.Parameters.TryGetValue(parameter.Key, out var value) && value != 0);

    // Independently animated boundaries can cross between keyframes.
    public static LuminanceThresholds GetThresholds(VideoEffect effect)
    {
        var lower = effect.GetParameter(LowerKey);
        var upper = effect.GetParameter(UpperKey);
        return new(Math.Min(lower, upper), Math.Max(lower, upper));
    }
}
