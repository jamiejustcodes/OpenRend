using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class AutomaticDissolveMediaTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticOverlapUsesTheCorrectTakeFramesInPreviewAndExport(bool ripple)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"openrend-auto-dissolve-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "colors.mkv");
            MediaFixture.Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "color=red:s=32x24:r=30000/1001:d=1.001",
                "-f", "lavfi", "-i", "color=blue:s=32x24:r=30000/1001:d=0.5005", "-f", "lavfi", "-i",
                "color=white:s=32x24:r=30000/1001:d=1.5015", "-filter_complex", "[0:v][1:v][2:v]concat=n=3:v=1:a=0",
                "-c:v", "ffv1", source);
            var original = File.ReadAllBytes(source);
            using var media = new MediaService();
            var asset = await media.ProbeAsync(source);
            var green = new MediaAsset { Path = "openrend://color?hex=#00FF00", HasVideo = true, DurationSeconds = 3 };
            var outgoing = new TimelineClip { MediaId = green.Id, DurationFrames = 60 };
            var incoming = new TimelineClip { MediaId = asset.Id, StartFrame = 60, SourceInFrame = 30, DurationFrames = 45 };
            var project = new ProjectDocument { Width = 32, Height = 24, FrameRate = new(30000, 1001), Media = [green, asset],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [outgoing, incoming] }] };
            var editor = new TimelineEditor(project);
            Assert.Equal(1, editor.ApplyDissolveToSelection([incoming.Id], 15, ripple));
            using var cpu = new TimelineRenderService(media, false);
            using var gpu = new TimelineRenderService(media);
            var overlap = cpu.RenderFrame(project, 52, 32, 24);
            Color(overlap, ripple ? 0 : 119, 136, ripple ? 119 : 0, 3);
            var after = cpu.RenderFrame(project, 65, 32, 24);
            Color(after, ripple ? 255 : 0, ripple ? 255 : 0, 255, 3);
            var hardware = gpu.RenderFrame(project, 52, 32, 24);
            Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
            for (var i = 0; i < overlap.Length; i++) Assert.InRange(Math.Abs(overlap[i] - hardware[i]), 0, 1);
            var output = Path.Combine(directory, "automatic.mp4");
            await new ExportService(cpu).ExportAsync(project, output);
            using var decoder = new MediaService();
            Color(decoder.GetFrame(output, project.FrameRate.Seconds(52), 32, 24), ripple ? 0 : 119, 136, ripple ? 119 : 0, 9);
            Color(decoder.GetFrame(output, project.FrameRate.Seconds(65), 32, 24), ripple ? 255 : 0, ripple ? 255 : 0, 255, 9);
            Assert.Equal(original, File.ReadAllBytes(source));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Color(byte[] pixels, int red, int green, int blue, int tolerance)
    {
        Assert.InRange((int)pixels[2], Math.Max(0, red - tolerance), Math.Min(255, red + tolerance));
        Assert.InRange((int)pixels[1], Math.Max(0, green - tolerance), Math.Min(255, green + tolerance));
        Assert.InRange((int)pixels[0], Math.Max(0, blue - tolerance), Math.Min(255, blue + tolerance));
    }
}
