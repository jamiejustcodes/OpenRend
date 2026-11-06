using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed record EvaluatedClip(TimelineTrack Track, TimelineClip Clip, MediaAsset Media, long SourceFrame, double Opacity, double Gain, double Pan)
{
    public CropState? Crop { get; init; }
    public double? SourceTimeSeconds { get; init; }
    // Dissolve participants form one composite group; scalar opacity alone cannot describe source coverage.
    public EvaluatedDissolve? Dissolve { get; init; }
    public TrackMotionKeyframe? TrackMotion { get; init; }
}
public sealed record EvaluatedDissolve(Guid OutgoingClipId, Guid IncomingClipId, double Progress, double? OutgoingWeight = null, double? IncomingWeight = null, VideoTransition? Transition = null);
public sealed record EvaluatedFrame(IReadOnlyList<EvaluatedClip> Video, IReadOnlyList<EvaluatedClip> Audio);

public static class TimelineEvaluator
{
    private static readonly Comparison<TimelineClip> ClipFadeOrderComparison = static (a, b) =>
    {
        var offsetA = a.StartFrame - a.FadeOffsetFrames;
        var offsetB = b.StartFrame - b.FadeOffsetFrames;
        var cmp = offsetA.CompareTo(offsetB);
        if (cmp != 0) return cmp;
        cmp = a.StartFrame.CompareTo(b.StartFrame);
        if (cmp != 0) return cmp;
        return a.Id.CompareTo(b.Id);
    };

    private static bool IsTrackSoloInGroups(IReadOnlyList<TrackGroup>? groups, Guid trackId)
    {
        if (groups is null || groups.Count == 0) return false;
        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            if (!group.IsSolo) continue;
            var ids = group.TrackIds;
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] == trackId) return true;
            }
        }
        return false;
    }

    private static bool IsTrackMutedInGroups(IReadOnlyList<TrackGroup>? groups, Guid trackId)
    {
        if (groups is null || groups.Count == 0) return false;
        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            if (!group.IsMuted) continue;
            var ids = group.TrackIds;
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] == trackId) return true;
            }
        }
        return false;
    }

    private static ClipEnvelope? FindEnabledEnvelope(IReadOnlyList<ClipEnvelope>? envelopes, EnvelopeType type)
    {
        if (envelopes is null) return null;
        for (var i = 0; i < envelopes.Count; i++)
        {
            var e = envelopes[i];
            if (e.Type == type && e.IsEnabled) return e;
        }
        return null;
    }

    private static MediaAsset? FindMedia(IReadOnlyList<MediaAsset> mediaList, Dictionary<Guid, MediaAsset>? map, Guid id)
    {
        if (map is not null) return map.GetValueOrDefault(id);
        for (var i = 0; i < mediaList.Count; i++)
        {
            if (mediaList[i].Id == id) return mediaList[i];
        }
        return null;
    }

    public static EvaluatedFrame Evaluate(ProjectDocument project, long frame)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (frame < 0) return new EvaluatedFrame([], []);
        var video = new List<EvaluatedClip>();
        var audio = new List<EvaluatedClip>();
        var mediaList = project.Media;
        var mediaMap = mediaList.Count > 16 ? mediaList.ToDictionary(m => m.Id) : null;

        var soloVideo = false;
        var soloAudio = false;
        var tracks = project.Tracks;
        for (var i = 0; i < tracks.Count; i++)
        {
            var t = tracks[i];
            var isSolo = t.IsSolo || IsTrackSoloInGroups(project.TrackGroups, t.Id);
            if (isSolo)
            {
                if (t.Kind == TrackKind.Video) soloVideo = true;
                else if (t.Kind == TrackKind.Audio) soloAudio = true;
            }
        }

        // Return back-to-front layers so callers can composite in enumeration order.
        for (var i = tracks.Count - 1; i >= 0; i--)
        {
            var track = tracks[i];
            if (IsTrackEffectivelyMuted(project, track, i)) continue;
            if (FindEnabledEnvelope(track.Envelopes, EnvelopeType.Mute) is { } trackMute && trackMute.ValueAt(frame) >= 0.5) continue;
            if (track.Kind == TrackKind.Video ? (soloVideo && !IsTrackEffectivelySolo(project, track, i)) : (soloAudio && !IsTrackEffectivelySolo(project, track, i))) continue;

            IReadOnlyList<TimelineClip> ordered = track.Clips;
            Dictionary<Guid, double>? transitionOpacity = null;
            Dictionary<Guid, EvaluatedDissolve>? dissolves = null;

            if (track.Kind == TrackKind.Video && track.Clips.Count > 0)
            {
                var hasActiveFadeIn = false;
                for (var cIdx = 0; cIdx < track.Clips.Count; cIdx++)
                {
                    var c = track.Clips[cIdx];
                    if (!c.IsMuted && c.FadeInFrames > 0 && c.StartFrame <= frame && frame < c.EndFrame)
                    {
                        hasActiveFadeIn = true;
                        break;
                    }
                }

                if (hasActiveFadeIn)
                {
                    var sortedClips = new TimelineClip[track.Clips.Count];
                    for (var k = 0; k < track.Clips.Count; k++) sortedClips[k] = track.Clips[k];
                    Array.Sort(sortedClips, ClipFadeOrderComparison);
                    ordered = sortedClips;
                    for (var cIdx = 0; cIdx < ordered.Count; cIdx++)
                    {
                        var incoming = ordered[cIdx];
                        if (incoming.IsMuted || incoming.FadeInFrames <= 0 || incoming.StartFrame > frame || frame >= incoming.EndFrame) continue;

                        var incomingPosition = frame - incoming.StartFrame + incoming.FadeOffsetFrames;
                        if (incomingPosition >= incoming.FadeInFrames) continue;

                        TimelineClip? outgoing = null;
                        for (var oIdx = 0; oIdx < ordered.Count; oIdx++)
                        {
                            var c = ordered[oIdx];
                            if (c.IsMuted || c.Id == incoming.Id || c.FadeOutFrames <= 0) continue;
                            if (c.StartFrame <= frame && frame < c.EndFrame &&
                                c.StartFrame - c.FadeOffsetFrames < incoming.StartFrame - incoming.FadeOffsetFrames &&
                                frame - c.StartFrame + c.FadeOffsetFrames >= (c.FadeDurationFrames ?? c.DurationFrames) - c.FadeOutFrames)
                            {
                                if (outgoing is null || c.StartFrame > outgoing.StartFrame)
                                {
                                    outgoing = c;
                                }
                            }
                        }

                        if (outgoing is null || (incoming.FadeInTransition is null && !IsCanonicalCrossfade(outgoing, incoming, ordered))) continue;

                        var outgoingAsset = FindMedia(mediaList, mediaMap, outgoing.MediaId);
                        if (outgoingAsset is null || !outgoingAsset.HasVideo) continue;

                        var incomingAsset = FindMedia(mediaList, mediaMap, incoming.MediaId);
                        if (incomingAsset is null || !incomingAsset.HasVideo) continue;

                        var hasInterfering = false;
                        for (var checkIdx = 0; checkIdx < ordered.Count; checkIdx++)
                        {
                            var c = ordered[checkIdx];
                            if (c.Id != outgoing.Id && c.Id != incoming.Id && c.StartFrame <= frame && frame < c.EndFrame)
                            {
                                hasInterfering = true;
                                break;
                            }
                        }
                        if (hasInterfering) continue;

                        var progress = (double)incomingPosition / incoming.FadeInFrames;
                        var inWeight = incoming.FadeInTransition is null
                            ? VideoFadeInterpolation.Evaluate(incoming.FadeInCurve, progress)
                            : progress;
                        var outWeight = incoming.FadeInTransition is null
                            ? 1 - VideoFadeInterpolation.Evaluate(incoming.FadeInReciprocalCurve, progress)
                            : 1 - progress;
                        var incomingAlpha = track.Opacity * incoming.Opacity * inWeight;

                        transitionOpacity ??= [];
                        dissolves ??= [];

                        // Compensate for the incoming alpha-over pass so lower tracks remain visible at the blend weight.
                        transitionOpacity[outgoing.Id] = incomingAlpha >= 1 ? 0 : outWeight / (1 - incomingAlpha);
                        transitionOpacity[incoming.Id] = inWeight;
                        var dissolve = new EvaluatedDissolve(outgoing.Id, incoming.Id, progress, outWeight, inWeight, incoming.FadeInTransition);
                        dissolves[outgoing.Id] = dissolve;
                        dissolves[incoming.Id] = dissolve;
                    }
                }
            }

            for (var cIdx = 0; cIdx < ordered.Count; cIdx++)
            {
                var clip = ordered[cIdx];
                if (clip.IsMuted || frame < clip.StartFrame || frame >= clip.EndFrame) continue;
                var asset = FindMedia(mediaList, mediaMap, clip.MediaId);
                if (asset is null) continue;

                var local = frame - clip.StartFrame;
                var fade = (transitionOpacity is not null && transitionOpacity.TryGetValue(clip.Id, out var blend))
                    ? blend
                    : track.Kind == TrackKind.Video ? VideoFadeAt(clip, local) : FadeAt(clip, local);
                var (left, right) = BalanceStages(track, clip);
                var peak = Math.Max(left, right);
                var pan = peak == 0 ? 0 : left < right ? 1 - left / right : right / left - 1;

                // Vegas Pro Undersample Rate (stop-motion / strobe effect)
                if (clip.UnderSampleRate > 0.001 && clip.UnderSampleRate < 0.999)
                {
                    var underStep = Math.Max(1, (int)Math.Round(1.0 / clip.UnderSampleRate));
                    if (underStep > 1)
                    {
                        local = (local / underStep) * underStep;
                    }
                }

                var effPlaybackRate = double.IsFinite(clip.PlaybackRate) && clip.PlaybackRate != 0 ? clip.PlaybackRate : 1.0;
                var sourceFrame = FindEnabledEnvelope(clip.Envelopes, EnvelopeType.Velocity) is { } velocityEnv
                    ? velocityEnv.CalculateAccumulatedSourceFrame(clip.SourceInFrame, local, effPlaybackRate, clip.IsReversed, clip.DurationFrames)
                    : clip.IsReversed
                        ? clip.SourceInFrame + (long)Math.Round(Math.Max(0, clip.DurationFrames - 1 - local) * Math.Abs(effPlaybackRate))
                        : clip.SourceInFrame + (long)Math.Round(local * effPlaybackRate);

                var opacity = clip.Opacity * track.Opacity * fade;
                if (FindEnabledEnvelope(track.Envelopes, EnvelopeType.Composite) is { } trackComp)
                {
                    opacity *= trackComp.ValueAt(frame);
                }
                if (FindEnabledEnvelope(clip.Envelopes, EnvelopeType.Composite) is { } compositeEnv)
                {
                    opacity *= compositeEnv.ValueAt(local);
                }

                var gain = clip.Gain * track.Gain * fade * peak;
                if (FindEnabledEnvelope(track.Envelopes, EnvelopeType.Volume) is { } trackVol)
                {
                    gain *= trackVol.ValueAt(frame);
                }
                if (FindEnabledEnvelope(clip.Envelopes, EnvelopeType.Volume) is { } volumeEnv)
                {
                    gain *= volumeEnv.ValueAt(local);
                }

                if (FindEnabledEnvelope(track.Envelopes, EnvelopeType.Pan) is { } trackPan)
                {
                    pan = Math.Clamp(pan + trackPan.ValueAt(frame), -1.0, 1.0);
                }

                var item = new EvaluatedClip(track, clip, asset, sourceFrame, opacity, gain, pan);
                var trackMotion = track.Motion?.Evaluate(frame, project.Width, project.Height);
                if (track.Kind == TrackKind.Video && asset.HasVideo) video.Add(item with
                    { Crop = CropAt(clip, asset, local), Dissolve = dissolves?.GetValueOrDefault(clip.Id),
                        SourceTimeSeconds = VideoSourceTime.Seconds(project.FrameRate, item.SourceFrame, asset.DurationSeconds, clip.VideoSourceMode),
                        TrackMotion = trackMotion });
                if (track.Kind == TrackKind.Audio && asset.HasAudio)
                {
                    var audioItem = clip.InvertPhase ? item with { Gain = -item.Gain } : item;
                    audio.Add(audioItem);
                }
            }
        }
        return new(video, audio);
    }

    private static bool IsCanonicalCrossfade(TimelineClip outgoing, TimelineClip incoming, IReadOnlyList<TimelineClip> clips)
    {
        if (outgoing.FadeOutFrames != incoming.FadeInFrames) return false;
        var start = (decimal)incoming.StartFrame - incoming.FadeOffsetFrames;
        var end = (decimal)outgoing.StartFrame - outgoing.FadeOffsetFrames + (outgoing.FadeDurationFrames ?? outgoing.DurationFrames);
        var incomingEnd = start + (incoming.FadeDurationFrames ?? incoming.DurationFrames);
        if (end - start != incoming.FadeInFrames || incomingEnd < end) return false;

        // Split pieces share an envelope. Check every start boundary in it, including parts outside this frame.
        var overlap = new List<TimelineClip>(4);
        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            if (clip.StartFrame < end && clip.EndFrame > start)
                overlap.Add(clip);
        }

        for (var i = 0; i < overlap.Count; i++)
        {
            var position = Math.Max(start, overlap[i].StartFrame);
            var count = 0;
            for (var j = 0; j < overlap.Count; j++)
            {
                if (overlap[j].StartFrame <= position && position < overlap[j].EndFrame)
                {
                    count++;
                    if (count > 2) return false;
                }
            }
        }
        return true;
    }

    public static double VideoFadeAt(TimelineClip clip, double localFrame)
    {
        var envelope = clip.FadeDurationFrames ?? clip.DurationFrames;
        var position = localFrame + clip.FadeOffsetFrames;
        var fade = 1.0;
        if (clip.FadeInFrames > 0)
            fade = VideoFadeInterpolation.Evaluate(clip.FadeInCurve, position / clip.FadeInFrames);
        if (clip.FadeOutFrames > 0)
        {
            // A tail complements the curve's output; reversing its input swaps Fast and Slow.
            var progress = (position - (envelope - clip.FadeOutFrames)) / clip.FadeOutFrames;
            fade = Math.Min(fade, 1 - VideoFadeInterpolation.Evaluate(clip.FadeOutCurve, progress));
        }
        return fade;
    }

    public static double FadeAt(TimelineClip clip, double localFrame)
    {
        var envelope = clip.FadeDurationFrames ?? clip.DurationFrames;
        var position = localFrame + clip.FadeOffsetFrames;
        var fade = 1.0;
        if (clip.FadeInFrames > 0) fade = Math.Min(fade, position / clip.FadeInFrames);
        if (clip.FadeOutFrames > 0) fade = Math.Min(fade, (envelope - position) / clip.FadeOutFrames);
        return Math.Clamp(fade, 0, 1);
    }

    public static CropState CropAt(TimelineClip clip, MediaAsset media, long localFrame)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(media);
        return clip.Crop?.Evaluate(checked(localFrame + clip.CropOffsetFrames)) ?? CropState.Identity(media.Width, media.Height);
    }

    public static MaskShape? MaskAt(TimelineClip clip, long localFrame)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (clip.Mask is null || !clip.Mask.IsEnabled) return null;
        return clip.Mask.Evaluate(checked(localFrame + clip.CropOffsetFrames));
    }

    public static (double Left, double Right) AudioGains(TimelineTrack track, TimelineClip clip, double localFrame)
    {
        var gain = track.Gain * clip.Gain * FadeAt(clip, localFrame);
        if (clip.InvertPhase) gain = -gain;
        var (left, right) = BalanceStages(track, clip);
        return (gain * left, gain * right);
    }

    private static bool IsTrackEffectivelyMuted(ProjectDocument project, TimelineTrack track, int trackIndex)
    {
        if (track.IsMuted) return true;
        if (IsTrackMutedInGroups(project.TrackGroups, track.Id)) return true;
        if (track.Kind == TrackKind.Video && !track.IsVisible) return true;
        if (track.Kind == TrackKind.Video && track.CompositeNestingLevel > 0)
        {
            var targetLevel = track.CompositeNestingLevel;
            for (var i = trackIndex - 1; i >= 0; i--)
            {
                var ancestor = project.Tracks[i];
                if (ancestor.Kind != TrackKind.Video) continue;
                if (ancestor.CompositeNestingLevel < targetLevel)
                {
                    if (ancestor.IsMuted || !ancestor.IsVisible) return true;
                    targetLevel = ancestor.CompositeNestingLevel;
                    if (targetLevel == 0) break;
                }
            }
        }
        return false;
    }

    private static bool IsTrackEffectivelySolo(ProjectDocument project, TimelineTrack track, int trackIndex)
    {
        if (track.IsSolo) return true;
        if (IsTrackSoloInGroups(project.TrackGroups, track.Id)) return true;
        if (track.Kind == TrackKind.Video)
        {
            for (var i = trackIndex + 1; i < project.Tracks.Count; i++)
            {
                var next = project.Tracks[i];
                if (next.Kind != TrackKind.Video) continue;
                if (next.CompositeNestingLevel <= track.CompositeNestingLevel) break;
                if (next.IsSolo) return true;
            }
            var targetLevel = track.CompositeNestingLevel;
            for (var i = trackIndex - 1; i >= 0; i--)
            {
                var ancestor = project.Tracks[i];
                if (ancestor.Kind != TrackKind.Video) continue;
                if (ancestor.CompositeNestingLevel < targetLevel)
                {
                    if (ancestor.IsSolo) return true;
                    targetLevel = ancestor.CompositeNestingLevel;
                    if (targetLevel == 0) break;
                }
            }
        }
        return false;
    }

    private static (double Left, double Right) BalanceStages(TimelineTrack track, TimelineClip clip)
    {
        var (tLeft, tRight) = AudioPanLaw.CalculateGains(track.PanMode, track.Pan);
        var (cLeft, cRight) = AudioPanLaw.CalculateGains(AudioPanMode.Balance, clip.Pan);
        // Opposing balance stages attenuate both channels rather than cancelling their pans.
        return (cLeft * tLeft, cRight * tRight);
    }
}