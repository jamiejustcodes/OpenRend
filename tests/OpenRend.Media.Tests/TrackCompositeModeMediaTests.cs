using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class TrackCompositeModeMediaTests
{
    private static ProjectDocument CreateBlendProject(TrackCompositeMode mode, string topColorHex, string bottomColorHex, double topOpacity = 1.0)
    {
        var project = new ProjectDocument { Width = 32, Height = 24 };
        var topAsset = new MediaAsset { Path = $"openrend://color?hex={topColorHex}", DurationSeconds = 3, HasVideo = true };
        var bottomAsset = new MediaAsset { Path = $"openrend://color?hex={bottomColorHex}", DurationSeconds = 3, HasVideo = true };

        project.Media.Add(topAsset);
        project.Media.Add(bottomAsset);

        // Track 0 is the upper/top track with the composite mode
        var topTrack = new TimelineTrack
        {
            Kind = TrackKind.Video,
            CompositeMode = mode,
            Opacity = topOpacity,
            Clips = [new TimelineClip { MediaId = topAsset.Id, DurationFrames = 30 }]
        };

        // Track 1 is the lower/bottom track (background)
        var bottomTrack = new TimelineTrack
        {
            Kind = TrackKind.Video,
            CompositeMode = TrackCompositeMode.SourceAlpha,
            Clips = [new TimelineClip { MediaId = bottomAsset.Id, DurationFrames = 30 }]
        };

        project.Tracks.Add(topTrack);
        project.Tracks.Add(bottomTrack);
        project.Validate();
        return project;
    }

    [Fact]
    public void MultiplyBlendModeMultipliesColorChannels()
    {
        // 50% Gray (128) over White (255) -> 128
        // Red (255, 0, 0) over Green (0, 255, 0) -> 0
        var project = CreateBlendProject(TrackCompositeMode.Multiply, "#808080", "#FFFFFF");
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);

        var pixels = renderer.RenderFrame(project, 0, 32, 24);
        // BGRA format: B=0, G=1, R=2, A=3
        Assert.InRange((int)pixels[2], 126, 130);
        Assert.InRange((int)pixels[1], 126, 130);
        Assert.InRange((int)pixels[0], 126, 130);
    }

    [Fact]
    public void AddBlendModeSumsColorChannelsCappedAt255()
    {
        // Red (255, 0, 0) + Green (0, 255, 0) -> Yellow (255, 255, 0)
        var project = CreateBlendProject(TrackCompositeMode.Add, "#FF0000", "#00FF00");
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);

        var pixels = renderer.RenderFrame(project, 0, 32, 24);
        Assert.InRange((int)pixels[2], 254, 255); // Red
        Assert.InRange((int)pixels[1], 254, 255); // Green
        Assert.Equal(0, pixels[0]);               // Blue
    }

    [Fact]
    public void SubtractBlendModeSubtractsSourceFromDestination()
    {
        // Destination = White (255, 255, 255), Source = Red (255, 0, 0) -> Cyan (0, 255, 255)
        var project = CreateBlendProject(TrackCompositeMode.Subtract, "#FF0000", "#FFFFFF");
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);

        var pixels = renderer.RenderFrame(project, 0, 32, 24);
        Assert.Equal(0, pixels[2]);               // Red (255 - 255 = 0)
        Assert.InRange((int)pixels[1], 254, 255); // Green (255 - 0 = 255)
        Assert.InRange((int)pixels[0], 254, 255); // Blue (255 - 0 = 255)
    }

    [Fact]
    public void DifferenceBlendModeComputesAbsoluteDifference()
    {
        // Source = Red (255, 0, 0), Destination = Yellow (255, 255, 0) -> Green (0, 255, 0)
        var project = CreateBlendProject(TrackCompositeMode.Difference, "#FF0000", "#FFFF00");
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);

        var pixels = renderer.RenderFrame(project, 0, 32, 24);
        Assert.Equal(0, pixels[2]);               // Red (|255 - 255| = 0)
        Assert.InRange((int)pixels[1], 254, 255); // Green (|255 - 0| = 255)
        Assert.Equal(0, pixels[0]);               // Blue
    }

    [Fact]
    public void ScreenBlendModeInvertsMultipliesAndInverts()
    {
        // Red (255, 0, 0) screened with Green (0, 255, 0) -> Yellow (255, 255, 0)
        var project = CreateBlendProject(TrackCompositeMode.Screen, "#FF0000", "#00FF00");
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);

        var pixels = renderer.RenderFrame(project, 0, 32, 24);
        Assert.InRange((int)pixels[2], 254, 255);
        Assert.InRange((int)pixels[1], 254, 255);
        Assert.Equal(0, pixels[0]);
    }

    [Fact]
    public void CutBlendModeCutsOutLowerTrackByAlpha()
    {
        // Full opacity cut turns destination completely black / transparent
        var project = CreateBlendProject(TrackCompositeMode.Cut, "#FFFFFF", "#FFFFFF", topOpacity: 1.0);
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);

        var pixels = renderer.RenderFrame(project, 0, 32, 24);
        Assert.Equal(0, pixels[2]);
        Assert.Equal(0, pixels[1]);
        Assert.Equal(0, pixels[0]);
        Assert.Equal(0, pixels[3]);
    }

    [Fact]
    public void BypassVideoEffects_SkipsClipAndTrackEffectsWhenRequested()
    {
        var project = new ProjectDocument { Width = 16, Height = 16 };
        var asset = new MediaAsset { Path = "openrend://color?hex=#000000", DurationSeconds = 2, HasVideo = true };
        project.Media.Add(asset);

        var track = new TimelineTrack { Kind = TrackKind.Video };
        var clip = new TimelineClip
        {
            MediaId = asset.Id,
            DurationFrames = 30,
            VideoEffects = [VideoEffect.Create(VideoEffectKind.Invert)]
        };
        track.Clips.Add(clip);
        project.Tracks.Add(track);
        project.Validate();

        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);

        // When bypassVideoEffects is false (default): Invert turns black (0) into white (255)
        var fxPixels = renderer.RenderFrame(project, 0, 16, 16, bypassVideoEffects: false);
        Assert.Equal(255, fxPixels[0]); // B
        Assert.Equal(255, fxPixels[1]); // G
        Assert.Equal(255, fxPixels[2]); // R

        // When bypassVideoEffects is true: Invert is bypassed and original black (0) is returned
        var cleanPixels = renderer.RenderFrame(project, 0, 16, 16, bypassVideoEffects: true);
        Assert.Equal(0, cleanPixels[0]); // B
        Assert.Equal(0, cleanPixels[1]); // G
        Assert.Equal(0, cleanPixels[2]); // R
    }
}
