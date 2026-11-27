using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class SwapTransitionTests
{
    private const int Width = 64, Height = 36;

    [Theory]
    [InlineData("Swap Up", 32, 7, 32, 28)]
    [InlineData("Swap Down", 32, 28, 32, 7)]
    [InlineData("Swap Left", 12, 18, 51, 18)]
    [InlineData("Swap Right", 51, 18, 12, 18)]
    public void SwapMovesBothPlanesAndBringsTheSecondForward(string preset, int firstX, int firstY, int secondX, int secondY)
    {
        var project = Project(preset);
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);
        var center = renderer.RenderFrame(project, 30, Width, Height);
        Assert.Equal((200, 0, 0), Pixel(center, firstX, firstY));
        Assert.Equal((0, 200, 0), Pixel(center, secondX, secondY));
        Assert.Equal((200, 0, 0), Pixel(renderer.RenderFrame(project, 24, Width, Height), 32, 18));
        Assert.Equal((0, 200, 0), Pixel(renderer.RenderFrame(project, 36, Width, Height), 32, 18));
        Assert.Equal(renderer.RenderFrame(project, 0, Width, Height), renderer.RenderFrame(project, 18, Width, Height));
        Assert.Equal(renderer.RenderFrame(project, 50, Width, Height), renderer.RenderFrame(project, 42, Width, Height));
    }

    [Fact]
    public async Task SwapPlanesRevealLowerTracksAndTheirBorderMatchesEncodedOutput()
    {
        var project = Project("Swap Left, White Border");
        var blue = new MediaAsset { Path = "openrend://color?hex=#0000C8", HasVideo = true };
        project.Media.Add(blue);
        project.Tracks.Add(new TimelineTrack { Kind = TrackKind.Video, Clips = [new() { MediaId = blue.Id, DurationFrames = 60 }] });
        using var media = new MediaService();
        using var cpu = new TimelineRenderService(media, useGpuCompositing: false);
        using var gpu = new TimelineRenderService(media);
        var frame = cpu.RenderFrame(project, 30, Width, Height);
        Assert.Equal((0, 0, 200), Pixel(frame, 32, 18));
        Assert.Equal((255, 255, 255), Pixel(frame, 30, 18));
        var accelerated = gpu.RenderFrame(project, 30, Width, Height);
        Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
        for (var index = 0; index < frame.Length; index++) Assert.InRange(Math.Abs(frame[index] - accelerated[index]), 0, 1);

        var folder = Path.Combine(Path.GetTempPath(), $"openrend-swap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "swap.mp4");
            await new ExportService(cpu).ExportAsync(project, path);
            using var decoder = new MediaService();
            var encoded = decoder.GetFrame(path, project.FrameRate.Seconds(30), Width, Height);
            Assert.InRange(Pixel(encoded, 12, 18).R, 194, 206);
            Assert.InRange(Pixel(encoded, 51, 18).G, 194, 206);
            Assert.InRange(Pixel(encoded, 30, 18).R, 225, 255);
        }
        finally { Directory.Delete(folder, recursive: true); }

        project.Tracks[0].Clips[1].FadeInTransition!.Parameters = new() { ["BorderFeather"] = "1" };
        var softened = cpu.RenderFrame(project, 30, Width, Height);
        Assert.InRange(Pixel(softened, 30, 18).B, 30, 240);
    }

    private static (int R, int G, int B) Pixel(byte[] frame, int x, int y)
    {
        var offset = (y * Width + x) * 4;
        return (frame[offset + 2], frame[offset + 1], frame[offset]);
    }

    private static ProjectDocument Project(string preset)
    {
        var red = new MediaAsset { Path = "openrend://color?hex=#C80000", HasVideo = true };
        var green = new MediaAsset { Path = "openrend://color?hex=#00C800", HasVideo = true };
        var outgoing = new TimelineClip { MediaId = red.Id, DurationFrames = 42, FadeOutFrames = 24 };
        var incoming = new TimelineClip { MediaId = green.Id, StartFrame = 18, DurationFrames = 42, FadeInFrames = 24,
            FadeInTransition = new() { Kind = VideoTransitionKind.Swap, Preset = preset } };
        return new ProjectDocument { Width = Width, Height = Height, FrameRate = new(30000, 1001), Media = [red, green],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [outgoing, incoming] }] };
    }
}
