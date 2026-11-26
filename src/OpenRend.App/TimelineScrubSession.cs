using System.Diagnostics;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OpenRend.Core;
using OpenRend.Media;

namespace OpenRend.App;

internal sealed class TimelineScrubSession : IAsyncDisposable
{
    internal const double MaxRate = 4;
    private const int SampleCount = MediaService.SampleRate / 25;
    private const int FadeSamples = MediaService.SampleRate / 500;
    private readonly record struct Request(long Frame, double Rate, double Gain, long RequestedAt);
    private readonly Channel<Request> requests = Channel.CreateBounded<Request>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = true
    });
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task playback;

    public TimelineScrubSession(TimelineRenderService renderer, ProjectDocument project, Task previous)
    {
        playback = Task.Run(() => PlayAsync(renderer, project, previous));
    }

    public bool Queue(long frame, double rate, double gain) =>
        requests.Writer.TryWrite(new(frame, rate, gain, Stopwatch.GetTimestamp()));

    private async Task PlayAsync(TimelineRenderService renderer, ProjectDocument project, Task previous)
    {
        try
        {
            await previous.ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(MediaService.SampleRate, MediaService.Channels))
            {
                BufferDuration = TimeSpan.FromMilliseconds(100),
                ReadFully = true
            };
            using var output = new WasapiOut(AudioClientShareMode.Shared, false, 30);
            output.Init(provider);
            output.PlaybackStopped += (_, e) => { if (e.Exception is { } error) requests.Writer.TryComplete(error); };
            var source = new float[(SampleCount * (int)MaxRate + 2) * MediaService.Channels];
            var samples = new float[SampleCount * MediaService.Channels];
            var bytes = new byte[samples.Length * sizeof(float)];
            var started = false;

            await foreach (var request in requests.Reader.ReadAllAsync(cancellation.Token).ConfigureAwait(false))
            {
                var time = project.FrameRate.Seconds(request.Frame);
                var origin = Math.Clamp(time - request.Rate * .014, 0, project.FrameRate.Seconds(project.DurationFrames));
                PlaybackAudioResampling.Render(renderer, project, origin, 0, SampleCount, request.Rate, source, samples);
                cancellation.Token.ThrowIfCancellationRequested();
                if (Stopwatch.GetElapsedTime(request.RequestedAt) > TimeSpan.FromMilliseconds(100)) continue;

                for (var frame = 0; frame < SampleCount; frame++)
                {
                    var fade = Math.Min(1.0, (double)Math.Min(frame, SampleCount - 1 - frame) / FadeSamples);
                    for (var channel = 0; channel < MediaService.Channels; channel++)
                    {
                        var index = frame * MediaService.Channels + channel;
                        samples[index] = Math.Clamp(samples[index] * (float)(request.Gain * fade), -1f, 1f);
                    }
                }
                provider.ClearBuffer();
                Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                provider.AddSamples(bytes, 0, bytes.Length);
                if (!started)
                {
                    output.Play();
                    started = true;
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { requests.Writer.TryComplete(error); }
        finally { requests.Writer.TryComplete(); }
    }

    public async ValueTask DisposeAsync()
    {
        requests.Writer.TryComplete();
        cancellation.Cancel();
        await playback.ConfigureAwait(false);
        cancellation.Dispose();
    }
}
