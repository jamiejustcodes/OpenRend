using System;
using System.IO;
using System.Linq;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class MediaGeneratorTests
{
    [Fact]
    public void MediaGenerator_Catalog_ContainsVegasProStandardGenerators()
    {
        var presets = MediaGeneratorCatalog.Presets;
        Assert.NotEmpty(presets);

        // Verify the 7 Vegas Pro Generator Kinds
        var kinds = presets.Select(p => p.Kind).Distinct().ToList();
        Assert.Contains(MediaGeneratorKind.TestPattern, kinds);
        Assert.Contains(MediaGeneratorKind.SolidColor, kinds);
        Assert.Contains(MediaGeneratorKind.Checkerboard, kinds);
        Assert.Contains(MediaGeneratorKind.ColorGradient, kinds);
        Assert.Contains(MediaGeneratorKind.NoiseTexture, kinds);

        // Verify Test Pattern presets
        var testPatterns = presets.Where(p => p.Kind == MediaGeneratorKind.TestPattern).Select(p => p.Name).ToList();
        Assert.Contains("(Default)", testPatterns);
        Assert.Contains("SMPTE Bars (NTSC)", testPatterns);
        Assert.Contains("SMPTE Bars (PAL)", testPatterns);
        Assert.Contains("Color Bars (NTSC)", testPatterns);
        Assert.Contains("Color Bars (PAL)", testPatterns);
        Assert.Contains("Ramp", testPatterns);
        Assert.Contains("Gradient", testPatterns);
    }

    [Fact]
    public void MediaGenerator_CreateUri_GeneratesValidFormat()
    {
        var uri = MediaGeneratorCatalog.CreateUri(
            MediaGeneratorKind.SolidColor,
            "Red",
            new Dictionary<string, string> { ["hex"] = "#FF0000" }
        );

        Assert.StartsWith("openrend://generator?", uri);
        Assert.Contains("kind=SolidColor", uri);
        Assert.Contains("preset=Red", uri);
        Assert.Contains("hex=%23FF0000", uri);
    }

    [Fact]
    public void MediaGenerator_Validation_DetectsInvalidKinds()
    {
        var data = new MediaGeneratorData
        {
            Kind = (MediaGeneratorKind)999
        };
        Assert.Throws<InvalidDataException>(() => data.Validate());

        var valid = new MediaGeneratorData
        {
            Kind = MediaGeneratorKind.TestPattern,
            PresetName = "SMPTE Bars (NTSC)"
        };
        valid.Validate(); // Should not throw
    }
}

