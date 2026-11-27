using System.Numerics;

namespace OpenRend.Media;

public sealed partial class TimelineRenderService
{
    private readonly record struct TransitionBorder(float Width, float Feather, Vector4 Color);
    private readonly record struct SwapPlane(DissolveSource? Source, Vector2 Center, float Scale, float Opacity);
    private readonly record struct SwapFrame(SwapPlane Background, SwapPlane Foreground, TransitionBorder Border);

    private static float GetFiniteTransitionNumber(IReadOnlyDictionary<string, string> parameters, string key, float fallback)
    {
        var value = GetParamFloat(parameters, key, fallback);
        return float.IsFinite(value) ? value : fallback;
    }

    private static TransitionBorder ReadTransitionBorder(IReadOnlyDictionary<string, string> parameters)
    {
        var black = new Vector4(0, 0, 0, 1);
        var color = GetParamColor(parameters, "BorderColor", black);
        if (!float.IsFinite(color.X) || !float.IsFinite(color.Y) || !float.IsFinite(color.Z) ||
            !float.IsFinite(color.W) || color.W is < 0 or > 1) color = black;
        return new(Math.Clamp(GetFiniteTransitionNumber(parameters, "BorderSize", 0), 0, 1),
            Math.Clamp(GetFiniteTransitionNumber(parameters, "BorderFeather", 0), 0, 1), color);
    }

    private static Vector4 BlendTransitionBorder(Vector4 sample, TransitionBorder settings, float distance, float opacity)
    {
        if (settings.Width == 0) return sample;
        var edge = settings.Width / 2;
        var feather = settings.Feather * edge;
        var coverage = feather > 0 ? Math.Clamp((edge - distance) / feather, 0, 1) : distance < edge ? 1 : 0;
        var border = settings.Color * (coverage * opacity);
        return border + sample * (1 - border.W);
    }

    private static SwapFrame BuildSwapFrame(DissolveSource? first, DissolveSource? second, float progress,
        IReadOnlyDictionary<string, string> parameters, float firstOpacity, float secondOpacity)
    {
        var center = new Vector2(.5f);
        if (progress <= 0) return new(default, new SwapPlane(first, center, 1, firstOpacity), default);
        if (progress >= 1) return new(default, new SwapPlane(second, center, 1, secondOpacity), default);
        var direction = (int)GetFiniteTransitionNumber(parameters, "Direction", 2) switch
        {
            0 => -Vector2.UnitY,
            1 => Vector2.UnitY,
            3 => Vector2.UnitX,
            _ => -Vector2.UnitX
        };
        var arc = MathF.Sin(MathF.PI * progress);
        var displacement = direction * (.37f * arc);
        var outgoing = new SwapPlane(first, center + displacement, 1 - .56f * progress, firstOpacity);
        var incoming = new SwapPlane(second, center - displacement, .44f + .56f * progress, secondOpacity);
        var border = ReadTransitionBorder(parameters);
        border = border with { Width = border.Width * arc };
        // The two planes clear each other before their foreground order changes.
        return progress < .5f ? new(incoming, outgoing, border) : new(outgoing, incoming, border);
    }

    private static Vector4 EvaluateSwapPixel(int x, int y, int width, int height, SwapFrame frame)
    {
        var pixel = new Vector2((x + .5f) / width, (y + .5f) / height);
        var front = SampleSwapPlane(frame.Foreground, frame.Border, pixel, width, height);
        if (front.W >= 1) return front;
        return front + SampleSwapPlane(frame.Background, frame.Border, pixel, width, height) * (1 - front.W);
    }

    private static Vector4 SampleSwapPlane(SwapPlane plane, TransitionBorder border, Vector2 pixel, int width, int height)
    {
        if (plane.Source is null) return Vector4.Zero;
        var delta = pixel - plane.Center;
        var half = plane.Scale / 2;
        var xEdge = half - MathF.Abs(delta.X);
        var yEdge = half - MathF.Abs(delta.Y);
        if (xEdge < 0 || yEdge < 0) return Vector4.Zero;
        var sample = SampleTransitionPlane(plane.Source, delta / plane.Scale + new Vector2(.5f), width, height) * plane.Opacity;
        var edge = MathF.Min(xEdge * width, yEdge * height) / Math.Min(width, height);
        return BlendTransitionBorder(sample, border, edge, plane.Opacity);
    }

    private static Vector4 SampleTransitionPlane(DissolveSource source, Vector2 position, int width, int height)
    {
        var x = Math.Clamp(position.X * width - .5f, 0, width - 1);
        var y = Math.Clamp(position.Y * height - .5f, 0, height - 1);
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(width - 1, x0 + 1);
        var y1 = Math.Min(height - 1, y0 + 1);
        var top = Vector4.Lerp(SampleDissolveLayer(source, x0, y0, width, height), SampleDissolveLayer(source, x1, y0, width, height), x - x0);
        var bottom = Vector4.Lerp(SampleDissolveLayer(source, x0, y1, width, height), SampleDissolveLayer(source, x1, y1, width, height), x - x0);
        return Vector4.Lerp(top, bottom, y - y0);
    }
}
