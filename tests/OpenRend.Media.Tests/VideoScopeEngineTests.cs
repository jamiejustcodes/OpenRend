using System;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class VideoScopeEngineTests
{
    [Theory]
    [InlineData(VideoScopeMode.Waveform)]
    [InlineData(VideoScopeMode.Vectorscope)]
    [InlineData(VideoScopeMode.RgbParade)]
    [InlineData(VideoScopeMode.Histogram)]
    public void RenderScope_GeneratesValidBgraBufferForAllModes(VideoScopeMode mode)
    {
        int srcW = 320;
        int srcH = 240;
        int stride = srcW * 4;
        var srcPixels = new byte[stride * srcH];

        // Fill with synthetic SMPTE color gradient
        for (int y = 0; y < srcH; y++)
        {
            for (int x = 0; x < srcW; x++)
            {
                int p = y * stride + x * 4;
                srcPixels[p] = (byte)(x * 255 / srcW);       // Blue
                srcPixels[p + 1] = (byte)(y * 255 / srcH);   // Green
                srcPixels[p + 2] = 128;                     // Red
                srcPixels[p + 3] = 255;                     // Alpha
            }
        }

        int scopeW = 256;
        int scopeH = 192;
        var output = VideoScopeEngine.RenderScope(srcPixels, srcW, srcH, stride, scopeW, scopeH, mode);

        Assert.NotNull(output);
        Assert.Equal(scopeW * scopeH * 4, output.Length);

        // Verify that buffer is not all black (it has graticule and trace content)
        bool hasContent = false;
        for (int i = 0; i < output.Length; i += 4)
        {
            if (output[i] != 0x0A || output[i + 1] != 0x0E || output[i + 2] != 0x0C)
            {
                hasContent = true;
                break;
            }
        }
        Assert.True(hasContent, $"Scope in mode {mode} should render visible graticule or trace content.");
    }

    [Fact]
    public void RenderScope_HandlesEmptyOrInvalidBuffersGracefully()
    {
        var output = VideoScopeEngine.RenderScope(
            ReadOnlySpan<byte>.Empty, 0, 0, 0, 128, 128, VideoScopeMode.Waveform);

        Assert.NotNull(output);
        Assert.Equal(128 * 128 * 4, output.Length);
    }

    [Fact]
    public void Waveform_PureWhiteAndPureBlack_MapToBoundaryLevels()
    {
        int srcW = 100;
        int srcH = 100;
        int stride = srcW * 4;
        var srcPixels = new byte[stride * srcH];

        // Left half pure black (0,0,0), right half pure white (255,255,255)
        for (int y = 0; y < srcH; y++)
        {
            for (int x = 50; x < srcW; x++)
            {
                int p = y * stride + x * 4;
                srcPixels[p] = 255;
                srcPixels[p + 1] = 255;
                srcPixels[p + 2] = 255;
                srcPixels[p + 3] = 255;
            }
        }

        int scopeW = 100;
        int scopeH = 100;
        var output = VideoScopeEngine.RenderScope(
            srcPixels, srcW, srcH, stride, scopeW, scopeH, VideoScopeMode.Waveform, VideoScopeScale.FullRange);

        Assert.NotNull(output);
        Assert.Equal(scopeW * scopeH * 4, output.Length);

        // In pure white right half, top row (y = 0 in scope) should contain bright phosphor pixels
        int topRowOffset = 0;
        bool hasTopWhiteTrace = false;
        for (int x = 50; x < scopeW; x++)
        {
            int p = topRowOffset + x * 4;
            if (output[p + 1] > 50) // Green phosphor component elevated
            {
                hasTopWhiteTrace = true;
                break;
            }
        }
        Assert.True(hasTopWhiteTrace, "Pure white pixels should produce waveform trace at the top of the scope.");

        // In pure black left half, bottom row (y = scopeH - 1) should contain black trace
        int bottomRowOffset = (scopeH - 1) * scopeW * 4;
        bool hasBottomBlackTrace = false;
        for (int x = 0; x < 50; x++)
        {
            int p = bottomRowOffset + x * 4;
            if (output[p + 1] > 50)
            {
                hasBottomBlackTrace = true;
                break;
            }
        }
        Assert.True(hasBottomBlackTrace, "Pure black pixels should produce waveform trace at the bottom of the scope.");
    }

    [Fact]
    public void Histogram_Collects256BinsAccurately()
    {
        int srcW = 64;
        int srcH = 64;
        int stride = srcW * 4;
        var srcPixels = new byte[stride * srcH];

        // Fill with uniform red color (R=200, G=50, B=20)
        for (int i = 0; i < srcPixels.Length; i += 4)
        {
            srcPixels[i] = 20;   // B
            srcPixels[i + 1] = 50;  // G
            srcPixels[i + 2] = 200; // R
            srcPixels[i + 3] = 255;
        }

        int scopeW = 256;
        int scopeH = 100;
        var output = VideoScopeEngine.RenderScope(
            srcPixels, srcW, srcH, stride, scopeW, scopeH, VideoScopeMode.Histogram);

        Assert.NotNull(output);
        Assert.Equal(scopeW * scopeH * 4, output.Length);

        // Verify that x = 200 (corresponding to R=200) has tall bar with red component
        int bottomY = scopeH - 1;
        int p = (bottomY * scopeW + 200) * 4;
        Assert.True(output[p + 2] > 0, "Histogram column for R=200 should contain red channel bar.");
    }
}
