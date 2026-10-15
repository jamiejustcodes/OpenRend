using OpenRend.Core;
using OpenRend.UI;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class PreviewDisplayTests
{
    private static byte[] Pattern(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 4;
            pixels[offset] = (byte)(x + y * 10); pixels[offset + 1] = (byte)x; pixels[offset + 2] = (byte)y; pixels[offset + 3] = 255;
        }
        return pixels;
    }

    [Fact]
    public void FullSizePreviewWithoutGuidesReusesTheUnchangedFrame()
    {
        var source = Pattern(4, 4); var before = source.ToArray();
        Assert.Same(source, PreviewDisplay.Apply(source, 4, 4, 1, false));
        Assert.Equal(before, source);
    }

    [Fact]
    public void DoubleZoomCropsTheCenterAndKeepsTheSourceUnchanged()
    {
        var source = Pattern(4, 4); var before = source.ToArray();
        var result = PreviewDisplay.Apply(source, 4, 4, 2, false);
        Assert.NotSame(source, result);
        Assert.Equal(before, source);
        Assert.Equal(new byte[] { 11, 11, 12, 12, 11, 11, 12, 12, 21, 21, 22, 22, 21, 21, 22, 22 }, result.Where((_, i) => i % 4 == 0));
        Assert.All(result.Where((_, i) => i % 4 == 3), alpha => Assert.Equal(255, alpha));
    }

    [Fact]
    public void HalfZoomCentersTheImageInsideOpaqueBlackMargins()
    {
        var result = PreviewDisplay.Apply(Pattern(4, 4), 4, 4, .5, false);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 11, 13, 0, 0, 31, 33, 0, 0, 0, 0, 0 }, result.Where((_, i) => i % 4 == 0));
        Assert.All(result.Where((_, i) => i % 4 == 3), alpha => Assert.Equal(255, alpha));
    }

    [Fact]
    public void SafeAreasBlendOnlyGuidePixelsOnAnIndependentDisplayBuffer()
    {
        var source = new byte[100 * 100 * 4];
        for (var i = 0; i < source.Length; i += 4) { source[i] = 10; source[i + 1] = 20; source[i + 2] = 30; source[i + 3] = 255; }
        var before = source.ToArray();
        var result = PreviewDisplay.Apply(source, 100, 100, 1, true);
        Assert.NotSame(source, result); Assert.Equal(before, source);
        foreach (var (x, y) in new[] { (50, 5), (5, 5), (50, 94), (50, 10), (10, 50), (89, 50) })
            Assert.Equal(new byte[] { 132, 137, 142, 255 }, result.Skip((y * 100 + x) * 4).Take(4));
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, result.Skip((50 * 100 + 50) * 4).Take(4));
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, result.Take(4));
    }

    [Theory]
    [InlineData(0, 4, 1)]
    [InlineData(4, 0, 1)]
    [InlineData(-1, 4, 1)]
    [InlineData(4, 4, .25)]
    [InlineData(4, 4, 1.25)]
    [InlineData(4, 4, double.NaN)]
    public void InvalidDisplayDimensionsAndZoomAreRejected(int width, int height, double zoom) => Assert.Throws<ArgumentException>(() => PreviewDisplay.Apply(new byte[64], width, height, zoom, false));

    [Fact]
    public void InvalidBufferLengthIsRejected() => Assert.Throws<ArgumentException>(() => PreviewDisplay.Apply(new byte[63], 4, 4, 1, false));

    [Theory]
    [InlineData(0, 30000, 1001, "00:00:00;00")]
    [InlineData(1799, 30000, 1001, "00:00:59;29")]
    [InlineData(1800, 30000, 1001, "00:01:00;02")]
    [InlineData(3597, 30000, 1001, "00:01:59;29")]
    [InlineData(3598, 30000, 1001, "00:02:00;02")]
    [InlineData(17981, 30000, 1001, "00:09:59;29")]
    [InlineData(17982, 30000, 1001, "00:10:00;00")]
    [InlineData(19782, 30000, 1001, "00:11:00;02")]
    [InlineData(107892, 30000, 1001, "01:00:00;00")]
    [InlineData(3599, 60000, 1001, "00:00:59;59")]
    [InlineData(3600, 60000, 1001, "00:01:00;04")]
    [InlineData(35963, 60000, 1001, "00:09:59;59")]
    [InlineData(35964, 60000, 1001, "00:10:00;00")]
    [InlineData(215784, 60000, 1001, "01:00:00;00")]
    [InlineData(1800, 30, 1, "00:01:00:00")]
    [InlineData(108000, 30, 1, "01:00:00:00")]
    [InlineData(24, 24000, 1001, "00:00:01:00")]
    public void TimecodeRespectsDropFrameMinuteTenMinuteAndHourBoundaries(long frame, int numerator, int denominator, string expected) => Assert.Equal(expected, TimelineView.FormatTimecode(frame, new Rational(numerator, denominator)));
}
