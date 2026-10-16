using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class VideoEffectMediaTests(MediaFixture fixture) : IClassFixture<MediaFixture>
{
    [Fact]
    public void OperationsPreserveAlphaAndInputOwnershipAndRespectOrder()
    {
        byte[] source = [0, 0, 255, 77, 200, 100, 50, 0];
        var original = (byte[])source.Clone();
        var invert = VideoEffect.Create(VideoEffectKind.Invert);
        var brightness = VideoEffect.Create(VideoEffectKind.BrightnessContrast);
        brightness.Amount = .25;
        var result = VideoEffectProcessor.Apply(source, [invert, brightness]);
        Assert.Equal(original, source);
        Assert.NotSame(source, result);
        Assert.Equal(new byte[] { 255, 255, 64, 77, 119, 219, 255, 0 }, result);
        var reversed = VideoEffectProcessor.Apply(source, [brightness, invert]);
        Assert.Equal(new byte[] { 191, 191, 0, 77, 0, 91, 141, 0 }, reversed);
        invert.IsEnabled = false;
        Assert.Same(source, VideoEffectProcessor.Apply(source, [invert]));
        Assert.Same(source, VideoEffectProcessor.Apply(source, [VideoEffect.Create(VideoEffectKind.Saturation)]));
    }

    [Fact]
    public void DesaturationAndPartialInvertUseTheirDeclaredAmounts()
    {
        var saturation = VideoEffect.Create(VideoEffectKind.Saturation);
        saturation.Amount = 0;
        Assert.Equal(new byte[] { 54, 54, 54, 128 }, VideoEffectProcessor.Apply([0, 0, 255, 128], [saturation]));
        var invert = VideoEffect.Create(VideoEffectKind.Invert);
        invert.Amount = .5;
        Assert.Equal(new byte[] { 128, 128, 128, 128 }, VideoEffectProcessor.Apply([0, 10, 255, 128], [invert]));
    }

    [Fact]
    public async Task EffectsApplyBeforeCropAndOpacityEquallyOnCpuAndD3D()
    {
        using var media = new MediaService();
        var source = await media.ProbeAsync(fixture.Quadrants);
        var clip = new TimelineClip { MediaId = source.Id, DurationFrames = 30, Opacity = .5,
            VideoEffects = [VideoEffect.Create(VideoEffectKind.Invert)],
            Crop = new() { Keyframes = [new() { State = new(32, 24, 16, 12, 0, 32, 24) }] } };
        var project = new ProjectDocument { Width = 64, Height = 48, Media = [source], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] };
        using var cpu = new TimelineRenderService(media, useGpuCompositing: false);
        using var gpu = new TimelineRenderService(media);
        var fallback = cpu.RenderFrame(project, 0, 64, 48);
        var actual = gpu.RenderFrame(project, 0, 64, 48);
        Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
        for (var p = 0; p < actual.Length; p++) Assert.InRange(Math.Abs(actual[p] - fallback[p]), 0, 1);
        var center = (24 * 64 + 32) * 4;
        Assert.InRange(actual[center], (byte)127, (byte)128);
        Assert.InRange(actual[center + 1], (byte)127, (byte)128);
        Assert.Equal(0, actual[center + 2]);
        clip.VideoEffects[0].IsEnabled = false;
        var bypassed = cpu.RenderFrame(project, 0, 64, 48);
        Assert.Equal(0, bypassed[center]);
        Assert.InRange(bypassed[center + 2], (byte)127, (byte)128);
        // Rendering never writes the effect into a decoder's source frame.
        var original = media.GetFrame(source.Path, 0, 64, 48);
        Assert.Equal(255, original[(12 * 64 + 16) * 4 + 2]);
    }

    [Fact]
    public async Task PreviewAndEncodedExportShareTheOrderedEffectChain()
    {
        using var media = new MediaService();
        var source = await media.ProbeAsync(fixture.Quadrants);
        var saturation = VideoEffect.Create(VideoEffectKind.Saturation);
        saturation.Amount = 0;
        var clip = new TimelineClip { MediaId = source.Id, DurationFrames = 3,
            VideoEffects = [saturation, VideoEffect.Create(VideoEffectKind.Invert)] };
        var project = new ProjectDocument { Width = 64, Height = 48, Media = [source], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] };
        project.Validate();
        using var renderer = new TimelineRenderService(media);
        var preview = renderer.RenderFrame(project, 0, 64, 48);
        var destination = Path.Combine(fixture.Directory, "event-effects.mp4");
        await new ExportService(renderer).ExportAsync(project, destination);
        var encoded = media.GetFrame(destination, 0, 64, 48);
        foreach (var (x, y) in new[] { (16, 12), (48, 12), (16, 36), (48, 36) })
        {
            var offset = (y * 64 + x) * 4;
            for (var channel = 0; channel < 3; channel++)
                Assert.InRange(Math.Abs(encoded[offset + channel] - preview[offset + channel]), 0, 8);
        }
        Assert.Equal(201, preview[(12 * 64 + 16) * 4]);
    }
}
