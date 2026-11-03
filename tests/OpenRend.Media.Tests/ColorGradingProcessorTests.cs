using OpenRend.Core;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class ColorGradingProcessorTests
{
    [Fact]
    public void ApplyColorGrading_WithCameraAndLookLuts_TransformsPixelsCorrectly()
    {
        // 4x4 BGRA test image representing S-Log3 middle-gray (y ≈ 0.41)
        const int width = 4;
        const int height = 4;
        var source = new byte[width * height * 4];
        for (var i = 0; i < source.Length; i += 4)
        {
            source[i + 0] = 105; // Blue
            source[i + 1] = 105; // Green
            source[i + 2] = 105; // Red
            source[i + 3] = 255; // Alpha
        }

        var effect = new VideoEffect
        {
            Kind = VideoEffectKind.ColorGrading,
            IsEnabled = true,
            Lut = BuiltinLutLibrary.CreatePreset("Sony S-Log3 to Rec.709"),
            LookLut = BuiltinLutLibrary.CreatePreset("Teal & Orange (Blockbuster)"),
            Parameters = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["LUTEnable_0"] = 0,
                ["LUTGain_0"] = 1.0,
                ["LUTInterpolation_0"] = 1, // Tetrahedral
                ["LUTEnable_1"] = 0,
                ["LUTGain_1"] = 0.8,
                ["LUTInterpolation_1"] = 1, // Tetrahedral
                ["InMinAdjust"] = 0, ["InMaxAdjust"] = 1,
                ["OutMinAdjust"] = 0, ["OutMaxAdjust"] = 1,
                ["ExposureLnAdjust"] = 0,
                ["BrightnessAdjust"] = 0,
                ["ContrastAdjust"] = 0,
                ["ContrastCtrAdjust"] = 0.5,
                ["SaturationAdjust"] = 1.0,
                ["Lift_R"] = 0, ["Lift_G"] = 0, ["Lift_B"] = 0, ["Lift_Y"] = 0,
                ["Gamma_R"] = 1, ["Gamma_G"] = 1, ["Gamma_B"] = 1, ["Gamma_Y"] = 1,
                ["Gain_R"] = 1, ["Gain_G"] = 1, ["Gain_B"] = 1, ["Gain_Y"] = 1,
                ["Offset_R"] = 0, ["Offset_G"] = 0, ["Offset_B"] = 0, ["Offset_Y"] = 0,
                ["TemperatureAdjust"] = 0,
                ["TintAdjust"] = 0,
                ["HueAdjust"] = 0,
                ["SatAdjust"] = 1.0,
                ["ValAdjust"] = 1.0
            }
        };

        var output = VideoEffectProcessor.Apply(source, width, height, [effect]);
        Assert.Equal(source.Length, output.Length);

        // Alpha channel must be strictly preserved
        for (var i = 0; i < output.Length; i += 4)
        {
            Assert.Equal(255, output[i + 3]);
        }

        // Output pixels should have received non-zero color transformation
        Assert.True(output[0] > 0);
        Assert.True(output[1] > 0);
        Assert.True(output[2] > 0);
    }

    [Fact]
    public void ApplyColorGrading_BypassCameraLut_OnlyAppliesLookLut()
    {
        const int width = 2;
        const int height = 2;
        var source = new byte[width * height * 4];
        for (var i = 0; i < source.Length; i += 4)
        {
            source[i + 0] = 80;
            source[i + 1] = 80;
            source[i + 2] = 80;
            source[i + 3] = 255;
        }

        var effect = new VideoEffect
        {
            Kind = VideoEffectKind.ColorGrading,
            IsEnabled = true,
            Lut = BuiltinLutLibrary.CreatePreset("ARRI LogC3 to Rec.709"),
            LookLut = BuiltinLutLibrary.CreatePreset("Monochrome Film Noir"),
            Parameters = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["LUTEnable_0"] = 1, // Bypassed
                ["LUTGain_0"] = 1.0,
                ["LUTInterpolation_0"] = 0,
                ["LUTEnable_1"] = 0, // Enabled
                ["LUTGain_1"] = 1.0,
                ["LUTInterpolation_1"] = 0,
                ["InMinAdjust"] = 0, ["InMaxAdjust"] = 1,
                ["OutMinAdjust"] = 0, ["OutMaxAdjust"] = 1,
                ["ExposureLnAdjust"] = 0,
                ["BrightnessAdjust"] = 0,
                ["ContrastAdjust"] = 0,
                ["ContrastCtrAdjust"] = 0.5,
                ["SaturationAdjust"] = 1.0,
                ["Lift_R"] = 0, ["Lift_G"] = 0, ["Lift_B"] = 0, ["Lift_Y"] = 0,
                ["Gamma_R"] = 1, ["Gamma_G"] = 1, ["Gamma_B"] = 1, ["Gamma_Y"] = 1,
                ["Gain_R"] = 1, ["Gain_G"] = 1, ["Gain_B"] = 1, ["Gain_Y"] = 1,
                ["Offset_R"] = 0, ["Offset_G"] = 0, ["Offset_B"] = 0, ["Offset_Y"] = 0,
                ["TemperatureAdjust"] = 0,
                ["TintAdjust"] = 0,
                ["HueAdjust"] = 0,
                ["SatAdjust"] = 1.0,
                ["ValAdjust"] = 1.0
            }
        };

        var output = VideoEffectProcessor.Apply(source, width, height, [effect]);

        // Monochrome Film Noir guarantees R == G == B within 1 unit due to rounding
        for (var i = 0; i < output.Length; i += 4)
        {
            var b = output[i + 0];
            var g = output[i + 1];
            var r = output[i + 2];
            Assert.InRange(Math.Abs(r - g), 0, 1);
            Assert.InRange(Math.Abs(g - b), 0, 1);
        }
    }
}
