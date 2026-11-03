using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class BuiltinLutLibraryTests
{
    [Theory]
    [InlineData("Sony S-Log3 to Rec.709")]
    [InlineData("ARRI LogC3 to Rec.709")]
    [InlineData("Panasonic V-Log to Rec.709")]
    [InlineData("RED Log3G10 to Rec.709")]
    [InlineData("Blackmagic Gen5 to Rec.709")]
    public void CameraLutPresets_GenerateValidCubeLut(string presetName)
    {
        var lut = BuiltinLutLibrary.CreatePreset(presetName);
        Assert.NotNull(lut);
        Assert.Equal(presetName, lut.Title);
        Assert.Equal(17, lut.Size);
        Assert.Equal(17 * 17 * 17 * 3, lut.Samples.Length);

        // All samples must be finite within [0, 1]
        for (var i = 0; i < lut.Samples.Length; i++)
        {
            Assert.True(double.IsFinite(lut.Samples[i]));
            Assert.InRange(lut.Samples[i], 0.0, 1.0);
        }

        // Validate method must succeed without throwing
        lut.Validate();
    }

    [Theory]
    [InlineData("Teal & Orange (Blockbuster)")]
    [InlineData("Film Bleach Bypass")]
    [InlineData("Warm Sunset / Golden Hour")]
    [InlineData("Vintage 1970s Kodachrome")]
    [InlineData("Cyberpunk Neon")]
    [InlineData("Monochrome Film Noir")]
    public void LookLutPresets_GenerateValidCubeLut(string presetName)
    {
        var lut = BuiltinLutLibrary.CreatePreset(presetName);
        Assert.NotNull(lut);
        Assert.Equal(presetName, lut.Title);
        Assert.Equal(17, lut.Size);
        Assert.Equal(17 * 17 * 17 * 3, lut.Samples.Length);

        for (var i = 0; i < lut.Samples.Length; i++)
        {
            Assert.True(double.IsFinite(lut.Samples[i]));
            Assert.InRange(lut.Samples[i], 0.0, 1.0);
        }

        lut.Validate();
    }

    [Fact]
    public void UnknownPreset_ReturnsNull()
    {
        Assert.Null(BuiltinLutLibrary.CreatePreset("NonExistentPreset"));
    }
}
