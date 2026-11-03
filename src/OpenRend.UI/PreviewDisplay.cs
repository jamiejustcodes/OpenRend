using System;

namespace OpenRend.UI;

public enum PreviewOverlayMode
{
    None,
    SafeAreas,
    RuleOfThirds,
    Grid4x4,
    GoldenRatio
}

public enum SplitScreenDivision
{
    LeftHalf,
    RightHalf,
    All
}

public enum SplitScreenSource
{
    FxBypassed,
    Clipboard
}

public sealed class SplitScreenConfig
{
    public bool Enabled { get; set; }
    public SplitScreenSource Source { get; set; } = SplitScreenSource.FxBypassed;
    public SplitScreenDivision Division { get; set; } = SplitScreenDivision.LeftHalf;
    public bool RetainSetting { get; set; } = true;
}

public static class PreviewDisplay
{
    public static byte[] Apply(byte[] bgra, int width, int height, double zoom, bool safeAreas) =>
        Apply(bgra, width, height, zoom, safeAreas ? PreviewOverlayMode.SafeAreas : PreviewOverlayMode.None);

    public static byte[] Apply(byte[] bgra, int width, int height, double zoom, PreviewOverlayMode overlay) =>
        Apply(bgra, width, height, zoom, overlay, null, null);

    public static byte[] Apply(byte[] bgra, int width, int height, double zoom, PreviewOverlayMode overlay, byte[]? referenceBgra, SplitScreenDivision? split)
    {
        var baseFrame = bgra;
        if (split.HasValue && referenceBgra != null)
        {
            baseFrame = ApplySplit(bgra, referenceBgra, width, height, split.Value);
        }

        if (width <= 0 || height <= 0 || baseFrame.Length != checked(width * height * 4) || zoom is not (.5 or 1 or 2))
            throw new ArgumentException("Invalid preview display dimensions or zoom.");
        var result = baseFrame;
        if (zoom != 1)
        {
            result = new byte[baseFrame.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var target = (y * width + x) * 4; result[target + 3] = 255;
                var sx = (int)Math.Floor((x + .5 - width / 2.0) / zoom + width / 2.0);
                var sy = (int)Math.Floor((y + .5 - height / 2.0) / zoom + height / 2.0);
                if (sx < 0 || sx >= width || sy < 0 || sy >= height) continue;
                Buffer.BlockCopy(baseFrame, (sy * width + sx) * 4, result, target, 4);
            }
        }
        if (overlay == PreviewOverlayMode.None) return result;
        if (ReferenceEquals(result, baseFrame)) result = (byte[])baseFrame.Clone();

        void Pixel(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            var offset = (y * width + x) * 4;
            for (var channel = 0; channel < 3; channel++)
                result[offset + channel] = (byte)((result[offset + channel] + 255) / 2);
        }

        switch (overlay)
        {
            case PreviewOverlayMode.SafeAreas:
                // SMPTE 90% Action Safe (5% inset) and 80% Title Safe (10% inset)
                foreach (var inset in new[] { .05, .1 })
                {
                    var left = Math.Clamp((int)Math.Round(width * inset), 0, width - 1); var right = width - left - 1;
                    var top = Math.Clamp((int)Math.Round(height * inset), 0, height - 1); var bottom = height - top - 1;
                    for (var x = left; x <= right; x++) { Pixel(x, top); Pixel(x, bottom); }
                    for (var y = top + 1; y < bottom; y++) { Pixel(left, y); Pixel(right, y); }
                }
                break;

            case PreviewOverlayMode.RuleOfThirds:
                // Rule of Thirds (3x3 grid)
                for (var i = 1; i <= 2; i++)
                {
                    var gx = Math.Clamp((int)Math.Round(width * (i / 3.0)), 0, width - 1);
                    var gy = Math.Clamp((int)Math.Round(height * (i / 3.0)), 0, height - 1);
                    for (var y = 0; y < height; y++) Pixel(gx, y);
                    for (var x = 0; x < width; x++) Pixel(x, gy);
                }
                break;

            case PreviewOverlayMode.Grid4x4:
                // 4x4 uniform grid
                for (var i = 1; i <= 3; i++)
                {
                    var gx = Math.Clamp((int)Math.Round(width * (i / 4.0)), 0, width - 1);
                    var gy = Math.Clamp((int)Math.Round(height * (i / 4.0)), 0, height - 1);
                    for (var y = 0; y < height; y++) Pixel(gx, y);
                    for (var x = 0; x < width; x++) Pixel(x, gy);
                }
                break;

            case PreviewOverlayMode.GoldenRatio:
                // Golden Ratio (phi grid at 0.382 and 0.618)
                foreach (var ratio in new[] { 0.382, 0.618 })
                {
                    var gx = Math.Clamp((int)Math.Round(width * ratio), 0, width - 1);
                    var gy = Math.Clamp((int)Math.Round(height * ratio), 0, height - 1);
                    for (var y = 0; y < height; y++) Pixel(gx, y);
                    for (var x = 0; x < width; x++) Pixel(x, gy);
                }
                break;
        }

        return result;
    }

    public static byte[] ApplySplit(byte[] currentBgra, byte[]? referenceBgra, int width, int height, SplitScreenDivision division)
    {
        if (width <= 0 || height <= 0 || currentBgra.Length != checked(width * height * 4))
            throw new ArgumentException("Invalid preview display dimensions.");
        if (referenceBgra == null || referenceBgra.Length != currentBgra.Length) return currentBgra;

        var result = (byte[])currentBgra.Clone();
        var midX = width / 2;

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width * 4;
            switch (division)
            {
                case SplitScreenDivision.LeftHalf:
                    // Reference on the left half, current on the right half
                    Buffer.BlockCopy(referenceBgra, rowOffset, result, rowOffset, midX * 4);
                    // 1px vertical divider line at midX (pure white #FFFFFF)
                    if (midX < width)
                    {
                        var divOffset = rowOffset + midX * 4;
                        result[divOffset] = 255;
                        result[divOffset + 1] = 255;
                        result[divOffset + 2] = 255;
                        result[divOffset + 3] = 255;
                    }
                    break;

                case SplitScreenDivision.RightHalf:
                    // Current on the left half, reference on the right half
                    var rightStart = (midX + 1) * 4;
                    var rightBytes = (width - midX - 1) * 4;
                    if (rightBytes > 0)
                    {
                        Buffer.BlockCopy(referenceBgra, rowOffset + rightStart, result, rowOffset + rightStart, rightBytes);
                    }
                    // 1px vertical divider line at midX
                    if (midX < width)
                    {
                        var divOffset = rowOffset + midX * 4;
                        result[divOffset] = 255;
                        result[divOffset + 1] = 255;
                        result[divOffset + 2] = 255;
                        result[divOffset + 3] = 255;
                    }
                    break;

                case SplitScreenDivision.All:
                    // 100% reference frame
                    Buffer.BlockCopy(referenceBgra, rowOffset, result, rowOffset, width * 4);
                    break;
            }
        }
        return result;
    }
}