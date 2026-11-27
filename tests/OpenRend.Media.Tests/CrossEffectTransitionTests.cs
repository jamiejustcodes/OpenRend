using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class CrossEffectTransitionTests
{
    private const int Width = 64, Height = 36;

    [Theory]
    [InlineData("ZoomA", null, true, false)]
    [InlineData("ZoomB", null, false, true)]
    [InlineData("Zoom", null, true, true)]
    [InlineData("Pixelate", null, true, true)]
    [InlineData("Blur", null, true, true)]
    [InlineData("Blur", "Destination", false, true)]
    [InlineData("ZoomA", "Destination", false, true)]
    public void EffectChangesOnlyTheTargetedImagesAndKeepsTheEndpoints(string effect, string? target, bool changesRed, bool changesGreen)
    {
        var (project, transition) = MakeProject(effect);
        if (target is not null) transition.Parameters!["ApplyTo"] = target;
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);
        var frame = renderer.RenderFrame(project, 30, Width, Height);
        transition.Parameters!["EffectType"] = "None";
        var plain = renderer.RenderFrame(project, 30, Width, Height);
        Assert.Equal(!changesRed, SameChannel(plain, frame, 2));
        Assert.Equal(!changesGreen, SameChannel(plain, frame, 1));
        transition.Parameters["EffectType"] = effect;
        Assert.Equal(renderer.RenderFrame(project, 0, Width, Height), renderer.RenderFrame(project, 18, Width, Height));
        Assert.Equal(renderer.RenderFrame(project, 50, Width, Height), renderer.RenderFrame(project, 42, Width, Height));
    }

    [Theory]
    [InlineData("Zoom")]
    [InlineData("Blur")]
    public async Task RenderedPatternAgreesWithExport(string effect)
    {
        var (project, _) = MakeProject(effect);
        project.Media[0].Path = "openrend://generator?kind=Checkerboard&size=4&color1=FFFFFF&color2=000000";
        project.Media[1].Path = "openrend://generator?kind=Checkerboard&size=6&color1=FFFFFF&color2=000000";
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);
        var preview = renderer.RenderFrame(project, 30, Width, Height);
        var folder = Path.Combine(Path.GetTempPath(), $"openrend-cross-effect-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "pattern.mp4");
            await new ExportService(renderer).ExportAsync(project, path);
            using var decoded = new MediaService();
            var encoded = decoded.GetFrame(path, project.FrameRate.Seconds(30), Width, Height);
            var meanError = Enumerable.Range(0, Width * Height)
                .Average(i => Math.Abs(preview[i * 4] - encoded[i * 4]));
            Assert.InRange(meanError, 0, 6);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task PixelationUsesRectangularBlocksInPreviewAndEncodedOutput()
    {
        var (project, transition) = MakeProject("Pixelate");
        transition.Parameters!["MaxX"] = "0.25";
        transition.Parameters["MaxY"] = "0.5";
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);
        using var accelerated = new TimelineRenderService(media);
        var preview = renderer.RenderFrame(project, 30, Width, Height);
        Assert.Equal(Pixel(preview, 2, 2), Pixel(preview, 12, 14));
        Assert.NotEqual(Pixel(preview, 2, 2), Pixel(preview, 2, 22));
        var gpu = accelerated.RenderFrame(project, 30, Width, Height);
        Assert.True(accelerated.IsD3DCompositing, accelerated.GpuUnavailableReason);
        for (var i = 0; i < preview.Length; i++) Assert.InRange(Math.Abs(preview[i] - gpu[i]), 0, 1);

        var folder = Path.Combine(Path.GetTempPath(), $"openrend-cross-effect-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "cross-effect.mp4");
            await new ExportService(renderer).ExportAsync(project, path);
            using var decoded = new MediaService();
            var encoded = decoded.GetFrame(path, project.FrameRate.Seconds(30), Width, Height);
            foreach (var (x, y) in new[] { (8, 8), (24, 8), (8, 27), (24, 27) })
            {
                var expected = Pixel(preview, x, y);
                var actual = Pixel(encoded, x, y);
                Assert.InRange(Math.Abs(expected.R - actual.R), 0, 10);
                Assert.InRange(Math.Abs(expected.G - actual.G), 0, 10);
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void FadeRangeAndBlurHonorCoverage()
    {
        var (project, transition) = MakeProject("Blur");
        project.Media[0].Path = "openrend://generator?kind=Checkerboard&size=4&color1=FFFF0000&color2=0000FF00";
        project.Media[1].Path = "openrend://color?hex=00000000";
        transition.Parameters!["FadeRange"] = "0.2";
        transition.Parameters["MaxBlur"] = "1";
        var blue = new MediaAsset { Path = "openrend://color?hex=0000FF", HasVideo = true };
        project.Media.Add(blue);
        project.Tracks.Add(new TimelineTrack { Kind = TrackKind.Video, Clips = [new() { MediaId = blue.Id, DurationFrames = 60 }] });
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);
        var beforeFade = Pixel(renderer.RenderFrame(project, 26, Width, Height), 25, 19);
        var duringFade = Pixel(renderer.RenderFrame(project, 30, Width, Height), 25, 19);
        var afterFade = Pixel(renderer.RenderFrame(project, 34, Width, Height), 25, 19);
        Assert.True(beforeFade.R > duringFade.R && duringFade.R > 0);
        Assert.Equal(0, duringFade.G);
        Assert.Equal((0, 0, 255), afterFade);
    }

    private static bool SameChannel(byte[] first, byte[] second, int channel) =>
        Enumerable.Range(0, Width * Height).All(i => first[i * 4 + channel] == second[i * 4 + channel]);

    private static (int R, int G, int B) Pixel(byte[] frame, int x, int y)
    {
        var offset = (y * Width + x) * 4;
        return (frame[offset + 2], frame[offset + 1], frame[offset]);
    }

    private static (ProjectDocument, VideoTransition) MakeProject(string effect)
    {
        var red = new MediaAsset { Path = "openrend://generator?kind=Checkerboard&size=4&color1=FF0000&color2=000000", HasVideo = true };
        var green = new MediaAsset { Path = "openrend://generator?kind=Checkerboard&size=6&color1=00FF00&color2=000000", HasVideo = true };
        var outgoing = new TimelineClip { MediaId = red.Id, DurationFrames = 42, FadeOutFrames = 24 };
        var transition = new VideoTransition { Kind = VideoTransitionKind.CrossEffect, Parameters = new() { ["EffectType"] = effect } };
        var incoming = new TimelineClip { MediaId = green.Id, StartFrame = 18, DurationFrames = 42, FadeInFrames = 24, FadeInTransition = transition };
        return (new ProjectDocument { Width = Width, Height = Height, FrameRate = new(30000, 1001), Media = [red, green],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [outgoing, incoming] }] }, transition);
    }
}
