using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class DissolveMediaTests
{
    [Fact]
    public void DefaultDissolveBlendsOpaqueGeneratorsInFrameOrder()
    {
        var project = CreateProject();

        using var media = new MediaService();
        using var cpu = new TimelineRenderService(media, useGpuCompositing: false);
        using var gpu = new TimelineRenderService(media);
        AssertColor(cpu.RenderFrame(project, 15, 32, 24), 255, 0);
        AssertColor(cpu.RenderFrame(project, 22, 32, 24), 136, 119);
        AssertColor(cpu.RenderFrame(project, 30, 32, 24), 0, 255);
        Assert.Equal(cpu.RenderFrame(project, 22, 32, 24), gpu.RenderFrame(project, 22, 32, 24));
    }

    [Fact]
    public async Task EncodedDissolveUsesTheSameTimelineFrames()
    {
        var project = CreateProject();
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var media = new MediaService();
            using var renderer = new TimelineRenderService(media, useGpuCompositing: false);
            var path = Path.Combine(directory, "dissolve.mp4");
            await new ExportService(renderer).ExportAsync(project, path);
            var decoded = media.GetFrame(path, project.FrameRate.Seconds(22), 32, 24);
            Assert.InRange((int)decoded[2], 126, 146);
            Assert.InRange((int)decoded[0], 109, 129);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static ProjectDocument CreateProject()
    {
        var project = new ProjectDocument { Width = 32, Height = 24 };
        var red = new MediaAsset { Path = "openrend://color?hex=#FF0000", DurationSeconds = 3, HasVideo = true };
        var blue = new MediaAsset { Path = "openrend://color?hex=#0000FF", DurationSeconds = 3, HasVideo = true };
        var editor = new TimelineEditor(project);
        editor.AddMedia(red);
        editor.AddMedia(blue);
        var track = editor.AddTrack(TrackKind.Video);
        var outgoing = editor.PlaceClip(red.Id, track.Id, 0, 30);
        var incoming = editor.PlaceClip(blue.Id, track.Id, 15, 30);
        editor.ApplyDissolve(outgoing.Id, incoming.Id);
        return project;
    }

    private static void AssertColor(byte[] pixels, int red, int blue)
    {
        Assert.InRange((int)pixels[2], red - 1, red + 1);
        Assert.Equal(0, pixels[1]);
        Assert.InRange((int)pixels[0], blue - 1, blue + 1);
        Assert.Equal(255, pixels[3]);
    }
}
