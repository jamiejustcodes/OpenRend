using OpenRend.Core;
using OpenRend.Timeline;

namespace OpenRend.Media;

public sealed class TimelineRenderService : IDisposable
{
    private readonly MediaService media;
    private readonly object compositorGate = new();
    private readonly Dictionary<(int Width, int Height), CachedCompositor> compositors = new();
    private readonly int compositorCapacity;
    private readonly bool useGpuCompositing;
    private bool disposed;
    private long compositorAccess;
    private int backend;
    public bool IsHardwareCompositing => Volatile.Read(ref backend) == 2;
    public bool IsD3DCompositing => Volatile.Read(ref backend) > 0;
    public string? GpuUnavailableReason { get; private set; }

    public TimelineRenderService(MediaService media, bool useGpuCompositing = true, int compositorCapacity = 3)
    {
        ArgumentNullException.ThrowIfNull(media);
        if (compositorCapacity < 1) throw new ArgumentOutOfRangeException(nameof(compositorCapacity));
        this.media = media;
        this.useGpuCompositing = useGpuCompositing;
        this.compositorCapacity = compositorCapacity;
    }

    public byte[] RenderFrame(ProjectDocument project, long frame, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        lock (compositorGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var output = new byte[checked(width * height * 4)];
            var compositor = GetCompositor(width, height);
            if (compositor != null)
            {
                if (NativeMethods.CompositorBegin(compositor) < 0) throw NativeMethods.Error("Could not begin timeline composition");
            }
            else for (var i = 3; i < output.Length; i += 4) output[i] = 255;
            foreach (var layer in TimelineEvaluator.Evaluate(project, frame).Video)
            {
                if (layer.Opacity <= 0) continue;
                var scale = Math.Min((double)width / Math.Max(1, layer.Media.Width), (double)height / Math.Max(1, layer.Media.Height));
                var layerWidth = Math.Clamp((int)Math.Round(layer.Media.Width * scale), 1, width);
                var layerHeight = Math.Clamp((int)Math.Round(layer.Media.Height * scale), 1, height);
                var pixels = media.GetFrame(layer.Media.Path, project.FrameRate.Seconds(layer.SourceFrame), layerWidth, layerHeight);
                if (compositor != null)
                {
                    if (NativeMethods.CompositorLayer(compositor, pixels, layerWidth, layerHeight, layerWidth * 4, layer.Opacity) < 0)
                        throw NativeMethods.Error("Could not composite timeline layer");
                }
                else Composite(output, width, height, pixels, layerWidth, layerHeight, layer.Opacity);
            }
            if (compositor != null && NativeMethods.CompositorReadback(compositor, output, output.Length) < 0)
                throw NativeMethods.Error("Could not read composited timeline frame");
            return output;
        }
    }

    private CompositorHandle? GetCompositor(int width, int height)
    {
        if (!useGpuCompositing || GpuUnavailableReason != null)
        {
            Volatile.Write(ref backend, 0);
            return null;
        }
        var key = (width, height);
        if (compositors.TryGetValue(key, out var existing))
        {
            existing.LastAccess = ++compositorAccess;
            Volatile.Write(ref backend, existing.IsHardware ? 2 : 1);
            return existing.Handle;
        }
        if (NativeMethods.CompositorCreate(width, height, out var handle) < 0)
        {
            var error = NativeMethods.Error("Could not create D3D timeline compositor");
            handle?.Dispose();
            GpuUnavailableReason = error.Message;
            Volatile.Write(ref backend, 0);
            return null;
        }
        if (compositors.Count >= compositorCapacity)
        {
            var oldest = compositors.MinBy(entry => entry.Value.LastAccess);
            oldest.Value.Handle.Dispose();
            compositors.Remove(oldest.Key);
        }
        var hardware = NativeMethods.CompositorIsHardware(handle) == 1;
        compositors.Add(key, new CachedCompositor(handle, hardware, ++compositorAccess));
        Volatile.Write(ref backend, hardware ? 2 : 1);
        return handle;
    }

    public void Dispose()
    {
        lock (compositorGate)
        {
            if (disposed) return;
            disposed = true;
            foreach (var compositor in compositors.Values) compositor.Handle.Dispose();
            compositors.Clear();
        }
    }

    private sealed class CachedCompositor(CompositorHandle handle, bool isHardware, long lastAccess)
    {
        internal CompositorHandle Handle { get; } = handle;
        internal bool IsHardware { get; } = isHardware;
        internal long LastAccess { get; set; } = lastAccess;
    }

    public float[] RenderAudio(ProjectDocument project, double seconds, int frames)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed), this);
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
        var output = new float[checked(frames * MediaService.Channels)];
        if (frames == 0) return output;
        var assets = project.Media.ToDictionary(asset => asset.Id);
        var solo = project.Tracks.Any(track => track.Kind == TrackKind.Audio && track.IsSolo);
        foreach (var track in project.Tracks)
        {
            if (track.Kind != TrackKind.Audio || track.IsMuted || (solo && !track.IsSolo)) continue;
            foreach (var clip in track.Clips)
            {
                if (!assets.TryGetValue(clip.MediaId, out var asset) || !asset.HasAudio || clip.Gain <= 0 || track.Gain <= 0) continue;
                var start = project.FrameRate.Seconds(clip.StartFrame);
                var end = project.FrameRate.Seconds(clip.EndFrame);
                // Include only sample instants inside the clip's half-open range.
                var first = (int)Math.Clamp(Math.Ceiling((start - seconds) * MediaService.SampleRate - 1e-7), 0, frames);
                var last = (int)Math.Clamp(Math.Ceiling((end - seconds) * MediaService.SampleRate - 1e-7), 0, frames);
                if (last <= first) continue;
                var localStart = seconds + (double)first / MediaService.SampleRate - start;
                var sourceStart = Math.Max(0, project.FrameRate.Seconds(clip.SourceInFrame) + localStart);
                var decoded = media.GetAudio(asset.Path, sourceStart, last - first);
                for (var sample = first; sample < last; sample++)
                {
                    var localFrame = (localStart + (double)(sample - first) / MediaService.SampleRate) * project.FrameRate.Value;
                    var gains = TimelineEvaluator.AudioGains(track, clip, localFrame);
                    var source = (sample - first) * MediaService.Channels;
                    output[sample * 2] += (float)(decoded[source] * gains.Left);
                    output[sample * 2 + 1] += (float)(decoded[source + 1] * gains.Right);
                }
            }
        }
        for (var i = 0; i < output.Length; i++) output[i] = Math.Clamp(output[i], -1, 1);
        return output;
    }

    private static void Composite(byte[] output, int width, int height, byte[] layer, int layerWidth, int layerHeight, double opacity)
    {
        var left = (width - layerWidth) / 2;
        var top = (height - layerHeight) / 2;
        for (var y = 0; y < layerHeight; y++)
        {
            var source = y * layerWidth * 4;
            var target = ((y + top) * width + left) * 4;
            for (var x = 0; x < layerWidth; x++, source += 4, target += 4)
            {
                var alpha = opacity * layer[source + 3] / 255;
                if (alpha >= 1)
                {
                    output[target] = layer[source];
                    output[target + 1] = layer[source + 1];
                    output[target + 2] = layer[source + 2];
                }
                else if (alpha > 0)
                {
                    for (var channel = 0; channel < 3; channel++)
                        output[target + channel] = (byte)Math.Clamp((int)Math.Round(layer[source + channel] * alpha + output[target + channel] * (1 - alpha)), 0, 255);
                }
            }
        }
    }
}
