using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class ColorGradingPixelTests
{
    private static byte[] SolidBgra(byte b, byte g, byte r, byte a, int count = 16)
    {
        var buffer = new byte[count * 4];
        for (var i = 0; i < buffer.Length; i += 4)
        {
            buffer[i] = b;
            buffer[i + 1] = g;
            buffer[i + 2] = r;
            buffer[i + 3] = a;
        }
        return buffer;
    }

    [Fact]
    public void ColorGradingDefaultStateIsIdentityAndDoesNotModifyPixels()
    {
        var effect = VideoEffect.Create(VideoEffectKind.ColorGrading);
        var source = SolidBgra(50, 100, 150, 200);
        var result = VideoEffectProcessor.Apply(source, 4, 4, [effect]);
        Assert.Same(source, result);
    }

    [Fact]
    public void ColorGradingPreservesAlphaChannelAcrossAllTransforms()
    {
        var effect = VideoEffect.Create(VideoEffectKind.ColorGrading);
        effect.SetParameter("ExposureLnAdjust", 1.5);
        effect.SetParameter("BrightnessAdjust", 0.2);
        effect.SetParameter("Lift_R", 0.3);
        effect.SetParameter("Gamma_G", 1.4);
        effect.SetParameter("Gain_B", 0.7);
        effect.SetParameter("TemperatureAdjust", 1500);

        var source = new byte[16 * 4];
        for (var i = 0; i < source.Length; i += 4)
        {
            source[i] = (byte)(i * 3 % 255);
            source[i + 1] = (byte)(i * 7 % 255);
            source[i + 2] = (byte)(i * 11 % 255);
            source[i + 3] = (byte)(40 + i * 5 % 200); // distinct alpha per pixel
        }

        var result = VideoEffectProcessor.Apply(source, 4, 4, [effect]);
        for (var i = 3; i < source.Length; i += 4)
        {
            Assert.Equal(source[i], result[i]);
        }
    }

    [Fact]
    public void LiftAdjustsShadowsWhileLeavingHighlightsAnchored()
    {
        var effect = VideoEffect.Create(VideoEffectKind.ColorGrading);
        effect.SetParameter("Lift_R", 0.5); // Lift red

        // Test black pixel (0, 0, 0) -> should be raised
        var black = SolidBgra(0, 0, 0, 255, 1);
        var blackResult = VideoEffectProcessor.Apply(black, 1, 1, [effect]);
        // Red should be raised: 0 + 0.5 * (1 - 0) = 0.5 -> ~128
        Assert.True(blackResult[2] > 100, $"Expected raised red in shadow, got {blackResult[2]}");
        Assert.Equal(0, blackResult[1]); // Green untouched
        Assert.Equal(0, blackResult[0]); // Blue untouched

        // Test white pixel (255, 255, 255) -> 1 - 1 = 0, so lift has no effect at 1.0
        var white = SolidBgra(255, 255, 255, 255, 1);
        var whiteResult = VideoEffectProcessor.Apply(white, 1, 1, [effect]);
        Assert.Equal(255, whiteResult[2]);
    }

    [Fact]
    public void GainScalesHighlightsProportionally()
    {
        var effect = VideoEffect.Create(VideoEffectKind.ColorGrading);
        effect.SetParameter("Gain_G", 1.5); // 1.5x gain on green

        var mid = SolidBgra(100, 100, 100, 255, 1);
        var result = VideoEffectProcessor.Apply(mid, 1, 1, [effect]);
        // 100 * 1.5 = 150
        Assert.InRange(result[1], 145, 155);
        Assert.Equal(100, result[0]); // Blue unchanged
        Assert.Equal(100, result[2]); // Red unchanged
    }

    [Fact]
    public void ExposureMultipliesByPowersOfTwo()
    {
        var effect = VideoEffect.Create(VideoEffectKind.ColorGrading);
        effect.SetParameter("ExposureLnAdjust", 1.0); // +1 EV = 2x

        var gray = SolidBgra(64, 64, 64, 255, 1);
        var result = VideoEffectProcessor.Apply(gray, 1, 1, [effect]);
        Assert.InRange(result[0], 126, 130);
        Assert.InRange(result[1], 126, 130);
        Assert.InRange(result[2], 126, 130);
    }

    [Fact]
    public void WhiteBalanceShiftsTemperatureAndTintAccurately()
    {
        var effect = VideoEffect.Create(VideoEffectKind.ColorGrading);
        effect.SetParameter("TemperatureAdjust", 2000); // Warm: red increases, blue decreases

        var gray = SolidBgra(128, 128, 128, 255, 1);
        var warmResult = VideoEffectProcessor.Apply(gray, 1, 1, [effect]);
        Assert.True(warmResult[2] > gray[2], "Red should increase with warm temperature");
        Assert.True(warmResult[0] < gray[0], "Blue should decrease with warm temperature");

        effect.SetParameter("TemperatureAdjust", 0);
        effect.SetParameter("TintAdjust", 50); // Tint: magenta shift (green decreases, red/blue increase)
        var tintResult = VideoEffectProcessor.Apply(gray, 1, 1, [effect]);
        Assert.True(tintResult[1] < gray[1], "Green should decrease with positive magenta tint");
        Assert.True(tintResult[2] > gray[2], "Red should increase with magenta tint");
        Assert.True(tintResult[0] > gray[0], "Blue should increase with magenta tint");
    }

    [Fact]
    public void DualLutSlotsEvaluateInProperOrderAndCanBeBypassed()
    {
        var effect = VideoEffect.Create(VideoEffectKind.ColorGrading);
        // Camera LUT: Inverts Red and Blue
        effect.Lut = CubeLutParser.Parse("LUT_3D_SIZE 2\n0 0 1\n0 1 1\n1 0 1\n1 1 1\n0 0 0\n0 1 0\n1 0 0\n1 1 0\n");
        // Look LUT: Scales down brightness
        effect.LookLut = CubeLutParser.Parse("LUT_3D_SIZE 2\n0 0 0\n0 0 0.5\n0 0.5 0\n0 0.5 0.5\n0.5 0 0\n0.5 0 0.5\n0.5 0.5 0\n0.5 0.5 0.5\n");

        var source = SolidBgra(0, 0, 255, 255, 1); // Pure Red in BGRA (B=0, G=0, R=255)
        var result = VideoEffectProcessor.Apply(source, 1, 1, [effect]);
        Assert.False(source.SequenceEqual(result));

        // Bypass Camera LUT (LUTEnable_0 = 1)
        effect.SetParameter("LUTEnable_0", 1);
        var bypassedCameraResult = VideoEffectProcessor.Apply(source, 1, 1, [effect]);
        Assert.False(result.SequenceEqual(bypassedCameraResult));

        // Bypass Look LUT too (LUTEnable_1 = 1)
        effect.SetParameter("LUTEnable_1", 1);
        var bothBypassed = VideoEffectProcessor.Apply(source, 1, 1, [effect]);
        Assert.Equal(source, bothBypassed);
    }

    [Fact]
    public void ColorCurvesModifyPixelsAccuratelyPerChannelAndMaster()
    {
        var effect = VideoEffect.Create(VideoEffectKind.ColorGrading);
        var curves = new ColorCurvesData();
        // Boost midtones on Red channel
        curves.Red.Points.Add(new CurvePoint { X = 0.5, Y = 0.8 });
        curves.Red.SortPoints();
        effect.Curves = curves;

        var midGray = SolidBgra(128, 128, 128, 255, 1);
        var result = VideoEffectProcessor.Apply(midGray, 1, 1, [effect]);
        // Red is index 2, Green is index 1, Blue is index 0
        Assert.True(result[2] > 180, $"Expected boosted red, got {result[2]}");
        Assert.InRange(result[1], 126, 130); // Green unchanged
        Assert.InRange(result[0], 126, 130); // Blue unchanged

        // Now also pull down Blue via Blue curve
        curves.Blue.Points.Add(new CurvePoint { X = 0.5, Y = 0.2 });
        curves.Blue.SortPoints();
        var result2 = VideoEffectProcessor.Apply(midGray, 1, 1, [effect]);
        Assert.True(result2[2] > 180);
        Assert.True(result2[0] < 80, $"Expected suppressed blue, got {result2[0]}");
    }
}

