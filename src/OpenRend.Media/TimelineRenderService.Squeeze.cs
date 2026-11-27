using System.Numerics;

namespace OpenRend.Media;

public sealed partial class TimelineRenderService
{
    private readonly record struct SqueezeSettings(bool Vertical, bool ReverseAxis, bool Centered, bool SqueezePrevious, TransitionBorder Border);

    private static SqueezeSettings ReadSqueezeSettings(IReadOnlyDictionary<string, string> parameters)
    {
        var direction = (int)GetFiniteTransitionNumber(parameters, "Direction", 2);
        return new SqueezeSettings(direction is 0 or 1 or 5, direction is 0 or 2, direction is 4 or 5,
            GetParamBool(parameters, "SqueezePreviousImage", true), ReadTransitionBorder(parameters));
    }

    private static Vector4 EvaluateSqueezePixel(DissolveSource? first, DissolveSource? second, int x, int y, int width, int height,
        float progress, SqueezeSettings settings, float firstOpacity, float secondOpacity)
    {
        if (progress <= 0) return SampleDissolveLayer(first, x, y, width, height) * firstOpacity;
        if (progress >= 1) return SampleDissolveLayer(second, x, y, width, height) * secondOpacity;
        var length = settings.Vertical ? height : width;
        var axis = ((settings.Vertical ? y : x) + .5f) / length;
        if (settings.ReverseAxis) axis = 1 - axis;

        var centeredAxis = axis - .5f;
        var edge = settings.Centered ? MathF.Abs(centeredAxis) - progress / 2 : axis - progress;
        var incoming = edge < 0;
        float mapped;
        if (settings.Centered)
        {
            if (!settings.SqueezePrevious) mapped = axis;
            else if (incoming) mapped = .5f + centeredAxis / progress;
            else mapped = .5f + MathF.CopySign(edge / (1 - progress), centeredAxis);
        }
        else if (incoming)
            mapped = settings.SqueezePrevious ? axis / progress : axis + 1 - progress;
        else
            mapped = settings.SqueezePrevious ? (axis - progress) / (1 - progress) : axis;
        if (settings.ReverseAxis) mapped = 1 - mapped;

        var source = incoming ? second : first;
        var opacity = incoming ? secondOpacity : firstOpacity;
        // Interpolate on the rescaled axis in premultiplied space to retain transparent edges.
        var sample = settings.Vertical
            ? SampleSqueezeAxis(source, x, mapped, width, height, vertical: true)
            : SampleSqueezeAxis(source, y, mapped, width, height, vertical: false);
        return BlendTransitionBorder(sample * opacity, settings.Border, MathF.Abs(edge), opacity);
    }

    private static Vector4 SampleSqueezeAxis(DissolveSource? source, int other, float axis, int width, int height, bool vertical)
    {
        var length = vertical ? height : width;
        var coordinate = Math.Clamp(axis * length - .5f, 0, length - 1);
        var before = (int)coordinate;
        var after = Math.Min(before + 1, length - 1);
        var first = vertical ? SampleDissolveLayer(source, other, before, width, height) : SampleDissolveLayer(source, before, other, width, height);
        var second = vertical ? SampleDissolveLayer(source, other, after, width, height) : SampleDissolveLayer(source, after, other, width, height);
        return Vector4.Lerp(first, second, coordinate - before);
    }
}
