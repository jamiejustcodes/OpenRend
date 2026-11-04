using System;
using System.Text.Json;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class HSLCurvesTests
{
    [Fact]
    public void DefaultHSLCurvesAreIdentity()
    {
        var hsl = new HSLCurvesData();
        Assert.False(hsl.HasAdjustments);
        Assert.True(hsl.HueVsHue.IsIdentity);
        Assert.True(hsl.HueVsSat.IsIdentity);
        Assert.True(hsl.HueVsLum.IsIdentity);
        Assert.True(hsl.SatVsSat.IsIdentity);
        Assert.True(hsl.LumVsSat.IsIdentity);

        Assert.Equal(6, hsl.HueVsHue.Points.Count);
        Assert.Equal(5, hsl.SatVsSat.Points.Count);

        // All default points should have Y = 0.5 (neutral)
        foreach (var p in hsl.HueVsHue.Points)
        {
            Assert.Equal(0.5, p.Y);
        }
    }

    [Fact]
    public void NonNeutralPointFlagsAdjustments()
    {
        var hsl = new HSLCurvesData();
        hsl.HueVsHue.Points[0].Y = 0.7; // shift red hue
        Assert.True(hsl.HasAdjustments);
        Assert.False(hsl.HueVsHue.IsIdentity);
    }

    [Fact]
    public void HuePeriodicInterpolationWrapsSmoothly()
    {
        var curve = HSLCurve.CreateDefaultHue();
        // Modify the first point (0°, Red) to 0.75 and last point (300°, Magenta) to 0.65
        curve.Points[0].Y = 0.75;
        curve.Points[^1].Y = 0.65;

        // Evaluate at 0.0 -> should be 0.75
        var valAt0 = curve.Evaluate(0.0, isHuePeriodic: true);
        Assert.Equal(0.75, valAt0, 3);

        // Evaluate near 1.0 (approaching 360°/0°) -> should smoothly interpolate between 300° (0.65) and 0° (0.75)
        var valNearEnd = curve.Evaluate(0.95, isHuePeriodic: true);
        Assert.InRange(valNearEnd, 0.65, 0.75);

        // Evaluate near 0.02 -> should smoothly interpolate between 300° and 0° or 0° and 60°
        var valNearStart = curve.Evaluate(0.02, isHuePeriodic: true);
        Assert.InRange(valNearStart, 0.5, 0.75);
    }

    [Fact]
    public void LinearNonPeriodicCurveClampsAtExtremes()
    {
        var curve = HSLCurve.CreateDefaultLinear();
        curve.Points[0].Y = 0.2;
        curve.Points[^1].Y = 0.9;

        Assert.Equal(0.2, curve.Evaluate(0.0, isHuePeriodic: false), 3);
        Assert.Equal(0.2, curve.Evaluate(-0.1, isHuePeriodic: false), 3);
        Assert.Equal(0.9, curve.Evaluate(1.0, isHuePeriodic: false), 3);
        Assert.Equal(0.9, curve.Evaluate(1.5, isHuePeriodic: false), 3);
    }

    [Fact]
    public void BuildLutGeneratesExpectedLutSize()
    {
        var curve = HSLCurve.CreateDefaultHue();
        curve.Points[1].Y = 0.8; // adjust yellow

        var lut = curve.BuildLut(360, isHuePeriodic: true);
        Assert.Equal(360, lut.Length);

        // Step corresponding to yellow (60° / 360 * 360 = 60)
        Assert.Equal(0.8, lut[60], 2);
    }

    [Fact]
    public void HSLCurvesDataDeepCloningProducesIndependentInstances()
    {
        var original = new HSLCurvesData();
        original.HueVsSat.Points[2].Y = 0.3;

        var clone = original.DeepClone();
        Assert.Equal(0.3, clone.HueVsSat.Points[2].Y);

        clone.HueVsSat.Points[2].Y = 0.9;
        Assert.Equal(0.3, original.HueVsSat.Points[2].Y);
        Assert.Equal(0.9, clone.HueVsSat.Points[2].Y);
    }

    [Fact]
    public void VideoEffectWithHSLCurvesSerializesAndDeserializesAccurately()
    {
        var effect = new VideoEffect
        {
            Kind = VideoEffectKind.ColorGrading,
            HSLCurves = new HSLCurvesData()
        };
        effect.HSLCurves.HueVsHue.Points[0].Y = 0.65;
        effect.HSLCurves.SatVsSat.Points[1].Y = 0.35;

        var json = JsonSerializer.Serialize(effect);
        var restored = JsonSerializer.Deserialize<VideoEffect>(json);

        Assert.NotNull(restored);
        Assert.NotNull(restored.HSLCurves);
        Assert.True(restored.HSLCurves.HasAdjustments);
        Assert.Equal(0.65, restored.HSLCurves.HueVsHue.Points[0].Y, 3);
        Assert.Equal(0.35, restored.HSLCurves.SatVsSat.Points[1].Y, 3);
    }
}
