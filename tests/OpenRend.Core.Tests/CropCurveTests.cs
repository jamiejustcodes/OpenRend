using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class CropCurveTests
{
    private static CropAnimation Curve(CropInterpolation mode) => new()
    {
        Keyframes = [new() { Frame = 0, State = CropState.Identity(320, 180), Interpolation = mode },
            new() { Frame = 60, State = CropState.Identity(320, 180) with { Width = 160, Height = 90 } }]
    };

    [Theory]
    [InlineData(CropInterpolation.Slow, 15, 310)]
    [InlineData(CropInterpolation.Slow, 30, 280)]
    [InlineData(CropInterpolation.Slow, 45, 230)]
    [InlineData(CropInterpolation.Fast, 15, 250)]
    [InlineData(CropInterpolation.Fast, 30, 200)]
    [InlineData(CropInterpolation.Fast, 45, 170)]
    [InlineData(CropInterpolation.Smooth, 15, 295)]
    [InlineData(CropInterpolation.Smooth, 30, 240)]
    [InlineData(CropInterpolation.Smooth, 45, 185)]
    [InlineData(CropInterpolation.Sharp, 15, 250)]
    [InlineData(CropInterpolation.Sharp, 30, 240)]
    [InlineData(CropInterpolation.Sharp, 45, 230)]
    public void TemporalCurvesReachKnownQuarterPoints(CropInterpolation mode, long frame, double width)
    {
        var crop = Curve(mode).Evaluate(frame);
        Assert.Equal(width, crop.Width, 10);
        Assert.Equal(width * 9 / 16, crop.Height, 10);
    }

    [Theory]
    [InlineData(CropInterpolation.Linear)]
    [InlineData(CropInterpolation.Hold)]
    [InlineData(CropInterpolation.Slow)]
    [InlineData(CropInterpolation.Fast)]
    [InlineData(CropInterpolation.Smooth)]
    [InlineData(CropInterpolation.Sharp)]
    public void CurveEndpointsClampAndZoomStaysMonotonic(CropInterpolation mode)
    {
        var animation = Curve(mode); animation.Validate();
        Assert.Equal(320, animation.Evaluate(-1).Width); Assert.Equal(320, animation.Evaluate(0).Width);
        var previous = 320.0;
        for (var frame = 1; frame <= 60; frame++)
        {
            var current = animation.Evaluate(frame).Width;
            Assert.InRange(current, 160, previous); previous = current;
        }
        Assert.Equal(160, animation.Evaluate(60).Width); Assert.Equal(160, animation.Evaluate(61).Width);
    }

    [Fact]
    public void LeftCurveControlsTheFollowingSegmentAcrossEveryGeometryComponent()
    {
        var animation = new CropAnimation { Keyframes = [
            new() { Frame = 0, State = new(320, 180, 10, 20, 0, 160, 90), Interpolation = CropInterpolation.Fast },
            new() { Frame = 60, State = new(160, 90, 50, 60, 90, 200, 130), Interpolation = CropInterpolation.Slow }
        ] };
        Assert.Equal(new CropState(250, 140.625, 27.5, 37.5, 39.375, 177.5, 107.5), animation.Evaluate(15));
        animation.Keyframes[1].Interpolation = CropInterpolation.Hold;
        Assert.Equal(new CropState(250, 140.625, 27.5, 37.5, 39.375, 177.5, 107.5), animation.Evaluate(15));
    }

    [Fact]
    public void SmoothnessFactorInterpolatesBetweenLinearAndCubicHermite()
    {
        // 0 to 60 frames, width 320 to 160 (delta = -160)
        // At frame 15: progress = 0.25
        // Linear width: 320 + (-160) * 0.25 = 280
        // Full Smooth (100%): 320 + (-160) * (0.0625 * 2.5) = 295
        var anim100 = new CropAnimation
        {
            Keyframes =
            [
                new() { Frame = 0, State = CropState.Identity(320, 180), Interpolation = CropInterpolation.Smooth, Smoothness = 100.0 },
                new() { Frame = 60, State = CropState.Identity(320, 180) with { Width = 160, Height = 90 } }
            ]
        };
        anim100.Validate();
        Assert.Equal(295, anim100.Evaluate(15).Width, 6);

        // 0% Smoothness -> behaves like linear
        var anim0 = new CropAnimation
        {
            Keyframes =
            [
                new() { Frame = 0, State = CropState.Identity(320, 180), Interpolation = CropInterpolation.Smooth, Smoothness = 0.0 },
                new() { Frame = 60, State = CropState.Identity(320, 180) with { Width = 160, Height = 90 } }
            ]
        };
        anim0.Validate();
        Assert.Equal(280, anim0.Evaluate(15).Width, 6);

        // 50% Smoothness -> exact midpoint between linear (280) and smooth (295) = 287.5
        var anim50 = new CropAnimation
        {
            Keyframes =
            [
                new() { Frame = 0, State = CropState.Identity(320, 180), Interpolation = CropInterpolation.Smooth, Smoothness = 50.0 },
                new() { Frame = 60, State = CropState.Identity(320, 180) with { Width = 160, Height = 90 } }
            ]
        };
        anim50.Validate();
        Assert.Equal(287.5, anim50.Evaluate(15).Width, 6);
    }

    [Fact]
    public void CropKeyframeSmoothnessValidatesBounds()
    {
        var anim = new CropAnimation
        {
            Keyframes =
            [
                new() { Frame = 0, State = CropState.Identity(320, 180), Interpolation = CropInterpolation.Smooth, Smoothness = 100.0 },
                new() { Frame = 60, State = CropState.Identity(320, 180) with { Width = 160, Height = 90 } }
            ]
        };
        anim.Validate();

        anim.Keyframes[0].Smoothness = -5;
        Assert.Throws<System.IO.InvalidDataException>(() => anim.Validate());

        anim.Keyframes[0].Smoothness = 105;
        Assert.Throws<System.IO.InvalidDataException>(() => anim.Validate());

        anim.Keyframes[0].Smoothness = double.NaN;
        Assert.Throws<System.IO.InvalidDataException>(() => anim.Validate());
    }
}
