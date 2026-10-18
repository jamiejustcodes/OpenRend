using System.Text.Json.Nodes;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class ColorGradingTests
{
    [Fact]
    public void ColorGradingDefinitionHasAll36ParametersWithValidRangesAndDefaults()
    {
        var def = VideoEffectCatalog.Get(VideoEffectKind.ColorGrading);
        Assert.Equal("Color Grading", def.Name);
        Assert.Equal("Color", def.Category);
        Assert.Equal(36, def.Parameters.Count);

        var effect = VideoEffect.Create(VideoEffectKind.ColorGrading);
        effect.Validate();

        // Color Wheels defaults
        Assert.Equal(0, effect.GetParameter("Lift_R"));
        Assert.Equal(0, effect.GetParameter("Lift_G"));
        Assert.Equal(0, effect.GetParameter("Lift_B"));
        Assert.Equal(0, effect.GetParameter("Lift_Y"));
        Assert.Equal(1, effect.GetParameter("Gamma_R"));
        Assert.Equal(1, effect.GetParameter("Gamma_G"));
        Assert.Equal(1, effect.GetParameter("Gamma_B"));
        Assert.Equal(1, effect.GetParameter("Gamma_Y"));
        Assert.Equal(1, effect.GetParameter("Gain_R"));
        Assert.Equal(1, effect.GetParameter("Gain_G"));
        Assert.Equal(1, effect.GetParameter("Gain_B"));
        Assert.Equal(1, effect.GetParameter("Gain_Y"));
        Assert.Equal(0, effect.GetParameter("Offset_R"));
        Assert.Equal(0, effect.GetParameter("Offset_G"));
        Assert.Equal(0, effect.GetParameter("Offset_B"));
        Assert.Equal(0, effect.GetParameter("Offset_Y"));

        // Tone defaults
        Assert.Equal(0, effect.GetParameter("ExposureLnAdjust"));
        Assert.Equal(0, effect.GetParameter("BrightnessAdjust"));
        Assert.Equal(0, effect.GetParameter("ContrastAdjust"));
        Assert.Equal(0.5, effect.GetParameter("ContrastCtrAdjust"));
        Assert.Equal(1, effect.GetParameter("SaturationAdjust"));

        // White Balance defaults
        Assert.Equal(0, effect.GetParameter("TemperatureAdjust"));
        Assert.Equal(0, effect.GetParameter("TintAdjust"));

        // Levels defaults
        Assert.Equal(0, effect.GetParameter("InMinAdjust"));
        Assert.Equal(1, effect.GetParameter("InMaxAdjust"));
        Assert.Equal(0, effect.GetParameter("OutMinAdjust"));
        Assert.Equal(1, effect.GetParameter("OutMaxAdjust"));

        // HSL defaults
        Assert.Equal(0, effect.GetParameter("HueAdjust"));
        Assert.Equal(1, effect.GetParameter("SatAdjust"));
        Assert.Equal(1, effect.GetParameter("ValAdjust"));

        // LUT slots defaults
        Assert.Equal(1, effect.GetParameter("LUTGain_0"));
        Assert.Equal(0, effect.GetParameter("LUTInterpolation_0"));
        Assert.Equal(0, effect.GetParameter("LUTEnable_0"));
        Assert.Equal(1, effect.GetParameter("LUTGain_1"));
        Assert.Equal(0, effect.GetParameter("LUTInterpolation_1"));
        Assert.Equal(0, effect.GetParameter("LUTEnable_1"));
    }

    [Fact]
    public void ColorGradingRejectsInvalidOrOutOfRangeParameters()
    {
        var effect = VideoEffect.Create(VideoEffectKind.ColorGrading);
        Assert.Throws<InvalidDataException>(() => effect.SetParameter("Lift_R", 1.5));
        Assert.Throws<InvalidDataException>(() => effect.SetParameter("Lift_R", -1.5));
        Assert.Throws<InvalidDataException>(() => effect.SetParameter("Gamma_R", -0.1));
        Assert.Throws<InvalidDataException>(() => effect.SetParameter("Gamma_R", 5.1));
        Assert.Throws<InvalidDataException>(() => effect.SetParameter("TemperatureAdjust", 4001));
        Assert.Throws<InvalidDataException>(() => effect.SetParameter("TintAdjust", -101));
        Assert.Throws<InvalidDataException>(() => effect.SetParameter("ExposureLnAdjust", 6));
        Assert.Throws<ArgumentException>(() => effect.SetParameter("NonExistentKey", 0));
    }

    [Fact]
    public void ColorGradingDeepClonesAllParametersAndDualLutsIndependently()
    {
        var effect = VideoEffect.Create(VideoEffectKind.ColorGrading);
        effect.SetParameter("Lift_R", 0.25);
        effect.SetParameter("ExposureLnAdjust", 1.5);
        effect.Lut = CubeLutParser.Parse("LUT_3D_SIZE 2\n0 0 0\n0 0 1\n0 1 0\n0 1 1\n1 0 0\n1 0 1\n1 1 0\n1 1 1\n");
        effect.LookLut = CubeLutParser.Parse("LUT_3D_SIZE 2\n1 1 1\n1 1 0\n1 0 1\n1 0 0\n0 1 1\n0 1 0\n0 0 1\n0 0 0\n");

        var clone = effect.DeepClone();
        Assert.NotSame(effect.Parameters, clone.Parameters);
        Assert.NotSame(effect.Lut, clone.Lut);
        Assert.NotSame(effect.LookLut, clone.LookLut);
        Assert.Equal(0.25, clone.GetParameter("Lift_R"));
        Assert.Equal(1.5, clone.GetParameter("ExposureLnAdjust"));
        Assert.NotNull(clone.Lut);
        Assert.NotNull(clone.LookLut);

        clone.SetParameter("Lift_R", -0.5);
        Assert.Equal(0.25, effect.GetParameter("Lift_R"));
        Assert.Equal(-0.5, clone.GetParameter("Lift_R"));
    }

    [Fact]
    public void LookLutIsRejectedOnNonColorGradingEffects()
    {
        var effect = VideoEffect.Create(VideoEffectKind.BrightnessContrast);
        effect.LookLut = CubeLutParser.Parse("LUT_3D_SIZE 2\n0 0 0\n0 0 1\n0 1 0\n0 1 1\n1 0 0\n1 0 1\n1 1 0\n1 1 1\n");
        Assert.Throws<InvalidDataException>(effect.Validate);
    }

    [Fact]
    public async Task ColorGradingRoundTripsThroughProjectSerializationUnderSchema10()
    {
        var asset = new MediaAsset { Path = "test.mkv", HasVideo = true, DurationSeconds = 5 };
        var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 150 };
        var project = new ProjectDocument
        {
            Media = [asset],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
        };

        var effect = VideoEffect.Create(VideoEffectKind.ColorGrading);
        effect.SetParameter("Lift_R", 0.15);
        effect.SetParameter("Gamma_G", 1.25);
        effect.SetParameter("Gain_B", 0.85);
        effect.SetParameter("ExposureLnAdjust", 0.75);
        effect.SetParameter("TemperatureAdjust", 600);
        effect.SetParameter("TintAdjust", -25);
        effect.SetParameter("InMinAdjust", 0.05);
        effect.SetParameter("InMaxAdjust", 0.95);
        effect.Lut = CubeLutParser.Parse("LUT_3D_SIZE 2\n0 0 0\n0 0 1\n0 1 0\n0 1 1\n1 0 0\n1 0 1\n1 1 0\n1 1 1\n");
        clip.VideoEffects.Add(effect);

        var path = Path.Combine(Path.GetTempPath(), "OpenRend-colorgrading-test-" + Guid.NewGuid().ToString("N") + ".openrend");
        try
        {
            await ProjectStore.SaveAsync(project, path);
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            Assert.Equal(ProjectStore.CurrentVersion, (int)manifest["Version"]!);

            var loaded = await ProjectStore.LoadAsync(path);
            var loadedFx = loaded.Tracks[0].Clips[0].VideoEffects[0];
            Assert.Equal(VideoEffectKind.ColorGrading, loadedFx.Kind);
            Assert.Equal(0.15, loadedFx.GetParameter("Lift_R"));
            Assert.Equal(1.25, loadedFx.GetParameter("Gamma_G"));
            Assert.Equal(0.85, loadedFx.GetParameter("Gain_B"));
            Assert.Equal(0.75, loadedFx.GetParameter("ExposureLnAdjust"));
            Assert.Equal(600, loadedFx.GetParameter("TemperatureAdjust"));
            Assert.Equal(-25, loadedFx.GetParameter("TintAdjust"));
            Assert.Equal(0.05, loadedFx.GetParameter("InMinAdjust"));
            Assert.Equal(0.95, loadedFx.GetParameter("InMaxAdjust"));
            Assert.NotNull(loadedFx.Lut);
            Assert.Equal(2, loadedFx.Lut.Size);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
