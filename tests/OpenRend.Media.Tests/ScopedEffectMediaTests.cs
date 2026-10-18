using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class ScopedEffectMediaTests(MediaFixture fixture) : IClassFixture<MediaFixture>
{
    [Fact]
    public async Task ScopedEffectsProcessInVegasProOrderMediaEventTrackOutput()
    {
        using var media = new MediaService();
        var source = await media.ProbeAsync(fixture.Quadrants);

        // Case 1: Pure red pixel at origin.
        // Media FX: Invert -> Turns red (0,0,255) into cyan (255,255,0)
        source.VideoEffects = [VideoEffect.Create(VideoEffectKind.Invert)];

        var clip = new TimelineClip
        {
            MediaId = source.Id,
            DurationFrames = 30,
            // Event FX: Invert -> Turns cyan (255,255,0) back to red (0,0,255)
            VideoEffects = [VideoEffect.Create(VideoEffectKind.Invert)]
        };

        var track = new TimelineTrack
        {
            Kind = TrackKind.Video,
            Clips = [clip],
            // Track FX: Invert -> Turns red (0,0,255) into cyan (255,255,0)
            VideoEffects = [VideoEffect.Create(VideoEffectKind.Invert)]
        };

        var project = new ProjectDocument
        {
            Width = 64,
            Height = 48,
            Media = [source],
            Tracks = [track],
            // Video Output FX: Invert -> Turns cyan (255,255,0) back to red (0,0,255)
            VideoOutputEffects = [VideoEffect.Create(VideoEffectKind.Invert)]
        };

        using var cpu = new TimelineRenderService(media, useGpuCompositing: false);
        using var gpu = new TimelineRenderService(media);

        var cpuPixels = cpu.RenderFrame(project, 0, 64, 48);
        var gpuPixels = gpu.RenderFrame(project, 0, 64, 48);

        // Verify pixel match between CPU and GPU
        for (var i = 0; i < cpuPixels.Length; i++)
        {
            Assert.InRange(Math.Abs(cpuPixels[i] - gpuPixels[i]), 0, 1);
        }

        // Top-left quadrant of fixture.Quadrants is Red: BGRA = (0, 0, 255, 255)
        // Media (invert) -> Event (invert) -> Track (invert) -> Output (invert) => (0, 0, 255, 255)
        var topLeft = (10 * 64 + 10) * 4;
        Assert.InRange(cpuPixels[topLeft + 2], (byte)250, (byte)255); // Red
        Assert.InRange(cpuPixels[topLeft + 0], (byte)0, (byte)5);     // Blue
        Assert.InRange(cpuPixels[topLeft + 1], (byte)0, (byte)5);     // Green

        // Now disable Output FX: result should be cyan (255, 255, 0, 255)
        project.VideoOutputEffects[0].IsEnabled = false;
        var cyanPixels = cpu.RenderFrame(project, 0, 64, 48);
        Assert.InRange(cyanPixels[topLeft + 0], (byte)250, (byte)255); // Blue
        Assert.InRange(cyanPixels[topLeft + 1], (byte)250, (byte)255); // Green
        Assert.InRange(cyanPixels[topLeft + 2], (byte)0, (byte)5);     // Red

        // Now disable Track FX: result should be red (0, 0, 255, 255)
        track.VideoEffects[0].IsEnabled = false;
        var redPixels = cpu.RenderFrame(project, 0, 64, 48);
        Assert.InRange(redPixels[topLeft + 2], (byte)250, (byte)255); // Red
        Assert.InRange(redPixels[topLeft + 0], (byte)0, (byte)5);     // Blue
        Assert.InRange(redPixels[topLeft + 1], (byte)0, (byte)5);     // Green

        // Now disable Event FX: result should be cyan (255, 255, 0, 255)
        clip.VideoEffects[0].IsEnabled = false;
        var mediaOnlyPixels = cpu.RenderFrame(project, 0, 64, 48);
        Assert.InRange(mediaOnlyPixels[topLeft + 0], (byte)250, (byte)255); // Blue
        Assert.InRange(mediaOnlyPixels[topLeft + 1], (byte)250, (byte)255); // Green
        Assert.InRange(mediaOnlyPixels[topLeft + 2], (byte)0, (byte)5);     // Red
    }

    [Fact]
    public async Task TrackFxProcessesWholeTrackCompositeIndependentOfOtherTracks()
    {
        using var media = new MediaService();
        var source = await media.ProbeAsync(fixture.Quadrants);

        // Track 1 has video with Invert Track FX
        var clip1 = new TimelineClip { MediaId = source.Id, DurationFrames = 30 };
        var track1 = new TimelineTrack
        {
            Kind = TrackKind.Video,
            Clips = [clip1],
            VideoEffects = [VideoEffect.Create(VideoEffectKind.Invert)]
        };

        // Track 2 has normal clip, no Track FX
        var clip2 = new TimelineClip { MediaId = source.Id, DurationFrames = 30 };
        var track2 = new TimelineTrack
        {
            Kind = TrackKind.Video,
            Clips = [clip2]
        };

        var project = new ProjectDocument
        {
            Width = 64,
            Height = 48,
            Media = [source],
            Tracks = [track1, track2]
        };

        using var cpu = new TimelineRenderService(media, useGpuCompositing: false);
        var frame = cpu.RenderFrame(project, 0, 64, 48);

        // Since track 1 is top track and opaque, inverted pixels are shown
        var topLeft = (10 * 64 + 10) * 4;
        Assert.InRange(frame[topLeft + 0], (byte)250, (byte)255); // Cyan (Blue + Green)
        Assert.InRange(frame[topLeft + 1], (byte)250, (byte)255);
        Assert.InRange(frame[topLeft + 2], (byte)0, (byte)5);

        // When track 1 is bypassed/muted, track 2 (non-inverted red) shows through
        track1.IsMuted = true;
        var frame2 = cpu.RenderFrame(project, 0, 64, 48);
        Assert.InRange(frame2[topLeft + 2], (byte)250, (byte)255); // Red
        Assert.InRange(frame2[topLeft + 0], (byte)0, (byte)5);
    }
}
