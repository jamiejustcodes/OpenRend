using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class EdgeTransitionTests
{
    private const int Width = 64, Height = 36;

    [Fact]
    public async Task BothEdgesRevealTheLowerTrackAndSurviveSplitsAndExport()
    {
        var (project, clip) = MakeProject();
        var editor = new TimelineEditor(project);
        editor.ApplyEdgeTransition(clip.Id, false, Wipe(), 12);
        editor.ApplyEdgeTransition(clip.Id, true, Wipe(), 12);
        using var media = new MediaService();
        using var cpu = new TimelineRenderService(media, useGpuCompositing: false);
        using var d3d = new TimelineRenderService(media);
        var frames = Enumerable.Range(0, 45).Select(frame => cpu.RenderFrame(project, frame, Width, Height)).ToArray();
        var blended = Pixel(frames[20], 8);
        Assert.InRange(blended.R, 75, 77);
        Assert.InRange(blended.B, 178, 180);
        Assert.Equal((0, 0, 255), Pixel(frames[4], 8));
        Assert.Equal(blended, Pixel(frames[10], 8));
        Assert.Equal((0, 0, 255), Pixel(frames[10], 56));
        Assert.Equal((0, 0, 255), Pixel(frames[34], 8));
        Assert.Equal(blended, Pixel(frames[34], 56));
        Assert.Equal((0, 0, 255), Pixel(frames[40], 56));

        var right = editor.SplitClip(clip.Id, 10);
        editor.SplitClip(right.Id, 34);
        for (var frame = 0; frame < frames.Length; frame++)
            Assert.Equal(frames[frame], cpu.RenderFrame(project, frame, Width, Height));
        foreach (var frame in new[] { 10, 34 })
        {
            var gpu = d3d.RenderFrame(project, frame, Width, Height);
            Assert.True(d3d.IsD3DCompositing, d3d.GpuUnavailableReason);
            for (var i = 0; i < gpu.Length; i++) Assert.InRange(Math.Abs(gpu[i] - frames[frame][i]), 0, 1);
        }

        var folder = Path.Combine(Path.GetTempPath(), $"openrend-edge-transitions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "edge.mp4");
            await new ExportService(cpu).ExportAsync(project, path);
            using var decoder = new MediaService();
            foreach (var frame in new[] { 10, 34 })
            {
                var encoded = decoder.GetFrame(path, project.FrameRate.Seconds(frame), Width, Height);
                foreach (var x in new[] { 8, 56 })
                {
                    var expected = Pixel(frames[frame], x);
                    var actual = Pixel(encoded, x);
                    Assert.InRange(Math.Abs(expected.R - actual.R), 0, 10);
                    Assert.InRange(Math.Abs(expected.B - actual.B), 0, 10);
                }
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void HeadProgressAndAdditiveFadeWorkWithoutAnOverlappingEvent()
    {
        var (project, clip) = MakeProject();
        var editor = new TimelineEditor(project);
        editor.ApplyEdgeTransition(clip.Id, false, Wipe(), 12);
        Assert.True(editor.CanEditTransitionProgress(clip.Id));
        editor.SetTransitionProgress(clip.Id, new TransitionProgressCurve
        {
            Points = [new(0, 0), new(6, 0)]
        });
        using var media = new MediaService();
        using var cpu = new TimelineRenderService(media, useGpuCompositing: false);
        Assert.Equal((0, 0, 255), Pixel(cpu.RenderFrame(project, 10, Width, Height), 8));
        editor.SetTransitionProgress(clip.Id, null);
        editor.SetVideoTransition(clip.Id, new() { Kind = VideoTransitionKind.Dissolve, Preset = "Additive Dissolve" });
        var midpoint = cpu.RenderFrame(project, 10, Width, Height);
        Assert.Equal(Pixel(midpoint, 8), Pixel(midpoint, 56));
        Assert.InRange(Pixel(midpoint, 8).R, 37, 39);
    }

    private static (ProjectDocument Project, TimelineClip Top) MakeProject()
    {
        var red = new MediaAsset { Path = "openrend://color?hex=FF0000", HasVideo = true };
        var blue = new MediaAsset { Path = "openrend://color?hex=0000FF", HasVideo = true };
        var top = new TimelineClip { MediaId = red.Id, StartFrame = 4, DurationFrames = 36, Opacity = .5 };
        return (new ProjectDocument { Width = Width, Height = Height, FrameRate = new(30000, 1001), Media = [red, blue],
            Tracks = [new() { Kind = TrackKind.Video, Opacity = .6, Clips = [top] },
                new() { Kind = TrackKind.Video, Clips = [new() { MediaId = blue.Id, DurationFrames = 48 }] }] }, top);
    }

    private static VideoTransition Wipe() => new() { Kind = VideoTransitionKind.LinearWipe, Preset = "Left-Right, Hard Edge" };

    private static (int R, int G, int B) Pixel(byte[] frame, int x)
    {
        var i = ((Height / 2) * Width + x) * 4;
        return (frame[i + 2], frame[i + 1], frame[i]);
    }
}
