using OpenRend.Core;

namespace OpenRend.Media;

internal sealed class TonalRangeTransform
{
    private readonly LuminanceThresholds limits;
    private readonly double feather;
    private readonly (double R, double G, double B) shadows, midtones, highlights, offset;

    public TonalRangeTransform(VideoEffect effect)
    {
        limits = TonalRangeGrading.GetThresholds(effect);
        // Narrow bands reduce the feather to keep the two transitions from overlapping.
        feather = Math.Min(.08, (limits.Upper - limits.Lower) * .5);
        shadows = ReadWheel(effect, TonalRangeGrading.Shadows);
        midtones = ReadWheel(effect, TonalRangeGrading.Midtones);
        highlights = ReadWheel(effect, TonalRangeGrading.Highlights);
        offset = ReadWheel(effect, TonalRangeGrading.Offset);
    }

    public (double R, double G, double B) Apply(double r, double g, double b)
    {
        // All four corrections use the same entering luminance.
        var luma = Math.Clamp(.2126 * r + .7152 * g + .0722 * b, 0, 1);
        var shadowWeight = 1 - Transition(luma, limits.Lower, feather);
        var highlightWeight = Transition(luma, limits.Upper, feather);
        var midtoneWeight = Math.Max(0, 1 - shadowWeight - highlightWeight);
        var offsetWeight = .5 + .5 * midtoneWeight;
        return (r + shadows.R * shadowWeight + midtones.R * midtoneWeight + highlights.R * highlightWeight + offset.R * offsetWeight,
                g + shadows.G * shadowWeight + midtones.G * midtoneWeight + highlights.G * highlightWeight + offset.G * offsetWeight,
                b + shadows.B * shadowWeight + midtones.B * midtoneWeight + highlights.B * highlightWeight + offset.B * offsetWeight);
    }

    private static double Transition(double luma, double center, double feather)
    {
        if (feather == 0) return luma < center ? 0 : 1;
        var t = Math.Clamp((luma - center + feather) / (2 * feather), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static (double, double, double) ReadWheel(VideoEffect effect, string prefix)
    {
        var y = effect.GetParameter(prefix + "_Y");
        return (effect.GetParameter(prefix + "_R") + y, effect.GetParameter(prefix + "_G") + y,
                effect.GetParameter(prefix + "_B") + y);
    }
}
