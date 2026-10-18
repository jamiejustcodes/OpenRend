using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class ClipLockAndMuteMediaTests
{
    [Fact]
    public void MutedVideoClipEvaluatesAsBlackInCpuAndGpuCompositor()
    {
        var red = new MediaAsset { Path = "openrend://color?hex=#C80000", HasVideo = true, DurationSeconds = 3 };
        var clip = new TimelineClip { MediaId = red.Id, DurationFrames = 30, Opacity = 1.0 };
        var project = new ProjectDocument
        {
            Width = 32,
            Height = 24,
            FrameRate = new(30, 1),
            Media = [red],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
        };
        project.Validate();

        using var media = new MediaService();
        using var cpu = new TimelineRenderService(media, false);
        using var gpu = new TimelineRenderService(media, true);

        // Active clip: red is visible
        var cpuFrameActive = cpu.RenderFrame(project, 5, 32, 24);
        var gpuFrameActive = gpu.RenderFrame(project, 5, 32, 24);
        Assert.InRange(cpuFrameActive[2], 190, 210); // Red channel
        Assert.InRange(gpuFrameActive[2], 190, 210);

        // Muted clip: renders empty/black background in both CPU and GPU
        clip.IsMuted = true;
        var cpuFrameMuted = cpu.RenderFrame(project, 5, 32, 24);
        var gpuFrameMuted = gpu.RenderFrame(project, 5, 32, 24);
        Assert.Equal(0, cpuFrameMuted[2]);
        Assert.Equal(0, gpuFrameMuted[2]);

        // Unmute restores rendering
        clip.IsMuted = false;
        var cpuFrameRestored = cpu.RenderFrame(project, 5, 32, 24);
        Assert.InRange(cpuFrameRestored[2], 190, 210);
    }

    [Fact]
    public void MutedAudioClipEvaluatesAsSilenceInAudioMix()
    {
        var tone = new MediaAsset { Path = Path.Combine(AppContext.BaseDirectory, "tone.wav"), HasAudio = true, DurationSeconds = 2 };
        // Create an audio clip
        var clip = new TimelineClip { MediaId = tone.Id, StartFrame = 0, DurationFrames = 60, Gain = 1.0 };
        var project = new ProjectDocument
        {
            FrameRate = new(30, 1),
            Media = [tone],
            Tracks = [new() { Kind = TrackKind.Audio, Clips = [clip] }]
        };

        using var media = new MediaService();
        using var render = new TimelineRenderService(media, false);

        // When clip is muted, RenderAudio should immediately skip it without even needing to decode samples
        clip.IsMuted = true;
        var mixed = render.RenderAudio(project, 0.0, 1024);
        Assert.Equal(2048, mixed.Length);
        Assert.All(mixed, sample => Assert.Equal(0.0f, sample));
    }
}
