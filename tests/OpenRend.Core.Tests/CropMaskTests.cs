using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public class CropMaskTests
{
    [Fact]
    public void MaskPoint_DefaultAndCustomConstructors_SetProperties()
    {
        var pt = new MaskPoint(100, 200, -20, -10, 20, 10, true);
        Assert.Equal(100, pt.X);
        Assert.Equal(200, pt.Y);
        Assert.Equal(-20, pt.InTangentX);
        Assert.Equal(-10, pt.InTangentY);
        Assert.Equal(20, pt.OutTangentX);
        Assert.Equal(10, pt.OutTangentY);
        Assert.True(pt.IsSplit);

        var clone = pt.DeepClone();
        Assert.Equal(pt.X, clone.X);
        Assert.Equal(pt.Y, clone.Y);
        Assert.Equal(pt.InTangentX, clone.InTangentX);
        Assert.Equal(pt.OutTangentX, clone.OutTangentX);
        Assert.Equal(pt.IsSplit, clone.IsSplit);
    }

    [Fact]
    public void MaskPoint_Lerp_InterpolatesCoordinatesAndTangents()
    {
        var p0 = new MaskPoint(0, 0, -10, 0, 10, 0, false);
        var p1 = new MaskPoint(100, 200, -20, -30, 20, 30, true);

        var mid = MaskPoint.Lerp(p0, p1, 0.5);
        Assert.Equal(50, mid.X);
        Assert.Equal(100, mid.Y);
        Assert.Equal(-15, mid.InTangentX);
        Assert.Equal(-15, mid.InTangentY);
        Assert.Equal(15, mid.OutTangentX);
        Assert.Equal(15, mid.OutTangentY);
        Assert.True(mid.IsSplit);
    }

    [Fact]
    public void MaskShape_CreateRectangle_ConstructsValidClosed4PointPath()
    {
        var rect = MaskShape.CreateRectangle(50, 60, 200, 100);
        Assert.Single(rect.Paths);
        var path = rect.Paths[0];
        Assert.True(path.IsClosed);
        Assert.Equal(4, path.Points.Count);
        Assert.Equal(50, path.Points[0].X);
        Assert.Equal(60, path.Points[0].Y);
        Assert.Equal(250, path.Points[2].X);
        Assert.Equal(160, path.Points[2].Y);

        rect.Validate();
    }

    [Fact]
    public void MaskShape_CreateOval_ConstructsSmoothBezierEllipse()
    {
        var oval = MaskShape.CreateOval(300, 200, 100, 50);
        Assert.Single(oval.Paths);
        var path = oval.Paths[0];
        Assert.True(path.IsClosed);
        Assert.Equal(4, path.Points.Count);

        // Top point (300, 150)
        Assert.Equal(300, path.Points[0].X);
        Assert.Equal(150, path.Points[0].Y);
        Assert.True(path.Points[0].InTangentX < 0);
        Assert.True(path.Points[0].OutTangentX > 0);

        oval.Validate();
    }

    [Fact]
    public void MaskAnimation_KeyframeInterpolation_EvaluatesBetweenKeyframes()
    {
        var anim = new MaskAnimation { IsEnabled = true };
        var shape0 = MaskShape.CreateRectangle(0, 0, 100, 100);
        var shape1 = MaskShape.CreateRectangle(100, 100, 200, 200);

        anim.Keyframes.Add(new MaskKeyframe { Frame = 0, Shape = shape0, Interpolation = CropInterpolation.Linear });
        anim.Keyframes.Add(new MaskKeyframe { Frame = 10, Shape = shape1, Interpolation = CropInterpolation.Linear });

        anim.Validate();

        var midShape = anim.Evaluate(5);
        Assert.Single(midShape.Paths);
        var pt0 = midShape.Paths[0].Points[0];
        Assert.Equal(50, pt0.X);
        Assert.Equal(50, pt0.Y);

        var pt2 = midShape.Paths[0].Points[2];
        Assert.Equal(200, pt2.X);
        Assert.Equal(200, pt2.Y);

        // Clamp boundaries
        var beforeShape = anim.Evaluate(-10);
        Assert.Equal(0, beforeShape.Paths[0].Points[0].X);
        var afterShape = anim.Evaluate(20);
        Assert.Equal(100, afterShape.Paths[0].Points[0].X);
    }

    [Fact]
    public void MaskPath_FeatherAndOpacity_InterpolateSmoothly()
    {
        var p0 = new MaskPath { Feather = 10, Opacity = 50, FeatherType = MaskFeatherType.Both };
        p0.Points.Add(new MaskPoint(0, 0));
        p0.Points.Add(new MaskPoint(100, 0));

        var p1 = new MaskPath { Feather = 30, Opacity = 90, FeatherType = MaskFeatherType.Both };
        p1.Points.Add(new MaskPoint(0, 100));
        p1.Points.Add(new MaskPoint(100, 100));

        var lerped = MaskPath.Lerp(p0, p1, 0.5);
        Assert.Equal(20, lerped.Feather);
        Assert.Equal(70, lerped.Opacity);
        Assert.Equal(50, lerped.Points[0].Y);
    }
}
