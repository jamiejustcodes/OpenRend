using OpenRend.Core;

namespace OpenRend.Media;

internal static class VideoEffectProcessor
{
    internal static bool HasActiveEffects(IReadOnlyList<VideoEffect> effects) => effects.Any(effect => effect.IsEnabled && (effect.Kind switch
    {
        VideoEffectKind.BrightnessContrast => effect.Amount != 0 || effect.SecondaryAmount != 0,
        VideoEffectKind.Saturation => effect.Amount != 1,
        VideoEffectKind.Invert => effect.Amount != 0,
        _ => false
    }));

    internal static byte[] Apply(byte[] source, IReadOnlyList<VideoEffect> effects)
    {
        if (!HasActiveEffects(effects)) return source;
        // Keep decoder buffers reusable and alpha unchanged. These operations use encoded RGB values.
        var output = (byte[])source.Clone();
        foreach (var effect in effects)
        {
            if (!effect.IsEnabled) continue;
            effect.Validate();
            for (var p = 0; p < output.Length; p += 4)
            {
                var b = output[p] / 255.0;
                var g = output[p + 1] / 255.0;
                var r = output[p + 2] / 255.0;
                switch (effect.Kind)
                {
                    case VideoEffectKind.BrightnessContrast:
                        var contrast = 1 + effect.SecondaryAmount;
                        b = (b - .5) * contrast + .5 + effect.Amount;
                        g = (g - .5) * contrast + .5 + effect.Amount;
                        r = (r - .5) * contrast + .5 + effect.Amount;
                        break;
                    case VideoEffectKind.Saturation:
                        var luma = .2126 * r + .7152 * g + .0722 * b;
                        b = luma + (b - luma) * effect.Amount;
                        g = luma + (g - luma) * effect.Amount;
                        r = luma + (r - luma) * effect.Amount;
                        break;
                    case VideoEffectKind.Invert:
                        b += (1 - 2 * b) * effect.Amount;
                        g += (1 - 2 * g) * effect.Amount;
                        r += (1 - 2 * r) * effect.Amount;
                        break;
                }
                output[p] = Channel(b); output[p + 1] = Channel(g); output[p + 2] = Channel(r);
            }
        }
        return output;
    }

    private static byte Channel(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
}
