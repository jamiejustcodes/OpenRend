using System.Numerics;
using OpenRend.Core;

namespace OpenRend.Media;

public sealed partial class TimelineRenderService
{
    private static void RenderCrossEffect(byte[] output, DissolveSource? first, DissolveSource? second, int width, int height,
        float progress, CrossEffectSettings settings, float firstOpacity, float secondOpacity)
    {
        var fade = settings.FadeRange > 0 ? Math.Clamp((progress - .5f) / settings.FadeRange + .5f, 0, 1)
            : progress < .5f ? 0 : 1;
        var firstWeight = firstOpacity * (1 - fade);
        var secondWeight = secondOpacity * fade;
        var affectFirst = settings.Target != CrossEffectTarget.Destination;
        var affectSecond = settings.Target != CrossEffectTarget.Source;
        var strength = progress > 0 && progress < 1 ? MathF.Sin(MathF.PI * progress) : 0;
        if (settings.Mode == CrossEffectMode.Blur)
        {
            var radius = (int)MathF.Round(Math.Min(width, height) * .15f * settings.Blur * strength);
            var firstBlur = affectFirst && first is not null ? firstWeight : 0;
            var secondBlur = affectSecond && second is not null ? secondWeight : 0;
            if (radius > 0 && (firstBlur > 0 || secondBlur > 0))
            {
                RenderCrossBlur(output, first, second, width, height, radius, firstBlur, secondBlur,
                    firstWeight - firstBlur, secondWeight - secondBlur);
                return;
            }
        }

        var inverseZoom = 1 / (1 + .8f * strength);
        var blockWidth = Math.Max(1, (int)MathF.Round(width * settings.PixelWidth * strength));
        var blockHeight = Math.Max(1, (int)MathF.Round(height * settings.PixelHeight * strength));
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sample = Sample(first, settings.SourceCenter, affectFirst, firstWeight, x, y) +
                Sample(second, settings.DestinationCenter, affectSecond, secondWeight, x, y);
            WriteTransitionSample(output, (y * width + x) * 4, sample);
        }

        Vector4 Sample(DissolveSource? source, Vector2 center, bool affected, float weight, int x, int y)
        {
            if (source is null || weight == 0) return Vector4.Zero;
            if (affected && strength > 0)
            {
                if (settings.Mode == CrossEffectMode.Zoom)
                {
                    var position = new Vector2((x + .5f) / width, (y + .5f) / height);
                    return SampleTransitionPlane(source, center + (position - center) * inverseZoom, width, height) * weight;
                }
                if (settings.Mode == CrossEffectMode.Pixelate)
                {
                    var left = x / blockWidth * blockWidth;
                    var top = y / blockHeight * blockHeight;
                    x = left + (Math.Min(blockWidth, width - left) - 1) / 2;
                    y = top + (Math.Min(blockHeight, height - top) - 1) / 2;
                }
            }
            return SampleDissolveLayer(source, x, y, width, height) * weight;
        }
    }

    private static void RenderCrossBlur(byte[] output, DissolveSource? first, DissolveSource? second, int width, int height,
        int radius, float firstBlur, float secondBlur, float firstClear, float secondClear)
    {
        // Running sums keep the blur cost linear in the frame size and use one scanline of scratch storage.
        var columns = new Vector4[width];
        var window = radius * 2 + 1;
        var inverseArea = 1f / (window * window);
        AccumulateRow(0, radius + 1);
        for (var y = 1; y <= radius; y++) AccumulateRow(y, 1);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sample = columns[x] * inverseArea;
                if (firstClear > 0) sample += SampleDissolveLayer(first, x, y, width, height) * firstClear;
                if (secondClear > 0) sample += SampleDissolveLayer(second, x, y, width, height) * secondClear;
                WriteTransitionSample(output, (y * width + x) * 4, sample);
            }
            if (y + 1 == height) break;
            AccumulateRow(Math.Max(0, y - radius), -1);
            AccumulateRow(Math.Min(height - 1, y + radius + 1), 1);
        }

        void AccumulateRow(int y, int coefficient)
        {
            var horizontal = Input(0, y) * (radius + 1);
            for (var x = 1; x <= radius; x++) horizontal += Input(x, y);
            for (var x = 0; x < width; x++)
            {
                columns[x] += horizontal * coefficient;
                if (x + 1 == width) break;
                horizontal += Input(Math.Min(width - 1, x + radius + 1), y) - Input(Math.Max(0, x - radius), y);
            }
        }

        Vector4 Input(int x, int y) =>
            (firstBlur > 0 ? SampleDissolveLayer(first, x, y, width, height) * firstBlur : Vector4.Zero) +
            (secondBlur > 0 ? SampleDissolveLayer(second, x, y, width, height) * secondBlur : Vector4.Zero);
    }
}
