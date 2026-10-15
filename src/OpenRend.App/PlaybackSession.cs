using NAudio.CoreAudioApi;
using NAudio.Wave;
using OpenRend.Core;
using OpenRend.Media;

namespace OpenRend.App;

public sealed class PlaybackSession : IDisposable, IAsyncDisposable
{
    private const int BlockFrames = 16384;
    private readonly CancellationTokenSource cancellation = new();
    private readonly object lifecycleGate = new();
    private readonly WasapiOut output;
    private readonly TimelineAudioBuffer buffer;
    private readonly Task producer = Task.CompletedTask;
    private readonly long startFrame;
    private readonly ProjectDocument project;
    private readonly long totalSamples;
    private long submittedSamples;
    private long lastFrame;
    private double masterGain;
    private int disposed;
    private Exception? outputError;

    public float PeakLeft => buffer.PeakLeft;
    public float PeakRight => buffer.PeakRight;
    public double MasterGain
    {
        get => Volatile.Read(ref masterGain);
        set
        {
            if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            Volatile.Write(ref masterGain, value);
        }
    }

    public long CurrentFrame
    {
        get
        {
            lock (lifecycleGate)
            {
                if (Volatile.Read(ref disposed) != 0) return Interlocked.Read(ref lastFrame);
                var samples = PlayedSamples();
                var frame = samples >= totalSamples ? project.DurationFrames : startFrame + (long)Math.Floor((double)samples / MediaService.SampleRate * project.FrameRate.Value + 1e-7);
                var previous = Interlocked.Read(ref lastFrame);
                while (frame > previous)
                {
                    var observed = Interlocked.CompareExchange(ref lastFrame, frame, previous);
                    if (observed == previous) return frame;
                    previous = observed;
                }
                return previous;
            }
        }
    }

    public bool IsFinished
    {
        get
        {
            lock (lifecycleGate)
                return Volatile.Read(ref disposed) != 0 || (producer.IsCompletedSuccessfully && PlayedSamples() >= totalSamples);
        }
    }

    public PlaybackSession(TimelineRenderService renderer, ProjectDocument document, long start, double masterGain = 1)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(document);
        if (start < 0 || start > document.DurationFrames) throw new ArgumentOutOfRangeException(nameof(start));
        project = document;
        startFrame = lastFrame = start;
        MasterGain = masterGain;
        totalSamples = checked((long)Math.Ceiling(project.FrameRate.Seconds(project.DurationFrames - start) * MediaService.SampleRate));
        buffer = new TimelineAudioBuffer(() => MasterGain);
        output = new WasapiOut(AudioClientShareMode.Shared, false, 60);
        try
        {
            output.Init(buffer);
            buffer.DevicePosition = DeviceSamples;
            output.PlaybackStopped += (_, args) => { if (args.Exception != null) Volatile.Write(ref outputError, args.Exception); };
            // Decode before the device starts so decoder startup cannot shift the timeline.
            var prefill = Math.Min(totalSamples, MediaService.SampleRate / 2);
            while (submittedSamples < prefill) Submit(renderer, (int)Math.Min(BlockFrames, prefill - submittedSamples));
            producer = Task.Run(async () =>
            {
                while (submittedSamples < totalSamples)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (buffer.BufferedSamples >= MediaService.SampleRate * .75)
                    {
                        await Task.Delay(10, cancellation.Token).ConfigureAwait(false);
                        continue;
                    }
                    Submit(renderer, (int)Math.Min(BlockFrames, totalSamples - submittedSamples));
                }
            });
            if (totalSamples > 0) output.Play();
        }
        catch
        {
            cancellation.Cancel();
            output.Dispose();
            try { producer.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            cancellation.Dispose();
            throw;
        }
    }

    public async Task CheckAsync()
    {
        if (Volatile.Read(ref outputError) is { } error) throw new MediaException($"Audio output failed: {error.Message}");
        if (producer.IsFaulted) await producer.ConfigureAwait(false);
    }

    private void Submit(TimelineRenderService renderer, int count)
    {
        cancellation.Token.ThrowIfCancellationRequested();
        var seconds = project.FrameRate.Seconds(startFrame) + (double)submittedSamples / MediaService.SampleRate;
        var samples = renderer.RenderAudio(project, seconds, count);
        cancellation.Token.ThrowIfCancellationRequested();
        var bytes = new byte[count * 8];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        buffer.AddSamples(bytes);
        submittedSamples += count;
    }

    private long DeviceSamples() => (long)Math.Floor((double)output.GetPosition() / output.OutputWaveFormat.AverageBytesPerSecond * MediaService.SampleRate);
    private long PlayedSamples() => Math.Min(totalSamples, buffer.PlayedSourceSamples(DeviceSamples()));

    private bool Stop()
    {
        lock (lifecycleGate)
        {
            if (Volatile.Read(ref disposed) != 0) return false;
            try { _ = CurrentFrame; }
            catch (Exception error) { Volatile.Write(ref outputError, error); }
            if (Interlocked.Exchange(ref disposed, 1) != 0) return false;
            cancellation.Cancel();
            try { output.Stop(); }
            catch (Exception error) { Volatile.Write(ref outputError, error); }
            try { output.Dispose(); }
            catch (Exception error) { Volatile.Write(ref outputError, error); }
            return true;
        }
    }

    public void Dispose()
    {
        if (!Stop()) return;
        try { producer.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        catch (Exception error) { Volatile.Write(ref outputError, error); }
        finally { cancellation.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (!Stop()) return;
        try { await producer.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception error) { Volatile.Write(ref outputError, error); }
        finally { cancellation.Dispose(); }
    }
}

internal sealed class TimelineAudioBuffer(Func<double> gain) : IWaveProvider
{
    private readonly BufferedWaveProvider buffer = new(WaveFormat.CreateIeeeFloatWaveFormat(MediaService.SampleRate, 2))
    { BufferDuration = TimeSpan.FromSeconds(2), DiscardOnBufferOverflow = false, ReadFully = false };
    private readonly object clockGate = new();
    private readonly Queue<(long Start, long End)> gaps = new();
    private long queuedSamples;
    private long elapsedGaps;
    private long lastDeviceSample;
    public Func<long>? DevicePosition { get; set; }
    public WaveFormat WaveFormat => buffer.WaveFormat;
    public int BufferedSamples => buffer.BufferedBytes / 8;
    public float PeakLeft { get; private set; }
    public float PeakRight { get; private set; }

    public void AddSamples(byte[] bytes) => buffer.AddSamples(bytes, 0, bytes.Length);

    public int Read(byte[] destination, int offset, int count)
    {
        lock (clockGate)
        {
            var device = DevicePosition?.Invoke() ?? 0;
            if (device > queuedSamples)
            {
                gaps.Enqueue((queuedSamples, device));
                queuedSamples = device;
            }
            var read = buffer.Read(destination, offset, count);
            if (read < count)
            {
                Array.Clear(destination, offset + read, count - read);
                gaps.Enqueue((queuedSamples + read / 8, queuedSamples + count / 8));
            }
            queuedSamples += count / 8;
            var level = (float)gain();
            var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(destination.AsSpan(offset, count));
            float left = 0, right = 0;
            for (var sample = 0; sample < samples.Length; sample += 2)
            {
                samples[sample] = Math.Clamp(samples[sample] * level, -1, 1);
                samples[sample + 1] = Math.Clamp(samples[sample + 1] * level, -1, 1);
                left = Math.Max(left, Math.Abs(samples[sample]));
                right = Math.Max(right, Math.Abs(samples[sample + 1]));
            }
            PeakLeft = left;
            PeakRight = right;
            return count;
        }
    }

    public long PlayedSourceSamples(long deviceSample)
    {
        lock (clockGate)
        {
            // Device time includes silence inserted on underrun; project time does not.
            var sample = Math.Clamp(Math.Max(lastDeviceSample, deviceSample), 0, queuedSamples);
            lastDeviceSample = sample;
            while (gaps.TryPeek(out var gap) && gap.End <= sample)
            {
                elapsedGaps += gap.End - gap.Start;
                gaps.Dequeue();
            }
            var silence = elapsedGaps;
            if (gaps.TryPeek(out var current) && sample > current.Start) silence += sample - current.Start;
            return Math.Max(0, sample - silence);
        }
    }
}
