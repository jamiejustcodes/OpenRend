using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class SqueezeTransitionTests
{
    private const int Width = 64, Height = 36;

    [Theory]
    [InlineData(0, 32, 30, 32, 5)]
    [InlineData(1, 32, 5, 32, 30)]
    [InlineData(2, 53, 18, 10, 18)]
    [InlineData(3, 10, 18, 53, 18)]
    [InlineData(4, 32, 18, 10, 18)]
    [InlineData(5, 32, 18, 32, 5)]
    public void OriginsPlaceTheirTwoImagesInDifferentRegions(int direction, int inX, int inY, int outX, int outY)
    {
        var (project, transition) = Project("openrend://color?hex=#C80000", "openrend://color?hex=#00C800");
        transition.Parameters!["Direction"] = direction.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);
        var frame = renderer.RenderFrame(project, 30, Width, Height);
        Assert.Equal((0, 200, 0), Pixel(frame, inX, inY));
        Assert.Equal((200, 0, 0), Pixel(frame, outX, outY));
        Assert.Equal(renderer.RenderFrame(project, 0, Width, Height), renderer.RenderFrame(project, 18, Width, Height));
        Assert.Equal(renderer.RenderFrame(project, 50, Width, Height), renderer.RenderFrame(project, 42, Width, Height));
    }

    [Fact]
    public void SqueezePreservesCoverageOverLowerTracks()
    {
        var (project, _) = Project("openrend://color?hex=#C80000", "openrend://color?hex=0000C800");
        var blue = new MediaAsset { Path = "openrend://color?hex=#0000C8", HasVideo = true };
        project.Media.Add(blue);
        project.Tracks.Add(new TimelineTrack { Kind = TrackKind.Video, Clips = [new() { MediaId = blue.Id, DurationFrames = 60 }] });
        project.Tracks[0].Clips[0].Opacity = .5;
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);
        var frame = renderer.RenderFrame(project, 30, Width, Height);
        Assert.Equal((0, 0, 200), Pixel(frame, 15, 18));
        var blend = Pixel(frame, 47, 18);
        Assert.InRange(blend.R, 99, 101);
        Assert.InRange(blend.B, 99, 101);
    }

    [Fact]
    public async Task SqueezesPlanarImagesRatherThanCroppingAndSharesThatFrameWithExport()
    {
        var (project, transition) = Project("openrend://generator?kind=ColorGradient&color1=000000&color2=FF0000",
            "openrend://generator?kind=ColorGradient&color1=000000&color2=00FF00");
        using var media = new MediaService();
        using var cpu = new TimelineRenderService(media, useGpuCompositing: false);
        using var gpu = new TimelineRenderService(media);
        var preview = cpu.RenderFrame(project, 30, Width, Height);
        Assert.InRange(Pixel(preview, 15, 18).G, 120, 130);
        Assert.InRange(Pixel(preview, 47, 18).R, 120, 130);
        var gpuFrame = gpu.RenderFrame(project, 30, Width, Height);
        for (var index = 0; index < preview.Length; index++) Assert.InRange(Math.Abs(preview[index] - gpuFrame[index]), 0, 1);

        var folder = Path.Combine(Path.GetTempPath(), $"openrend-squeeze-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "squeeze.mp4");
            await new ExportService(cpu).ExportAsync(project, path);
            using var decoded = new MediaService();
            var encoded = decoded.GetFrame(path, project.FrameRate.Seconds(30), Width, Height);
            Assert.InRange(Math.Abs(Pixel(preview, 15, 18).G - Pixel(encoded, 15, 18).G), 0, 8);
            Assert.InRange(Math.Abs(Pixel(preview, 47, 18).R - Pixel(encoded, 47, 18).R), 0, 8);
        }
        finally { Directory.Delete(folder, recursive: true); }

        transition.Parameters!["SqueezePreviousImage"] = "false";
        var unsqueezed = cpu.RenderFrame(project, 30, Width, Height);
        Assert.InRange(Pixel(unsqueezed, 15, 18).G, 151, 163);
        Assert.InRange(Pixel(unsqueezed, 47, 18).R, 151, 163);
        transition.Parameters["BorderSize"] = "0.15";
        transition.Parameters["BorderFeather"] = "0.5";
        transition.Parameters["BorderColor"] = "1 1 1";
        var border = cpu.RenderFrame(project, 30, Width, Height);
        Assert.Equal((255, 255, 255), Pixel(border, 31, 18));
        Assert.Equal(Pixel(unsqueezed, 15, 18), Pixel(border, 15, 18));
    }

    private static (int R, int G, int B) Pixel(byte[] frame, int x, int y)
    {
        var offset = (y * Width + x) * 4;
        return (frame[offset + 2], frame[offset + 1], frame[offset]);
    }

    private static (ProjectDocument, VideoTransition) Project(string outgoingSource, string incomingSource)
    {
        var first = new MediaAsset { Path = outgoingSource, HasVideo = true };
        var second = new MediaAsset { Path = incomingSource, HasVideo = true };
        var transition = new VideoTransition { Kind = VideoTransitionKind.Squeeze, Preset = "Squeeze Left-Right", Parameters = new() };
        var outgoing = new TimelineClip { MediaId = first.Id, DurationFrames = 42, FadeOutFrames = 24 };
        var incoming = new TimelineClip { MediaId = second.Id, StartFrame = 18, DurationFrames = 42, FadeInFrames = 24, FadeInTransition = transition };
        return (new ProjectDocument { Width = Width, Height = Height, FrameRate = new(30000, 1001), Media = [first, second],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [outgoing, incoming] }] }, transition);
    }
}
