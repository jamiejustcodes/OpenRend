using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public class MaskRasterizerTests
{
    [Fact]
    public void ApplyMask_PositiveRectangle_KeepsInsideAndClearsOutside()
    {
        const int width = 100;
        const int height = 100;
        var pixels = new byte[width * height * 4];

        // Fill test image with opaque white (BGRA: 255, 255, 255, 255)
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 255;
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }

        // Positive rectangle mask from (25, 25) to (75, 75)
        var mask = MaskShape.CreateRectangle(25, 25, 50, 50);
        mask.Paths[0].Mode = MaskMode.Positive;
        mask.Paths[0].FeatherType = MaskFeatherType.None;
        mask.Paths[0].AntiAlias = false;

        MaskRasterizer.ApplyMask(pixels, width, height, mask);

        // Center pixel (50, 50) must remain opaque
        var centerOffset = (50 * width + 50) * 4;
        Assert.Equal(255, pixels[centerOffset + 3]);
        Assert.Equal(255, pixels[centerOffset]);

        // Corner pixel (5, 5) outside mask must be transparent
        var cornerOffset = (5 * width + 5) * 4;
        Assert.Equal(0, pixels[cornerOffset + 3]);
    }

    [Fact]
    public void ApplyMask_NegativeRectangle_CutsHole()
    {
        const int width = 100;
        const int height = 100;
        var pixels = new byte[width * height * 4];

        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 255;
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }

        // Negative rectangle mask from (25, 25) to (75, 75)
        var mask = MaskShape.CreateRectangle(25, 25, 50, 50);
        mask.Paths[0].Mode = MaskMode.Negative;
        mask.Paths[0].FeatherType = MaskFeatherType.None;
        mask.Paths[0].AntiAlias = false;

        MaskRasterizer.ApplyMask(pixels, width, height, mask);

        // Center pixel (50, 50) inside negative mask must be cut out (transparent)
        var centerOffset = (50 * width + 50) * 4;
        Assert.Equal(0, pixels[centerOffset + 3]);

        // Corner pixel (5, 5) outside negative mask must remain opaque
        var cornerOffset = (5 * width + 5) * 4;
        Assert.Equal(255, pixels[cornerOffset + 3]);
    }

    [Fact]
    public void ApplyMask_WithFeathering_GeneratesSmoothAlphaGradient()
    {
        const int width = 100;
        const int height = 100;
        var pixels = new byte[width * height * 4];

        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 200;
            pixels[i + 1] = 200;
            pixels[i + 2] = 200;
            pixels[i + 3] = 255;
        }

        var mask = MaskShape.CreateRectangle(30, 30, 40, 40);
        mask.Paths[0].Mode = MaskMode.Positive;
        mask.Paths[0].FeatherType = MaskFeatherType.Both;
        mask.Paths[0].Feather = 20.0; // 20% feather

        MaskRasterizer.ApplyMask(pixels, width, height, mask);

        // Center (50, 50) has high alpha
        var centerAlpha = pixels[(50 * width + 50) * 4 + 3];
        Assert.True(centerAlpha > 200);

        // Edge region (e.g. 30, 50) has intermediate feathered alpha between 0 and 255
        var edgeAlpha = pixels[(30 * width + 50) * 4 + 3];
        Assert.True(edgeAlpha > 10 && edgeAlpha < 250);

        // Far outside (5, 5) has near-zero alpha
        var outsideAlpha = pixels[(5 * width + 5) * 4 + 3];
        Assert.True(outsideAlpha < 10);
    }
}
