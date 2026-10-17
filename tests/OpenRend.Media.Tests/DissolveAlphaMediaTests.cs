using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class DissolveAlphaMediaTests
{
    [Theory]
    [InlineData(255, 0, 1, 1, 1)]
    [InlineData(128, 64, 1, 1, 1)]
    [InlineData(128, 192, .4, .8, .5)]
    [InlineData(0, 0, 1, 1, 1)]
    public void TransparentGeneratorsInterpolatePremultipliedColorAndCoverage(int outgoingAlpha, int incomingAlpha,
        double outgoingOpacity, double incomingOpacity, double trackOpacity)
    {
        var project = Project($"#{outgoingAlpha:X2}FF0000", $"#{incomingAlpha:X2}0000FF");
        project.Tracks[0].Opacity = trackOpacity;
        project.Tracks[0].Clips[0].Opacity = outgoingOpacity;
        project.Tracks[0].Clips[1].Opacity = incomingOpacity;
        var expectedRed = outgoingAlpha * outgoingOpacity * trackOpacity * .5;
        var expectedBlue = incomingAlpha * incomingOpacity * trackOpacity * .5;
        using var media = new MediaService();
        using var cpu = new TimelineRenderService(media, false);
        using var gpu = new TimelineRenderService(media);
        var expected = cpu.RenderFrame(project, 15, 32, 24);
        Pixel(expected, 32, 16, 12, expectedRed, 255 - expectedRed - expectedBlue, expectedBlue);
        SamePixels(expected, gpu.RenderFrame(project, 15, 32, 24));
        Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
    }

    [Fact]
    public async Task AlphaEffectsRemainTransparentInPreviewAndEncodedDissolves()
    {
        var project = Project("#FF0000", "#0000FF");
        var keyer = VideoEffect.Create(VideoEffectKind.ChromaKeyer);
        keyer.SetParameter("Red", 0); keyer.SetParameter("Green", 0); keyer.SetParameter("Blue", 1);
        project.Tracks[0].Clips[1].VideoEffects = [keyer];
        using var media = new MediaService();
        using var cpu = new TimelineRenderService(media, false);
        using var gpu = new TimelineRenderService(media);
        var expected = cpu.RenderFrame(project, 15, 32, 24);
        Pixel(expected, 32, 16, 12, 127.5, 127.5, 0);
        SamePixels(expected, gpu.RenderFrame(project, 15, 32, 24));
        var path = Path.Combine(Path.GetTempPath(), $"openrend-dissolve-alpha-{Guid.NewGuid():N}.mp4");
        try
        {
            await new ExportService(cpu).ExportAsync(project, path);
            using var decodedMedia = new MediaService();
            var encoded = decodedMedia.GetFrame(path, .5, 32, 24);
            Pixel(encoded, 32, 16, 12, 127.5, 127.5, 0, 8);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SplitsPreserveTransparentPairPixelsAtEveryTransitionFrame()
    {
        var project = Project("#80FF0000", "#400000FF");
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, false);
        var expected = Enumerable.Range(10, 10).Select(frame => renderer.RenderFrame(project, frame, 32, 24)).ToArray();
        var editor = new TimelineEditor(project);
        editor.SplitClip(project.Tracks[0].Clips[1].Id, 14);
        editor.SplitClip(project.Tracks[0].Clips[0].Id, 17);
        for (var frame = 10; frame < 20; frame++) Assert.Equal(expected[frame - 10], renderer.RenderFrame(project, frame, 32, 24));
    }

    [Fact]
    public async Task DecodedPerPixelAlphaSurvivesResizingAndTheDissolveGroup()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"openrend-dissolve-source-alpha-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var raw = Path.Combine(directory, "alpha.bgra");
            var source = Path.Combine(directory, "alpha.mkv");
            var pixels = new byte[64 * 48 * 4 * 30];
            for (var frame = 0; frame < 30; frame++)
            for (var y = 0; y < 48; y++)
            for (var x = 0; x < 64; x++)
            {
                var offset = ((frame * 48 + y) * 64 + x) * 4;
                pixels[offset + 2] = 255;
                pixels[offset + 3] = x < 32 ? (byte)255 : (byte)0;
            }
            File.WriteAllBytes(raw, pixels);
            MediaFixture.Run("ffmpeg", "-v", "error", "-f", "rawvideo", "-pixel_format", "bgra", "-video_size", "64x48", "-framerate", "30", "-i", raw,
                "-c:v", "ffv1", "-pix_fmt", "bgra", source);
            using var media = new MediaService();
            var asset = await media.ProbeAsync(source);
            var project = Project("#FF0000", "#800000FF");
            project.Media.Add(asset);
            project.Tracks[0].Clips[0].MediaId = asset.Id;
            using var cpu = new TimelineRenderService(media, false);
            using var gpu = new TimelineRenderService(media);
            var expected = cpu.RenderFrame(project, 15, 32, 24);
            Pixel(expected, 32, 4, 12, 127.5, 63.5, 64);
            Pixel(expected, 32, 28, 12, 0, 191, 64);
            SamePixels(expected, gpu.RenderFrame(project, 15, 32, 24));
            Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task IncomingLetterboxOrOutOfSourceCropDoesNotHoldTheOutgoingAtFullOpacity(bool cropOutside, bool alphaEffect)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"openrend-dissolve-geometry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "blue.mkv");
            MediaFixture.Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "color=blue:s=32x16:r=30:d=1", "-c:v", "ffv1", path);
            var original = File.ReadAllBytes(path);
            using var media = new MediaService();
            var asset = await media.ProbeAsync(path);
            var project = Project("#FF0000", "#0000FF");
            project.Media.Add(asset);
            var incoming = project.Tracks[0].Clips[1];
            incoming.MediaId = asset.Id;
            if (alphaEffect)
            {
                var levels = VideoEffect.Create(VideoEffectKind.Levels);
                levels.SetParameter("Channel", 4);
                levels.SetParameter("OutputWhite", .5);
                incoming.VideoEffects = [levels];
            }
            if (cropOutside) incoming.Crop = new CropAnimation { Keyframes = [new CropKeyframe
                { State = CropState.Identity(32, 16) with { CenterX = 32 } }] };
            using var cpu = new TimelineRenderService(media, false);
            using var gpu = new TimelineRenderService(media);
            var expected = cpu.RenderFrame(project, 15, 32, 24);
            var blue = alphaEffect ? 63.5 : 127.5;
            var green = alphaEffect ? 64 : 0;
            Pixel(expected, 32, 16, 0, 127.5, 127.5, 0);
            if (cropOutside)
            {
                Pixel(expected, 32, 28, 12, 127.5, 127.5, 0);
                Pixel(expected, 32, 4, 12, 127.5, green, blue, 2);
            }
            else Pixel(expected, 32, 16, 12, 127.5, green, blue, 2);
            SamePixels(expected, gpu.RenderFrame(project, 15, 32, 24));
            Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static ProjectDocument Project(string outgoingColor, string incomingColor)
    {
        var project = new ProjectDocument { Width = 32, Height = 24 };
        var red = new MediaAsset { Path = "openrend://color?hex=" + outgoingColor, DurationSeconds = 2, HasVideo = true };
        var blue = new MediaAsset { Path = "openrend://color?hex=" + incomingColor, DurationSeconds = 2, HasVideo = true };
        var green = new MediaAsset { Path = "openrend://color?hex=#00FF00", DurationSeconds = 2, HasVideo = true };
        project.Media = [red, blue, green];
        var editor = new TimelineEditor(project);
        var track = editor.AddTrack(TrackKind.Video);
        var outgoing = editor.PlaceClip(red.Id, track.Id, 0, 20);
        var incoming = editor.PlaceClip(blue.Id, track.Id, 10, 20);
        editor.ApplyDissolve(outgoing.Id, incoming.Id);
        var lower = editor.AddTrack(TrackKind.Video);
        editor.PlaceClip(green.Id, lower.Id, 0, 30);
        return project;
    }

    private static void Pixel(byte[] pixels, int width, int x, int y, double red, double green, double blue, int tolerance = 1)
    {
        var offset = (y * width + x) * 4;
        foreach (var (channel, expected) in new[] { (0, blue), (1, green), (2, red) })
            Assert.InRange((int)pixels[offset + channel], (int)Math.Round(expected) - tolerance, (int)Math.Round(expected) + tolerance);
        Assert.Equal(255, pixels[offset + 3]);
    }

    private static void SamePixels(byte[] expected, byte[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++) Assert.InRange(Math.Abs(expected[index] - actual[index]), 0, 1);
    }
}
