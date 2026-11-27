using System.Globalization;
using System.Numerics;

namespace OpenRend.Core;

public enum CrossEffectMode { None, Zoom, Pixelate, Blur }
public enum CrossEffectTarget { Both, Source, Destination }

public readonly record struct CrossEffectSettings(CrossEffectMode Mode, CrossEffectTarget Target, float FadeRange,
    Vector2 SourceCenter, Vector2 DestinationCenter, float PixelWidth, float PixelHeight, float Blur)
{
    public static CrossEffectSettings Read(IReadOnlyDictionary<string, string> parameters)
    {
        var name = parameters.GetValueOrDefault("EffectType") ?? "Zoom";
        var legacyTarget = name.ToUpperInvariant() switch
        {
            "ZOOMA" => CrossEffectTarget.Source,
            "ZOOMB" => CrossEffectTarget.Destination,
            _ => CrossEffectTarget.Both
        };
        var mode = Enum.TryParse<CrossEffectMode>(name, true, out var selected) && Enum.IsDefined(selected)
            ? selected : CrossEffectMode.Zoom;
        var target = Enum.TryParse<CrossEffectTarget>(parameters.GetValueOrDefault("ApplyTo"), true, out var applied) && Enum.IsDefined(applied)
            ? applied : legacyTarget;
        return new(mode, target, Number("FadeRange", 1),
            new(Number("SourceCenterX", .5f), Number("SourceCenterY", .5f)),
            new(Number("DestinationCenterX", .5f), Number("DestinationCenterY", .5f)),
            Number("MaxX", .14f), Number("MaxY", .14f), Number("MaxBlur", .5f));

        float Number(string key, float fallback) =>
            float.TryParse(parameters.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)
                ? Math.Clamp(value, 0, 1) : fallback;
    }
}
