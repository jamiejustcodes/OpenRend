using System.Text.Json.Nodes;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class ColorCurveTests
{
    [Fact]
    public void IdentityCurveEvaluatesLinearlyAndBuildsIdentityLut()
    {
        var curve = ColorCurve.Identity();
        Assert.True(curve.IsIdentity);
        Assert.Equal(2, curve.Points.Count);

        Assert.Equal(0.0, curve.Evaluate(0.0), 3);
        Assert.Equal(0.25, curve.Evaluate(0.25), 3);
        Assert.Equal(0.5, curve.Evaluate(0.5), 3);
        Assert.Equal(0.75, curve.Evaluate(0.75), 3);
        Assert.Equal(1.0, curve.Evaluate(1.0), 3);

        // Clamping
        Assert.Equal(0.0, curve.Evaluate(-0.5), 3);
        Assert.Equal(1.0, curve.Evaluate(1.5), 3);

        var lut = curve.BuildLut();
        Assert.Equal(256, lut.Length);
        for (int i = 0; i < 256; i++)
        {
            Assert.Equal(i, lut[i]);
        }
    }

    [Fact]
    public void InvertedCurveEvaluatesCorrectlyAndBuildsInvertedLut()
    {
        var curve = new ColorCurve
        {
            Points =
            [
                new() { X = 0, Y = 1, OutHandleX = 1.0 / 3.0, OutHandleY = -1.0 / 3.0 },
                new() { X = 1, Y = 0, InHandleX = -1.0 / 3.0, InHandleY = 1.0 / 3.0 }
            ]
        };

        Assert.False(curve.IsIdentity);
        Assert.Equal(1.0, curve.Evaluate(0.0), 2);
        Assert.Equal(0.5, curve.Evaluate(0.5), 2);
        Assert.Equal(0.0, curve.Evaluate(1.0), 2);

        var lut = curve.BuildLut();
        Assert.Equal(255, lut[0]);
        Assert.Equal(0, lut[255]);
    }

    [Fact]
    public void VegasCurveStringRoundtripsAccurately()
    {
        var curve = new ColorCurve
        {
            Points =
            [
                new() { X = 0.0, Y = 0.05, InHandleX = 0, InHandleY = 0, OutHandleX = 0.1, OutHandleY = 0.2 },
                new() { X = 0.5, Y = 0.6, InHandleX = -0.15, InHandleY = -0.1, OutHandleX = 0.15, OutHandleY = 0.1 },
                new() { X = 1.0, Y = 0.95, InHandleX = -0.1, InHandleY = -0.2, OutHandleX = 0, OutHandleY = 0 }
            ]
        };

        var str = curve.ToCurveString();
        Assert.StartsWith("3:", str);

        var parsed = ColorCurve.FromCurveString(str);
        Assert.Equal(3, parsed.Points.Count);
        Assert.Equal(0.0, parsed.Points[0].X, 4);
        Assert.Equal(0.05, parsed.Points[0].Y, 4);
        Assert.Equal(0.5, parsed.Points[1].X, 4);
        Assert.Equal(0.6, parsed.Points[1].Y, 4);
        Assert.Equal(1.0, parsed.Points[2].X, 4);
        Assert.Equal(0.95, parsed.Points[2].Y, 4);
    }

    [Fact]
    public void CurvesRejectedOnNonColorGradingEffects()
    {
        var effect = VideoEffect.Create(VideoEffectKind.BrightnessContrast);
        effect.Curves = new ColorCurvesData();
        Assert.Throws<InvalidDataException>(effect.Validate);
    }

    [Fact]
    public void ColorGradingDeepClonesCurvesIndependently()
    {
        var effect = VideoEffect.Create(VideoEffectKind.ColorGrading);
        var curves = new ColorCurvesData();
        curves.Master.Points.Add(new CurvePoint { X = 0.5, Y = 0.8 });
        curves.Master.SortPoints();
        effect.Curves = curves;

        var clone = effect.DeepClone();
        Assert.NotNull(clone.Curves);
        Assert.NotSame(effect.Curves, clone.Curves);
        Assert.NotSame(effect.Curves.Master, clone.Curves.Master);
        Assert.Equal(3, clone.Curves.Master.Points.Count);

        clone.Curves.Master.Points[1].Y = 0.2;
        Assert.Equal(0.8, effect.Curves.Master.Points[1].Y);
        Assert.Equal(0.2, clone.Curves.Master.Points[1].Y);
    }

    [Fact]
    public async Task ColorGradingCurvesRoundTripThroughProjectSerializationUnderSchema10()
    {
        var asset = new MediaAsset { Path = "test.mkv", HasVideo = true, DurationSeconds = 5 };
        var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 150 };
        var project = new ProjectDocument
        {
            Media = [asset],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
        };

        var effect = VideoEffect.Create(VideoEffectKind.ColorGrading);
        var curves = new ColorCurvesData();
        curves.Red.Points.Add(new CurvePoint { X = 0.5, Y = 0.75 });
        curves.Red.SortPoints();
        curves.Blue.Points[0].Y = 0.1;
        effect.Curves = curves;
        clip.VideoEffects.Add(effect);

        var path = Path.Combine(Path.GetTempPath(), "OpenRend-curves-test-" + Guid.NewGuid().ToString("N") + ".openrend");
        try
        {
            await ProjectStore.SaveAsync(project, path);
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            Assert.Equal(ProjectStore.CurrentVersion, (int)manifest["Version"]!);

            var loaded = await ProjectStore.LoadAsync(path);
            var loadedFx = loaded.Tracks[0].Clips[0].VideoEffects[0];
            Assert.Equal(VideoEffectKind.ColorGrading, loadedFx.Kind);
            Assert.NotNull(loadedFx.Curves);
            Assert.Equal(3, loadedFx.Curves.Red.Points.Count);
            Assert.Equal(0.5, loadedFx.Curves.Red.Points[1].X);
            Assert.Equal(0.75, loadedFx.Curves.Red.Points[1].Y);
            Assert.Equal(0.1, loadedFx.Curves.Blue.Points[0].Y);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
