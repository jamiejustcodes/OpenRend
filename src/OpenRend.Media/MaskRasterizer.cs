using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenRend.Core;

namespace OpenRend.Media;

/// <summary>
/// Rasterizes Bézier mask paths into alpha channels with multi-pass Gaussian edge feathering,
/// matching Vegas Pro 1:1 Pan/Crop Mask rasterization semantics.
/// </summary>
[SupportedOSPlatform("windows")]
public static class MaskRasterizer
{
    public static void ApplyMask(byte[] sourcePixels, int width, int height, MaskShape? mask)
    {
        if (sourcePixels == null || width <= 0 || height <= 0 || mask == null || mask.Paths.Count == 0) return;
        var pixelCount = width * height;
        if (sourcePixels.Length < pixelCount * 4) return;

        // Cumulative combined alpha buffer initialized to fully opaque (255)
        var combinedAlpha = new byte[pixelCount];
        Array.Fill(combinedAlpha, (byte)255);

        var hasRenderedPath = false;

        foreach (var path in mask.Paths)
        {
            if (path.Points.Count < 2) continue;

            var pathAlpha = RasterizePath(path, width, height);
            if (pathAlpha == null) continue;

            hasRenderedPath = true;

            // Combine each path with the cumulative matte
            for (var i = 0; i < pixelCount; i++)
            {
                var a = pathAlpha[i];
                var cur = combinedAlpha[i];
                // Subtractive / additive combination
                combinedAlpha[i] = (byte)Math.Clamp((cur * a) / 255, 0, 255);
            }
        }

        if (!hasRenderedPath) return;

        // Apply combined matte to the source BGRA buffer
        for (var i = 0; i < pixelCount; i++)
        {
            var p = i * 4;
            var matte = combinedAlpha[i];
            var origA = sourcePixels[p + 3];
            var finalA = (byte)Math.Clamp((origA * matte) / 255, 0, 255);
            sourcePixels[p + 3] = finalA;
            if (finalA == 0)
            {
                sourcePixels[p] = 0;
                sourcePixels[p + 1] = 0;
                sourcePixels[p + 2] = 0;
            }
        }
    }

    public static byte[]? RasterizePath(MaskPath path, int width, int height)
    {
        if (path.Points.Count < 2 || width <= 0 || height <= 0) return null;

        var pixelCount = width * height;
        using var gPath = new GraphicsPath();
        gPath.FillMode = FillMode.Winding;

        var count = path.Points.Count;
        var segmentCount = path.IsClosed ? count : count - 1;

        for (var i = 0; i < segmentCount; i++)
        {
            var p0 = path.Points[i];
            var nextIndex = (i + 1) % count;
            var p1 = path.Points[nextIndex];

            var hasOut = Math.Abs(p0.OutTangentX) > 1e-4 || Math.Abs(p0.OutTangentY) > 1e-4;
            var hasIn = Math.Abs(p1.InTangentX) > 1e-4 || Math.Abs(p1.InTangentY) > 1e-4;

            if (hasOut || hasIn)
            {
                var c1x = (float)(p0.X + p0.OutTangentX);
                var c1y = (float)(p0.Y + p0.OutTangentY);
                var c2x = (float)(p1.X + p1.InTangentX);
                var c2y = (float)(p1.Y + p1.InTangentY);
                gPath.AddBezier((float)p0.X, (float)p0.Y, c1x, c1y, c2x, c2y, (float)p1.X, (float)p1.Y);
            }
            else
            {
                gPath.AddLine((float)p0.X, (float)p0.Y, (float)p1.X, (float)p1.Y);
            }
        }

        if (path.IsClosed) gPath.CloseFigure();

        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = path.AntiAlias ? SmoothingMode.AntiAlias : SmoothingMode.None;
            g.Clear(Color.Black);
            using var brush = new SolidBrush(Color.White);
            g.FillPath(brush, gPath);
        }

        var rawAlpha = new byte[pixelCount];
        var bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = bmpData.Stride;
            var ptr = bmpData.Scan0;
            var buffer = new byte[Math.Abs(stride) * height];
            Marshal.Copy(ptr, buffer, 0, buffer.Length);

            for (var y = 0; y < height; y++)
            {
                var lineOffset = y * stride;
                var targetOffset = y * width;
                for (var x = 0; x < width; x++)
                {
                    // Bitmap red channel is 255 inside the path, 0 outside, with anti-aliasing shades along edge
                    rawAlpha[targetOffset + x] = buffer[lineOffset + x * 4 + 2];
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bmpData);
        }

        // Edge feathering
        byte[] effectiveAlpha;
        var featherRadius = (int)Math.Round(path.Feather * 0.01 * Math.Min(width, height) * 0.5);
        if (featherRadius > 0 && path.FeatherType != MaskFeatherType.None)
        {
            var blurred = GaussianBlurAlpha(rawAlpha, width, height, featherRadius);
            effectiveAlpha = new byte[pixelCount];
            switch (path.FeatherType)
            {
                case MaskFeatherType.Both:
                    Array.Copy(blurred, effectiveAlpha, pixelCount);
                    break;
                case MaskFeatherType.Out:
                    for (var i = 0; i < pixelCount; i++) effectiveAlpha[i] = Math.Max(rawAlpha[i], blurred[i]);
                    break;
                case MaskFeatherType.In:
                    for (var i = 0; i < pixelCount; i++) effectiveAlpha[i] = Math.Min(rawAlpha[i], blurred[i]);
                    break;
                default:
                    effectiveAlpha = rawAlpha;
                    break;
            }
        }
        else
        {
            effectiveAlpha = rawAlpha;
        }

        // Apply opacity and mask mode (Positive vs Negative)
        var opacityFactor = Math.Clamp(path.Opacity / 100.0, 0.0, 1.0);
        var outputAlpha = new byte[pixelCount];
        var isNegative = path.Mode == MaskMode.Negative;

        for (var i = 0; i < pixelCount; i++)
        {
            var a = effectiveAlpha[i];
            if (isNegative) a = (byte)(255 - a);
            outputAlpha[i] = (byte)Math.Clamp(Math.Round(a * opacityFactor), 0, 255);
        }

        return outputAlpha;
    }

    public static byte[] GaussianBlurAlpha(byte[] source, int width, int height, int radius)
    {
        if (radius <= 0) return (byte[])source.Clone();
        const int passes = 3;
        var passRadius = Math.Max(1, (int)Math.Round(radius * 0.58));
        var current = (byte[])source.Clone();
        for (var pass = 0; pass < passes; pass++)
        {
            current = BoxPassAlpha(current, width, height, passRadius, horizontal: true);
            current = BoxPassAlpha(current, width, height, passRadius, horizontal: false);
        }
        return current;
    }

    private static byte[] BoxPassAlpha(byte[] source, int width, int height, int radius, bool horizontal)
    {
        var output = new byte[source.Length];
        var lines = horizontal ? height : width;
        var length = horizontal ? width : height;
        var stride = horizontal ? 1 : width;
        var lineStride = horizontal ? width : 1;
        var windowSize = radius * 2 + 1;
        var invWindow = 1.0 / windowSize;

        for (var line = 0; line < lines; line++)
        {
            var lineStart = line * lineStride;
            double sum = 0;
            for (var offset = -radius; offset <= radius; offset++)
            {
                var pos = Math.Clamp(offset, 0, length - 1);
                sum += source[lineStart + pos * stride];
            }
            for (var pos = 0; pos < length; pos++)
            {
                output[lineStart + pos * stride] = (byte)Math.Clamp(Math.Round(sum * invWindow), 0, 255);
                var removePos = Math.Clamp(pos - radius, 0, length - 1);
                var addPos = Math.Clamp(pos + radius + 1, 0, length - 1);
                sum += source[lineStart + addPos * stride] - source[lineStart + removePos * stride];
            }
        }
        return output;
    }
}
