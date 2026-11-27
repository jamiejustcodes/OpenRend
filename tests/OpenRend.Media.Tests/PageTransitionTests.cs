using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class PageTransitionTests
{
    private const int Width = 96, Height = 54;

    [Theory]
    [InlineData(VideoTransitionKind.PageLoop)]
    [InlineData(VideoTransitionKind.PagePeel)]
    [InlineData(VideoTransitionKind.PageRoll)]
    public void AFlatPageFollowsTheAngleAndKeepsExactEndpoints(VideoTransitionKind kind)
    {
        var (project, transition) = MakeProject(kind);
        project.Media[0].Path = "openrend://color?hex=FF0000";
        project.Media[1].Path = "openrend://color?hex=00FF00";
        var parameters = transition.Parameters!;
        parameters["Radius"] = "0";
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);
        Assert.Equal(Render(0), Render(18));
        Assert.Equal(Render(50), Render(42));

        var leftward = Render(30);
        Assert.Equal((255, 0, 0), Pixel(leftward, Width / 4, Height / 2));
        Assert.Equal((0, 255, 0), Pixel(leftward, 3 * Width / 4, Height / 2));
        parameters["Angle"] = "180";
        var rightward = Render(30);
        Assert.Equal((0, 255, 0), Pixel(rightward, Width / 4, Height / 2));
        Assert.Equal((255, 0, 0), Pixel(rightward, 3 * Width / 4, Height / 2));
        parameters["Angle"] = "90";
        var upward = Render(30);
        Assert.Equal((255, 0, 0), Pixel(upward, Width / 2, Height / 4));
        Assert.Equal((0, 255, 0), Pixel(upward, Width / 2, 3 * Height / 4));
        parameters.Remove("Angle");
        parameters["Corner"] = "TopLeft";
        var fromCorner = Render(30);
        Assert.Equal((0, 255, 0), Pixel(fromCorner, Width / 8, Height / 8));
        Assert.Equal((255, 0, 0), Pixel(fromCorner, 7 * Width / 8, 7 * Height / 8));

        byte[] Render(long frame) => renderer.RenderFrame(project, frame, Width, Height);
    }

    [Theory]
    [InlineData(VideoTransitionKind.PageLoop, "Radius", "0")]
    [InlineData(VideoTransitionKind.PageLoop, "LoopPosition", "0.4")]
    [InlineData(VideoTransitionKind.PageLoop, "Opacity", "0")]
    [InlineData(VideoTransitionKind.PageLoop, "Perspective", "1")]
    [InlineData(VideoTransitionKind.PageLoop, "LightColor", "0 0 1 1")]
    [InlineData(VideoTransitionKind.PagePeel, "Radius", "0")]
    [InlineData(VideoTransitionKind.PagePeel, "SlideAmount", "1")]
    [InlineData(VideoTransitionKind.PagePeel, "Opacity", "0")]
    [InlineData(VideoTransitionKind.PagePeel, "Perspective", "1")]
    [InlineData(VideoTransitionKind.PagePeel, "LightColor", "0 0 1 1")]
    [InlineData(VideoTransitionKind.PageRoll, "Radius", "0")]
    [InlineData(VideoTransitionKind.PageRoll, "SlideAmount", "1")]
    [InlineData(VideoTransitionKind.PageRoll, "Opacity", "0")]
    [InlineData(VideoTransitionKind.PageRoll, "Perspective", "1")]
    [InlineData(VideoTransitionKind.PageRoll, "LightColor", "0 0 1 1")]
    public void APageControlChangesTheRenderedFold(VideoTransitionKind kind, string key, string value)
    {
        var (project, transition) = MakeProject(kind);
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);
        var original = renderer.RenderFrame(project, 30, Width, Height);
        transition.Parameters![key] = value;
        var changed = renderer.RenderFrame(project, 30, Width, Height);
        Assert.False(original.AsSpan().SequenceEqual(changed));
    }

    [Fact]
    public void FoldLightHonorsAlphaAndAllThreePageShapesAreDistinct()
    {
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);
        var peel = MakeProject(VideoTransitionKind.PagePeel).Item1;
        var roll = MakeProject(VideoTransitionKind.PageRoll).Item1;
        var loop = MakeProject(VideoTransitionKind.PageLoop).Item1;
        var peeled = renderer.RenderFrame(peel, 30, Width, Height);
        var rolled = renderer.RenderFrame(roll, 30, Width, Height);
        var looped = renderer.RenderFrame(loop, 30, Width, Height);
        Assert.False(peeled.AsSpan().SequenceEqual(rolled));
        Assert.False(rolled.AsSpan().SequenceEqual(looped));
        Assert.False(peeled.AsSpan().SequenceEqual(looped));

        peel.Media[0].Path = "openrend://color?hex=00FF0000";
        peel.Media[1].Path = "openrend://color?hex=0000FF00";
        var background = new MediaAsset { Path = "openrend://color?hex=0000FF", HasVideo = true };
        peel.Media.Add(background);
        peel.Tracks.Add(new() { Kind = TrackKind.Video, Clips = [new() { MediaId = background.Id, DurationFrames = 60 }] });
        Assert.Equal(renderer.RenderFrame(peel, 0, Width, Height), renderer.RenderFrame(peel, 30, Width, Height));
        Assert.Equal((0, 0, 255), Pixel(renderer.RenderFrame(peel, 30, Width, Height), Width / 2, Height / 2));
    }

    [Fact]
    public async Task PageRollPreviewAgreesWithHardwareCompositionAndDecodedExport()
    {
        var (project, _) = MakeProject(VideoTransitionKind.PageRoll);
        project.Media[0].Path = "openrend://generator?kind=Checkerboard&size=7&color1=EEEEEE&color2=242424";
        project.Media[1].Path = "openrend://generator?kind=Checkerboard&size=9&color1=CCCCCC&color2=393939";
        using var media = new MediaService();
        using var cpu = new TimelineRenderService(media, useGpuCompositing: false);
        using var d3d = new TimelineRenderService(media);
        var preview = cpu.RenderFrame(project, 30, Width, Height);
        var hardwarePreview = d3d.RenderFrame(project, 30, Width, Height);
        Assert.True(d3d.IsD3DCompositing, d3d.GpuUnavailableReason);
        for (var i = 0; i < preview.Length; i++) Assert.InRange(Math.Abs(preview[i] - hardwarePreview[i]), 0, 1);

        var folder = Path.Combine(Path.GetTempPath(), $"openrend-page-roll-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "page-roll.mp4");
            await new ExportService(cpu).ExportAsync(project, path);
            using var decoder = new MediaService();
            var encoded = decoder.GetFrame(path, project.FrameRate.Seconds(30), Width, Height);
            var meanError = Enumerable.Range(0, Width * Height).Average(i => Math.Abs(preview[i * 4] - encoded[i * 4]));
            Assert.InRange(meanError, 0, 6);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static (int R, int G, int B) Pixel(byte[] frame, int x, int y)
    {
        var offset = (y * Width + x) * 4;
        return (frame[offset + 2], frame[offset + 1], frame[offset]);
    }

    private static (ProjectDocument, VideoTransition) MakeProject(VideoTransitionKind kind)
    {
        var red = new MediaAsset { Path = "openrend://generator?kind=Checkerboard&size=6&color1=E00000&color2=480000", HasVideo = true };
        var green = new MediaAsset { Path = "openrend://generator?kind=Checkerboard&size=8&color1=00EA00&color2=003500", HasVideo = true };
        var outgoing = new TimelineClip { MediaId = red.Id, DurationFrames = 42, FadeOutFrames = 24 };
        var transition = new VideoTransition { Kind = kind, Parameters = new() { ["Angle"] = "0" } };
        var incoming = new TimelineClip { MediaId = green.Id, StartFrame = 18, DurationFrames = 42, FadeInFrames = 24, FadeInTransition = transition };
        return (new ProjectDocument { Width = Width, Height = Height, FrameRate = new(30000, 1001), Media = [red, green],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [outgoing, incoming] }] }, transition);
    }
}
