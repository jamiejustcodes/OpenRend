using System.Globalization;

namespace OpenRend.Core;

public readonly record struct PageTransitionSettings(float Angle, float Radius, float Opacity, float LoopPosition,
    float SlideAmount, float Perspective)
{
    public const string DefaultLightColor = "1 1 1 1";

    public static bool Supports(VideoTransitionKind kind) =>
        kind is VideoTransitionKind.PageLoop or VideoTransitionKind.PagePeel or VideoTransitionKind.PageRoll;

    public static PageTransitionSettings Read(VideoTransitionKind kind, IReadOnlyDictionary<string, string> parameters)
    {
        // Early project files store a named corner in place of an angle.
        var cornerAngle = parameters.GetValueOrDefault("Corner")?.ToUpperInvariant() switch
        {
            "TOPLEFT" => 225,
            "TOPRIGHT" => 315,
            "BOTTOMRIGHT" => 45,
            "BOTTOMLEFT" => 135,
            _ => 0
        };
        return new(Number("Angle", cornerAngle, 360), Number("Radius", kind == VideoTransitionKind.PageLoop ? .34f : .46f),
            Number("Opacity", 1), Number("LoopPosition", .85f), Number("SlideAmount", 0), Number("Perspective", .3f));

        float Number(string key, float fallback, float max = 1) =>
            float.TryParse(parameters.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)
                ? Math.Clamp(value, 0, max) : fallback;
    }
}
