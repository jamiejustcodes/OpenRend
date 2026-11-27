using System.Numerics;
using OpenRend.Core;

namespace OpenRend.Media;

public sealed partial class TimelineRenderService
{
    private readonly record struct PageProjection(Vector2 Travel, Vector2 Across, float Span, float PixelSpan,
        float Frontier, float FoldWidth, float Translation);

    private static void RenderPageTransition(byte[] output, DissolveSource? first, DissolveSource? second, int width, int height,
        float progress, VideoTransitionKind kind, IReadOnlyDictionary<string, string> parameters, float firstOpacity, float secondOpacity)
    {
        if (progress is <= 0 or >= 1)
        {
            var source = progress <= 0 ? first : second;
            var opacity = progress <= 0 ? firstOpacity : secondOpacity;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                WriteTransitionSample(output, (y * width + x) * 4, SampleDissolveLayer(source, x, y, width, height) * opacity);
            return;
        }

        var settings = PageTransitionSettings.Read(kind, parameters);
        var (sin, cos) = MathF.SinCos(settings.Angle * (MathF.PI / 180));
        var travel = new Vector2(-cos, -sin);
        var across = new Vector2(-travel.Y, travel.X);
        var span = MathF.Abs(travel.X) + MathF.Abs(travel.Y);
        var pixelSpan = (MathF.Abs(travel.X) / width + MathF.Abs(travel.Y) / height) / span;
        var loop = kind == VideoTransitionKind.PageLoop;
        var peel = kind == VideoTransitionKind.PagePeel;
        var translation = loop ? 1 - progress : .65f * progress * (1 - progress) * settings.SlideAmount;
        var foldWidth = settings.Radius * (loop ? .62f : peel ? .47f : .33f) * (4 * progress * (1 - progress));
        if (!loop) foldWidth = MathF.Min(foldWidth, progress);
        var projection = new PageProjection(travel, across, span, pixelSpan, loop ? progress : progress - translation,
            foldWidth, translation);
        var white = new Vector4(255, 255, 255, 1);
        var light = GetParamColor(parameters, "LightColor", white);
        if (!float.IsFinite(light.X + light.Y + light.Z + light.W) || light.W is < 0 or > 1) light = white;
        light = Vector4.Clamp(light, Vector4.Zero, new Vector4(light.W * 255, light.W * 255, light.W * 255, light.W));
        var moving = loop ? second : first;
        var background = loop ? first : second;
        var movingOpacity = loop ? secondOpacity : firstOpacity;
        var backgroundOpacity = loop ? firstOpacity : secondOpacity;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var point = new Vector2((x + .5f) / width - .5f, (y + .5f) / height - .5f);
            var distance = Vector2.Dot(point, travel) / span + .5f - projection.Frontier;
            Vector4 sample;
            if (moving is null) sample = Vector4.Zero;
            else if (!loop && translation == 0 && distance >= pixelSpan / 2)
                sample = SampleDissolveLayer(moving, x, y, width, height);
            else if (loop) sample = SampleLoop(moving, point, distance, projection, settings, light, width, height);
            else sample = SampleTurningPage(moving, point, distance, projection, settings, light, peel, width, height);
            sample *= movingOpacity;
            if (sample.W < 1)
                sample += SampleDissolveLayer(background, x, y, width, height) * (backgroundOpacity * (1 - sample.W));
            WriteTransitionSample(output, (y * width + x) * 4, sample);
        }
    }

    private static Vector4 SampleLoop(DissolveSource source, Vector2 point, float distance, PageProjection projection,
        PageTransitionSettings settings, Vector4 light, int width, int height)
    {
        var coverage = Math.Clamp(.5f - distance / projection.PixelSpan, 0, 1);
        if (coverage == 0) return Vector4.Zero;
        var sourcePoint = point + projection.Travel * (projection.Span * projection.Translation);
        var loopWidth = projection.FoldWidth * (1 + .6f * settings.Perspective);
        var offset = distance + (1 - settings.LoopPosition);
        if (loopWidth <= 0 || MathF.Abs(offset) >= loopWidth)
            return SamplePage(source, sourcePoint, width, height) * coverage;

        var section = offset / loopWidth;
        var lift = 1 - section * section;
        sourcePoint -= projection.Travel * (projection.Span * projection.FoldWidth * .52f * lift);
        sourcePoint = ScaleFold(sourcePoint, projection.Across, lift * settings.Perspective * projection.FoldWidth);
        var sample = SamplePage(source, sourcePoint, width, height);
        return LightPage(sample, light, .34f * lift * (1 - section), .38f * lift * (1 + section)) *
            (coverage * (1 - (1 - settings.Opacity) * lift));
    }

    private static Vector4 SampleTurningPage(DissolveSource source, Vector2 point, float distance, PageProjection projection,
        PageTransitionSettings settings, Vector4 light, bool peel, int width, int height)
    {
        var sourcePoint = point + projection.Travel * (projection.Span * projection.Translation);
        var separation = projection.FoldWidth * (1 + settings.Perspective * .8f);
        var coverage = Math.Clamp(.5f + (distance + separation) / projection.PixelSpan, 0, 1);
        if (coverage == 0) return Vector4.Zero;
        if (distance >= 0 || separation <= 0) return SamplePage(source, sourcePoint, width, height) * coverage;

        // Recover the distance along the curved paper from the visible cross section.
        var section = Math.Clamp(-distance / separation, 0, 1);
        var arc = MathF.Asin(section);
        var lift = 1 - MathF.Cos(arc);
        var sourceDistance = projection.FoldWidth * (peel ? arc + lift * .42f : arc);
        sourcePoint += projection.Travel * (projection.Span * (-distance - sourceDistance));
        sourcePoint = ScaleFold(sourcePoint, projection.Across, lift * settings.Perspective * projection.FoldWidth * 2);
        var sample = SamplePage(source, sourcePoint, width, height);
        var sheen = peel ? .48f * section * section : .5f * MathF.Sin(2 * arc);
        var shade = peel ? .25f * section : .6f * lift;
        return LightPage(sample, light, sheen, shade) * (coverage * (1 - (1 - settings.Opacity) * section));
    }

    private static Vector2 ScaleFold(Vector2 point, Vector2 axis, float lift) =>
        point - axis * (Vector2.Dot(point, axis) * (lift / (1 + lift)));

    private static Vector4 SamplePage(DissolveSource source, Vector2 position, int width, int height)
    {
        if (MathF.Abs(position.X) > .5f || MathF.Abs(position.Y) > .5f) return Vector4.Zero;
        return SampleTransitionPlane(source, position + new Vector2(.5f), width, height);
    }

    private static Vector4 LightPage(Vector4 sample, Vector4 light, float sheen, float shade)
    {
        var reflected = new Vector3(light.X, light.Y, light.Z) * (sample.W * sheen);
        var rgb = new Vector3(sample.X, sample.Y, sample.Z) * (1 - shade - sheen * light.W) + reflected;
        return new(Vector3.Clamp(rgb, Vector3.Zero, new Vector3(sample.W * 255)), sample.W);
    }
}
