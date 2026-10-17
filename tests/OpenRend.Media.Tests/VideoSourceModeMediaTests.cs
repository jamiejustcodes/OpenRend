using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class VideoSourceModeMediaTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "openrend-source-mode-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(directory, "eight-colors.nut");
    private readonly byte[] original;

    public VideoSourceModeMediaTests()
    {
        Directory.CreateDirectory(directory);
        var raw = new byte[8 * 32 * 24 * 4];
        for (var frame = 0; frame < 8; frame++)
        for (var pixel = 0; pixel < 32 * 24; pixel++)
        {
            var offset = (frame * 32 * 24 + pixel) * 4;
            raw[offset] = (byte)(5 + frame * 10); raw[offset + 1] = (byte)(10 + frame * 20);
            raw[offset + 2] = (byte)(20 + frame * 30); raw[offset + 3] = 255;
        }
        var input = Path.Combine(directory, "eight-colors.bgra"); File.WriteAllBytes(input, raw);
        // NUT keeps individual timestamps off Matroska's millisecond rounding grid.
        MediaFixture.Run("ffmpeg", "-v", "error", "-f", "rawvideo", "-pixel_format", "bgra", "-video_size", "32x24",
            "-framerate", "8", "-i", input, "-c:v", "ffv1", "-pix_fmt", "bgra", Source);
        original = File.ReadAllBytes(Source);
    }

    private async Task<(ProjectDocument Project, TimelineClip Clip)> Project(MediaService media, VideoSourceMode mode, long sourcePhase = 0)
    {
        var asset = await media.ProbeAsync(Source);
        Assert.InRange(asset.DurationSeconds, .8, 1.01);
        var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 240, SourceInFrame = sourcePhase, VideoSourceMode = mode };
        return (new() { Width = 32, Height = 24, FrameRate = new(30000, 1001), Media = [asset],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] }, clip);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task LoopUsesContinuousRationalPhaseAcrossSeveralCyclesAndNegativePhase(long sourcePhase)
    {
        using var media = new MediaService(); using var reference = new MediaService();
        var (project, _) = await Project(media, VideoSourceMode.Loop, sourcePhase);
        using var cpu = new TimelineRenderService(media, false); using var gpu = new TimelineRenderService(media);
        var colors = new HashSet<byte>();
        foreach (var frame in new long[] { 0, 1, 17, 46, 99, 234 })
        {
            var seconds = Seconds(project.FrameRate, sourcePhase + frame, project.Media[0].DurationSeconds, true);
            var expected = reference.GetFrame(Source, seconds, 32, 24);
            var actual = cpu.RenderFrame(project, frame, 32, 24); Assert.Equal(expected, actual); colors.Add(actual[2]);
            Near(actual, gpu.RenderFrame(project, frame, 32, 24), 1);
        }
        Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason); Assert.True(colors.Count > 2);
        Assert.Equal(original, File.ReadAllBytes(Source));
    }

    [Fact]
    public async Task HoldLastFrameWorksWhenTheFirstDecoderRequestIsBeyondTheSourceEnd()
    {
        using var media = new MediaService();
        var (project, _) = await Project(media, VideoSourceMode.HoldLastFrame);
        using var cpu = new TimelineRenderService(media, false);
        Pixel(cpu.RenderFrame(project, 200, 32, 24), 75, 150, 230);
        using var coldGpuMedia = new MediaService(); using var gpu = new TimelineRenderService(coldGpuMedia);
        Pixel(gpu.RenderFrame(project, 200, 32, 24), 75, 150, 230); Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
        Assert.Equal(original, File.ReadAllBytes(Source));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MappedTimeAlsoFeedsCroppedAndEffectedDecodePaths(bool crop, bool effect)
    {
        using var media = new MediaService(); using var reference = new MediaService();
        var (project, clip) = await Project(media, VideoSourceMode.Loop, -5);
        if (crop) clip.Crop = new() { Keyframes = [new() { Frame = 0, State = CropState.Identity(32, 24) with { Width = 16 } }] };
        if (effect) clip.VideoEffects = [VideoEffect.Create(VideoEffectKind.Invert)];
        const long frame = 99;
        var expected = reference.GetFrame(Source, Seconds(project.FrameRate, frame - 5, project.Media[0].DurationSeconds, true), 32, 24);
        if (effect) expected = VideoEffectProcessor.Apply(expected, 32, 24, clip.VideoEffects, frame);
        using var cpu = new TimelineRenderService(media, false); using var gpu = new TimelineRenderService(media);
        var actual = cpu.RenderFrame(project, frame, 32, 24); CenterEquals(expected, actual);
        Near(actual, gpu.RenderFrame(project, frame, 32, 24), 1); Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
        Assert.Equal(original, File.ReadAllBytes(Source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DissolveDecodesBothParticipantsAtTheirMappedTimes(bool transformed)
    {
        using var media = new MediaService(); using var reference = new MediaService();
        var (project, outgoing) = await Project(media, VideoSourceMode.Loop);
        outgoing.DurationFrames = 60; outgoing.FadeOutFrames = 30;
        var incoming = new TimelineClip { MediaId = outgoing.MediaId, StartFrame = 30, DurationFrames = 60, SourceInFrame = 60,
            VideoSourceMode = VideoSourceMode.HoldLastFrame, FadeInFrames = 30, FadeInTransition = new() };
        project.Tracks[0].Clips.Add(incoming);
        if (transformed)
        {
            outgoing.Crop = new() { Keyframes = [new() { Frame = 0, State = CropState.Identity(32, 24) with { Width = 16 } }] };
            incoming.Crop = outgoing.Crop.DeepClone();
            outgoing.VideoEffects = [VideoEffect.Create(VideoEffectKind.Invert)]; incoming.VideoEffects = [VideoEffect.Create(VideoEffectKind.Invert)];
        }
        const long frame = 45;
        var first = reference.GetFrame(Source, Seconds(project.FrameRate, 45, project.Media[0].DurationSeconds, true), 32, 24);
        var second = reference.GetFrame(Source, project.Media[0].DurationSeconds, 32, 24);
        if (transformed) { first = VideoEffectProcessor.Apply(first, 32, 24, outgoing.VideoEffects); second = VideoEffectProcessor.Apply(second, 32, 24, incoming.VideoEffects); }
        using var cpu = new TimelineRenderService(media, false); using var gpu = new TimelineRenderService(media);
        var actual = cpu.RenderFrame(project, frame, 32, 24); var offset = (12 * 32 + 16) * 4;
        for (var channel = 0; channel < 3; channel++) Assert.InRange(Math.Abs(actual[offset + channel] - (first[channel] + second[channel]) / 2.0), 0, 1);
        Near(actual, gpu.RenderFrame(project, frame, 32, 24), 1); Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
        Assert.Equal(original, File.ReadAllBytes(Source));
    }

    [Theory]
    [InlineData(VideoSourceMode.Loop)]
    [InlineData(VideoSourceMode.HoldLastFrame)]
    public async Task EncodedOutputUsesTheSameSourceMappingAsPreview(VideoSourceMode mode)
    {
        using var media = new MediaService();
        var (project, _) = await Project(media, mode, mode == VideoSourceMode.Loop ? -5 : 0); project.Tracks[0].Clips[0].DurationFrames = 90;
        using var render = new TimelineRenderService(media, false);
        var output = Path.Combine(directory, mode + ".mp4"); await new ExportService(render).ExportAsync(project, output);
        using var decoded = new MediaService();
        foreach (var frame in new long[] { 0, 1, 17, 44, 89 })
        {
            var expected = render.RenderFrame(project, frame, 32, 24);
            var actual = decoded.GetFrame(output, project.FrameRate.Seconds(frame), 32, 24);
            Near(expected, actual, 9);
        }
        Assert.Equal(original, File.ReadAllBytes(Source));
    }

    private static double Seconds(Rational rate, long frame, double duration, bool loop)
    {
        var time = (decimal)frame * rate.Denominator / rate.Numerator;
        if (!loop) return (double)Math.Clamp(time, 0, (decimal)duration);
        var remainder = time % (decimal)duration;
        return (double)(remainder < 0 ? remainder + (decimal)duration : remainder);
    }
    private static void Pixel(byte[] data, int blue, int green, int red)
    {
        Assert.Equal((byte)blue, data[0]); Assert.Equal((byte)green, data[1]); Assert.Equal((byte)red, data[2]); Assert.Equal((byte)255, data[3]);
    }
    private static void CenterEquals(byte[] expected, byte[] actual)
    {
        var offset = (12 * 32 + 16) * 4;
        for (var channel = 0; channel < 4; channel++) Assert.InRange(Math.Abs(expected[channel] - actual[offset + channel]), 0, 1);
    }
    private static void Near(byte[] expected, byte[] actual, int tolerance)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++) Assert.InRange(Math.Abs(expected[i] - actual[i]), 0, tolerance);
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
