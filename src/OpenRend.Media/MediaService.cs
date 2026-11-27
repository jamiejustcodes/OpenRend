using OpenRend.Core;

namespace OpenRend.Media;

public sealed class MediaService : IDisposable
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;
    private readonly object gate = new();
    private readonly Dictionary<DecoderKey, CachedDecoder> decoders = new();
    private readonly int capacity;
    private bool disposed;
    private long access;

    public MediaService(int decoderCapacity = 8)
    {
        if (decoderCapacity < 1) throw new ArgumentOutOfRangeException(nameof(decoderCapacity));
        capacity = decoderCapacity;
    }

    public Task<MediaAsset> ProbeAsync(string path, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Media file was not found.", fullPath);
        if (NativeMethods.Probe(fullPath, out var metadata) < 0) throw NativeMethods.Error("Could not inspect media");
        cancellationToken.ThrowIfCancellationRequested();
        return new MediaAsset
        {
            Path = fullPath,
            DurationSeconds = metadata.DurationSeconds,
            Width = metadata.Width,
            Height = metadata.Height,
            FrameRate = metadata.FrameRate,
            HasVideo = metadata.VideoStreams > 0,
            HasAudio = metadata.AudioStreams > 0,
            AudioChannels = metadata.Channels,
            AudioSampleRate = metadata.SampleRate
        };
    }, cancellationToken);

    public static ProjectDocument CreateSourcePreview(MediaAsset source, Rational frameRate, FrameRange? sourceRange = null)
    {
        var asset = source.DeepClone();
        var preview = new ProjectDocument { FrameRate = frameRate, Media = [asset] };
        var length = Math.Max(1, frameRate.Frames(asset.DurationSeconds));
        var range = sourceRange ?? new FrameRange(0, length);
        range.Validate(length);
        if (asset.HasVideo)
        {
            preview.Width = asset.Width;
            preview.Height = asset.Height;
            AddTrack(TrackKind.Video);
        }
        if (asset.HasAudio) AddTrack(TrackKind.Audio);
        return preview;

        void AddTrack(TrackKind kind) => preview.Tracks.Add(new TimelineTrack
        {
            Kind = kind, Clips = [new TimelineClip { MediaId = asset.Id, SourceInFrame = range.StartFrame, DurationFrames = range.FrameCount }]
        });
    }

    public Task<byte[]> GetFrameAsync(string path, double seconds, int width, int height, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var frame = GetFrame(path, seconds, width, height);
        cancellationToken.ThrowIfCancellationRequested();
        return frame;
    }, cancellationToken);

    public Task<byte[]> GetThumbnailAsync(string path, double seconds, int width, int height, CancellationToken cancellationToken = default) => GetFrameAsync(path, seconds, width, height, cancellationToken);

    public Task<byte[]> GetFrameAsync(MediaAsset asset, double seconds, int width, int height, CancellationToken cancellationToken = default) =>
        GetFrameAsync(asset.Path, VideoFileTime(asset, seconds), width, height, cancellationToken);

    public byte[] GetFrame(MediaAsset asset, double seconds, int width, int height) =>
        GetFrame(asset.Path, VideoFileTime(asset, seconds), width, height);

    public static double VideoFileTime(MediaAsset asset, double seconds)
    {
        if (asset.SourceRange is not { } range) return seconds;
        // Keep end previews and hold-last playback on the included side of the decoder's PTS tolerance.
        var last = Math.Max(range.StartSeconds, range.EndSeconds - 0.000001);
        var fileTime = range.IsReversed ? last - seconds : range.StartSeconds + seconds;
        return Math.Clamp(fileTime, range.StartSeconds, last);
    }

    public byte[] GetFrame(string path, double seconds, int width, int height)
    {
        var result = new byte[checked(width * height * 4)];
        GetFrame(path, seconds, width, height, result);
        return result;
    }

    public void GetFrame(string path, double seconds, int width, int height, byte[] destination)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        ArgumentNullException.ThrowIfNull(destination);
        var requiredBytes = checked(width * height * 4);
        if (destination.Length < requiredBytes) throw new ArgumentException("Destination buffer is too small.", nameof(destination));
        lock (gate)
        {
            var decoder = GetDecoder(path, width, height);
            if (NativeMethods.DecoderFrame(decoder, seconds, destination, requiredBytes) < 0) throw NativeMethods.Error("Could not decode video frame");
        }
    }

    public float[] GetAudio(string path, double seconds, int frames)
    {
        var result = new float[checked(frames * Channels)];
        if (frames == 0) return result;
        GetAudio(path, seconds, frames, result);
        return result;
    }

    public void GetAudio(string path, double seconds, int frames, float[] destination)
    {
        ValidateAudioDestination(seconds, frames, destination);
        DecodeAudio(path, seconds, frames, destination);
    }

    private static void ValidateAudioDestination(double seconds, int frames, float[] destination)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Length < checked(frames * Channels)) throw new ArgumentException("Destination buffer is too small.", nameof(destination));
    }

    private void DecodeAudio(string path, double seconds, int frames, float[] destination)
    {
        if (frames == 0) return;
        lock (gate)
        {
            var decoder = GetDecoder(path, 1, 1);
            var read = NativeMethods.DecoderAudio(decoder, seconds, frames, destination, frames * Channels);
            if (read < 0) throw NativeMethods.Error("Could not decode audio samples");
            if (read < frames) Array.Clear(destination, read * Channels, (frames - read) * Channels);
        }
    }

    public float[] GetAudio(MediaAsset asset, double seconds, int frames)
    {
        var result = new float[checked(frames * Channels)];
        GetAudio(asset, seconds, frames, result);
        return result;
    }

    public void GetAudio(MediaAsset asset, double seconds, int frames, float[] destination)
    {
        ValidateAudioDestination(seconds, frames, destination);
        if (asset.SourceRange is not { } range)
        {
            DecodeAudio(asset.Path, seconds, frames, destination);
            return;
        }

        var count = range.IsReversed
            ? DecodeReversedAudio(asset.Path, range, seconds, frames, destination)
            : DecodeForwardSubclipAudio(asset.Path, range, seconds, frames, destination);
        Array.Clear(destination, count * Channels, (frames - count) * Channels);
    }

    private int DecodeForwardSubclipAudio(string path, MediaSourceRange range, double seconds, int frames, float[] destination)
    {
        var available = Math.Max(0, range.DurationSeconds - seconds);
        var count = (int)Math.Clamp(Math.Ceiling(available * SampleRate - 1e-7), 0, frames);
        DecodeAudio(path, range.StartSeconds + seconds, count, destination);
        return count;
    }

    private int DecodeReversedAudio(string path, MediaSourceRange range, double seconds, int frames, float[] destination)
    {
        if (seconds >= range.DurationSeconds) return 0;
        // Anchor all blocks to the same sample grid so adjacent requests do not repeat or skip a sample.
        var durationSamples = AudioFramesInRange(range.TickCount, range.TicksPerSecond);
        var remaining = durationSamples - Math.Round(seconds * SampleRate, MidpointRounding.AwayFromZero);
        var count = (int)Math.Clamp(remaining, 0, frames);
        if (count == 0) return 0;

        var first = range.StartSeconds + (remaining - count) / SampleRate;
        DecodeAudio(path, first, count, destination);
        ReverseStereo(destination, count);
        return count;
    }

    internal static long AudioFramesInRange(Int128 ticks, long ticksPerSecond) =>
        checked((long)((ticks * SampleRate + ticksPerSecond - 1) / ticksPerSecond));

    internal static void ReverseStereo(float[] samples, int frames)
    {
        for (int left = 0, right = (frames - 1) * Channels; left < right; left += Channels, right -= Channels)
        {
            (samples[left], samples[right]) = (samples[right], samples[left]);
            (samples[left + 1], samples[right + 1]) = (samples[right + 1], samples[left + 1]);
        }
    }

    public async Task<float[]> GetWaveformAsync(string path, int bins = 256, CancellationToken cancellationToken = default) =>
        await GetWaveformAsync(await ProbeAsync(path, cancellationToken).ConfigureAwait(false), bins, cancellationToken).ConfigureAwait(false);

    public Task<float[]> GetWaveformAsync(MediaAsset asset, int bins = 256, CancellationToken cancellationToken = default)
    {
        if (bins is < 1 or > 16384) throw new ArgumentOutOfRangeException(nameof(bins));
        if (!asset.HasAudio) return Task.FromResult(new float[bins]);
        return Task.Run(() =>
        {
            var peaks = new float[bins];
            // A short evenly spaced window keeps long recordings cheap to index.
            var window = (int)Math.Clamp(asset.DurationSeconds * SampleRate / bins, 1, 2048);
            for (var bin = 0; bin < bins; bin++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var samples = GetAudio(asset, asset.DurationSeconds * bin / bins, window);
                var peak = 0f;
                foreach (var sample in samples) peak = Math.Max(peak, Math.Abs(sample));
                peaks[bin] = Math.Min(1, peak);
            }
            return peaks;
        }, cancellationToken);
    }

    public void ClearCache()
    {
        lock (gate)
        {
            foreach (var item in decoders.Values) item.Handle.Dispose();
            decoders.Clear();
        }
    }

    private DecoderHandle GetDecoder(string path, int width, int height)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var key = new DecoderKey(System.IO.Path.GetFullPath(path), width, height);
        if (decoders.TryGetValue(key, out var existing))
        {
            existing.LastAccess = ++access;
            return existing.Handle;
        }
        if (NativeMethods.DecoderOpen(key.Path, width, height, out var handle) < 0)
        {
            handle?.Dispose();
            throw NativeMethods.Error("Could not open media decoder");
        }
        if (decoders.Count >= capacity)
        {
            var oldest = decoders.MinBy(entry => entry.Value.LastAccess);
            oldest.Value.Handle.Dispose();
            decoders.Remove(oldest.Key);
        }
        decoders.Add(key, new CachedDecoder(handle, ++access));
        return handle;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            ClearCache();
        }
    }

    private readonly record struct DecoderKey(string Path, int Width, int Height);
    private sealed class CachedDecoder(DecoderHandle handle, long lastAccess)
    {
        internal DecoderHandle Handle { get; } = handle;
        internal long LastAccess { get; set; } = lastAccess;
    }
}
