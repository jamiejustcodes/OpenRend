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

    [Fact]
    [Trait("Category", "AudioDevice")]
    public async Task WasapiLoopbackContainsTheTimelineTone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openrend-playback-{Guid.NewGuid():N}.wav");
        try
        {
            using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)))
            {
                var samples = new float[48000 * 2];
                for (var frame = 0; frame < 48000; frame++)
                    samples[frame * 2] = samples[frame * 2 + 1] = (float)(.12 * Math.Sin(2 * Math.PI * 1733 * frame / 48000));
                writer.WriteSamples(samples, 0, samples.Length);
            }
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
            capture.StartRecording();
            await using (var playback = new PlaybackSession(renderer, project, 0, .6))
            {
                var timeout = Stopwatch.StartNew();
                while (!playback.IsFinished && timeout.Elapsed < TimeSpan.FromSeconds(3))
                {
                    await playback.CheckAsync();
                    await Task.Delay(10);
                }
                Assert.True(playback.IsFinished);
                await Task.Delay(100);
            }
            capture.StopRecording();
            var bytes = recorded.SelectMany(chunk => chunk).ToArray();
            Assert.NotEmpty(bytes);
            Assert.Equal(32, capture.WaveFormat.BitsPerSample);
            var captured = MemoryMarshal.Cast<byte, float>(bytes.AsSpan()).ToArray();
            var channels = capture.WaveFormat.Channels;
            var rate = capture.WaveFormat.SampleRate;
            var tone = Amplitude(captured, channels, rate, 1733);
            var neighbour = Math.Max(Amplitude(captured, channels, rate, 1400), Amplitude(captured, channels, rate, 2100));
            log.WriteLine($"Loopback {rate}Hz/{channels}ch, samples={captured.Length / channels}, tone1733 amplitude={tone:F5}, neighbour={neighbour:F5}");
            Assert.True(tone > .005, $"Timeline tone missing from loopback: amplitude={tone}");
            Assert.True(tone > neighbour * 5);
        }
        finally { File.Delete(path); }
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
