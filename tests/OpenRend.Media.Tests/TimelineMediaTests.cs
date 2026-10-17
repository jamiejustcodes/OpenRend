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
    public string DelayedAudio => Path.Combine(Directory, "delayed-audio.mkv");
    public string DelayedVideo => Path.Combine(Directory, "delayed-video.mkv");
    public string ResampleTail => Path.Combine(Directory, "tail-44100.wav");
    public string VariableRate => Path.Combine(Directory, "variable-rate.mkv");
    public string TimestampOffset => Path.Combine(Directory, "timestamp-offset.mkv");
    public string RotatedVideo => Path.Combine(Directory, "rotation-90.mp4");
    public string Quadrants => Path.Combine(Directory, "quadrants.mkv");
    public MediaFixture()
    {
        System.IO.Directory.CreateDirectory(Directory);
        Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=red:s=160x90:r=30:d=1",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=1",
            "-f", "lavfi", "-i", "color=blue:s=160x90:r=30:d=1",
            "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=1",
            "-filter_complex", "[0:v][1:a][2:v][3:a]concat=n=2:v=1:a=1[v][a]",
            "-map", "[v]", "-map", "[a]", "-c:v", "ffv1", "-c:a", "pcm_f32le", Source);
        Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=red:s=160x90:r=30:d=1",
            "-itsoffset", "0.25", "-f", "lavfi", "-i", "sine=frequency=997:sample_rate=48000:duration=0.75",
            "-map", "0:v", "-map", "1:a", "-c:v", "ffv1", "-c:a", "pcm_f32le", DelayedAudio);
        Run("ffmpeg", "-v", "error", "-y", "-itsoffset", "0.25", "-f", "lavfi", "-i", "color=red:s=160x90:r=20:d=0.75",
            "-f", "lavfi", "-i", "sine=frequency=997:sample_rate=48000:duration=1", "-map", "0:v", "-map", "1:a", "-fps_mode:v", "passthrough", "-enc_time_base:v", "1/1000", "-c:v", "ffv1", "-c:a", "pcm_f32le", DelayedVideo);
        Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=997:sample_rate=44100:duration=1", "-c:a", "pcm_f32le", ResampleTail);
        Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=red:s=32x16:r=25:d=0.04",
            "-f", "lavfi", "-i", "color=green:s=32x16:r=25:d=0.04", "-f", "lavfi", "-i", "color=blue:s=32x16:r=25:d=0.04",
            "-f", "lavfi", "-i", "color=white:s=32x16:r=25:d=0.04", "-filter_complex",
            "[0:v][1:v][2:v][3:v]concat=n=4:v=1:a=0,settb=1/1000,setpts='if(eq(N,0),0,if(eq(N,1),120,if(eq(N,2),520,560)))'[v]",
            "-map", "[v]", "-fps_mode", "vfr", "-c:v", "ffv1", VariableRate);
        Run("ffmpeg", "-v", "error", "-y", "-i", Source, "-c", "copy", "-output_ts_offset", "5", TimestampOffset);
        var coded = Path.Combine(Directory, "rotation-coded.mp4");
        Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=red:s=32x32:r=1:d=1", "-f", "lavfi", "-i", "color=blue:s=32x32:r=1:d=1",
            "-filter_complex", "[0:v][1:v]hstack[v]", "-map", "[v]", "-c:v", "libx264", "-pix_fmt", "yuv420p", coded);
        Run("ffmpeg", "-v", "error", "-y", "-display_rotation:v:0", "90", "-i", coded, "-c", "copy", RotatedVideo);
        var raw = Path.Combine(Directory, "quadrants.bgra");
        var pixels = new byte[64 * 48 * 4 * 30];
        for (var frame = 0; frame < 30; frame++)
            for (var y = 0; y < 48; y++)
                for (var x = 0; x < 64; x++)
                {
                    var p = ((frame * 48 + y) * 64 + x) * 4;
                    pixels[p] = y >= 24 ? (byte)255 : (byte)0;
                    pixels[p + 1] = x >= 32 ? (byte)255 : (byte)0;
                    pixels[p + 2] = y < 24 ? (byte)255 : (byte)0;
                    pixels[p + 3] = 255;
                }
        File.WriteAllBytes(raw, pixels);
        Run("ffmpeg", "-v", "error", "-y", "-f", "rawvideo", "-pixel_format", "bgra", "-video_size", "64x48", "-framerate", "30", "-i", raw, "-c:v", "ffv1", "-pix_fmt", "bgra", Quadrants);
    }

    internal static string Run(string executable, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(MediaExecutables.Find(executable))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
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
    public async Task CropSamplingMatchesCpuForZoomPanRotationPivotAndTransparentEdges()
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.Quadrants);
        var bottom = await media.ProbeAsync(fixture.Source);
        var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 30 };
        var project = new ProjectDocument { Width = 64, Height = 48, Media = [asset, bottom], Tracks =
            [new TimelineTrack { Kind = TrackKind.Video, Clips = [clip] },
             new TimelineTrack { Kind = TrackKind.Video, Clips = [new TimelineClip { MediaId = bottom.Id, SourceInFrame = 30, DurationFrames = 30 }] }] };
        using var gpu = new TimelineRenderService(media);
        using var cpu = new TimelineRenderService(media, useGpuCompositing: false);
        var states = new[]
        {
            new CropState(32,24,16,12,0,32,24),
            new CropState(64,48,48,24,0,32,24),
            new CropState(64,48,32,24,90,32,24),
            new CropState(39,29,30.25,19.5,17.7,13,31),
            new CropState(23,41,19,39,-36.2,32,24),
            new CropState(64,48,200,200,0,32,24)
        };
        clip.Opacity = .37;
        foreach (var state in states)
        {
            clip.Crop = new() { Keyframes = [new() { State = state }] };
            project.Validate();
            var expected = cpu.RenderFrame(project, 0, 77, 59);
            var actual = gpu.RenderFrame(project, 0, 77, 59);
            for (var p = 0; p < actual.Length; p++) Assert.InRange(Math.Abs(actual[p] - expected[p]), 0, 2);
        }
        Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
        clip.Opacity = 1;
        clip.Crop = new() { Keyframes = [new() { State = states[0] }] };
        var zoom = gpu.RenderFrame(project, 0, 64, 48);
        AssertRgb(zoom, 64, 16, 12, 255, 0, 0);
        clip.Crop = new() { Keyframes = [new() { State = states[1] }] };
        var pan = gpu.RenderFrame(project, 0, 64, 48);
        AssertRgb(pan, 64, 20, 5, 255, 255, 0);
        AssertRgb(pan, 64, 60, 20, 0, 0, 255);
        clip.Crop = new() { Keyframes = [new() { State = states[2] }] };
        var rotated = gpu.RenderFrame(project, 0, 64, 48);
        AssertRgb(rotated, 64, 15, 5, 255, 255, 0);
        AssertRgb(rotated, 64, 49, 5, 0, 255, 255);
        AssertRgb(rotated, 64, 15, 42, 255, 0, 0);
    }

    [Fact]
    public async Task AnimatedCropPreviewAndEncodedExportUseSameFramesAfterSplit()
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.Quadrants);
        var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 30, Crop = new() { Keyframes =
            [new() { State = new(32,24,16,12,0,32,24) }, new() { Frame = 15, State = new(32,24,48,36,0,32,24), Interpolation = CropInterpolation.Hold }] } };
        var project = new ProjectDocument { Width = 64, Height = 48, Media = [asset], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] };
        project.Validate();
        using var renderer = new TimelineRenderService(media);
        var before = renderer.RenderFrame(project, 20, 64, 48);
        AssertRgb(before, 64, 32, 24, 0, 255, 255);
        new TimelineEditor(project).SplitClip(clip.Id, 10);
        Assert.Equal(before, renderer.RenderFrame(project, 20, 64, 48));
        var destination = Path.Combine(fixture.Directory, "animated-crop.mp4");
        await new ExportService(renderer).ExportAsync(project, destination);
        AssertRgb(media.GetFrame(destination, 0, 64, 48), 64, 16, 12, 255, 0, 0, 8);
        AssertRgb(media.GetFrame(destination, 20.0 / 30, 64, 48), 64, 32, 24, 0, 255, 255, 8);
    }

    [Theory]
    [InlineData(CropInterpolation.Linear, 48, 16)]
    [InlineData(CropInterpolation.Hold, 64, 64)]
    [InlineData(CropInterpolation.Slow, 60, 28)]
    [InlineData(CropInterpolation.Fast, 36, 4)]
    [InlineData(CropInterpolation.Smooth, 54, 10)]
    [InlineData(CropInterpolation.Sharp, 36, 28)]
    public async Task CropEasingMovesRealImageBoundaryInPreviewAndEncodedOutput(CropInterpolation interpolation, int quarterEdge, int threeQuarterEdge)
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.Quadrants);
        var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 30, Crop = new() { Keyframes =
            [new() { State = new(32,24,16,12,0,32,24), Interpolation = interpolation },
             new() { Frame = 24, State = new(32,24,48,12,0,32,24) }] } };
        var project = new ProjectDocument { Width = 64, Height = 48, Media = [asset], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] };
        project.Validate();
        using var gpu = new TimelineRenderService(media);
        using var cpu = new TimelineRenderService(media, useGpuCompositing: false);
        var probes = new[] { (Frame: 6, Edge: quarterEdge), (Frame: 18, Edge: threeQuarterEdge) };
        foreach (var probe in probes)
        {
            var actual = gpu.RenderFrame(project, probe.Frame, 64, 48);
            var fallback = cpu.RenderFrame(project, probe.Frame, 64, 48);
            Assert.Equal(probe.Edge, GreenEdge(actual));
            for (var p = 0; p < actual.Length; p++) Assert.InRange(Math.Abs(actual[p] - fallback[p]), 0, 1);
        }
        Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
        var destination = Path.Combine(fixture.Directory, $"crop-easing-{interpolation}.mp4");
        await new ExportService(gpu).ExportAsync(project, destination);
        foreach (var probe in probes)
        {
            var encoded = media.GetFrame(destination, project.FrameRate.Seconds(probe.Frame), 64, 48);
            Assert.InRange(GreenEdge(encoded), Math.Max(0, probe.Edge - 2), Math.Min(64, probe.Edge + 2));
        }

        // The moving red/yellow boundary measures geometry directly; no evaluator values are reused.
        static int GreenEdge(byte[] pixels)
        {
            for (var x = 0; x < 64; x++) if (pixels[(12 * 64 + x) * 4 + 1] >= 128) return x;
            return 64;
        }
    }

    [Fact]
    public async Task CropCoordinatesUseDisplayedDimensionsAfterQuarterTurnMetadata()
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.RotatedVideo);
        var source = media.GetFrame(asset.Path, 0, 32, 64);
        var project = new ProjectDocument { Width = 32, Height = 32, Media = [asset], Tracks =
            [new() { Kind = TrackKind.Video, Clips = [new() { MediaId = asset.Id, DurationFrames = 30, Crop = new() { Keyframes =
                [new() { State = new(32,32,16,16,0,16,32) }] } }] }] };
        project.Validate();
        using var gpu = new TimelineRenderService(media);
        using var cpu = new TimelineRenderService(media, useGpuCompositing: false);
        var actual = gpu.RenderFrame(project, 0, 32, 32);
        var fallback = cpu.RenderFrame(project, 0, 32, 32);
        for (var p = 0; p < actual.Length; p++)
        {
            Assert.InRange(Math.Abs(source[p] - actual[p]), 0, 1);
            Assert.InRange(Math.Abs(fallback[p] - actual[p]), 0, 1);
        }
    }

    [Fact]
    public async Task MissingLegacyDimensionsRenderOriginalAndCroppedSourcesWithoutChangingMetadata()
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.Quadrants);
        asset.Width = 0;
        asset.Height = 0;
        var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 30 };
        var project = new ProjectDocument { Width = 64, Height = 48, Media = [asset], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] };
        project.Validate();
        using var gpu = new TimelineRenderService(media);
        using var cpu = new TimelineRenderService(media, useGpuCompositing: false);
        foreach (var cropped in new[] { false, true })
        {
            clip.Crop = cropped ? new() { Keyframes = [new() { State = new(32,24,48,36,0,32,24) }] } : null;
            var actual = gpu.RenderFrame(project, 0, 64, 48);
            var fallback = cpu.RenderFrame(project, 0, 64, 48);
            AssertRgb(actual, 64, 16, 12, cropped ? 0 : 255, cropped ? 255 : 0, cropped ? 255 : 0);
            for (var p = 0; p < actual.Length; p++) Assert.InRange(Math.Abs(actual[p] - fallback[p]), 0, 1);
            Assert.Equal(0, asset.Width);
            Assert.Equal(0, asset.Height);
        }
    }

    private static void AssertRgb(byte[] pixels, int width, int x, int y, int red, int green, int blue, int tolerance = 1)
    {
        var p = (y * width + x) * 4;
        Assert.InRange((int)pixels[p], blue - tolerance, blue + tolerance);
        Assert.InRange((int)pixels[p + 1], green - tolerance, green + tolerance);
        Assert.InRange((int)pixels[p + 2], red - tolerance, red + tolerance);
        Assert.Equal(255, pixels[p + 3]);
    }

    [Fact]
    public async Task TrackLevelsAffectActualPixelsStereoSamplesAndEncodedOutput()
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.Source);
        var video = new TimelineTrack { Kind = TrackKind.Video, Opacity = .5, Clips = [new TimelineClip { MediaId = asset.Id, DurationFrames = 30, Opacity = .5 }] };
        var audio = new TimelineTrack { Kind = TrackKind.Audio, Gain = .5, Pan = .5, Clips = [new TimelineClip { MediaId = asset.Id, DurationFrames = 30, Gain = .8, Pan = -.5 }] };
        var project = new ProjectDocument { Width = 160, Height = 90, Media = [asset], Tracks = [video, audio] };
        project.Validate();
        using var renderer = new TimelineRenderService(media);
        Pixel(renderer.RenderFrame(project, 10, 160, 90), red: 64, blue: 0);
        var source = media.GetAudio(asset.Path, .25, 4096);
        var actual = renderer.RenderAudio(project, .25, 4096);
        for (var sample = 0; sample < actual.Length; sample++) Assert.InRange(Math.Abs(source[sample] * .2 - actual[sample]), 0, .000001);
        var destination = Path.Combine(fixture.Directory, "track-levels.mp4");
        await new ExportService(renderer).ExportAsync(project, destination);
        Pixel(media.GetFrame(destination, .25, 160, 90), red: 64, blue: 0, tolerance: 8);
        var encoded = media.GetAudio(destination, .25, 4096);
        foreach (var channel in new[] { 0, 1 }) Assert.InRange(Rms(encoded, channel) / Rms(source, channel), .17, .23);
        audio.Pan = -1; audio.Clips[0].Pan = 1;
        Assert.All(renderer.RenderAudio(project, .25, 4096), sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void DelayedAudioKeepsItsContainerOffsetAndLeadingSilence()
    {
        using var media = new MediaService();
        var leading = media.GetAudio(fixture.DelayedAudio, 0, 9600);
        Assert.InRange(Rms(leading, 0), 0, .000001);
        var boundary = media.GetAudio(fixture.DelayedAudio, .2, 4800);
        Assert.All(boundary.Take(2400 * 2), sample => Assert.InRange(Math.Abs(sample), 0, .000001));
        Assert.InRange(Rms(boundary.Skip(2400 * 2).ToArray(), 0), .04, .1);
        Assert.InRange(Rms(media.GetAudio(fixture.DelayedAudio, .4, 4096), 0), .04, .1);
    }

    [Fact]
    public void DelayedVideoDoesNotDisplayItsFirstFrameBeforeItsPresentationTime()
    {
        using var media = new MediaService();
        var firstTimestamp = MediaFixture.Run("ffprobe", "-v", "error", "-select_streams", "v", "-show_entries", "frame=best_effort_timestamp_time", "-of", "csv=p=0", fixture.DelayedVideo).Split('\n')[0];
        Assert.Equal(.25, double.Parse(firstTimestamp, System.Globalization.CultureInfo.InvariantCulture), 3);
        foreach (var seconds in new[] { 0, .1, .249, .6, .2, .25 })
        {
            Pixel(media.GetFrame(fixture.DelayedVideo, seconds, 160, 90), red: seconds < .25 ? 0 : 255, blue: 0);
        }
        Assert.InRange(Rms(media.GetAudio(fixture.DelayedVideo, .05, 4096), 0), .04, .1);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    [InlineData(-90)]
    public async Task QuarterTurnDisplayRotationMatchesFfmpegAutorotation(int angle)
    {
        var rotated = fixture.RotatedVideo;
        if (angle != 90)
        {
            rotated = Path.Combine(fixture.Directory, $"rotation-{angle}.mp4");
            MediaFixture.Run("ffmpeg", "-v", "error", "-y", "-display_rotation:v:0", angle.ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", Path.Combine(fixture.Directory, "rotation-coded.mp4"), "-c", "copy", rotated);
        }
        using var inspection = JsonDocument.Parse(MediaFixture.Run("ffprobe", "-v", "error", "-select_streams", "v:0", "-show_streams", "-of", "json", rotated));
        var stream = inspection.RootElement.GetProperty("streams")[0];
        var matrix = stream.GetProperty("side_data_list").EnumerateArray().Single(item => item.GetProperty("side_data_type").GetString() == "Display Matrix");
        Assert.Equal((angle % 360 + 360) % 360, (matrix.GetProperty("rotation").GetInt32() % 360 + 360) % 360);
        using var media = new MediaService();
        var asset = await media.ProbeAsync(rotated);
        var portrait = angle % 180 != 0;
        Assert.Equal(portrait ? 32 : 64, asset.Width);
        Assert.Equal(portrait ? 64 : 32, asset.Height);
        var reference = Path.Combine(fixture.Directory, $"rotation-reference-{angle}.bgra");
        MediaFixture.Run("ffmpeg", "-v", "error", "-y", "-i", rotated, "-frames:v", "1", "-pix_fmt", "bgra", "-f", "rawvideo", reference);
        var expected = await File.ReadAllBytesAsync(reference);
        var actual = media.GetFrame(rotated, 0, asset.Width, asset.Height);
        Assert.Equal(expected.Length, actual.Length);
        for (var channel = 0; channel < expected.Length; channel++) Assert.InRange(Math.Abs(expected[channel] - actual[channel]), 0, 2);
    }

    [Fact]
    public async Task ResamplingKeepsTheLast44100HzSamples()
    {
        using var media = new MediaService();
        var reference = Path.Combine(fixture.Directory, "tail-reference.f32le");
        MediaFixture.Run("ffmpeg", "-v", "error", "-y", "-i", fixture.ResampleTail, "-f", "f32le", "-ar", "48000", "-ac", "2", reference);
        var expectedBytes = await File.ReadAllBytesAsync(reference);
        var expected = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(expectedBytes.AsSpan()).ToArray();
        var actual = media.GetAudio(fixture.ResampleTail, 0, 48064);
        Assert.Equal(48000 * 2, expected.Length);
        for (var sample = expected.Length - 256; sample < expected.Length; sample++)
            Assert.InRange(Math.Abs(expected[sample] - actual[sample]), 0, .00001f);
        Assert.InRange(Rms(actual.Skip(47968 * 2).Take(32 * 2).ToArray(), 0), .03, .1);
        Assert.All(actual.Skip(48000 * 2), sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void VariableRateVideoHoldsPreviousFrameUntilTheNextPresentationTimestamp()
    {
        using var media = new MediaService();
        foreach (var seconds in new[] { .12, .2, .5, .519, .15 })
        {
            var pixels = media.GetFrame(fixture.VariableRate, seconds, 32, 16);
            Assert.InRange((int)pixels[0], 0, 2);
            Assert.InRange((int)pixels[1], 125, 130);
            Assert.InRange((int)pixels[2], 0, 2);
        }
        Pixel(media.GetFrame(fixture.VariableRate, .52, 32, 16), red: 0, blue: 255);
        Pixel(media.GetFrame(fixture.VariableRate, .119, 32, 16), red: 255, blue: 0);
    }

    [Fact]
    public async Task CommonContainerTimestampOffsetDoesNotAddFiveSecondsToMedia()
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.TimestampOffset);
        Assert.InRange(asset.DurationSeconds, 1.99, 2.01);
        Pixel(media.GetFrame(fixture.TimestampOffset, .2, 160, 90), red: 255, blue: 0);
        Pixel(media.GetFrame(fixture.TimestampOffset, 1.2, 160, 90), red: 0, blue: 255);
        Assert.InRange(Rms(media.GetAudio(fixture.TimestampOffset, .2, 4096), 0), .04, .1);
    }

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

    [Theory]
    [InlineData(30, 1, 33, 15)]
    [InlineData(30000, 1001, 33, 17)]
    [InlineData(24000, 1001, 27, 13)]
    public async Task RenderRangeStartsVideoAndAudioAtTheRequestedProjectFrame(int numerator, int denominator, long startFrame, long frames)
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.Source);
        var project = new ProjectDocument { Width = 160, Height = 90, FrameRate = new Rational(numerator, denominator), Media = [asset] };
        var editor = new TimelineEditor(project);
        var duration = project.FrameRate.Frames(asset.DurationSeconds);
        editor.PlaceClip(asset.Id, editor.AddTrack(TrackKind.Video).Id, 0, duration);
        editor.PlaceClip(asset.Id, editor.AddTrack(TrackKind.Audio).Id, 0, duration);
        using var renderer = new TimelineRenderService(media, false);
        var destination = Path.Combine(fixture.Directory, $"range-{numerator}.mp4");
        await new ExportService(renderer).ExportAsync(project, destination, new ExportOptions { Range = new FrameRange(startFrame, frames),
            TemplateId = numerator == 24000 ? "project-mp4-high" : RenderTemplateCatalog.DefaultId });
        using var inspection = JsonDocument.Parse(MediaFixture.Run("ffprobe", "-v", "error", "-count_frames", "-show_streams", "-of", "json", destination));
        var streams = inspection.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video");
        var audio = streams.Single(stream => stream.GetProperty("codec_type").GetString() == "audio");
        Assert.Equal(frames.ToString(), video.GetProperty("nb_read_frames").GetString());
        Assert.Equal(project.FrameRate.ToString(), video.GetProperty("r_frame_rate").GetString());
        var expectedSamples = (long)decimal.Ceiling((decimal)frames * denominator * 48000 / numerator);
        Assert.InRange(Math.Abs(audio.GetProperty("duration_ts").GetInt64() - expectedSamples), 0, 1);
        Pixel(media.GetFrame(destination, 0, 160, 90), red: 0, blue: 255, tolerance: 10);
        var samples = media.GetAudio(destination, .1, 4096);
        double ToneEnergy(int frequency)
        {
            double real = 0, imaginary = 0;
            for (var i = 0; i < samples.Length / 2; i++)
            {
                var angle = 2 * Math.PI * frequency * i / MediaService.SampleRate;
                real += samples[i * 2] * Math.Cos(angle);
                imaginary += samples[i * 2] * Math.Sin(angle);
            }
            return real * real + imaginary * imaginary;
        }
        Assert.True(ToneEnergy(880) > 10 * ToneEnergy(440), "The range must start in the second source tone.");
        Assert.Equal(duration, project.DurationFrames);
        Assert.Equal(0, project.Tracks[0].Clips[0].StartFrame);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(59, 2)]
    [InlineData(long.MaxValue, 1)]
    public void InvalidRenderRangesFailBeforeCreatingOutput(long start, long count)
    {
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, false);
        var asset = new MediaAsset { Path = "openrend://color?hex=#FF0000", DurationSeconds = 2, HasVideo = true };
        var project = new ProjectDocument { Media = [asset], Tracks = [new() { Kind = TrackKind.Video,
            Clips = [new() { MediaId = asset.Id, DurationFrames = 60 }] }] };
        var path = Path.Combine(fixture.Directory, "invalid-range.mp4");
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new ExportService(renderer).ExportAsync(project, path,
            new ExportOptions { Range = new FrameRange(start, count) }); });
        Assert.False(File.Exists(path));
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
