using System.Runtime.Versioning;
using OpenRend.Core;
using OpenRend.Timeline;

namespace OpenRend.Media;

[SupportedOSPlatform("windows")]
public sealed partial class TimelineRenderService : IDisposable
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

    public byte[] RenderFrame(ProjectDocument project, long frame, int width, int height, bool bypassVideoEffects = false)
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
            var layers = TimelineEvaluator.Evaluate(project, frame).Video;
            var trackGroups = layers.GroupBy(l => l.Track.Id).ToList();
            byte[]? branchBuffer = null;

            foreach (var trackGroup in trackGroups)
            {
                var track = trackGroup.First().Track;
                if (track.IsAdjustmentTrack)
                {
                    var adjustedBuffer = (byte[])output.Clone();
                    foreach (var layer in trackGroup)
                    {
                        if (layer.Opacity <= 0) continue;
                        if (!bypassVideoEffects && VideoEffectProcessor.HasActiveEffects(layer.Clip.VideoEffects))
                        {
                            adjustedBuffer = VideoEffectProcessor.Apply(adjustedBuffer, width, height, layer.Clip.VideoEffects, frame);
                        }
                    }
                    if (!bypassVideoEffects && VideoEffectProcessor.HasActiveEffects(track.VideoEffects))
                    {
                        adjustedBuffer = VideoEffectProcessor.Apply(adjustedBuffer, width, height, track.VideoEffects, frame);
                    }
                    if (track.Opacity >= 0.9999 && track.CompositeMode == TrackCompositeMode.SourceAlpha)
                    {
                        Array.Copy(adjustedBuffer, output, output.Length);
                    }
                    else
                    {
                        var blended = new byte[output.Length];
                        Array.Copy(output, blended, output.Length);
                        Composite(blended, width, height, adjustedBuffer, width, height, track.Opacity, track.CompositeMode);
                        Array.Copy(blended, output, output.Length);
                    }
                    continue;
                }

                var trackMotion = trackGroup.First().TrackMotion;
                var hasMotion = trackMotion is not null && !trackMotion.IsIdentity(project.Width, project.Height);
                var hasTrackEffects = !bypassVideoEffects && VideoEffectProcessor.HasActiveEffects(track.VideoEffects);
                var customBlend = track.CompositeMode != TrackCompositeMode.SourceAlpha;
                var isChild = track.CompositeNestingLevel > 0;
                if (isChild && branchBuffer == null)
                {
                    branchBuffer = new byte[checked(width * height * 4)];
                }
                var needsOwnBuffer = hasTrackEffects || customBlend || hasMotion || isChild || branchBuffer != null;
                var trackBuffer = needsOwnBuffer ? new byte[checked(width * height * 4)] : output;
                var currentCompositor = needsOwnBuffer ? null : compositor;

                foreach (var layer in trackGroup)
                {
                    var effOpacity = needsOwnBuffer ? (track.Opacity > 0 ? layer.Opacity / track.Opacity : layer.Opacity) : layer.Opacity;
                    if (layer.Dissolve is { } dissolve)
                    {
                        if (layer.Clip.Id != dissolve.OutgoingClipId) continue;
                        var incoming = layers.FirstOrDefault(candidate => candidate.Clip.Id == dissolve.IncomingClipId);
                        if (incoming is not null)
                        {
                            var dissolvePixels = RenderDissolve(project, frame, layer, incoming, dissolve, width, height);
                            if (currentCompositor is not null)
                            {
                                if (NativeMethods.CompositorLayer(currentCompositor, dissolvePixels, width, height, width * 4, layer.Track.Opacity) < 0)
                                    throw NativeMethods.Error("Could not composite dissolve timeline layer");
                            }
                            else Composite(trackBuffer, width, height, dissolvePixels, width, height, needsOwnBuffer ? 1.0 : layer.Track.Opacity);
                            continue;
                        }
                    }
                    if (layer.Opacity <= 0) continue;
                    if (IsSubtitleLayer(layer))
                    {
                        if (string.IsNullOrWhiteSpace(layer.Clip.Name)) continue;
                        var style = ExtractSubtitleStyle(layer.Track.Name, layer.Clip, layer.Media.Path, frame);
                        var renderHandles = ActiveSubtitleClipId.HasValue && ActiveSubtitleClipId.Value == layer.Clip.Id;
                        var subtitlePixels = SubtitleRenderer.RenderSubtitle(layer.Clip.Name, width, height, style, renderHandles, out _);
                        if (!bypassVideoEffects && VideoEffectProcessor.HasActiveEffects(layer.Clip.VideoEffects))
                        {
                            subtitlePixels = VideoEffectProcessor.Apply(subtitlePixels, width, height, layer.Clip.VideoEffects, frame);
                        }
                        if (currentCompositor != null)
                        {
                            if (NativeMethods.CompositorLayer(currentCompositor, subtitlePixels, width, height, width * 4, effOpacity) < 0)
                                throw NativeMethods.Error("Could not composite subtitle timeline layer");
                        }
                        else Composite(trackBuffer, width, height, subtitlePixels, width, height, effOpacity);
                        continue;
                    }
                    if (IsGeneratorLayer(layer))
                    {
                        var colorPixels = MediaGeneratorSynthesizer.Render(layer.Media, width, height, frame);
                        if (!bypassVideoEffects && VideoEffectProcessor.HasActiveEffects(layer.Clip.VideoEffects))
                        {
                            colorPixels = VideoEffectProcessor.Apply(colorPixels, width, height, layer.Clip.VideoEffects, frame);
                        }
                        if (currentCompositor != null)
                        {
                            if (NativeMethods.CompositorLayer(currentCompositor, colorPixels, width, height, width * 4, effOpacity) < 0)
                                throw NativeMethods.Error("Could not composite color generator timeline layer");
                        }
                        else Composite(trackBuffer, width, height, colorPixels, width, height, effOpacity);
                        continue;
                    }
                    var (sourceWidth, sourceHeight) = SourceDimensions(layer.Media);
                    var crop = layer.Clip.Crop is null ? CropState.Identity(sourceWidth, sourceHeight) : layer.Crop ?? CropState.Identity(sourceWidth, sourceHeight);
                    crop.Validate();
                    var transformed = crop.Width != sourceWidth || crop.Height != sourceHeight ||
                        crop.CenterX != sourceWidth / 2.0 || crop.CenterY != sourceHeight / 2.0 || crop.RotationDegrees % 360 != 0;
                    var hasMask = layer.Clip.Mask?.IsEnabled == true;
                    if (transformed || hasMask || (!bypassVideoEffects && (VideoEffectProcessor.HasActiveEffects(layer.Clip.VideoEffects) || VideoEffectProcessor.HasActiveEffects(layer.Media.VideoEffects))))
                    {
                        if (sourceWidth <= 0 || sourceHeight <= 0 || sourceWidth > 16384 || sourceHeight > 16384 || (long)sourceWidth * sourceHeight * 4 > int.MaxValue)
                            throw new MediaException("Source dimensions exceed video processing limits.");
                        // Effects precede resampling, so preview size cannot change their source pixels.
                        // Decode honors display rotation before sampling.
                        var sourcePixels = media.GetFrame(layer.Media.Path, (layer.SourceTimeSeconds ?? project.FrameRate.Seconds(layer.SourceFrame)), sourceWidth, sourceHeight);
                        if (!bypassVideoEffects && VideoEffectProcessor.HasActiveEffects(layer.Media.VideoEffects))
                        {
                            sourcePixels = VideoEffectProcessor.Apply(sourcePixels, sourceWidth, sourceHeight, layer.Media.VideoEffects, frame);
                        }
                        if (!bypassVideoEffects && VideoEffectProcessor.HasActiveEffects(layer.Clip.VideoEffects))
                        {
                            sourcePixels = VideoEffectProcessor.Apply(sourcePixels, sourceWidth, sourceHeight, layer.Clip.VideoEffects, frame);
                        }
                        if (hasMask)
                        {
                            var maskShape = TimelineEvaluator.MaskAt(layer.Clip, checked(layer.SourceFrame - layer.Clip.SourceInFrame));
                            MaskRasterizer.ApplyMask(sourcePixels, sourceWidth, sourceHeight, maskShape);
                        }
                        var transform = SamplingTransform(crop, sourceWidth, sourceHeight, width, height);
                        if (currentCompositor != null)
                        {
                            if (NativeMethods.CompositorLayerTransform(currentCompositor, sourcePixels, sourceWidth, sourceHeight, checked(sourceWidth * 4), effOpacity, in transform) < 0)
                                throw NativeMethods.Error("Could not composite cropped timeline layer");
                        }
                        else CompositeTransformed(trackBuffer, width, height, sourcePixels, sourceWidth, sourceHeight, effOpacity, transform);
                        continue;
                    }
                    var scale = Math.Min((double)width / sourceWidth, (double)height / sourceHeight);
                    var layerWidth = Math.Clamp((int)Math.Round(sourceWidth * scale), 1, width);
                    var layerHeight = Math.Clamp((int)Math.Round(sourceHeight * scale), 1, height);
                    var pixels = media.GetFrame(layer.Media.Path, (layer.SourceTimeSeconds ?? project.FrameRate.Seconds(layer.SourceFrame)), layerWidth, layerHeight);
                    if (currentCompositor != null)
                    {
                        if (NativeMethods.CompositorLayer(currentCompositor, pixels, layerWidth, layerHeight, layerWidth * 4, effOpacity) < 0)
                            throw NativeMethods.Error("Could not composite timeline layer");
                    }
                    else Composite(trackBuffer, width, height, pixels, layerWidth, layerHeight, effOpacity);
                }

                if (needsOwnBuffer)
                {
                    if (isChild)
                    {
                        if (hasTrackEffects)
                        {
                            trackBuffer = VideoEffectProcessor.Apply(trackBuffer, width, height, track.VideoEffects, frame);
                        }
                        Composite(branchBuffer!, width, height, trackBuffer, width, height, track.Opacity, track.CompositeMode);
                        continue;
                    }

                    if (branchBuffer != null)
                    {
                        var merged = new byte[trackBuffer.Length];
                        Composite(merged, width, height, branchBuffer, width, height, 1.0);
                        Composite(merged, width, height, trackBuffer, width, height, 1.0);
                        trackBuffer = merged;
                        branchBuffer = null;
                    }

                    if (hasTrackEffects)
                    {
                        trackBuffer = VideoEffectProcessor.Apply(trackBuffer, width, height, track.VideoEffects, frame);
                    }
                    if (hasMotion && trackMotion != null)
                    {
                        var motionCrop = new CropState(
                            Width: Math.Max(0.001, trackMotion.Width * width / project.Width),
                            Height: Math.Max(0.001, trackMotion.Height * height / project.Height),
                            CenterX: width / 2.0 - trackMotion.PositionX * width / project.Width,
                            CenterY: height / 2.0 - trackMotion.PositionY * height / project.Height,
                            RotationDegrees: trackMotion.RotationZ,
                            PivotX: width / 2.0,
                            PivotY: height / 2.0
                        );
                        motionCrop.Validate();
                        var transform = SamplingTransform(motionCrop, width, height, width, height);
                        if (compositor != null && !customBlend)
                        {
                            if (NativeMethods.CompositorLayerTransform(compositor, trackBuffer, width, height, checked(width * 4), track.Opacity, in transform) < 0)
                                throw NativeMethods.Error("Could not composite track motion layer");
                        }
                        else
                        {
                            var transformedTrack = new byte[trackBuffer.Length];
                            CompositeTransformed(transformedTrack, width, height, trackBuffer, width, height, 1.0, transform);
                            Composite(output, width, height, transformedTrack, width, height, track.Opacity, track.CompositeMode);
                        }
                    }
                    else if (compositor != null && !customBlend)
                    {
                        if (NativeMethods.CompositorLayer(compositor, trackBuffer, width, height, width * 4, track.Opacity) < 0)
                            throw NativeMethods.Error("Could not composite track FX layer");
                    }
                    else
                    {
                        Composite(output, width, height, trackBuffer, width, height, track.Opacity, track.CompositeMode);
                    }
                }
            }
            if (branchBuffer != null)
            {
                Composite(output, width, height, branchBuffer, width, height, 1.0);
            }
            if (compositor != null && NativeMethods.CompositorReadback(compositor, output, output.Length) < 0)
                throw NativeMethods.Error("Could not read composited timeline frame");

            if (VideoEffectProcessor.HasActiveEffects(project.VideoOutputEffects))
            {
                output = VideoEffectProcessor.Apply(output, width, height, project.VideoOutputEffects, frame);
            }
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
        if (project.MasterAudioMute) return output;

        var assets = project.Media.ToDictionary(asset => asset.Id);
        var solo = project.Tracks.Any(track => track.Kind == TrackKind.Audio && track.IsSolo);
        float[]? trackBuffer = null;
        var busBuffers = new Dictionary<Guid, float[]>();
        if (project.AudioBuses != null)
        {
            foreach (var bus in project.AudioBuses)
            {
                busBuffers[bus.Id] = new float[output.Length];
            }
        }

        foreach (var track in project.Tracks)
        {
            if (track.Kind != TrackKind.Audio || track.IsMuted || (solo && !track.IsSolo)) continue;

            bool hasClipsInWindow = false;
            foreach (var clip in track.Clips)
            {
                if (clip.IsMuted || !assets.TryGetValue(clip.MediaId, out var asset) || !asset.HasAudio || clip.Gain <= 0 || track.Gain <= 0) continue;
                var start = project.FrameRate.Seconds(clip.StartFrame);
                var end = project.FrameRate.Seconds(clip.EndFrame);
                // Include only sample instants inside the clip's half-open range.
                var first = (int)Math.Clamp(Math.Ceiling((start - seconds) * MediaService.SampleRate - 1e-7), 0, frames);
                var last = (int)Math.Clamp(Math.Ceiling((end - seconds) * MediaService.SampleRate - 1e-7), 0, frames);
                if (last <= first) continue;

                if (!hasClipsInWindow)
                {
                    hasClipsInWindow = true;
                    trackBuffer ??= new float[output.Length];
                    Array.Clear(trackBuffer, 0, trackBuffer.Length);
                }

                var localStart = seconds + (double)first / MediaService.SampleRate - start;
                var sourceStart = Math.Max(0, project.FrameRate.Seconds(clip.SourceInFrame) + localStart);
                var decoded = media.GetAudio(asset.Path, sourceStart, last - first);
                for (var sample = first; sample < last; sample++)
                {
                    var localFrame = (localStart + (double)(sample - first) / MediaService.SampleRate) * project.FrameRate.Value;
                    var gains = TimelineEvaluator.AudioGains(track, clip, localFrame);
                    var source = (sample - first) * MediaService.Channels;
                    trackBuffer![sample * 2] += (float)(decoded[source] * gains.Left);
                    trackBuffer![sample * 2 + 1] += (float)(decoded[source + 1] * gains.Right);
                }
            }

            if (hasClipsInWindow && trackBuffer != null)
            {
                // 1. Track Phase Inversion
                if (track.InvertPhase)
                {
                    AudioDspService.InvertPhase(trackBuffer);
                }

                // 2. Track Audio FX Chain (Track EQ, Track Compressor, Track Noise Gate)
                if (track.AudioEffects != null && track.AudioEffects.Count > 0)
                {
                    AudioDspService.ProcessAudioChain(trackBuffer, MediaService.SampleRate, track.AudioEffects);
                }

                // 3. Accumulate track into assigned sub-bus or master bus
                if (track.BusTrackId.HasValue && busBuffers.TryGetValue(track.BusTrackId.Value, out var busBuf))
                {
                    for (var i = 0; i < busBuf.Length; i++)
                    {
                        busBuf[i] += trackBuffer[i];
                    }
                }
                else
                {
                    for (var i = 0; i < output.Length; i++)
                    {
                        output[i] += trackBuffer[i];
                    }
                }
            }
        }

        // 3b. Process and accumulate Audio Sub-Busses into Master Bus
        if (project.AudioBuses != null && project.AudioBuses.Count > 0)
        {
            var busSolo = project.AudioBuses.Any(b => b.IsSolo);
            foreach (var bus in project.AudioBuses)
            {
                if (bus.IsMuted || (busSolo && !bus.IsSolo)) continue;
                if (!busBuffers.TryGetValue(bus.Id, out var busBuf)) continue;

                if (bus.AudioEffects != null && bus.AudioEffects.Count > 0)
                {
                    AudioDspService.ProcessAudioChain(busBuf, MediaService.SampleRate, bus.AudioEffects);
                }

                var busGain = (float)bus.Gain;
                var leftPan = (float)Math.Clamp(1.0 - bus.Pan, 0.0, 1.0);
                var rightPan = (float)Math.Clamp(1.0 + bus.Pan, 0.0, 1.0);

                for (var s = 0; s < frames; s++)
                {
                    output[s * 2] += busBuf[s * 2] * busGain * leftPan;
                    output[s * 2 + 1] += busBuf[s * 2 + 1] * busGain * rightPan;
                }
            }
        }

        // 4. Master Audio FX Chain
        if (project.MasterAudioEffects != null && project.MasterAudioEffects.Count > 0)
        {
            AudioDspService.ProcessAudioChain(output, MediaService.SampleRate, project.MasterAudioEffects);
        }

        // 5. Master Gain & Dim (-20 dB = 0.1)
        var masterGain = (float)(project.MasterAudioGain * (project.MasterAudioDim ? 0.1 : 1.0));
        if (Math.Abs(masterGain - 1.0f) > 1e-4f)
        {
            for (var i = 0; i < output.Length; i++)
            {
                output[i] *= masterGain;
            }
        }

        for (var i = 0; i < output.Length; i++) output[i] = Math.Clamp(output[i], -1, 1);
        return output;
    }

    private static void Composite(byte[] output, int width, int height, byte[] layer, int layerWidth, int layerHeight, double opacity, TrackCompositeMode mode = TrackCompositeMode.SourceAlpha)
    {
        var left = (width - layerWidth) / 2;
        var top = (height - layerHeight) / 2;
        for (var y = 0; y < layerHeight; y++)
        {
            var source = y * layerWidth * 4;
            var target = ((y + top) * width + left) * 4;
            for (var x = 0; x < layerWidth; x++, source += 4, target += 4)
            {
                var alpha = opacity * layer[source + 3] / 255.0;
                if (alpha <= 0) continue;

                if (mode == TrackCompositeMode.Cut)
                {
                    var cutFactor = 1.0 - alpha;
                    for (var channel = 0; channel < 4; channel++)
                        output[target + channel] = (byte)Math.Clamp((int)Math.Round(output[target + channel] * cutFactor), 0, 255);
                    continue;
                }

                if (mode == TrackCompositeMode.SourceAlpha)
                {
                    if (alpha >= 1)
                    {
                        output[target] = layer[source];
                        output[target + 1] = layer[source + 1];
                        output[target + 2] = layer[source + 2];
                        output[target + 3] = 255;
                    }
                    else
                    {
                        for (var channel = 0; channel < 3; channel++)
                            output[target + channel] = (byte)Math.Clamp((int)Math.Round(layer[source + channel] * alpha + output[target + channel] * (1 - alpha)), 0, 255);
                        output[target + 3] = (byte)Math.Clamp((int)Math.Round(alpha * 255 + output[target + 3] * (1 - alpha)), 0, 255);
                    }
                    continue;
                }

                for (var channel = 0; channel < 3; channel++)
                {
                    var s = layer[source + channel] / 255.0;
                    var d = output[target + channel] / 255.0;
                    var blended = BlendChannel(mode, s, d);
                    output[target + channel] = (byte)Math.Clamp((int)Math.Round((blended * alpha + d * (1.0 - alpha)) * 255.0), 0, 255);
                }
                output[target + 3] = (byte)Math.Clamp((int)Math.Round(alpha * 255 + output[target + 3] * (1 - alpha)), 0, 255);
            }
        }
    }

    private static double BlendChannel(TrackCompositeMode mode, double s, double d) => mode switch
    {
        TrackCompositeMode.Add => Math.Min(1.0, s + d),
        TrackCompositeMode.Subtract => Math.Max(0.0, d - s),
        TrackCompositeMode.Multiply => s * d,
        TrackCompositeMode.Screen => s + d - s * d,
        TrackCompositeMode.Overlay => d < 0.5 ? 2.0 * s * d : 1.0 - 2.0 * (1.0 - s) * (1.0 - d),
        TrackCompositeMode.HardLight => s < 0.5 ? 2.0 * s * d : 1.0 - 2.0 * (1.0 - s) * (1.0 - d),
        TrackCompositeMode.Darken => Math.Min(s, d),
        TrackCompositeMode.Lighten => Math.Max(s, d),
        TrackCompositeMode.Difference => Math.Abs(d - s),
        TrackCompositeMode.DifferenceSquared => (d - s) * (d - s),
        TrackCompositeMode.Dodge => s >= 1.0 ? 1.0 : Math.Min(1.0, d / (1.0 - s)),
        TrackCompositeMode.Burn => s <= 0.0 ? 0.0 : 1.0 - Math.Min(1.0, (1.0 - d) / s),
        _ => s
    };

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
                output[target + 3] = (byte)Math.Clamp((int)Math.Round(alpha * 255 + output[target + 3] * (1 - alpha)), 0, 255);
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

    private static bool IsGeneratorLayer(EvaluatedClip layer)
    {
        return layer.Media.IsGenerated ||
               layer.Media.Path.StartsWith("openrend://generator", StringComparison.OrdinalIgnoreCase) ||
               layer.Media.Path.StartsWith("openrend://color", StringComparison.OrdinalIgnoreCase);
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
