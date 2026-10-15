namespace OpenRend.UI;

public static class PreviewDisplay
{
    public static byte[] Apply(byte[] bgra, int width, int height, double zoom, bool safeAreas)
    {
        if (width <= 0 || height <= 0 || bgra.Length != checked(width * height * 4) || zoom is not (.5 or 1 or 2))
            throw new ArgumentException("Invalid preview display dimensions or zoom.");
        var result = bgra;
        if (zoom != 1)
        {
            result = new byte[bgra.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var target = (y * width + x) * 4; result[target + 3] = 255;
                var sx = (int)Math.Floor((x + .5 - width / 2.0) / zoom + width / 2.0);
                var sy = (int)Math.Floor((y + .5 - height / 2.0) / zoom + height / 2.0);
                if (sx < 0 || sx >= width || sy < 0 || sy >= height) continue;
                Buffer.BlockCopy(bgra, (sy * width + sx) * 4, result, target, 4);
            }
        }
        if (!safeAreas) return result;
        if (ReferenceEquals(result, bgra)) result = (byte[])bgra.Clone();
        void Pixel(int x, int y)
        {
            var offset = (y * width + x) * 4;
            for (var channel = 0; channel < 3; channel++) result[offset + channel] = (byte)((result[offset + channel] + 255) / 2);
        }
        foreach (var inset in new[] { .05, .1 })
        {
            var left = Math.Clamp((int)Math.Round(width * inset), 0, width - 1); var right = width - left - 1;
            var top = Math.Clamp((int)Math.Round(height * inset), 0, height - 1); var bottom = height - top - 1;
            for (var x = left; x <= right; x++) { Pixel(x, top); Pixel(x, bottom); }
            for (var y = top + 1; y < bottom; y++) { Pixel(left, y); Pixel(right, y); }
        }
        return result;
    }
}
