namespace OpenRend.Timeline;

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRend.Core;

public sealed class PasteEventAttributesOptions
{
    public bool VideoEffects { get; set; } = true;
    public bool PanCrop { get; set; } = true;
    public bool VideoSwitches { get; set; } = true;
    public bool VideoFadesAndOpacity { get; set; } = true;

    public bool AudioEffects { get; set; } = true;
    public bool AudioGainAndPan { get; set; } = true;
    public bool AudioPitchAndChannels { get; set; } = true;
    public bool AudioSwitches { get; set; } = true;
    public bool AudioFades { get; set; } = true;

    public bool ReplaceExistingFX { get; set; } = true;
}

public sealed partial class TimelineEditor
{
    public int PasteEventAttributes(TimelineClip sourceClip, IEnumerable<Guid> targetClipIds, PasteEventAttributesOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sourceClip);
        ArgumentNullException.ThrowIfNull(targetClipIds);
        options ??= new PasteEventAttributesOptions();

        var targetSet = targetClipIds.ToHashSet();
        if (targetSet.Count == 0) return 0;

        int modifiedCount = 0;
        Execute(p =>
        {
            var targets = p.Tracks.SelectMany(t => t.Clips.Where(c => targetSet.Contains(c.Id)).Select(c => (Track: t, Clip: c))).ToList();
            foreach (var (track, clip) in targets)
            {
                if (track.IsLocked || clip.IsLocked) continue;

                if (track.Kind == TrackKind.Video)
                {
                    if (options.VideoEffects && sourceClip.VideoEffects != null)
                    {
                        var clonedFx = sourceClip.VideoEffects.Select(fx => fx.DeepClone()).ToList();
                        if (options.ReplaceExistingFX)
                        {
                            clip.VideoEffects = clonedFx;
                        }
                        else
                        {
                            clip.VideoEffects ??= [];
                            clip.VideoEffects.AddRange(clonedFx);
                        }
                    }

                    if (options.PanCrop)
                    {
                        clip.Crop = sourceClip.Crop?.DeepClone();
                        clip.Mask = sourceClip.Mask?.DeepClone();
                    }

                    if (options.VideoSwitches)
                    {
                        clip.IsMuted = sourceClip.IsMuted;
                        clip.VideoSourceMode = sourceClip.VideoSourceMode;
                        clip.PlaybackRate = sourceClip.PlaybackRate;
                        clip.ResampleMode = sourceClip.ResampleMode;
                        clip.UnderSampleRate = sourceClip.UnderSampleRate;
                        clip.MaintainAspectRatio = sourceClip.MaintainAspectRatio;
                        clip.ReduceInterlace = sourceClip.ReduceInterlace;
                    }

                    if (options.VideoFadesAndOpacity)
                    {
                        clip.Opacity = sourceClip.Opacity;
                        clip.FadeInCurve = sourceClip.FadeInCurve;
                        clip.FadeOutCurve = sourceClip.FadeOutCurve;
                    }

                    modifiedCount++;
                }
                else if (track.Kind == TrackKind.Audio)
                {
                    if (options.AudioEffects && sourceClip.AudioEffects != null)
                    {
                        var clonedFx = sourceClip.AudioEffects.Select(fx => fx.DeepClone()).ToList();
                        if (options.ReplaceExistingFX)
                        {
                            clip.AudioEffects = clonedFx;
                        }
                        else
                        {
                            clip.AudioEffects ??= [];
                            clip.AudioEffects.AddRange(clonedFx);
                        }
                    }

                    if (options.AudioGainAndPan)
                    {
                        clip.Gain = sourceClip.Gain;
                        clip.Pan = sourceClip.Pan;
                    }

                    if (options.AudioPitchAndChannels)
                    {
                        clip.PitchSemis = sourceClip.PitchSemis;
                        clip.InvertPhase = sourceClip.InvertPhase;
                        clip.Normalize = sourceClip.Normalize;
                        clip.ChannelMode = sourceClip.ChannelMode;
                    }

                    if (options.AudioSwitches)
                    {
                        clip.IsMuted = sourceClip.IsMuted;
                        clip.PlaybackRate = sourceClip.PlaybackRate;
                    }

                    if (options.AudioFades)
                    {
                        clip.FadeInFrames = sourceClip.FadeInFrames;
                        clip.FadeOutFrames = sourceClip.FadeOutFrames;
                    }

                    modifiedCount++;
                }
            }
        });

        return modifiedCount;
    }
}

