using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class OverlapToCutMediaTests
{
    [Theory]
    [InlineData(CutTransitionAlignment.BeforeCut, 60, 255, 255, 255)]
    [InlineData(CutTransitionAlignment.CenteredOnCut, 45, 0, 0, 255)]
    [InlineData(CutTransitionAlignment.AfterCut, 30, 255, 0, 0)]
    public async Task PreviewAndExportCutAtTheSameFrameWithTheSurvivingTake(CutTransitionAlignment alignment, long cut, int red, int green, int blue)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"openrend-overlap-cut-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            // NUT retains the rational timestamps; Matroska's millisecond grid shifts this color boundary.
            var source = Path.Combine(directory, "colors.nut");
            MediaFixture.Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "color=red:s=32x24:r=30000/1001:d=1.001",
                "-f", "lavfi", "-i", "color=blue:s=32x24:r=30000/1001:d=0.5005", "-f", "lavfi", "-i",
                "color=white:s=32x24:r=30000/1001:d=1.5015", "-filter_complex", "[0:v][1:v][2:v]concat=n=3:v=1:a=0",
                "-c:v", "ffv1", source);
            var original = File.ReadAllBytes(source);
            using var media = new MediaService();
            var asset = await media.ProbeAsync(source);
            var color = new MediaAsset { Path = "openrend://color?hex=#00FF00", HasVideo = true, DurationSeconds = 3 };
            var outgoing = new TimelineClip { MediaId = color.Id, DurationFrames = 60, FadeOutFrames = 30 };
            var incoming = new TimelineClip { MediaId = asset.Id, StartFrame = 30, SourceInFrame = 15, DurationFrames = 60,
                FadeInFrames = 30, FadeInTransition = new() };
            var project = new ProjectDocument { Width = 32, Height = 24, FrameRate = new(30000, 1001), Media = [color, asset],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [outgoing, incoming] }] };
            new TimelineEditor(project).ConvertOverlapToCut(outgoing.Id, incoming.Id, alignment);
            using var cpu = new TimelineRenderService(media, false);
            using var gpu = new TimelineRenderService(media);
            foreach (var frame in new[] { cut - 1, cut, cut + 1 })
            {
                var pixels = cpu.RenderFrame(project, frame, 32, 24);
                Color(pixels, frame < cut ? 0 : red, frame < cut ? 255 : green, frame < cut ? 0 : blue, 3);
                var hardware = gpu.RenderFrame(project, frame, 32, 24);
                Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
                for (var i = 0; i < pixels.Length; i++) Assert.InRange(Math.Abs(pixels[i] - hardware[i]), 0, 1);
            }
            var output = Path.Combine(directory, "cut.mp4");
            await new ExportService(cpu).ExportAsync(project, output);
            using var decoder = new MediaService();
            foreach (var frame in new[] { cut - 1, cut, cut + 1 })
                Color(decoder.GetFrame(output, project.FrameRate.Seconds(frame), 32, 24),
                    frame < cut ? 0 : red, frame < cut ? 255 : green, frame < cut ? 0 : blue, 9);
            Assert.Equal(90, project.DurationFrames);
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
