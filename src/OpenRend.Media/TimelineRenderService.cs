using System.Runtime.Versioning;
using OpenRend.Core;
using OpenRend.Timeline;

namespace OpenRend.Media;

[SupportedOSPlatform("windows")]
public sealed class TimelineRenderService : IDisposable
{
    private readonly MediaService media;
    private readonly object compositorGate = new();
    private readonly Dictionary<(int Width, int Height), CachedCompositor> compositors = new();
    private readonly Dictionary<string, (int Width, int Height)> recoveredDimensions = new(StringComparer.OrdinalIgnoreCase);
    private readonly int compositorCapacity;
    private readonly bool useGpuCompositing;
    private bool disposed;
    private long compositorAccess;
    private int backend;
    public bool IsHardwareCompositing => Volatile.Read(ref backend) == 2;
    public bool IsD3DCompositing => Volatile.Read(ref backend) > 0;
    public string? GpuUnavailableReason { get; private set; }
    public Guid? ActiveSubtitleClipId { get; set; }

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
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || (long)width * height * 4 > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(width));
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
                if (IsSubtitleLayer(layer))
                {
                    if (string.IsNullOrWhiteSpace(layer.Clip.Name)) continue;
                    var style = ExtractSubtitleStyle(layer.Track.Name, layer.Clip, layer.Media.Path, frame);
                    var renderHandles = ActiveSubtitleClipId.HasValue && ActiveSubtitleClipId.Value == layer.Clip.Id;
                    var subtitlePixels = SubtitleRenderer.RenderSubtitle(layer.Clip.Name, width, height, style, renderHandles, out _);
                    if (VideoEffectProcessor.HasActiveEffects(layer.Clip.VideoEffects))
                    {
                        subtitlePixels = VideoEffectProcessor.Apply(subtitlePixels, width, height, layer.Clip.VideoEffects, frame);
                    }
                    if (compositor != null)
                    {
                        if (NativeMethods.CompositorLayer(compositor, subtitlePixels, width, height, width * 4, layer.Opacity) < 0)
                            throw NativeMethods.Error("Could not composite subtitle timeline layer");
                    }
                    else Composite(output, width, height, subtitlePixels, width, height, layer.Opacity);
                    continue;
                }
                if (IsColorGeneratorLayer(layer))
                {
                    var colorPixels = RenderColorGenerator(layer.Media.Path, width, height);
                    if (VideoEffectProcessor.HasActiveEffects(layer.Clip.VideoEffects))
                    {
                        colorPixels = VideoEffectProcessor.Apply(colorPixels, width, height, layer.Clip.VideoEffects, frame);
                    }
                    if (compositor != null)
                    {
                        if (NativeMethods.CompositorLayer(compositor, colorPixels, width, height, width * 4, layer.Opacity) < 0)
                            throw NativeMethods.Error("Could not composite color generator timeline layer");
                    }
                    else Composite(output, width, height, colorPixels, width, height, layer.Opacity);
                    continue;
                }
                var (sourceWidth, sourceHeight) = SourceDimensions(layer.Media);
                var crop = layer.Clip.Crop is null ? CropState.Identity(sourceWidth, sourceHeight) : layer.Crop ?? CropState.Identity(sourceWidth, sourceHeight);
                crop.Validate();
                var transformed = crop.Width != sourceWidth || crop.Height != sourceHeight ||
                    crop.CenterX != sourceWidth / 2.0 || crop.CenterY != sourceHeight / 2.0 || crop.RotationDegrees % 360 != 0;
                if (transformed || VideoEffectProcessor.HasActiveEffects(layer.Clip.VideoEffects))
                {
                    if (sourceWidth <= 0 || sourceHeight <= 0 || sourceWidth > 16384 || sourceHeight > 16384 || (long)sourceWidth * sourceHeight * 4 > int.MaxValue)
                        throw new MediaException("Source dimensions exceed video processing limits.");
                    // Effects precede resampling, so preview size cannot change their source pixels.
                    // Decode honors display rotation before sampling.
                    var sourcePixels = media.GetFrame(layer.Media.Path, project.FrameRate.Seconds(layer.SourceFrame), sourceWidth, sourceHeight);
                    sourcePixels = VideoEffectProcessor.Apply(sourcePixels, sourceWidth, sourceHeight, layer.Clip.VideoEffects, frame);
                    var transform = SamplingTransform(crop, sourceWidth, sourceHeight, width, height);
                    if (compositor != null)
                    {
                        if (NativeMethods.CompositorLayerTransform(compositor, sourcePixels, sourceWidth, sourceHeight, checked(sourceWidth * 4), layer.Opacity, in transform) < 0)
                            throw NativeMethods.Error("Could not composite cropped timeline layer");
                    }
                    else CompositeTransformed(output, width, height, sourcePixels, sourceWidth, sourceHeight, layer.Opacity, transform);
                    continue;
                }
                var scale = Math.Min((double)width / sourceWidth, (double)height / sourceHeight);
                var layerWidth = Math.Clamp((int)Math.Round(sourceWidth * scale), 1, width);
                var layerHeight = Math.Clamp((int)Math.Round(sourceHeight * scale), 1, height);
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

    private (int Width, int Height) SourceDimensions(MediaAsset asset)
    {
        if (asset.Width > 0 && asset.Height > 0) return (asset.Width, asset.Height);
        var path = Path.GetFullPath(asset.Path);
        if (recoveredDimensions.TryGetValue(path, out var dimensions)) return dimensions;
        // Older imports may omit dimensions. Inspect without changing their saved edit decisions.
        var inspected = media.ProbeAsync(path).GetAwaiter().GetResult();
        if (!inspected.HasVideo || inspected.Width <= 0 || inspected.Height <= 0)
            throw new MediaException("Video source dimensions are unavailable.");
        if (recoveredDimensions.Count >= 32) recoveredDimensions.Remove(recoveredDimensions.Keys.First());
        dimensions = (inspected.Width, inspected.Height);
        recoveredDimensions.Add(path, dimensions);
        return dimensions;
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
            recoveredDimensions.Clear();
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

    private static NativeMethods.LayerTransform SamplingTransform(CropState crop, int sourceWidth, int sourceHeight, int width, int height)
    {
        var radians = (crop.RotationDegrees % 360) * Math.PI / 180;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var x = crop.CenterX - crop.Width / 2 - crop.PivotX;
        var y = crop.CenterY - crop.Height / 2 - crop.PivotY;
        var scale = Math.Min(width / crop.Width, height / crop.Height);
        return new()
        {
            Xx = cosine * crop.Width / sourceWidth, Xy = -sine * crop.Height / sourceWidth,
            Tx = (crop.PivotX + cosine * x - sine * y) / sourceWidth,
            Yx = sine * crop.Width / sourceHeight, Yy = cosine * crop.Height / sourceHeight,
            Ty = (crop.PivotY + sine * x + cosine * y) / sourceHeight,
            DestinationWidth = Math.Clamp((int)Math.Round(crop.Width * scale), 1, width),
            DestinationHeight = Math.Clamp((int)Math.Round(crop.Height * scale), 1, height)
        };
    }

    private static void CompositeTransformed(byte[] output, int width, int height, byte[] layer, int sourceWidth, int sourceHeight, double opacity, NativeMethods.LayerTransform transform)
    {
        var left = (width - transform.DestinationWidth) / 2;
        var top = (height - transform.DestinationHeight) / 2;
        // Both paths sample texel centers with clamped bilinear filtering inside the image.
        // Out-of-source samples are transparent, preserving lower timeline layers.
        for (var y = 0; y < transform.DestinationHeight; y++)
        {
            var v = (y + .5) / transform.DestinationHeight;
            for (var x = 0; x < transform.DestinationWidth; x++)
            {
                var u = (x + .5) / transform.DestinationWidth;
                var sourceU = transform.Xx * u + transform.Xy * v + transform.Tx;
                var sourceV = transform.Yx * u + transform.Yy * v + transform.Ty;
                if (sourceU < 0 || sourceU > 1 || sourceV < 0 || sourceV > 1) continue;
                var sx = Math.Clamp(sourceU * sourceWidth - .5, 0, sourceWidth - 1);
                var sy = Math.Clamp(sourceV * sourceHeight - .5, 0, sourceHeight - 1);
                var x0 = (int)sx;
                var y0 = (int)sy;
                var x1 = Math.Min(x0 + 1, sourceWidth - 1);
                var y1 = Math.Min(y0 + 1, sourceHeight - 1);
                var wx = sx - x0;
                var wy = sy - y0;
                var p00 = (y0 * sourceWidth + x0) * 4;
                var p01 = (y0 * sourceWidth + x1) * 4;
                var p10 = (y1 * sourceWidth + x0) * 4;
                var p11 = (y1 * sourceWidth + x1) * 4;
                var a00 = layer[p00 + 3] * (1 - wx) * (1 - wy);
                var a01 = layer[p01 + 3] * wx * (1 - wy);
                var a10 = layer[p10 + 3] * (1 - wx) * wy;
                var a11 = layer[p11 + 3] * wx * wy;
                var coverage = a00 + a01 + a10 + a11;
                if (coverage <= 0) continue;
                // Filtering coverage and premultiplied color together prevents keyed-away RGB from creating fringes.
                double SampleColor(int channel) => (layer[p00 + channel] * a00 + layer[p01 + channel] * a01 +
                    layer[p10 + channel] * a10 + layer[p11 + channel] * a11) / coverage;
                var alpha = opacity * coverage / 255;
                var target = ((y + top) * width + x + left) * 4;
                for (var channel = 0; channel < 3; channel++)
                    output[target + channel] = (byte)Math.Clamp((int)Math.Round(SampleColor(channel) * alpha + output[target + channel] * (1 - alpha)), 0, 255);
            }
        }
    }

    private static bool IsSubtitleLayer(EvaluatedClip layer)
    {
        return layer.Track.Name.StartsWith("Subtitle", StringComparison.OrdinalIgnoreCase)
            || layer.Track.Name.Contains("Subtitles", StringComparison.OrdinalIgnoreCase)
            || layer.Track.Name.StartsWith("Titles & Text", StringComparison.OrdinalIgnoreCase)
            || layer.Track.Name.StartsWith("Text Media", StringComparison.OrdinalIgnoreCase)
            || layer.Media.Path.StartsWith("openrend://subtitles", StringComparison.OrdinalIgnoreCase)
            || layer.Media.Path.StartsWith("openrend://text", StringComparison.OrdinalIgnoreCase)
            || layer.Media.Path.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)
            || layer.Media.Name.Contains("Subtitle", StringComparison.OrdinalIgnoreCase)
            || layer.Media.Name.Contains("Titles & Text", StringComparison.OrdinalIgnoreCase);
    }

    private static SubtitleStyle ExtractSubtitleStyle(string trackName, TimelineClip clip, string mediaPath, long frame)
    {
        SubtitleStyle style;
        if (mediaPath.StartsWith("openrend://text?", StringComparison.OrdinalIgnoreCase))
        {
            style = SubtitleStyle.FromQuery(mediaPath.Substring("openrend://text?".Length));
        }
        else
        {
            var styleName = "Classic White";
            if (trackName.Contains("YouTube Yellow", StringComparison.OrdinalIgnoreCase) || trackName.Contains("Shorts Yellow", StringComparison.OrdinalIgnoreCase))
                styleName = "YouTube Shorts (Yellow)";
            else if (trackName.Contains("YouTube White", StringComparison.OrdinalIgnoreCase) || trackName.Contains("Shorts White", StringComparison.OrdinalIgnoreCase))
                styleName = "YouTube Shorts (White)";
            else if (trackName.Contains("Neon Pop", StringComparison.OrdinalIgnoreCase) || trackName.Contains("Neon Green", StringComparison.OrdinalIgnoreCase))
                styleName = "Neon Pop";
            else if (trackName.Contains("Banner", StringComparison.OrdinalIgnoreCase))
                styleName = "Banner Subtitle";
            else if (trackName.Contains("Subtitles", StringComparison.OrdinalIgnoreCase) || trackName.Contains("Default", StringComparison.OrdinalIgnoreCase))
                styleName = "Subtitles";

            style = SubtitleStyle.FromPreset(styleName);

            // 9-point alignment parsing
            if (trackName.Contains("Top Left", StringComparison.OrdinalIgnoreCase) || trackName.Contains("Top_Left", StringComparison.OrdinalIgnoreCase))
                style.AnchorAlignment = "Top_Left";
            else if (trackName.Contains("Top Right", StringComparison.OrdinalIgnoreCase) || trackName.Contains("Top_Right", StringComparison.OrdinalIgnoreCase))
                style.AnchorAlignment = "Top_Right";
            else if (trackName.Contains("Top Center", StringComparison.OrdinalIgnoreCase) || trackName.Contains("Top_Center", StringComparison.OrdinalIgnoreCase))
                style.AnchorAlignment = "Top_Center";
            else if (trackName.Contains("Middle Left", StringComparison.OrdinalIgnoreCase) || trackName.Contains("Middle_Left", StringComparison.OrdinalIgnoreCase))
                style.AnchorAlignment = "Middle_Left";
            else if (trackName.Contains("Middle Right", StringComparison.OrdinalIgnoreCase) || trackName.Contains("Middle_Right", StringComparison.OrdinalIgnoreCase))
                style.AnchorAlignment = "Middle_Right";
            else if (trackName.Contains("Middle Center", StringComparison.OrdinalIgnoreCase) || trackName.Contains("Middle_Center", StringComparison.OrdinalIgnoreCase))
                style.AnchorAlignment = "Middle_Center";
            else if (trackName.Contains("Bottom Left", StringComparison.OrdinalIgnoreCase) || trackName.Contains("Bottom_Left", StringComparison.OrdinalIgnoreCase))
                style.AnchorAlignment = "Bottom_Left";
            else if (trackName.Contains("Bottom Right", StringComparison.OrdinalIgnoreCase) || trackName.Contains("Bottom_Right", StringComparison.OrdinalIgnoreCase))
                style.AnchorAlignment = "Bottom_Right";
            else if (trackName.Contains("Bottom Center", StringComparison.OrdinalIgnoreCase) || trackName.Contains("Bottom_Center", StringComparison.OrdinalIgnoreCase))
                style.AnchorAlignment = "Bottom_Center";
        }

        // Karaoke active word detection: calculate relative playhead offset within clip
        if (trackName.Contains("Karaoke", StringComparison.OrdinalIgnoreCase) ||
            trackName.Contains("Highlight", StringComparison.OrdinalIgnoreCase) ||
            trackName.Contains("Automatic", StringComparison.OrdinalIgnoreCase))
        {
            style.AutomaticHighlight = true;
        }

        if (style.AutomaticHighlight && clip.DurationFrames > 0)
        {
            var words = clip.Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 1)
            {
                var relFrame = Math.Clamp(frame - clip.StartFrame, 0, clip.DurationFrames - 1);
                var activeIdx = (int)Math.Clamp((double)relFrame / clip.DurationFrames * words.Length, 0, words.Length - 1);
                style.ActiveWordIndex = activeIdx;
            }
        }

        return style;
    }

    private static bool IsColorGeneratorLayer(EvaluatedClip layer)
    {
        return layer.Media.Path.StartsWith("openrend://color", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] RenderColorGenerator(string mediaPath, int width, int height)
    {
        if (width <= 0 || height <= 0) return Array.Empty<byte>();

        // Pure black solid generator matches Vegas Pro default
        var hex = "#000000";
        var idx = mediaPath.IndexOf("hex=", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var end = mediaPath.IndexOf('&', idx);
            hex = end >= 0 ? mediaPath.Substring(idx + 4, end - (idx + 4)) : mediaPath.Substring(idx + 4);
            hex = Uri.UnescapeDataString(hex);
        }

        var color = SubtitleStyle.ParseColor(hex, System.Drawing.Color.Black);
        var pixels = new byte[width * height * 4];

        // Format32bppArgb / BGRA in memory: B is byte 0, G is byte 1, R is byte 2, A is byte 3
        uint pixelValue = (uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B);
        System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(pixels.AsSpan()).Fill(pixelValue);
        return pixels;
    }
}
