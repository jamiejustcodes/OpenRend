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
            HasAudio = metadata.AudioStreams > 0
        };
    }, cancellationToken);

    public Task<byte[]> GetFrameAsync(string path, double seconds, int width, int height, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var frame = GetFrame(path, seconds, width, height);
        cancellationToken.ThrowIfCancellationRequested();
        return frame;
    }, cancellationToken);

    public Task<byte[]> GetThumbnailAsync(string path, double seconds, int width, int height, CancellationToken cancellationToken = default) => GetFrameAsync(path, seconds, width, height, cancellationToken);

    public byte[] GetFrame(string path, double seconds, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var result = new byte[checked(width * height * 4)];
        lock (gate)
        {
            var decoder = GetDecoder(path, width, height);
            if (NativeMethods.DecoderFrame(decoder, seconds, result, result.Length) < 0) throw NativeMethods.Error("Could not decode video frame");
        }
        return result;
    }

    public float[] GetAudio(string path, double seconds, int frames)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
        var result = new float[checked(frames * Channels)];
        if (frames == 0) return result;
        lock (gate)
        {
            var decoder = GetDecoder(path, 1, 1);
            var read = NativeMethods.DecoderAudio(decoder, seconds, frames, result, result.Length);
            if (read < 0) throw NativeMethods.Error("Could not decode audio samples");
            if (read < frames) Array.Clear(result, read * Channels, (frames - read) * Channels);
        }
        return result;
    }

    public async Task<float[]> GetWaveformAsync(string path, int bins = 256, CancellationToken cancellationToken = default)
    {
        if (bins is < 1 or > 16384) throw new ArgumentOutOfRangeException(nameof(bins));
        var metadata = await ProbeAsync(path, cancellationToken).ConfigureAwait(false);
        if (!metadata.HasAudio) return new float[bins];
        return await Task.Run(() =>
        {
            var peaks = new float[bins];
            // A short evenly spaced window keeps long recordings cheap to index.
            var window = Math.Min(2048, Math.Max(1, (int)(metadata.DurationSeconds * SampleRate / bins)));
            for (var bin = 0; bin < bins; bin++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var samples = GetAudio(path, metadata.DurationSeconds * bin / bins, window);
                var peak = 0f;
                foreach (var sample in samples) peak = Math.Max(peak, Math.Abs(sample));
                peaks[bin] = Math.Min(1, peak);
            }
            return peaks;
        }, cancellationToken).ConfigureAwait(false);
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
