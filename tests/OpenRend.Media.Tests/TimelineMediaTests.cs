using System.Diagnostics;
using System.Text.Json;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;
using Xunit.Abstractions;

namespace OpenRend.Media.Tests;

public sealed class MediaFixture : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "openrend-tests-" + Guid.NewGuid().ToString("N"));
    public string Source => Path.Combine(Directory, "colours and tones.mkv");
    public MediaFixture()
    {
        System.IO.Directory.CreateDirectory(Directory);
        Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=red:s=160x90:r=30:d=1",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=1",
            "-f", "lavfi", "-i", "color=blue:s=160x90:r=30:d=1",
            "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=1",
            "-filter_complex", "[0:v][1:a][2:v][3:a]concat=n=2:v=1:a=1[v][a]",
            "-map", "[v]", "-map", "[a]", "-c:v", "ffv1", "-c:a", "pcm_f32le", Source);
    }

    internal static string Run(string executable, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(MediaExecutables.Find(executable))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(60_000)) { process.Kill(true); throw new TimeoutException("Media command timed out."); }
        if (process.ExitCode != 0) throw new InvalidOperationException(error.GetAwaiter().GetResult());
        return output.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Directory, recursive: true); }
        catch (IOException) { }
    }
}

public sealed class TimelineMediaTests(MediaFixture fixture, ITestOutputHelper log) : IClassFixture<MediaFixture>
{
    [Fact]
    public async Task D3DCompositorMatchesCpuAcrossResizesAndConcurrentCallers()
    {
        using var media = new MediaService();
        var project = await EditedProject(media);
        project.Tracks[0].Clips[0].Opacity = .37;
        using var gpu = new TimelineRenderService(media, compositorCapacity: 2);
        using var cpu = new TimelineRenderService(media, useGpuCompositing: false);
        var sizes = new[] { (160, 90), (188, 100), (80, 44), (160, 90), (160, 92) };
        await Task.WhenAll(sizes.Select(size => Task.Run(() =>
        {
            var expected = cpu.RenderFrame(project, 35, size.Item1, size.Item2);
            var actual = gpu.RenderFrame(project, 35, size.Item1, size.Item2);
            Assert.Equal(expected.Length, actual.Length);
            for (var i = 0; i < expected.Length; i++) Assert.InRange(Math.Abs(expected[i] - actual[i]), 0, 1);
        })));
        Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
        Assert.False(cpu.IsD3DCompositing);
        log.WriteLine($"D3D compositor: {(gpu.IsHardwareCompositing ? "hardware" : "WARP")}; five output sizes/concurrent callers agree with CPU within one byte.");
    }

    [Fact]
    public async Task ProbeDecodeAndWaveformReadRealUnicodeMedia()
    {
        var unicode = Path.Combine(fixture.Directory, "média 日本語.mkv");
        File.Copy(fixture.Source, unicode, true);
        using var media = new MediaService(2);
        var asset = await media.ProbeAsync(unicode);
        Assert.True(asset.HasVideo);
        Assert.True(asset.HasAudio);
        Assert.Equal(160, asset.Width);
        Assert.Equal(90, asset.Height);
        Assert.InRange(asset.DurationSeconds, 1.99, 2.01);
        Assert.InRange(asset.FrameRate, 29.99, 30.01);
        Pixel(await media.GetFrameAsync(unicode, .2, 160, 90), red: 255, blue: 0);
        Pixel(await media.GetFrameAsync(unicode, 1.2, 160, 90), red: 0, blue: 255);
        Pixel(await media.GetFrameAsync(unicode, .1, 160, 90), red: 255, blue: 0);
        var waveform = await media.GetWaveformAsync(unicode, 32);
        Assert.Equal(32, waveform.Length);
        Assert.All(waveform, peak => Assert.InRange(peak, .07f, .13f));
    }

    [Fact]
    public async Task PreviewAndExportRespectOffsetTrimSplitLayersMuteGainAndPan()
    {
        using var media = new MediaService();
        var project = await EditedProject(media);
        using var renderer = new TimelineRenderService(media);
        Pixel(renderer.RenderFrame(project, 10, 160, 90), red: 0, blue: 0);
        Pixel(renderer.RenderFrame(project, 20, 160, 90), red: 0, blue: 255);
        Pixel(renderer.RenderFrame(project, 35, 160, 90), red: 128, blue: 128);
        Pixel(renderer.RenderFrame(project, 55, 160, 90), red: 128, blue: 0);
        var silent = renderer.RenderAudio(project, .1, 4096);
        Assert.All(silent, value => Assert.Equal(0, value));
        var mixed = renderer.RenderAudio(project, .7, 4096);
        Assert.InRange(Rms(mixed, 0), .02, .05);
        Assert.InRange(Rms(mixed, 1), 0, .0001);
        var destination = Path.Combine(fixture.Directory, "edited.mp4");
        var progress = new List<double>();
        await new ExportService(renderer).ExportAsync(project, destination, new InlineProgress(value => progress.Add(value)));
        Assert.Equal(1, progress[^1]);
        Assert.True(progress.SequenceEqual(progress.Order()));
        using var inspection = JsonDocument.Parse(MediaFixture.Run("ffprobe", "-v", "error", "-count_frames", "-show_streams", "-show_format", "-of", "json", destination));
        var streams = inspection.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video");
        Assert.Equal("h264", video.GetProperty("codec_name").GetString());
        Assert.Equal("60", video.GetProperty("nb_read_frames").GetString());
        Assert.Equal(160, video.GetProperty("width").GetInt32());
        Assert.Equal(90, video.GetProperty("height").GetInt32());
        Assert.Contains(streams, stream => stream.GetProperty("codec_name").GetString() == "aac");
        var exported = await media.ProbeAsync(destination);
        Assert.InRange(exported.DurationSeconds, 1.99, 2.06);
        Pixel(media.GetFrame(destination, 10.0 / 30, 160, 90), red: 0, blue: 0, tolerance: 12);
        Pixel(media.GetFrame(destination, 20.0 / 30, 160, 90), red: 0, blue: 255, tolerance: 12);
        Pixel(media.GetFrame(destination, 35.0 / 30, 160, 90), red: 128, blue: 128, tolerance: 12);
        Pixel(media.GetFrame(destination, 55.0 / 30, 160, 90), red: 128, blue: 0, tolerance: 12);
        var exportedAudio = media.GetAudio(destination, .7, 4096);
        Assert.InRange(Rms(exportedAudio, 0), .02, .05);
        Assert.InRange(Rms(exportedAudio, 1), 0, .003);
    }

    [Fact]
    public async Task FractionalAudioFadesAndVideoFadeUseTheSameEnvelopeAfterSplit()
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.Source);
        var project = new ProjectDocument { Width = 160, Height = 90, Media = [asset] };
        var editor = new TimelineEditor(project);
        var video = editor.AddTrack(TrackKind.Video);
        var audio = editor.AddTrack(TrackKind.Audio);
        var v = editor.PlaceClip(asset.Id, video.Id, 0, 30);
        var a = editor.PlaceClip(asset.Id, audio.Id, 0, 30);
        editor.SetClipLevels(v.Id, fadeInFrames: 30);
        editor.SetClipLevels(a.Id, fadeInFrames: 30);
        using var renderer = new TimelineRenderService(media);
        var beforeFrame = renderer.RenderFrame(project, 20, 160, 90);
        var beforeAudio = renderer.RenderAudio(project, .64, 4096);
        editor.SplitClip(v.Id, 15);
        editor.SplitClip(a.Id, 15);
        Assert.Equal(beforeFrame, renderer.RenderFrame(project, 20, 160, 90));
        var afterAudio = renderer.RenderAudio(project, .64, 4096);
        for (var i = 0; i < beforeAudio.Length; i++) Assert.InRange(Math.Abs(beforeAudio[i] - afterAudio[i]), 0, .00001f);
        Pixel(beforeFrame, red: 170, blue: 0);
    }

    [Fact]
    public async Task CancellationPreservesDestinationAndRemovesPartialOutput()
    {
        using var media = new MediaService();
        var project = await EditedProject(media);
        var destination = Path.Combine(fixture.Directory, "cancelled.mp4");
        byte[] original = [1, 2, 3, 4, 5];
        await File.WriteAllBytesAsync(destination, original);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(value => { if (value > .22) cancellation.Cancel(); });
        using var renderer = new TimelineRenderService(media);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ExportService(renderer).ExportAsync(project, destination, progress, cancellation.Token));
        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Empty(System.IO.Directory.GetFiles(fixture.Directory, ".openrend-*.mp4"));
    }

    [Theory]
    [InlineData(30000, 1001, 1)]
    [InlineData(30000, 1001, 47)]
    [InlineData(24000, 1001, 43)]
    public async Task FractionalRateExportKeepsTheCompleteAudioTail(int numerator, int denominator, long frames)
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.Source);
        var project = new ProjectDocument { Width = 160, Height = 90, FrameRate = new Rational(numerator, denominator), Media = [asset] };
        var editor = new TimelineEditor(project);
        editor.PlaceClip(asset.Id, editor.AddTrack(TrackKind.Video).Id, 0, frames);
        editor.PlaceClip(asset.Id, editor.AddTrack(TrackKind.Audio).Id, 0, frames);
        var destination = Path.Combine(fixture.Directory, $"fractional-{numerator}-{frames}.mp4");
        using var renderer = new TimelineRenderService(media);
        await new ExportService(renderer).ExportAsync(project, destination);
        using var inspection = JsonDocument.Parse(MediaFixture.Run("ffprobe", "-v", "error", "-count_frames", "-show_streams", "-of", "json", destination));
        var streams = inspection.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video");
        var audio = streams.Single(stream => stream.GetProperty("codec_type").GetString() == "audio");
        Assert.Equal(frames.ToString(), video.GetProperty("nb_read_frames").GetString());
        Assert.Equal("1/48000", audio.GetProperty("time_base").GetString());
        var expectedSamples = (long)Math.Ceiling(project.FrameRate.Seconds(frames) * MediaService.SampleRate);
        Assert.InRange(Math.Abs(audio.GetProperty("duration_ts").GetInt64() - expectedSamples), 0, 1);
        if (frames > 1)
        {
            var tail = media.GetAudio(destination, project.FrameRate.Seconds(frames) - .02, 480);
            Assert.InRange(Rms(tail, 0), .035, .1);
        }
    }

    [Fact]
    public async Task DecodeFailurePreservesDestination()
    {
        using var media = new MediaService();
        var project = await EditedProject(media);
        project.Media[0].Path = Path.Combine(fixture.Directory, "missing.mkv");
        var destination = Path.Combine(fixture.Directory, "failed.mp4");
        byte[] original = [8, 7, 6];
        await File.WriteAllBytesAsync(destination, original);
        using var renderer = new TimelineRenderService(media);
        await Assert.ThrowsAsync<MediaException>(() => new ExportService(renderer).ExportAsync(project, destination));
        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Empty(System.IO.Directory.GetFiles(fixture.Directory, ".openrend-*.mp4"));
    }

    private async Task<ProjectDocument> EditedProject(MediaService media)
    {
        var asset = await media.ProbeAsync(fixture.Source);
        var project = new ProjectDocument { Width = 160, Height = 90, FrameRate = new Rational(30, 1), Media = [asset] };
        var editor = new TimelineEditor(project);
        var top = editor.AddTrack(TrackKind.Video);
        var bottom = editor.AddTrack(TrackKind.Video);
        var audio = editor.AddTrack(TrackKind.Audio);
        var muted = editor.AddTrack(TrackKind.Audio);
        var under = editor.PlaceClip(asset.Id, bottom.Id, 15, 60);
        editor.TrimClip(under.Id, 30, 30);
        editor.SplitClip(under.Id, 30);
        var over = editor.PlaceClip(asset.Id, top.Id, 30, 30);
        editor.SetClipLevels(over.Id, opacity: .5);
        var sound = editor.PlaceClip(asset.Id, audio.Id, 15, 60);
        editor.TrimClip(sound.Id, 15, 30);
        editor.SetClipLevels(sound.Id, gain: .5, pan: -1);
        editor.SplitClip(sound.Id, 30);
        editor.PlaceClip(asset.Id, muted.Id, 0, 60);
        editor.SetTrackState(muted.Id, isMuted: true);
        return project;
    }

    private static void Pixel(byte[] bgra, int red, int blue, int tolerance = 4)
    {
        var pixel = bgra.Length / 8 / 4 * 4;
        Assert.InRange((int)bgra[pixel], Math.Max(0, blue - tolerance), Math.Min(255, blue + tolerance));
        Assert.InRange((int)bgra[pixel + 1], 0, tolerance);
        Assert.InRange((int)bgra[pixel + 2], Math.Max(0, red - tolerance), Math.Min(255, red + tolerance));
        Assert.Equal(255, bgra[pixel + 3]);
    }

    private static double Rms(float[] stereo, int channel)
    {
        var squares = 0.0;
        for (var i = channel; i < stereo.Length; i += 2) squares += stereo[i] * stereo[i];
        return Math.Sqrt(squares / (stereo.Length / 2));
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
