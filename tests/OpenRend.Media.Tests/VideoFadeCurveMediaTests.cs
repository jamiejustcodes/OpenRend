using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class VideoFadeCurveMediaTests
{
    [Theory]
    [InlineData(VideoFadeCurve.Linear, .5)]
    [InlineData(VideoFadeCurve.Fast, .75)]
    [InlineData(VideoFadeCurve.Slow, .25)]
    [InlineData(VideoFadeCurve.Smooth, .5)]
    [InlineData(VideoFadeCurve.Sharp, .5)]
    public async Task VideoFadesShareCpuGpuAndEncodedTiming(VideoFadeCurve curve, double midpoint)
    {
        var red = new MediaAsset { Path = "openrend://color?hex=#C80000", HasVideo = true, DurationSeconds = 3 };
        var blue = new MediaAsset { Path = "openrend://color?hex=#000050", HasVideo = true, DurationSeconds = 3 };
        var clip = new TimelineClip { MediaId = red.Id, DurationFrames = 60, FadeInFrames = 30, FadeOutFrames = 30,
            FadeInCurve = curve, FadeOutCurve = curve, Opacity = .8 };
        var project = new ProjectDocument { Width = 32, Height = 24, FrameRate = new(30000, 1001), Media = [red, blue],
            Tracks = [new() { Kind = TrackKind.Video, Opacity = .75, Clips = [clip] },
                new() { Kind = TrackKind.Video, Clips = [new() { MediaId = blue.Id, DurationFrames = 60 }] }] };
        project.Validate();
        using var media = new MediaService();
        using var cpu = new TimelineRenderService(media, false);
        using var gpu = new TimelineRenderService(media);
        foreach (var (frame, amount) in new[] { (0, 0.0), (15, midpoint), (30, 1.0), (45, 1 - midpoint), (60, 0.0) })
        {
            var expected = cpu.RenderFrame(project, frame, 32, 24);
            var actual = gpu.RenderFrame(project, frame, 32, 24);
            Assert.Equal(expected.Length, actual.Length);
            for (var i = 0; i < expected.Length; i++) Assert.InRange(Math.Abs(expected[i] - actual[i]), 0, 1);
            if (frame < 60)
            {
                Assert.InRange(Math.Abs(expected[2] - 120 * amount), 0, 1);
                Assert.InRange(Math.Abs(expected[0] - 80 * (1 - .6 * amount)), 0, 1);
            }
        }
        Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
        var folder = Path.Combine(Path.GetTempPath(), "openrend-video-fades-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var output = Path.Combine(folder, "fade.mp4"); await new ExportService(cpu).ExportAsync(project, output);
            using var decoded = new MediaService();
            foreach (var frame in new[] { 15, 45 })
            {
                var preview = cpu.RenderFrame(project, frame, 32, 24);
                var encoded = decoded.GetFrame(output, project.FrameRate.Seconds(frame), 32, 24);
                for (var i = 0; i < preview.Length; i++) Assert.InRange(Math.Abs(preview[i] - encoded[i]), 0, 6);
            }
            var before = Enumerable.Range(0, 60).Select(frame => cpu.RenderFrame(project, frame, 32, 24)).ToArray();
            var editor = new TimelineEditor(project); var right = editor.SplitClip(clip.Id, 11); editor.SplitClip(right.Id, 49);
            for (var frame = 0; frame < 60; frame++) Assert.Equal(before[frame], cpu.RenderFrame(project, frame, 32, 24));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task ReciprocalCrossfadeSharesCpuGpuAndEncodedTiming()
    {
        var red = new MediaAsset { Path = "openrend://color?hex=#C80000", HasVideo = true, DurationSeconds = 3 };
        var green = new MediaAsset { Path = "openrend://color?hex=#00C800", HasVideo = true, DurationSeconds = 3 };
        var outgoing = new TimelineClip { MediaId = red.Id, StartFrame = 0, DurationFrames = 45, FadeOutFrames = 30 };
        var incoming = new TimelineClip { MediaId = green.Id, StartFrame = 15, DurationFrames = 45, FadeInFrames = 30,
            FadeInCurve = VideoFadeCurve.Linear, FadeInReciprocalCurve = VideoFadeCurve.Fast };
        var project = new ProjectDocument { Width = 32, Height = 24, FrameRate = new(30, 1), Media = [red, green],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [outgoing, incoming] }] };
        project.Validate();

        using var media = new MediaService();
        using var cpu = new TimelineRenderService(media, false);
        using var gpu = new TimelineRenderService(media);

        // At frame 30 (t=0.5): incoming Linear=0.5 (Green=200*0.5=100), outgoing 1-Fast(0.5)=0.25 (Red=200*0.25=50)
        var expected = cpu.RenderFrame(project, 30, 32, 24);
        var actual = gpu.RenderFrame(project, 30, 32, 24);
        for (var i = 0; i < expected.Length; i++) Assert.InRange(Math.Abs(expected[i] - actual[i]), 0, 1);
        Assert.InRange(Math.Abs(expected[1] - 100), 0, 1);
        Assert.InRange(Math.Abs(expected[2] - 50), 0, 1);

        var folder = Path.Combine(Path.GetTempPath(), "openrend-recip-crossfade-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var output = Path.Combine(folder, "recip.mp4"); await new ExportService(cpu).ExportAsync(project, output);
            using var decoded = new MediaService();
            var encoded = decoded.GetFrame(output, project.FrameRate.Seconds(30), 32, 24);
            for (var i = 0; i < expected.Length; i++) Assert.InRange(Math.Abs(expected[i] - encoded[i]), 0, 6);

            var before = Enumerable.Range(15, 30).Select(f => cpu.RenderFrame(project, f, 32, 24)).ToArray();
            var editor = new TimelineEditor(project);
            editor.SplitClip(incoming.Id, 25);
            for (var i = 0; i < 30; i++) Assert.Equal(before[i], cpu.RenderFrame(project, 15 + i, 32, 24));
        }
        finally { Directory.Delete(folder, true); }
    }
}
