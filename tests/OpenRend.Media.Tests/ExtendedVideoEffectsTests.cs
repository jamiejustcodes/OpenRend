using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class ExtendedVideoEffectsTests
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
                var val = isWhite ? (byte)255 : (byte)0;
                data[idx] = val;
                data[idx + 1] = val;
                data[idx + 2] = val;
                data[idx + 3] = 255;
            }
        }
        return data;
    }

    [Fact]
    public void LinearBlurBypassesAtZeroAndBlursAlongAngleVector()
    {
        var effect = VideoEffect.Create(VideoEffectKind.LinearBlur);
        var source = CreateCheckerboard(16, 16);
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.Amount = 0.5;
        effect.SetParameter("Angle", 0); // horizontal
        var horizontalBlur = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, horizontalBlur);

        effect.SetParameter("Angle", 90); // vertical
        var verticalBlur = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, verticalBlur);
        Assert.NotEqual(horizontalBlur, verticalBlur);
    }

    [Fact]
    public void RadialBlurBypassesAtZeroAndZoomsFromCenter()
    {
        var effect = VideoEffect.Create(VideoEffectKind.RadialBlur);
        var source = CreateCheckerboard(16, 16);
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.Amount = 0.6;
        effect.SetParameter("CenterX", 0.5);
        effect.SetParameter("CenterY", 0.5);
        var centerResult = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, centerResult);

        effect.SetParameter("CenterX", 0.0);
        effect.SetParameter("CenterY", 0.0);
        var cornerResult = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(centerResult, cornerResult);
    }

    [Fact]
    public void ChromaBlurBypassesAtZeroAndPreservesPureLumaWhileSmoothingColor()
    {
        var effect = VideoEffect.Create(VideoEffectKind.ChromaBlur);
        var source = CreateCheckerboard(16, 16);
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        // Grayscale checkerboard has U=0, V=0. Chroma blur should leave monochrome pixels unchanged.
        effect.SetParameter("Horizontal", 5);
        effect.SetParameter("Vertical", 5);
        var monoResult = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.Equal(source, monoResult);

        // Color split: high chrominance edge
        var colorImage = new byte[8 * 8 * 4];
        for (var i = 0; i < colorImage.Length; i += 4)
        {
            var isLeft = (i / 4) % 8 < 4;
            colorImage[i] = (byte)(isLeft ? 255 : 0);     // Blue
            colorImage[i + 1] = 0;                       // Green
            colorImage[i + 2] = (byte)(isLeft ? 0 : 255); // Red
            colorImage[i + 3] = 255;                     // Alpha
        }
        var blurredColor = VideoEffectProcessor.Apply(colorImage, 8, 8, [effect]);
        Assert.NotEqual(colorImage, blurredColor);
        // Alpha must remain intact
        for (var i = 3; i < blurredColor.Length; i += 4)
            Assert.Equal(255, blurredColor[i]);
    }

    [Fact]
    public void SpherizeAndPinchPunchBypassAtZeroAndDistortCoordinates()
    {
        var spherize = VideoEffect.Create(VideoEffectKind.Spherize);
        var pinch = VideoEffect.Create(VideoEffectKind.PinchPunch);
        var source = CreateCheckerboard(16, 16);

        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [spherize]));
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [pinch]));

        spherize.Amount = 0.75;
        var sphered = VideoEffectProcessor.Apply(source, 16, 16, [spherize]);
        Assert.NotEqual(source, sphered);

        pinch.Amount = 0.75;
        var pinched = VideoEffectProcessor.Apply(source, 16, 16, [pinch]);
        Assert.NotEqual(source, pinched);
        Assert.NotEqual(sphered, pinched);
    }

    [Fact]
    public void TvSimulatorAppliesScanlinesAndDeterministicStatic()
    {
        var effect = VideoEffect.Create(VideoEffectKind.TvSimulator);
        var source = CreateCheckerboard(16, 16);
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.Amount = 0.8; // interlacing
        var scanlineResult = VideoEffectProcessor.Apply(source, 16, 16, [effect], projectFrame: 10);
        Assert.NotEqual(source, scanlineResult);

        // Frame-seeded static noise produces repeatable output on the same frame, different on another frame
        effect.SetParameter("Static", 0.5);
        var staticF10a = VideoEffectProcessor.Apply(source, 16, 16, [effect], projectFrame: 10);
        var staticF10b = VideoEffectProcessor.Apply(source, 16, 16, [effect], projectFrame: 10);
        var staticF20 = VideoEffectProcessor.Apply(source, 16, 16, [effect], projectFrame: 20);

        Assert.Equal(staticF10a, staticF10b);
        Assert.NotEqual(staticF10a, staticF20);
    }

    [Fact]
    public void CropModulatesAlphaOutsideBoundaries()
    {
        var effect = VideoEffect.Create(VideoEffectKind.Crop);
        var source = new byte[10 * 10 * 4];
        Array.Fill<byte>(source, 255);

        Assert.Same(source, VideoEffectProcessor.Apply(source, 10, 10, [effect]));

        effect.SetParameter("XCrop", 0.2); // 2 pixels on each side
        effect.SetParameter("YCrop", 0.2);
        effect.SetParameter("Opacity", 1.0); // fully transparent cropped area

        var cropped = VideoEffectProcessor.Apply(source, 10, 10, [effect]);
        Assert.NotEqual(source, cropped);

        // Center pixel (5, 5) should remain untouched
        var centerIdx = (5 * 10 + 5) * 4;
        Assert.Equal(255, cropped[centerIdx + 3]);

        // Corner pixel (0, 0) should have alpha = 0
        Assert.Equal(0, cropped[3]);
    }

    [Fact]
    public void NewsprintBypassesAtZeroAndTransformsHalftoneGrid()
    {
        var effect = VideoEffect.Create(VideoEffectKind.Newsprint);
        var source = CreateCheckerboard(16, 16);
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.Amount = 4; // 4px dot size
        effect.SetParameter("Type", 0); // Color
        var colorNews = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, colorNews);

        effect.SetParameter("Type", 1); // Monochrome
        var monoNews = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, monoNews);
        Assert.NotEqual(colorNews, monoNews);
    }

    [Fact]
    public void GradientMapBypassesAtZeroAndTransfersLuma()
    {
        var effect = VideoEffect.Create(VideoEffectKind.GradientMap);
        byte[] source = [0, 0, 0, 255, 128, 128, 128, 255, 255, 255, 255, 255];
        Assert.Same(source, VideoEffectProcessor.Apply(source, 3, 1, [effect]));

        effect.Amount = 1.0;
        effect.SetParameter("Mode", 0); // Blend
        var blendResult = VideoEffectProcessor.Apply(source, 3, 1, [effect]);
        Assert.NotEqual(source, blendResult);

        effect.SetParameter("Mode", 1); // Add
        var addResult = VideoEffectProcessor.Apply(source, 3, 1, [effect]);
        Assert.NotEqual(source, addResult);
        Assert.NotEqual(blendResult, addResult);
    }

    [Fact]
    public void DefocusBypassesAtZeroAndAppliesBloomBlur()
    {
        var effect = VideoEffect.Create(VideoEffectKind.Defocus);
        var source = CreateCheckerboard(16, 16);
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.Amount = 3;
        var defocused = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, defocused);
    }

    [Fact]
    public void LabAdjustBypassesAtDefaultAndAdjustsPerceptualChrominance()
    {
        var effect = VideoEffect.Create(VideoEffectKind.LabAdjust);
        byte[] source = [128, 128, 128, 255];
        Assert.Same(source, VideoEffectProcessor.Apply(source, 1, 1, [effect]));

        effect.SetParameter("ChannelA", 0.4); // Red-Green axis
        var redShifted = VideoEffectProcessor.Apply(source, 1, 1, [effect]);
        Assert.NotEqual(source, redShifted);

        effect.SetParameter("ChannelA", 0.0);
        effect.SetParameter("ChannelB", 0.4); // Yellow-Blue axis
        var yellowShifted = VideoEffectProcessor.Apply(source, 1, 1, [effect]);
        Assert.NotEqual(source, yellowShifted);
        Assert.NotEqual(redShifted, yellowShifted);
    }

    [Fact]
    public void MaskGeneratorCreatesAlphaMaskFromLuminance()
    {
        var effect = VideoEffect.Create(VideoEffectKind.MaskGenerator);
        effect.SetParameter("Type", 4); // Alpha
        effect.SetParameter("Invert", 0);
        effect.SetParameter("LowIn", 0);
        effect.SetParameter("HighIn", 1);
        effect.SetParameter("LowOut", 0);
        effect.SetParameter("HighOut", 1);

        byte[] source = [0, 0, 0, 255, 255, 255, 255, 255];
        Assert.Same(source, VideoEffectProcessor.Apply(source, 2, 1, [effect]));

        // Luminance mask: dark pixel should have lower alpha than white pixel
        effect.SetParameter("Type", 0); // Luminance
        var masked = VideoEffectProcessor.Apply(source, 2, 1, [effect]);
        Assert.NotEqual(source, masked);
        Assert.True(masked[3] < masked[7]); // Black pixel alpha < White pixel alpha
    }

    [Fact]
    public void PictureInPictureBypassesAtDefaultAndScalesAndRotates()
    {
        var effect = VideoEffect.Create(VideoEffectKind.PictureInPicture);
        var source = CreateCheckerboard(16, 16);
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.SetParameter("Scale", 0.5);
        var scaled = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, scaled);

        // Outside pip rect (corner pixel), alpha should be zero
        Assert.Equal(0, scaled[3]);
        // Center pixel should be non-zero
        var centerIdx = (8 * 16 + 8) * 4;
        Assert.NotEqual(0, scaled[centerIdx + 3]);
    }

    [Fact]
    public void LightRaysBypassesAtZeroAndEmitsRaysFromSource()
    {
        var effect = VideoEffect.Create(VideoEffectKind.LightRays);
        var source = CreateCheckerboard(16, 16);
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.Amount = 0.8;
        effect.SetParameter("LightSourceX", 0.5);
        effect.SetParameter("LightSourceY", 0.5);
        var rays = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, rays);
    }

    [Fact]
    public void StarburstBypassesAtZeroAndGeneratesHighlightSpikes()
    {
        var effect = VideoEffect.Create(VideoEffectKind.Starburst);
        var source = CreateCheckerboard(16, 16);
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.Amount = 60; // Strength
        effect.SetParameter("Threshold", 50);
        effect.SetParameter("Radius", 6);
        effect.SetParameter("Points", 4);
        var burst = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, burst);
    }

    [Fact]
    public void TinyPlanetBypassesAtDefaultAndWarpsSphericalProjection()
    {
        var effect = VideoEffect.Create(VideoEffectKind.TinyPlanet);
        var source = CreateCheckerboard(16, 16);
        Assert.Same(source, VideoEffectProcessor.Apply(source, 16, 16, [effect]));

        effect.SetParameter("Latitude", 60);
        var planet = VideoEffectProcessor.Apply(source, 16, 16, [effect]);
        Assert.NotEqual(source, planet);
    }

    [Fact]
    public void TimecodeRendersBurnInDigitsOntoFrame()
    {
        var effect = VideoEffect.Create(VideoEffectKind.Timecode);
        var source = new byte[64 * 32 * 4];
        Array.Fill<byte>(source, 100);

        var tcFrame0 = VideoEffectProcessor.Apply(source, 64, 32, [effect], projectFrame: 0);
        var tcFrame30 = VideoEffectProcessor.Apply(source, 64, 32, [effect], projectFrame: 30);

        Assert.NotEqual(source, tcFrame0);
        Assert.NotEqual(source, tcFrame30);
        // Different frames render different digits!
        Assert.NotEqual(tcFrame0, tcFrame30);
    }
}
