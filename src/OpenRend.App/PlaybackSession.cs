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
    private readonly long endFrame;
    private readonly double originSeconds;
    private long submittedSamples;
    private long lastFrame;
    private double masterGain;
    private int disposed;
    private bool started;
    private Exception? outputError;
    private readonly float[] playbackAudioBuffer = new float[BlockFrames * MediaService.Channels];
    private readonly byte[] playbackByteBuffer = new byte[BlockFrames * MediaService.Channels * sizeof(float)];
    private readonly float[]? shuttleAudioBuffer;
    private readonly TimelinePlaybackLoop? loop;
    private readonly float[]? cachedLoop;

    public double Rate { get; }
    internal MasterMeterReading MeterReading => buffer.MeterReading;
    internal void ClearMeterClips() => buffer.ClearMeterClips();
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
                if (Volatile.Read(ref disposed) != 0 || !started) return lastFrame;
                var samples = PlayedSamples();
                if (loop is not null) return lastFrame = loop.FrameAt(samples);
                var elapsed = (long)Math.Floor(samples * Math.Abs(Rate) / MediaService.SampleRate * project.FrameRate.Value + 1e-7);
                var frame = samples >= totalSamples ? endFrame : startFrame + Math.Sign(Rate) * elapsed;
                lastFrame = Rate > 0 ? Math.Max(lastFrame, frame) : Math.Min(lastFrame, frame);
                return lastFrame;
            }
        }
    }

    public bool IsFinished
    {
        get
        {
            lock (lifecycleGate)
                return Volatile.Read(ref disposed) != 0 || (loop is null && producer.IsCompletedSuccessfully && PlayedSamples() >= totalSamples);
        }
    }

    public PlaybackSession(TimelineRenderService renderer, ProjectDocument document, long start, double masterGain = 1,
        double rate = 1, bool startImmediately = true, FrameRange? loopRange = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(document);
        if (start < 0 || start > document.DurationFrames) throw new ArgumentOutOfRangeException(nameof(start));
        if (Math.Abs(rate) is not (.25 or .5 or 1 or 2 or 4 or 8 or 16)) throw new ArgumentOutOfRangeException(nameof(rate));
        project = document;
        startFrame = lastFrame = start;
        endFrame = rate > 0 ? project.DurationFrames : 0;
        originSeconds = project.FrameRate.Seconds(startFrame);
        Rate = rate;
        MasterGain = masterGain;
        if (loopRange is { } range)
        {
            if (rate != 1) throw new ArgumentOutOfRangeException(nameof(rate));
            range.Validate(project.DurationFrames);
            loop = new TimelinePlaybackLoop(range, project.FrameRate, startFrame);
            // Short sounds can wrap many times in one device block. Render their mix once.
            if (loop.SampleCount <= MediaService.SampleRate / 2)
            {
                cachedLoop = new float[(int)loop.SampleCount * MediaService.Channels];
                renderer.RenderAudio(project, loop.SecondsAt(0), (int)loop.SampleCount, cachedLoop, clampOutput: false);
            }
        }
        totalSamples = loop is not null ? long.MaxValue : checked((long)Math.Ceiling(project.FrameRate.Seconds(Math.Abs(endFrame - startFrame)) * MediaService.SampleRate / Math.Abs(Rate)));
        if (rate != 1)
            shuttleAudioBuffer = new float[PlaybackAudioResampling.Window(0, BlockFrames, rate).FrameCount * MediaService.Channels + MediaService.Channels];
        buffer = new TimelineAudioBuffer(() => MasterGain);
        output = new WasapiOut(AudioClientShareMode.Shared, false, 60);
        try
        {
            output.Init(buffer);
            buffer.DevicePosition = DeviceSamples;
            output.PlaybackStopped += (_, args) => { if (args.Exception != null) Volatile.Write(ref outputError, args.Exception); };
            // Decode before the device starts so decoder startup cannot shift the timeline.
            var prefill = Math.Min(totalSamples, rate == 1 ? MediaService.SampleRate / 2 : MediaService.SampleRate / 10);
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
            if (startImmediately) Start();
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

    public void Start()
    {
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            if (started) return;
            if (totalSamples > 0) output.Play();
            started = true;
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
        const int frameBytes = MediaService.Channels * sizeof(float);
        if (loop is not null)
        {
            for (var written = 0; written < count;)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var (offset, length) = loop.Window(submittedSamples + written, count - written);
                if (cachedLoop is null)
                {
                    renderer.RenderAudio(project, loop.SecondsAt(offset), length, playbackAudioBuffer, clampOutput: false);
                    Buffer.BlockCopy(playbackAudioBuffer, 0, playbackByteBuffer, written * frameBytes, length * frameBytes);
                }
                else
                {
                    Buffer.BlockCopy(cachedLoop, (int)offset * frameBytes, playbackByteBuffer, written * frameBytes, length * frameBytes);
                }
                written += length;
            }
        }
        else
        {
            if (shuttleAudioBuffer is null)
                renderer.RenderAudio(project, originSeconds + (double)submittedSamples / MediaService.SampleRate, count, playbackAudioBuffer, clampOutput: false);
            else
                PlaybackAudioResampling.Render(renderer, project, originSeconds, submittedSamples, count, Rate, shuttleAudioBuffer, playbackAudioBuffer);
            Buffer.BlockCopy(playbackAudioBuffer, 0, playbackByteBuffer, 0, count * frameBytes);
        }
        cancellation.Token.ThrowIfCancellationRequested();
        buffer.AddSamples(playbackByteBuffer, 0, count * frameBytes);
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

internal static class PlaybackAudioResampling
{
    internal static void Render(TimelineRenderService renderer, ProjectDocument project, double originSeconds,
        long submitted, int count, double rate, float[] source, float[] output)
    {
        var window = Window(submitted, count, rate);
        var firstAvailable = (long)Math.Ceiling(-originSeconds * MediaService.SampleRate - 1e-7);
        var silence = (int)Math.Clamp(firstAvailable - window.StartFrame, 0, window.FrameCount);
        var sourceStart = Math.Max(0, originSeconds + (double)(window.StartFrame + silence) / MediaService.SampleRate);
        var sourceFrames = window.FrameCount - silence;
        renderer.RenderAudio(project, sourceStart, sourceFrames, source);
        if (silence > 0)
        {
            Array.Copy(source, 0, source, silence * MediaService.Channels, sourceFrames * MediaService.Channels);
            Array.Clear(source, 0, silence * MediaService.Channels);
        }
        Resample(source, window.FirstSample, rate, output, count);
    }

    internal static (long StartFrame, int FrameCount, double FirstSample) Window(long submitted, int count, double rate)
    {
        // Reverse playback starts just inside the frame boundary.
        var first = submitted * rate - (rate < 0 ? 1 : 0);
        var last = first + (count - 1) * rate + Math.Sign(rate) * (FilterWidth(rate) - 1);
        var start = (long)Math.Floor(Math.Min(first, last));
        var end = (long)Math.Ceiling(Math.Max(first, last));
        return (start, checked((int)(end - start + 1)), first - start);
    }

    internal static void Resample(float[] source, double firstSample, double rate, float[] destination, int count)
    {
        var width = FilterWidth(rate);
        var direction = Math.Sign(rate) * MediaService.Channels;
        for (var index = 0; index < count; index++)
        {
            var position = firstSample + index * rate;
            var frame = (int)Math.Floor(position);
            var fraction = (float)(position - frame);
            var sample = frame * MediaService.Channels;
            var output = index * MediaService.Channels;
            // Average each source interval when speeding up. Interpolate between samples when slowing down.
            for (var channel = 0; channel < MediaService.Channels; channel++)
            {
                var input = sample + channel;
                float value = 0;
                float next = 0;
                for (var tap = 0; tap < width; tap++, input += direction)
                {
                    value += source[input];
                    if (fraction != 0) next += source[input + MediaService.Channels];
                }
                destination[output + channel] = (value + fraction * (next - value)) / width;
            }
        }
    }

    private static int FilterWidth(double rate) => Math.Max(1, (int)Math.Abs(rate));
}

internal sealed class TimelineAudioBuffer(Func<double> gain) : IWaveProvider
{
    private readonly BufferedWaveProvider buffer = new(WaveFormat.CreateIeeeFloatWaveFormat(MediaService.SampleRate, 2))
    { BufferDuration = TimeSpan.FromSeconds(2), DiscardOnBufferOverflow = false, ReadFully = false };
    private readonly object clockGate = new();
    private readonly Queue<(long Start, long End)> gaps = new();
    private readonly MasterAudioMeter meter = new();
    private MasterMeterReading meterReading = MasterMeterReading.Empty;
    private long queuedSamples;
    private long elapsedGaps;
    private long lastDeviceSample;
    public Func<long>? DevicePosition { get; set; }
    public WaveFormat WaveFormat => buffer.WaveFormat;
    public int BufferedSamples => buffer.BufferedBytes / 8;
    public MasterMeterReading MeterReading => Volatile.Read(ref meterReading);

    public void AddSamples(byte[] bytes) => buffer.AddSamples(bytes, 0, bytes.Length);
    public void AddSamples(byte[] bytes, int offset, int count) => buffer.AddSamples(bytes, offset, count);

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
            Volatile.Write(ref meterReading, meter.Measure(samples));
            // Keep the program ceiling at the same point as export, before monitor gain.
            for (var sample = 0; sample < samples.Length; sample++)
                samples[sample] = Math.Clamp(Math.Clamp(samples[sample], -1, 1) * level, -1, 1);
            return count;
        }
    }

    public void ClearMeterClips()
    {
        lock (clockGate)
        {
            meter.ClearClips();
            Volatile.Write(ref meterReading, meterReading with { LeftClipped = false, RightClipped = false });
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
