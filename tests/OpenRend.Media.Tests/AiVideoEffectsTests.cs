using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class AiVideoEffectsTests
{
    private static byte[] CreateCheckerboard(int width, int height)
    {
        var data = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var idx = (y * width + x) * 4;
                var isWhite = ((x / 4) + (y / 4)) % 2 == 0;
                var val = isWhite ? (byte)200 : (byte)60;
                data[idx] = val;
                data[idx + 1] = val;
                data[idx + 2] = val;
                data[idx + 3] = 255;
            }
        }
        return data;
    }

    [Fact]
    public void AiAutoReframeBypassesAtIdentityAndRendersAspectPreview()
    {
        var effect = VideoEffect.Create(VideoEffectKind.AiAutoReframe);
        var source = CreateCheckerboard(16, 16);
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.SetParameter("Preview", 2); // Portrait 9:16
        var portrait = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, portrait);

        effect.SetParameter("Preview", 4); // Square 1:1
        var square = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, square);
        Assert.NotEqual(portrait, square);
    }

    [Fact]
    public void AiColorizationBypassesAtDefaultAndModulatesChroma()
    {
        var effect = VideoEffect.Create(VideoEffectKind.AiColorization);
        var source = CreateCheckerboard(16, 16);
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.SetParameter("ColorShiftA", 0.5);
        var shiftedA = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, shiftedA);

        effect.SetParameter("ColorShiftB", -0.5);
        var shiftedB = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(shiftedA, shiftedB);
    }

    [Fact]
    public void AiDehazeBypassesAtZeroAndRecoversContrast()
    {
        var effect = VideoEffect.Create(VideoEffectKind.AiDehaze);
        var source = CreateCheckerboard(16, 16);
        effect.Amount = 0;
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.Amount = 0.8;
        var dehazed = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, dehazed);
    }

    [Fact]
    public void AiSharpenBypassesAtZeroAndSharpensEdges()
    {
        var effect = VideoEffect.Create(VideoEffectKind.AiSharpen);
        var source = CreateCheckerboard(16, 16);
        effect.Amount = 0;
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.Amount = 1.0;
        var sharpened = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, sharpened);
    }

    [Fact]
    public void AiSmoothBypassesAtZeroAndSmoothesTextures()
    {
        var effect = VideoEffect.Create(VideoEffectKind.AiSmooth);
        var source = CreateCheckerboard(16, 16);
        effect.Amount = 0;
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.Amount = 0.7;
        var smoothed = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, smoothed);
    }

    [Fact]
    public void AiStyleTransferBypassesAtZeroAndAppliesPainterlyPalette()
    {
        var effect = VideoEffect.Create(VideoEffectKind.AiStyleTransfer);
        var source = CreateCheckerboard(16, 16);
        effect.Amount = 0;
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.Amount = 1.0;
        effect.SetParameter("Style", 0); // Picasso
        var picasso = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, picasso);

        effect.SetParameter("Style", 1); // Afremov Night Alley
        var afremov = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, afremov);
        Assert.NotEqual(picasso, afremov);
    }

    [Fact]
    public void AiUpscaleBypassesAtOneAndEnhancesResolution()
    {
        var effect = VideoEffect.Create(VideoEffectKind.AiUpscale);
        var source = CreateCheckerboard(16, 16);
        effect.SetParameter("Scaling", 1.0);
        effect.SetParameter("ImproveSharpness", 0);
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.SetParameter("Scaling", 2.0);
        effect.SetParameter("ImproveSharpness", 1);
        var upscaled = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, upscaled);
    }

    [Fact]
    public void AiZDepthBypassesAtIdentityAndRendersDepthMap()
    {
        var effect = VideoEffect.Create(VideoEffectKind.AiZDepth);
        var source = CreateCheckerboard(16, 16);
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.SetParameter("Visualization", 2); // Depth map grayscale
        var depthMap = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, depthMap);

        effect.SetParameter("Visualization", 1); // Thresholded depth
        var thresh = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(depthMap, thresh);
    }

    [Fact]
    public void AiSmartMaskBypassesAtIdentityAndOutputsMatteAndOverlay()
    {
        var effect = VideoEffect.Create(VideoEffectKind.AiSmartMask);
        var source = CreateCheckerboard(16, 16);
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.SetParameter("Visualization", 1); // View Matte
        var matte = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, matte);

        effect.SetParameter("Visualization", 2); // Mask Overlay
        var overlay = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, overlay);
        Assert.NotEqual(matte, overlay);
    }
}
