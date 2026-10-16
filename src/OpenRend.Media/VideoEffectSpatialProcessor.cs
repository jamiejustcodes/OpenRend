using OpenRend.Core;

namespace OpenRend.Media;

internal static partial class VideoEffectProcessor
{
    private static byte[] Mirror(byte[] source, int width, int height, double horizontal, double vertical)
    {
        var output = new byte[source.Length];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var p00 = (y * width + x) * 4; var p01 = (y * width + width - 1 - x) * 4;
            var p10 = ((height - 1 - y) * width + x) * 4; var p11 = ((height - 1 - y) * width + width - 1 - x) * 4;
            var a00 = source[p00 + 3] * (1 - horizontal) * (1 - vertical); var a01 = source[p01 + 3] * horizontal * (1 - vertical);
            var a10 = source[p10 + 3] * (1 - horizontal) * vertical; var a11 = source[p11 + 3] * horizontal * vertical; var alpha = a00 + a01 + a10 + a11;
            output[p00 + 3] = Byte(alpha);
            if (alpha > 0) for (var c = 0; c < 3; c++) output[p00 + c] = Byte((source[p00 + c] * a00 + source[p01 + c] * a01 + source[p10 + c] * a10 + source[p11 + c] * a11) / alpha);
        }
        return output;
    }

    private static byte[] Blur(byte[] source, int width, int height, int radius, bool gaussian)
    {
        if (radius == 0) return source;
        // Three box passes approximate a Gaussian, with bounded work per pixel at every source resolution.
        var passes = gaussian ? 3 : 1; var passRadius = gaussian ? Math.Max(1, (int)Math.Round(radius * .58)) : radius;
        var output = source;
        for (var pass = 0; pass < passes; pass++) output = BoxPass(BoxPass(output, width, height, passRadius, true), width, height, passRadius, false);
        return output;
    }

    private static byte[] BoxPass(byte[] source, int width, int height, int radius, bool horizontal)
    {
        var output = (byte[])source.Clone(); var lines = horizontal ? height : width; var length = horizontal ? width : height;
        var stride = horizontal ? 4 : width * 4;
        for (var line = 0; line < lines; line++)
        {
            var start = horizontal ? line * width * 4 : line * 4;
            double b = 0, g = 0, r = 0, alpha = 0;
            void Accumulate(int position, int sign)
            { var p = start + Math.Clamp(position, 0, length - 1) * stride; var a = source[p + 3] / 255.0; alpha += a * sign; b += source[p] * a * sign; g += source[p + 1] * a * sign; r += source[p + 2] * a * sign; }
            for (var offset = -radius; offset <= radius; offset++) Accumulate(offset, 1);
            for (var position = 0; position < length; position++)
            {
                var p = start + position * stride;
                if (alpha > 0) { output[p] = Byte(b / alpha); output[p + 1] = Byte(g / alpha); output[p + 2] = Byte(r / alpha); }
                // Color filters preserve the source matte, and hidden RGB does not bleed across its edge.
                Accumulate(position - radius, -1); Accumulate(position + radius + 1, 1);
            }
        }
        return output;
    }

    private static byte[] Neighborhood(byte[] source, int width, int height, int radius, double extremum)
    {
        if (radius == 0) return source;
        var output = (byte[])source.Clone(); Span<byte> values = stackalloc byte[49];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) for (var channel = 0; channel < 3; channel++)
        {
            var count = 0; var center = source[(y * width + x) * 4 + channel];
            for (var dy = -radius; dy <= radius; dy++) for (var dx = -radius; dx <= radius; dx++)
            {
                var neighbor = (Math.Clamp(y + dy, 0, height - 1) * width + Math.Clamp(x + dx, 0, width - 1)) * 4;
                var alpha = source[neighbor + 3] / 255.0;
                values[count++] = Byte(source[neighbor + channel] * alpha + center * (1 - alpha));
            }
            values[..count].Sort(); var p = (y * width + x) * 4 + channel;
            output[p] = extremum == 0 ? values[count / 2] : Byte(source[p] * (1 - Math.Abs(extremum)) + (extremum > 0 ? values[count - 1] : values[0]) * Math.Abs(extremum));
        }
        return output;
    }

    private static byte[] Pixelate(byte[] source, int width, int height, int block)
    {
        if (block <= 1) return source;
        var output = (byte[])source.Clone();
        for (var top = 0; top < height; top += block) for (var left = 0; left < width; left += block)
        {
            var right = Math.Min(width, left + block); var bottom = Math.Min(height, top + block); double r = 0, g = 0, b = 0, alpha = 0;
            for (var y = top; y < bottom; y++) for (var x = left; x < right; x++)
            { var p = (y * width + x) * 4; var a = source[p + 3] / 255.0; alpha += a; r += source[p + 2] * a; g += source[p + 1] * a; b += source[p] * a; }
            if (alpha <= 0) continue;
            for (var y = top; y < bottom; y++) for (var x = left; x < right; x++)
            { var p = (y * width + x) * 4; output[p] = Byte(b / alpha); output[p + 1] = Byte(g / alpha); output[p + 2] = Byte(r / alpha); }
        }
        return output;
    }

    private static byte[] Convolution(byte[] source, int width, int height, VideoEffect effect)
    {
        var output = (byte[])source.Clone(); var kernel = new double[9];
        for (var y = 0; y < 3; y++) for (var x = 0; x < 3; x++) kernel[y * 3 + x] = effect.GetParameter($"K{y}{x}");
        var bias = effect.GetParameter("Bias") * 255;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) for (var c = 0; c < 3; c++)
        {
            var value = bias; var center = source[(y * width + x) * 4 + c];
            for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
            {
                var neighbor = (Math.Clamp(y + dy, 0, height - 1) * width + Math.Clamp(x + dx, 0, width - 1)) * 4;
                var alpha = source[neighbor + 3] / 255.0;
                value += (source[neighbor + c] * alpha + center * (1 - alpha)) * kernel[(dy + 1) * 3 + dx + 1];
            }
            output[(y * width + x) * 4 + c] = Byte(value);
        }
        return output;
    }

    private static byte[] Polar(byte[] source, int width, int height, Func<double, double, (double R, double A)> transform)
    {
        var scale = Math.Min(width, height) / 2.0; var cx = (width - 1) / 2.0; var cy = (height - 1) / 2.0;
        return Resample(source, width, height, (x, y) =>
        { var nx = (x - cx) / scale; var ny = (y - cy) / scale; var (r, a) = transform(Math.Sqrt(nx * nx + ny * ny), Math.Atan2(ny, nx)); return (cx + r * Math.Cos(a) * scale, cy + r * Math.Sin(a) * scale); });
    }

    private static byte[] Resample(byte[] source, int width, int height, Func<double, double, (double X, double Y)> transform, bool wrap = false)
    {
        var output = new byte[source.Length];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var (sx, sy) = transform(x, y);
            if (!double.IsFinite(sx) || !double.IsFinite(sy) || (!wrap && (sx < -.5 || sy < -.5 || sx > width - .5 || sy > height - .5))) continue;
            if (!wrap) { sx = Math.Clamp(sx, 0, width - 1); sy = Math.Clamp(sy, 0, height - 1); }
            var x0 = (int)Math.Floor(sx); var y0 = (int)Math.Floor(sy); var wx = sx - x0; var wy = sy - y0;
            int Index(int ix, int iy) => ((wrap ? (int)Wrap(iy, height) : Math.Clamp(iy, 0, height - 1)) * width + (wrap ? (int)Wrap(ix, width) : Math.Clamp(ix, 0, width - 1))) * 4;
            var p00 = Index(x0, y0); var p01 = Index(x0 + 1, y0); var p10 = Index(x0, y0 + 1); var p11 = Index(x0 + 1, y0 + 1);
            var w00 = (1 - wx) * (1 - wy); var w01 = wx * (1 - wy); var w10 = (1 - wx) * wy; var w11 = wx * wy;
            var a00 = source[p00 + 3] * w00; var a01 = source[p01 + 3] * w01; var a10 = source[p10 + 3] * w10; var a11 = source[p11 + 3] * w11; var alpha = a00 + a01 + a10 + a11;
            var target = (y * width + x) * 4; output[target + 3] = Byte(alpha);
            // Interpolate premultiplied color, then return the straight-alpha buffer expected by composition.
            if (alpha > 0) for (var c = 0; c < 3; c++) output[target + c] = Byte((source[p00 + c] * a00 + source[p01 + c] * a01 + source[p10 + c] * a10 + source[p11 + c] * a11) / alpha);
        }
        return output;
    }
}
