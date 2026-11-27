using System.Drawing;
using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class WhiteBalanceSamplerTests
{
    [Fact]
    public void RegionCorrectionRespondsToTheRenderedGradeAndAveragesOnlySelectedPixels()
    {
        byte[] pixels = [15, 245, 20, 255, 100, 140, 150, 255, 132, 132, 154, 255, 250, 5, 230, 255];
        var grade = VideoEffect.Create(VideoEffectKind.ColorGrading);
        grade.SetParameter("Gamma_G", 1.3);
        var current = new WhiteBalanceSampler.Adjustment(180, -8);
        var displayed = Render(current);

        var result = WhiteBalanceSampler.Solve(current, displayed, 4, 1, new Rectangle(1, 0, 2, 1), Render);
        Assert.Equal(WhiteBalanceSampler.SampleStatus.Adjusted, result.Status);
        Assert.NotEqual(current.Temperature, result.Adjustment.Temperature);
        Assert.NotEqual(current.Tint, result.Adjustment.Tint);
        var corrected = Render(result.Adjustment);
        var blue = corrected[4] + corrected[8];
        var green = corrected[5] + corrected[9];
        var red = corrected[6] + corrected[10];
        Assert.InRange(Math.Abs(red - blue), 0, 4);
        Assert.InRange(Math.Abs(red - green), 0, 4);

        byte[] Render(WhiteBalanceSampler.Adjustment adjustment)
        {
            var trial = grade.DeepClone();
            adjustment.ApplyTo(trial);
            return VideoEffectProcessor.Apply((byte[])pixels.Clone(), 4, 1, [trial]);
        }
    }

    [Theory]
    [InlineData(0, 0, 0, WhiteBalanceSampler.SampleStatus.TooDark)]
    [InlineData(150, 150, 150, WhiteBalanceSampler.SampleStatus.AlreadyNeutral)]
    [InlineData(100, 150, 170, WhiteBalanceSampler.SampleStatus.NoImprovement)]
    public void ASelectionWithNoUsableCorrectionKeepsTheExistingValues(int blue, int green, int red, WhiteBalanceSampler.SampleStatus expected)
    {
        byte[] pixels = [(byte)blue, (byte)green, (byte)red, 255];
        var existing = new WhiteBalanceSampler.Adjustment(500, -20);
        var result = WhiteBalanceSampler.Solve(existing, pixels, 1, 1, new Rectangle(0, 0, 1, 1), _ => pixels);
        Assert.Equal(expected, result.Status);
        Assert.Equal(existing, result.Adjustment);
    }
}
