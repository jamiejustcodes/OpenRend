using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class RemovedTransitionMediaTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingDefaultPluginKeepsThePlainCrossfadePixelsIncludingSplitAlpha(bool split)
    {
        var project = new ProjectDocument { Width = 32, Height = 24 };
        var red = new MediaAsset { Path = "openrend://color?hex=#80FF0000", HasVideo = true, DurationSeconds = 2 };
        var blue = new MediaAsset { Path = "openrend://color?hex=#C00000FF", HasVideo = true, DurationSeconds = 2 };
        var green = new MediaAsset { Path = "openrend://color?hex=#00FF00", HasVideo = true, DurationSeconds = 2 };
        project.Media = [red, blue, green];
        var editor = new TimelineEditor(project);
        var track = editor.AddTrack(TrackKind.Video);
        var outgoing = editor.PlaceClip(red.Id, track.Id, 0, 30);
        var incoming = editor.PlaceClip(blue.Id, track.Id, 10, 30);
        editor.ApplyDissolve(outgoing.Id, incoming.Id);
        track.Opacity = .6; outgoing.Opacity = .4; incoming.Opacity = .8;
        var lower = editor.AddTrack(TrackKind.Video);
        editor.PlaceClip(green.Id, lower.Id, 0, 40);
        using var media = new MediaService();
        using var cpu = new TimelineRenderService(media, false);
        using var gpu = new TimelineRenderService(media);
        var expected = Enumerable.Range(10, 20).Select(frame => cpu.RenderFrame(project, frame, 32, 24)).ToArray();
        if (split)
        {
            var right = editor.SplitClip(incoming.Id, 16);
            editor.SplitClip(outgoing.Id, 24);
            editor.RemoveVideoTransition(right.Id);
        }
        editor.RemoveVideoTransition(incoming.Id);
        for (var frame = 10; frame < 30; frame++)
        {
            Assert.Equal(expected[frame - 10], cpu.RenderFrame(project, frame, 32, 24));
            var actual = gpu.RenderFrame(project, frame, 32, 24);
            for (var index = 0; index < actual.Length; index++) Assert.InRange(Math.Abs(actual[index] - expected[frame - 10][index]), 0, 1);
        }
        Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
    }
}
