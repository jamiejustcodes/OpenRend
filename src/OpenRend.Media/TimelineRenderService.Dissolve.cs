using System.Numerics;
using OpenRend.Core;
using OpenRend.Timeline;

namespace OpenRend.Media;

public sealed partial class TimelineRenderService
{
    private byte[] RenderDissolve(ProjectDocument project, long frame, EvaluatedClip outgoing, EvaluatedClip incoming,
        double progress, int width, int height)
    {
        var output = new byte[checked(width * height * 4)];
        if (outgoing.Track.Opacity <= 0) return output;
        var first = outgoing.Clip.Opacity <= 0 || progress >= 1 ? null : PrepareDissolveLayer(project, frame, outgoing, width, height);
        var second = incoming.Clip.Opacity <= 0 || progress <= 0 ? null : PrepareDissolveLayer(project, frame, incoming, width, height);
        var firstWeight = (float)(outgoing.Clip.Opacity * (1 - progress));
        var secondWeight = (float)(incoming.Clip.Opacity * progress);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            // Interpolate coverage with premultiplied color; two alpha-over passes cannot represent transparent dissolves.
            var sample = SampleDissolveLayer(first, x, y, width, height) * firstWeight +
                SampleDissolveLayer(second, x, y, width, height) * secondWeight;
            if (sample.W <= 0) continue;
            var offset = (y * width + x) * 4;
            output[offset] = Quantize(sample.X / sample.W);
            output[offset + 1] = Quantize(sample.Y / sample.W);
            output[offset + 2] = Quantize(sample.Z / sample.W);
            output[offset + 3] = Quantize(sample.W * 255);
        }
        return output;
    }

    private static byte Quantize(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);

    private DissolveSource? PrepareDissolveLayer(ProjectDocument project, long frame, EvaluatedClip layer, int width, int height)
    {
        if (IsSubtitleLayer(layer))
        {
            if (string.IsNullOrWhiteSpace(layer.Clip.Name)) return null;
            var style = ExtractSubtitleStyle(layer.Track.Name, layer.Clip, layer.Media.Path, frame);
            var handles = ActiveSubtitleClipId == layer.Clip.Id;
            var pixels = SubtitleRenderer.RenderSubtitle(layer.Clip.Name, width, height, style, handles, out _);
            pixels = VideoEffectProcessor.Apply(pixels, width, height, layer.Clip.VideoEffects, frame);
            return new(pixels, width, height, null);
        }
        if (IsColorGeneratorLayer(layer))
        {
            var pixels = RenderColorGenerator(layer.Media.Path, width, height);
            pixels = VideoEffectProcessor.Apply(pixels, width, height, layer.Clip.VideoEffects, frame);
            return new(pixels, width, height, null);
        }
        var (sourceWidth, sourceHeight) = SourceDimensions(layer.Media);
        var crop = layer.Clip.Crop is null ? CropState.Identity(sourceWidth, sourceHeight) : layer.Crop ?? CropState.Identity(sourceWidth, sourceHeight);
        crop.Validate();
        var transformed = crop.Width != sourceWidth || crop.Height != sourceHeight ||
            crop.CenterX != sourceWidth / 2.0 || crop.CenterY != sourceHeight / 2.0 || crop.RotationDegrees % 360 != 0;
        if (transformed || VideoEffectProcessor.HasActiveEffects(layer.Clip.VideoEffects))
        {
            if (sourceWidth <= 0 || sourceHeight <= 0 || sourceWidth > 16384 || sourceHeight > 16384 || (long)sourceWidth * sourceHeight * 4 > int.MaxValue)
                throw new MediaException("Source dimensions exceed video processing limits.");
            var pixels = media.GetFrame(layer.Media.Path, (layer.SourceTimeSeconds ?? project.FrameRate.Seconds(layer.SourceFrame)), sourceWidth, sourceHeight);
            pixels = VideoEffectProcessor.Apply(pixels, sourceWidth, sourceHeight, layer.Clip.VideoEffects, frame);
            return new(pixels, sourceWidth, sourceHeight, SamplingTransform(crop, sourceWidth, sourceHeight, width, height));
        }
        var scale = Math.Min((double)width / sourceWidth, (double)height / sourceHeight);
        var layerWidth = Math.Clamp((int)Math.Round(sourceWidth * scale), 1, width);
        var layerHeight = Math.Clamp((int)Math.Round(sourceHeight * scale), 1, height);
        return new(media.GetFrame(layer.Media.Path, (layer.SourceTimeSeconds ?? project.FrameRate.Seconds(layer.SourceFrame)), layerWidth, layerHeight), layerWidth, layerHeight, null);
    }

    private static Vector4 SampleDissolveLayer(DissolveSource? source, int x, int y, int width, int height)
    {
        if (source is null) return Vector4.Zero;
        var destinationWidth = source.Transform?.DestinationWidth ?? source.Width;
        var destinationHeight = source.Transform?.DestinationHeight ?? source.Height;
        x -= (width - destinationWidth) / 2;
        y -= (height - destinationHeight) / 2;
        if (x < 0 || x >= destinationWidth || y < 0 || y >= destinationHeight) return Vector4.Zero;
        if (source.Transform is not { } transform) return PremultipliedPixel(source, x, y);
        var u = (x + .5) / destinationWidth;
        var v = (y + .5) / destinationHeight;
        var sourceU = transform.Xx * u + transform.Xy * v + transform.Tx;
        var sourceV = transform.Yx * u + transform.Yy * v + transform.Ty;
        if (sourceU < 0 || sourceU > 1 || sourceV < 0 || sourceV > 1) return Vector4.Zero;
        var sx = Math.Clamp(sourceU * source.Width - .5, 0, source.Width - 1);
        var sy = Math.Clamp(sourceV * source.Height - .5, 0, source.Height - 1);
        var x0 = (int)sx;
        var y0 = (int)sy;
        var x1 = Math.Min(x0 + 1, source.Width - 1);
        var y1 = Math.Min(y0 + 1, source.Height - 1);
        var wx = (float)(sx - x0);
        var wy = (float)(sy - y0);
        return Vector4.Lerp(Vector4.Lerp(PremultipliedPixel(source, x0, y0), PremultipliedPixel(source, x1, y0), wx),
            Vector4.Lerp(PremultipliedPixel(source, x0, y1), PremultipliedPixel(source, x1, y1), wx), wy);
    }

    private static Vector4 PremultipliedPixel(DissolveSource source, int x, int y)
    {
        var offset = (y * source.Width + x) * 4;
        var alpha = source.Pixels[offset + 3] / 255f;
        return new(source.Pixels[offset] * alpha, source.Pixels[offset + 1] * alpha, source.Pixels[offset + 2] * alpha, alpha);
    }

    private sealed record DissolveSource(byte[] Pixels, int Width, int Height, NativeMethods.LayerTransform? Transform);
}
