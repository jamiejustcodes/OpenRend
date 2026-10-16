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
}
