using System;
using System.Collections.Generic;
using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class MediaGeneratorSynthesizerTests
{
    [Fact]
    public void SolidColor_GeneratesUniformRgbaPixels()
    {
        var media = new MediaAsset
        {
            Generator = new MediaGeneratorData
            {
                Kind = MediaGeneratorKind.SolidColor,
                Parameters = new Dictionary<string, string> { ["hex"] = "#E53935" } // Red
            }
        };

        var pixels = MediaGeneratorSynthesizer.Render(media, 100, 100);
        Assert.Equal(100 * 100 * 4, pixels.Length);

        // BGRA format: B=0x35, G=0x39, R=0xE5, A=0xFF
        Assert.Equal(0x35, pixels[0]); // B
        Assert.Equal(0x39, pixels[1]); // G
        Assert.Equal(0xE5, pixels[2]); // R
        Assert.Equal(0xFF, pixels[3]); // A
    }

    [Fact]
    public void TestPattern_GeneratesSMPTEBarsWithCorrectBands()
    {
        var media = new MediaAsset
        {
            Generator = new MediaGeneratorData
            {
                Kind = MediaGeneratorKind.TestPattern,
                PresetName = "SMPTE Bars (NTSC)",
                Parameters = new Dictionary<string, string> { ["type"] = "smpte_ntsc" }
            }
        };

        const int w = 700;
        const int h = 300;
        var pixels = MediaGeneratorSynthesizer.Render(media, w, h);
        Assert.Equal(w * h * 4, pixels.Length);

        // Top band (y = 50): 7 bars
        // Bar 0: Gray (192, 192, 192)
        int idxBar0 = (50 * w + 50) * 4;
        Assert.Equal(192, pixels[idxBar0]);     // B
        Assert.Equal(192, pixels[idxBar0 + 1]); // G
        Assert.Equal(192, pixels[idxBar0 + 2]); // R

        // Bar 1: Yellow (192, 192, 0) -> B=0, G=192, R=192
        int idxBar1 = (50 * w + 150) * 4;
        Assert.Equal(0, pixels[idxBar1]);       // B
        Assert.Equal(192, pixels[idxBar1 + 1]); // G
        Assert.Equal(192, pixels[idxBar1 + 2]); // R

        // Bar 5: Red (192, 0, 0) -> B=0, G=0, R=192
        int idxBar5 = (50 * w + 550) * 4;
        Assert.Equal(0, pixels[idxBar5]);       // B
        Assert.Equal(0, pixels[idxBar5 + 1]);   // G
        Assert.Equal(192, pixels[idxBar5 + 2]); // R
    }

    [Fact]
    public void TestPattern_Ramp_GeneratesLinearGrayscale()
    {
        var media = new MediaAsset
        {
            Generator = new MediaGeneratorData
            {
                Kind = MediaGeneratorKind.TestPattern,
                PresetName = "Ramp",
                Parameters = new Dictionary<string, string> { ["type"] = "ramp" }
            }
        };

        const int w = 256;
        const int h = 10;
        var pixels = MediaGeneratorSynthesizer.Render(media, w, h);

        // Check x=0 is 0 and x=255 is 255
        Assert.Equal(0, pixels[0]);
        Assert.Equal(0, pixels[1]);
        Assert.Equal(0, pixels[2]);

        int lastIdx = 255 * 4;
        Assert.Equal(255, pixels[lastIdx]);
        Assert.Equal(255, pixels[lastIdx + 1]);
        Assert.Equal(255, pixels[lastIdx + 2]);
    }

    [Fact]
    public void Checkerboard_GeneratesAlternatingTiles()
    {
        var media = new MediaAsset
        {
            Generator = new MediaGeneratorData
            {
                Kind = MediaGeneratorKind.Checkerboard,
                Parameters = new Dictionary<string, string>
                {
                    ["size"] = "10",
                    ["color1"] = "#FFFFFF",
                    ["color2"] = "#000000"
                }
            }
        };

        var pixels = MediaGeneratorSynthesizer.Render(media, 40, 40);

        // Tile (0, 0) -> White (x=5, y=5)
        int idx0 = (5 * 40 + 5) * 4;
        Assert.Equal(255, pixels[idx0]);
        Assert.Equal(255, pixels[idx0 + 1]);
        Assert.Equal(255, pixels[idx0 + 2]);

        // Tile (1, 0) -> Black (x=15, y=5)
        int idx1 = (5 * 40 + 15) * 4;
        Assert.Equal(0, pixels[idx1]);
        Assert.Equal(0, pixels[idx1 + 1]);
        Assert.Equal(0, pixels[idx1 + 2]);

        // Tile (1, 1) -> White (x=15, y=15)
        int idx11 = (15 * 40 + 15) * 4;
        Assert.Equal(255, pixels[idx11]);
        Assert.Equal(255, pixels[idx11 + 1]);
        Assert.Equal(255, pixels[idx11 + 2]);
    }
}

