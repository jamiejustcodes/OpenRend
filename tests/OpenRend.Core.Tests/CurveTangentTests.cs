using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class CurveTangentTests
{
    [Fact]
    public void LegacyHslPointsRetainSmoothstepAndManualHueHandlesWrapAtTheSeam()
    {
        var legacy = new HSLCurve { Points = [new(0, .25), new(1, .75)] };
        Assert.Equal(.328125, legacy.Evaluate(.25, isHuePeriodic: false), 12);

        var hue = new HSLCurve { Points = [new(.1, .5), new(.65, .5), new(.85, .5)] };
        Assert.True(hue.IsIdentity);
        hue.Points[0].InTangent = new(-.08, .3);
        hue.Validate();
        Assert.False(hue.IsIdentity);
        Assert.Equal(.5, hue.Evaluate(.4), 12);
        Assert.True(hue.Evaluate(0) > .55);
        Assert.Equal(hue.Evaluate(0), hue.Evaluate(1), 12);
        Assert.Equal(.5, hue.Evaluate(.1), 12);
        hue.Points[0].InTangent = new(.08, .3);
        Assert.Throws<InvalidDataException>(hue.Validate);
    }

    [Fact]
    public async Task LinkedStateAndOptionalTangentOffsetsRoundTripTogether()
    {
        var asset = new MediaAsset { Path = "source.mp4", HasVideo = true, DurationSeconds = 2, FrameRate = 30 };
        var grade = VideoEffect.Create(VideoEffectKind.ColorGrading);
        grade.Curves = new();
        grade.Curves.Master.Points.Insert(1, new(.4, .5) { TangentsUnlinked = true, OutHandleY = .2 });
        grade.HSLCurves = new();
        var huePoint = grade.HSLCurves.HueVsLum.Points[0];
        huePoint.InTangent = new(-.05, .2);
        huePoint.TangentsUnlinked = true;
        var project = new ProjectDocument
        {
            Media = [asset], VideoOutputEffects = [grade],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [new() { MediaId = asset.Id, DurationFrames = 30 }] }]
        };
        var directory = Path.Combine(Path.GetTempPath(), "OpenRend-Tangents-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "tangents.openrend");
        try
        {
            await ProjectStore.SaveAsync(project, path);
            var loaded = Assert.Single((await ProjectStore.LoadAsync(path)).VideoOutputEffects);
            Assert.True(grade.ContentEquals(loaded));
            Assert.True(loaded.Curves!.Master.Points[1].TangentsUnlinked);
            Assert.True(loaded.HSLCurves!.HueVsLum.Points[0].TangentsUnlinked);
            Assert.Equal(new CurveTangent(-.05, .2), loaded.HSLCurves.HueVsLum.Points[0].InTangent);
            Assert.Null(loaded.HSLCurves.HueVsLum.Points[0].OutTangent);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
