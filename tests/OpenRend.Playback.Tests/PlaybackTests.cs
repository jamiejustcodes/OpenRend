using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using Xunit;
using Xunit.Abstractions;

namespace OpenRend.Playback.Tests;

public sealed class PlaybackTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData(.25)]
    [InlineData(-.25)]
    [InlineData(.5)]
    [InlineData(.75)]
    [InlineData(-1)]
    [InlineData(-1.5)]
    [InlineData(2)]
    [InlineData(3.25)]
    [InlineData(-4)]
    [InlineData(16)]
    public void ShuttleResamplingKeepsTheStereoSampleGridAcrossBlocks(double rate)
    {
        const int duration = 19;
        var whole = Resample(0, duration);
        var first = Resample(0, 7);
        var second = Resample(7, duration - 7);
        Assert.Equal(whole, first.Concat(second));

        var averageOffset = Math.Sign(rate) * (Math.Max(1, (int)Math.Abs(rate)) - 1) / 2.0;
        for (var frame = 0; frame < duration; frame++)
        {
            var expected = frame * rate - (rate < 0 ? 1 : 0) + averageOffset;
            Assert.Equal(expected, whole[frame * 2]);
            Assert.Equal(-expected, whole[frame * 2 + 1]);
        }

        float[] Resample(long submitted, int count)
        {
            var window = PlaybackAudioResampling.Window(submitted, count, rate);
            var input = new float[window.FrameCount * 2];
            for (var frame = 0; frame < window.FrameCount; frame++)
            {
                input[frame * 2] = window.StartFrame + frame;
                input[frame * 2 + 1] = -input[frame * 2];
            }
            var samples = new float[count * 2];
            PlaybackAudioResampling.Resample(input, window.FirstSample, rate, samples, count);
            return samples;
        }
    }

    [Fact]
    public void ProviderClockFreezesAcrossInsertedSilenceAndResumesAtTheNextSourceSample()
    {
        var buffer = new TimelineAudioBuffer(() => 1);
        var bytes = new byte[480 * 8];
        buffer.Read(bytes, 0, bytes.Length);
        Assert.Equal(0, buffer.PlayedSourceSamples(480));
        buffer.AddSamples(new byte[960 * 8]);
        buffer.Read(bytes, 0, bytes.Length);
        Assert.Equal(240, buffer.PlayedSourceSamples(720));
        Assert.Equal(480, buffer.PlayedSourceSamples(960));
        buffer.Read(bytes, 0, bytes.Length);
        Assert.Equal(960, buffer.PlayedSourceSamples(1440));
        buffer.Read(bytes, 0, bytes.Length);
        Assert.Equal(960, buffer.PlayedSourceSamples(1920));
    }

    [Fact]
    public void ProviderClockAccountsForDeviceUnderrunAndAppliesGainWhenAudioIsRead()
    {
        double gain = .5;
        long device = 0;
        var buffer = new TimelineAudioBuffer(() => gain) { DevicePosition = () => device };
        var samples = Enumerable.Repeat(.8f, 960 * 2).ToArray();
        buffer.AddSamples(MemoryMarshal.AsBytes(samples.AsSpan()).ToArray());
        var bytes = new byte[480 * 8];
        buffer.Read(bytes, 0, bytes.Length);
        Assert.All(MemoryMarshal.Cast<byte, float>(bytes.AsSpan()).ToArray(), sample => Assert.Equal(.4f, sample));
        device = 960;
        Assert.Equal(480, buffer.PlayedSourceSamples(device));
        gain = .25;
        buffer.Read(bytes, 0, bytes.Length);
        Assert.All(MemoryMarshal.Cast<byte, float>(bytes.AsSpan()).ToArray(), sample => Assert.Equal(.2f, sample));
        Assert.Equal(720, buffer.PlayedSourceSamples(1200));
        Assert.Equal(960, buffer.PlayedSourceSamples(1440));
    }

    [Fact]
    public void LoopClockWrapsAtTheSameFractionalRateSamplesAsTheRenderer()
    {
        var loop = new TimelinePlaybackLoop(new FrameRange(10, 7), new Rational(30000, 1001), 13);
        Assert.Equal(11212, loop.SampleCount);
        var (offset, wrap) = loop.Window(0, int.MaxValue);
        Assert.Equal(4804.8, offset, 8);
        Assert.Equal(6407, wrap);
        Assert.Equal(13, loop.FrameAt(0));
        Assert.Equal(16, loop.FrameAt(wrap - 1));
        Assert.Equal(10, loop.FrameAt(wrap));
        const long elapsedOver100000Cycles = 1_121_120_000;
        Assert.Equal(loop.Window(wrap, int.MaxValue), loop.Window(wrap + elapsedOver100000Cycles, int.MaxValue));
        Assert.Equal(10, loop.FrameAt(wrap + elapsedOver100000Cycles));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(19)]
    [Trait("Category", "AudioDevice")]
    public async Task WasapiRepeatsTheSelectedRangeWithoutSilentBuffers(int frameCount)
    {
        var path = Path.Combine(Path.GetTempPath(), $"openrend-loop-{Guid.NewGuid():N}.wav");
        try
        {
            WriteStereoTone(path);
            using var media = new MediaService();
            using var renderer = new TimelineRenderService(media);
            var asset = await media.ProbeAsync(path);
            var range = new FrameRange(10, frameCount);
            var project = new ProjectDocument
            {
                FrameRate = new Rational(30000, 1001), Media = [asset],
                Tracks = [new TimelineTrack { Kind = TrackKind.Audio, Clips = [new TimelineClip
                {
                    MediaId = asset.Id, StartFrame = range.StartFrame, DurationFrames = range.FrameCount
                }] }]
            };
            using var capture = new WasapiLoopbackCapture();
            var recorded = new ConcurrentQueue<byte[]>();
            capture.DataAvailable += (_, args) => recorded.Enqueue(args.Buffer.AsSpan(0, args.BytesRecorded).ToArray());
            await using var playback = new PlaybackSession(renderer, project, 13, startImmediately: false, loopRange: range);
            Assert.Equal(13, playback.CurrentFrame);
            capture.StartRecording();
            playback.Start();
            var timer = Stopwatch.StartNew();
            var wraps = 0;
            var previous = 13L;
            while (timer.Elapsed < TimeSpan.FromSeconds(1.8))
            {
                await Task.Delay(10);
                await playback.CheckAsync();
                var frame = playback.CurrentFrame;
                Assert.False(playback.IsFinished);
                Assert.InRange(frame, range.StartFrame, range.EndFrame - 1);
                if (frame < previous) wraps++;
                previous = frame;
            }
            capture.StopRecording();
            Assert.True(wraps >= 2, $"The audio clock only wrapped {wraps} times.");
            Assert.Equal(32, capture.WaveFormat.BitsPerSample);
            var samples = MemoryMarshal.Cast<byte, float>(recorded.SelectMany(chunk => chunk).ToArray().AsSpan()).ToArray();
            var channels = capture.WaveFormat.Channels;
            var window = capture.WaveFormat.SampleRate * channels / 200;
            var active = samples.Chunk(window).Select(chunk => chunk.Max(sample => Math.Abs(sample)) > .01f).ToArray();
            var first = Array.IndexOf(active, true);
            var last = Array.LastIndexOf(active, true);
            Assert.True(first >= 0 && last - first > 250, "Loopback did not capture enough playback.");
            Assert.DoesNotContain(false, active[first..(last + 1)]);
            log.WriteLine($"Observed {wraps} wraps and {(last - first + 1) * 5} ms of continuous loopback audio.");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait("Category", "AudioDevice")]
    public async Task ActualWasapiClockSeeksPausesAndDrainsTheFinalHardwareBuffer()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        log.WriteLine($"Audio endpoint: {device.FriendlyName}; mix format: {device.AudioClient.MixFormat}");
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media);
        var asset = new MediaAsset { Path = Path.Combine(Path.GetTempPath(), "unused-silent-project.wav"), DurationSeconds = 2 };
        var project = new ProjectDocument
        {
            FrameRate = new Rational(30, 1),
            Media = [asset],
            Tracks = [new TimelineTrack { Kind = TrackKind.Audio, IsMuted = true, Clips = [new TimelineClip { MediaId = asset.Id, DurationFrames = 60 }] }]
        };
        var timer = Stopwatch.StartNew();
        var session = new PlaybackSession(renderer, project, 15);
        var first = session.CurrentFrame;
        Assert.InRange(first, 15, 16);
        await Task.Delay(260);
        await session.CheckAsync();
        var paused = session.CurrentFrame;
        Assert.InRange(paused, 20, 25);
        session.Dispose();
        var stopped = session.CurrentFrame;
        await Task.Delay(140);
        Assert.Equal(stopped, session.CurrentFrame);
        log.WriteLine($"Seek origin15, frame after260ms={paused}; stopped frame={stopped}; elapsed={timer.ElapsedMilliseconds}ms");
        await using var resumed = new PlaybackSession(renderer, project, 48);
        Assert.InRange(resumed.CurrentFrame, 48, 49);
        var drain = Stopwatch.StartNew();
        while (!resumed.IsFinished && drain.Elapsed < TimeSpan.FromSeconds(3))
        {
            await resumed.CheckAsync();
            await Task.Delay(5);
        }
        Assert.True(resumed.IsFinished);
        Assert.Equal(60, resumed.CurrentFrame);
        Assert.InRange(drain.ElapsedMilliseconds, 340, 1000);
        log.WriteLine($"Final12 frames drained in {drain.ElapsedMilliseconds}ms and clock reached {resumed.CurrentFrame}.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-.5)]
    [Trait("Category", "AudioDevice")]
    public async Task WasapiLoopbackContainsTheTimelineTone(double speed)
    {
        var path = Path.Combine(Path.GetTempPath(), $"openrend-playback-{Guid.NewGuid():N}.wav");
        try
        {
            WriteStereoTone(path);
            using var media = new MediaService();
            using var renderer = new TimelineRenderService(media);
            var asset = await media.ProbeAsync(path);
            var project = new ProjectDocument
            {
                FrameRate = new Rational(30, 1),
                Media = [asset],
                Tracks = [new TimelineTrack { Kind = TrackKind.Audio, Clips = [new TimelineClip { MediaId = asset.Id, DurationFrames = 24 }] }]
            };
            using var capture = new WasapiLoopbackCapture();
            var recorded = new ConcurrentQueue<byte[]>();
            capture.DataAvailable += (_, args) => recorded.Enqueue(args.Buffer.AsSpan(0, args.BytesRecorded).ToArray());
            var origin = speed > 0 ? 0 : project.DurationFrames;
            await using (var playback = new PlaybackSession(renderer, project, origin, .6, speed, startImmediately: false))
            {
                Assert.Equal(origin, playback.CurrentFrame);
                capture.StartRecording();
                playback.Start();
                var timeout = Stopwatch.StartNew();
                while (!playback.IsFinished && timeout.Elapsed < TimeSpan.FromSeconds(3))
                {
                    await playback.CheckAsync();
                    await Task.Delay(10);
                }
                Assert.True(playback.IsFinished);
                Assert.Equal(speed > 0 ? project.DurationFrames : 0, playback.CurrentFrame);
                await Task.Delay(100);
            }
            capture.StopRecording();
            var bytes = recorded.SelectMany(chunk => chunk).ToArray();
            Assert.NotEmpty(bytes);
            Assert.Equal(32, capture.WaveFormat.BitsPerSample);
            var captured = MemoryMarshal.Cast<byte, float>(bytes.AsSpan()).ToArray();
            var channels = capture.WaveFormat.Channels;
            var rate = capture.WaveFormat.SampleRate;
            var frequency = 1733 * Math.Abs(speed);
            var tone = Amplitude(captured, channels, rate, frequency);
            var neighbour = Math.Max(Amplitude(captured, channels, rate, 1400 * Math.Abs(speed)),
                Amplitude(captured, channels, rate, 2100 * Math.Abs(speed)));
            log.WriteLine($"Loopback {rate}Hz/{channels}ch, samples={captured.Length / channels}, tone{frequency} amplitude={tone:F5}, neighbour={neighbour:F5}");
            Assert.True(tone > .005, $"Timeline tone missing from loopback: amplitude={tone}");
            Assert.True(tone > neighbour * 5);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait("Category", "AudioDevice")]
    public async Task WasapiLoopbackPlaysTheMarkedAudioRangeFromItsSourceInPoint()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openrend-trimmer-audio-{Guid.NewGuid():N}.wav");
        try
        {
            WriteStereoTone(path, silentLeadFrames: 24000);
            using var media = new MediaService();
            using var renderer = new TimelineRenderService(media);
            var asset = await media.ProbeAsync(path);
            var range = new FrameRange(15, 12);
            var preview = MediaService.CreateSourcePreview(asset, new Rational(30, 1), range);
            var eventClip = Assert.Single(Assert.Single(preview.Tracks).Clips);
            Assert.Equal((15L, 12L), (eventClip.SourceInFrame, eventClip.DurationFrames));

            using var capture = new WasapiLoopbackCapture();
            var recorded = new ConcurrentQueue<byte[]>();
            capture.DataAvailable += (_, args) => recorded.Enqueue(args.Buffer.AsSpan(0, args.BytesRecorded).ToArray());
            await using (var playback = new PlaybackSession(renderer, preview, 0, .6, startImmediately: false,
                loopRange: new FrameRange(0, preview.DurationFrames)))
            {
                capture.StartRecording();
                playback.Start();
                await Task.Delay(900);
                await playback.CheckAsync();
                Assert.False(playback.IsFinished);
                Assert.InRange(playback.CurrentFrame, 0, range.FrameCount - 1);
            }
            capture.StopRecording();
            var recordedBytes = recorded.SelectMany(chunk => chunk).ToArray();
            Assert.NotEmpty(recordedBytes);
            Assert.Equal(32, capture.WaveFormat.BitsPerSample);
            var samples = MemoryMarshal.Cast<byte, float>(recordedBytes.AsSpan()).ToArray();
            var tone = Amplitude(samples, capture.WaveFormat.Channels, capture.WaveFormat.SampleRate, 1733);
            Assert.True(tone > .005, $"The tone after the source In point was missing. Amplitude {tone:F5}.");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait("Category", "AudioDevice")]
    public async Task TimelineScrubOutputsRecentAudioAndDisposesItsDevice()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openrend-scrub-{Guid.NewGuid():N}.wav");
        try
        {
            WriteStereoTone(path);
            using var media = new MediaService();
            using var renderer = new TimelineRenderService(media);
            var asset = await media.ProbeAsync(path);
            var project = new ProjectDocument
            {
                FrameRate = new Rational(30, 1), Media = [asset],
                Tracks = [new TimelineTrack { Kind = TrackKind.Audio, Clips = [new TimelineClip { MediaId = asset.Id, DurationFrames = 30 }] }]
            };
            using var capture = new WasapiLoopbackCapture();
            var recorded = new ConcurrentQueue<byte[]>();
            capture.DataAvailable += (_, args) => recorded.Enqueue(args.Buffer.AsSpan(0, args.BytesRecorded).ToArray());
            capture.StartRecording();
            await using (var scrub = new TimelineScrubSession(renderer, project, Task.CompletedTask))
            {
                for (var frame = 14; frame >= 4; frame--)
                {
                    Assert.True(scrub.Queue(frame, -.75, .75));
                    await Task.Delay(35);
                }
                await Task.Delay(60);
            }
            capture.StopRecording();
            var samples = MemoryMarshal.Cast<byte, float>(recorded.SelectMany(chunk => chunk).ToArray().AsSpan()).ToArray();
            Assert.Contains(samples, sample => Math.Abs(sample) > .01f);
        }
        finally { File.Delete(path); }
    }

    private static void WriteStereoTone(string path, int silentLeadFrames = 0)
    {
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        var samples = new float[48000 * 2];
        for (var frame = silentLeadFrames; frame < 48000; frame++)
            samples[frame * 2] = samples[frame * 2 + 1] = (float)(.12 * Math.Sin(2 * Math.PI * 1733 * frame / 48000));
        writer.WriteSamples(samples, 0, samples.Length);
    }

    private static double Amplitude(float[] samples, int channels, int rate, double frequency)
    {
        double real = 0, imaginary = 0;
        var frames = samples.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var angle = 2 * Math.PI * frequency * frame / rate;
            real += samples[frame * channels] * Math.Cos(angle);
            imaginary += samples[frame * channels] * Math.Sin(angle);
        }
        return 2 * Math.Sqrt(real * real + imaginary * imaginary) / frames;
    }
}
