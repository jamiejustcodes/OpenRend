using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    /// <summary>
    /// Evaluates whether the specified clip IDs (or adjacent neighbor for a single clip) contain healable split boundaries.
    /// Aligned with Vegas Pro "Edit > Event Heal" (command ID 44709).
    /// </summary>
    public bool CanHeal(IEnumerable<Guid> clipIds)
    {
        var ids = clipIds?.ToHashSet();
        if (ids == null || ids.Count == 0) return false;

        if (ids.Count == 1)
        {
            var targetId = ids.First();
            var match = Project.Tracks.SelectMany(t => t.Clips.Where(c => c.Id == targetId).Select(c => (Track: t, Clip: c))).FirstOrDefault();
            if (match.Track == null || match.Clip == null || match.Track.IsLocked || match.Clip.IsLocked) return false;

            var (track, clip) = match;
            var prev = track.Clips.FirstOrDefault(c => c.Id != clip.Id && c.StartFrame + c.DurationFrames == clip.StartFrame);
            if (prev != null && AreClipsHealable(prev, clip, track)) return true;

            var next = track.Clips.FirstOrDefault(c => c.Id != clip.Id && clip.StartFrame + clip.DurationFrames == c.StartFrame);
            if (next != null && AreClipsHealable(clip, next, track)) return true;

            return false;
        }

        foreach (var track in Project.Tracks)
        {
            if (track.IsLocked) continue;
            var trackClips = track.Clips.Where(c => ids.Contains(c.Id) && !c.IsLocked).OrderBy(c => c.StartFrame).ToList();
            for (int i = 0; i < trackClips.Count - 1; i++)
            {
                if (AreClipsHealable(trackClips[i], trackClips[i + 1], track))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Joins split contiguous events sharing the same source stream back into a unified event.
    /// Preserves original source frame alignment, fades, envelopes, crop, takes, and group sync.
    /// Aligned with Vegas Pro "Edit > Event Heal" (command ID 44709).
    /// </summary>
    public IReadOnlyList<TimelineClip> HealEvents(IEnumerable<Guid> clipIds, bool ignoreGroups = false)
    {
        var inputIds = clipIds?.ToHashSet() ?? [];
        if (inputIds.Count == 0) return [];

        var expandedIds = ignoreGroups ? new HashSet<Guid>(inputIds) : ExpandSelection(inputIds, ignoreGroups: false);
        var healedClips = new List<TimelineClip>();

        Execute("Event Heal", _ =>
        {
            // If single clip selected, check its immediate left/right neighbor on track
            if (inputIds.Count == 1)
            {
                var singleId = inputIds.First();
                var match = Project.Tracks.SelectMany(t => t.Clips.Where(c => c.Id == singleId).Select(c => (Track: t, Clip: c))).FirstOrDefault();
                if (match.Track != null && match.Clip != null && !match.Track.IsLocked && !match.Clip.IsLocked)
                {
                    var (track, clip) = match;
                    var prev = track.Clips.FirstOrDefault(c => c.Id != clip.Id && c.StartFrame + c.DurationFrames == clip.StartFrame);
                    if (prev != null && AreClipsHealable(prev, clip, track))
                    {
                        expandedIds.Add(prev.Id);
                        if (!ignoreGroups && prev.GroupId is Guid g1)
                        {
                            foreach (var pClip in Project.Tracks.SelectMany(t => t.Clips.Where(c => c.GroupId == g1)))
                                expandedIds.Add(pClip.Id);
                        }
                    }
                    else
                    {
                        var next = track.Clips.FirstOrDefault(c => c.Id != clip.Id && clip.StartFrame + clip.DurationFrames == c.StartFrame);
                        if (next != null && AreClipsHealable(clip, next, track))
                        {
                            expandedIds.Add(next.Id);
                            if (!ignoreGroups && next.GroupId is Guid g2)
                            {
                                foreach (var nClip in Project.Tracks.SelectMany(t => t.Clips.Where(c => c.GroupId == g2)))
                                    expandedIds.Add(nClip.Id);
                            }
                        }
                    }
                }
            }

            foreach (var track in Project.Tracks)
            {
                if (track.IsLocked) continue;

                bool trackModified;
                do
                {
                    trackModified = false;
                    var clips = track.Clips.OrderBy(c => c.StartFrame).ToList();
                    for (int i = 0; i < clips.Count - 1; i++)
                    {
                        var left = clips[i];
                        var right = clips[i + 1];

                        if (expandedIds.Contains(left.Id) && expandedIds.Contains(right.Id) && AreClipsHealable(left, right, track))
                        {
                            MergeClipInto(left, right);
                            track.Clips.Remove(right);
                            expandedIds.Remove(right.Id);
                            expandedIds.Add(left.Id);
                            if (!healedClips.Any(c => c.Id == left.Id))
                                healedClips.Add(left);

                            trackModified = true;
                            break;
                        }
                    }
                } while (trackModified);
            }
        });

        return healedClips;
    }

    /// <summary>
    /// Closes empty timeline gaps between selected events or across all events on a single track.
    /// Shifts subsequent events leftward to touch preceding event ends and preserves group sync.
    /// Aligned with Vegas Pro "Edit > Close Gaps" (command ID 44713).
    /// </summary>
    public void CloseGaps(IEnumerable<Guid>? clipIds = null, int? singleTrackIndex = null)
    {
        var ids = clipIds?.ToHashSet();

        Execute("Close Gaps", _ =>
        {
            if (ids != null && ids.Count >= 2)
            {
                // Multi-event mode: close gaps between selected events on each track
                foreach (var track in Project.Tracks)
                {
                    if (track.IsLocked) continue;

                    var trackSelected = track.Clips
                        .Where(c => ids.Contains(c.Id) && !c.IsLocked)
                        .OrderBy(c => c.StartFrame)
                        .ToList();

                    if (trackSelected.Count < 2) continue;

                    long cumulativeShift = 0;
                    for (int i = 1; i < trackSelected.Count; i++)
                    {
                        var prev = trackSelected[i - 1];
                        var curr = trackSelected[i];
                        var prevEnd = prev.StartFrame + prev.DurationFrames;
                        var currShiftedStart = curr.StartFrame - cumulativeShift;

                        if (currShiftedStart > prevEnd)
                        {
                            var gap = currShiftedStart - prevEnd;
                            cumulativeShift += gap;
                        }

                        if (cumulativeShift > 0)
                        {
                            curr.StartFrame -= cumulativeShift;

                            // Synchronize other clips in the same group on other tracks
                            if (curr.GroupId is Guid grp)
                            {
                                foreach (var otherTrack in Project.Tracks.Where(t => t.Id != track.Id && !t.IsLocked))
                                {
                                    foreach (var otherClip in otherTrack.Clips.Where(c => c.GroupId == grp && !c.IsLocked))
                                    {
                                        otherClip.StartFrame -= cumulativeShift;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // Single-track mode: close all gaps between events on targeted track
                var targetIdx = singleTrackIndex ?? 0;
                if (targetIdx < 0 || targetIdx >= Project.Tracks.Count) return;

                var track = Project.Tracks[targetIdx];
                if (track.IsLocked) return;

                var orderedClips = track.Clips.Where(c => !c.IsLocked).OrderBy(c => c.StartFrame).ToList();
                if (orderedClips.Count < 2) return;

                long cumulativeShift = 0;
                for (int i = 1; i < orderedClips.Count; i++)
                {
                    var prev = orderedClips[i - 1];
                    var curr = orderedClips[i];
                    var prevEnd = prev.StartFrame + prev.DurationFrames;
                    var currShiftedStart = curr.StartFrame - cumulativeShift;

                    if (currShiftedStart > prevEnd)
                    {
                        var gap = currShiftedStart - prevEnd;
                        cumulativeShift += gap;
                    }

                    if (cumulativeShift > 0)
                    {
                        curr.StartFrame -= cumulativeShift;

                        if (curr.GroupId is Guid grp)
                        {
                            foreach (var otherTrack in Project.Tracks.Where(t => t.Id != track.Id && !t.IsLocked))
                            {
                                foreach (var otherClip in otherTrack.Clips.Where(c => c.GroupId == grp && !c.IsLocked))
                                {
                                    otherClip.StartFrame -= cumulativeShift;
                                }
                            }
                        }
                    }
                }
            }
        });
    }

    /// <summary>
    /// Evaluates whether any of the specified clips are missing their complementary audio/video stream from the source media.
    /// Aligned with Vegas Pro "Insert > Add Missing Stream for Selected Event" (command ID 44712).
    /// </summary>
    public bool CanAddMissingStream(IEnumerable<Guid> clipIds)
    {
        var ids = clipIds?.ToHashSet();
        if (ids == null || ids.Count == 0) return false;

        foreach (var id in ids)
        {
            var match = Project.Tracks.SelectMany(t => t.Clips.Where(c => c.Id == id).Select(c => (Track: t, Clip: c))).FirstOrDefault();
            if (match.Track == null || match.Clip == null || match.Track.IsLocked || match.Clip.IsLocked) continue;

            var (track, clip) = match;
            if (clip.IsAdjustmentEvent || clip.MediaId == Guid.Empty) continue;

            var media = Project.Media.FirstOrDefault(m => m.Id == clip.MediaId);
            if (media == null) continue;

            if (track.Kind == TrackKind.Video && media.HasAudio)
            {
                var hasAudio = Project.Tracks
                    .Where(t => t.Kind == TrackKind.Audio)
                    .SelectMany(t => t.Clips)
                    .Any(c => (clip.GroupId != null && c.GroupId == clip.GroupId) ||
                              (c.MediaId == clip.MediaId && c.StartFrame == clip.StartFrame && c.DurationFrames == clip.DurationFrames));
                if (!hasAudio) return true;
            }
            else if (track.Kind == TrackKind.Audio && media.HasVideo)
            {
                var hasVideo = Project.Tracks
                    .Where(t => t.Kind == TrackKind.Video)
                    .SelectMany(t => t.Clips)
                    .Any(c => (clip.GroupId != null && c.GroupId == clip.GroupId) ||
                              (c.MediaId == clip.MediaId && c.StartFrame == clip.StartFrame && c.DurationFrames == clip.DurationFrames));
                if (!hasVideo) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds complementary audio or video stream for selected events from source media assets.
    /// Links newly created complementary stream into a group with the source event.
    /// Aligned with Vegas Pro "Insert > Add Missing Stream for Selected Event" (command ID 44712).
    /// </summary>
    public IReadOnlyList<TimelineClip> AddMissingStream(IEnumerable<Guid> clipIds)
    {
        var targets = clipIds?.ToList() ?? [];
        var createdClips = new List<TimelineClip>();
        if (targets.Count == 0) return createdClips;

        Execute("Add Missing Stream for Selected Event", _ =>
        {
            foreach (var id in targets)
            {
                var match = Project.Tracks.SelectMany(t => t.Clips.Where(c => c.Id == id).Select(c => (Track: t, Clip: c))).FirstOrDefault();
                if (match.Track == null || match.Clip == null) continue;

                var (track, clip) = match;
                if (track.IsLocked || clip.IsLocked || clip.IsAdjustmentEvent || clip.MediaId == Guid.Empty) continue;

                var media = Project.Media.FirstOrDefault(m => m.Id == clip.MediaId);
                if (media == null) continue;

                if (track.Kind == TrackKind.Video)
                {
                    if (!media.HasAudio) continue;

                    var hasAudio = Project.Tracks
                        .Where(t => t.Kind == TrackKind.Audio)
                        .SelectMany(t => t.Clips)
                        .Any(c => (clip.GroupId != null && c.GroupId == clip.GroupId) ||
                                  (c.MediaId == clip.MediaId && c.StartFrame == clip.StartFrame && c.DurationFrames == clip.DurationFrames));
                    if (hasAudio) continue;

                    var trackIdx = Project.Tracks.IndexOf(track);
                    var audioTrack = Project.Tracks
                        .Skip(trackIdx + 1)
                        .FirstOrDefault(t => t.Kind == TrackKind.Audio && !t.IsLocked && !t.Clips.Any(c => c.StartFrame < clip.EndFrame && c.EndFrame > clip.StartFrame));

                    if (audioTrack == null)
                    {
                        audioTrack = new TimelineTrack
                        {
                            Id = Guid.NewGuid(),
                            Kind = TrackKind.Audio,
                            Name = $"Audio {Project.Tracks.Count(t => t.Kind == TrackKind.Audio) + 1}"
                        };
                        Project.Tracks.Insert(trackIdx + 1, audioTrack);
                    }

                    clip.GroupId ??= Guid.NewGuid();

                    var newAudio = new TimelineClip
                    {
                        Id = Guid.NewGuid(),
                        GroupId = clip.GroupId,
                        MediaId = clip.MediaId,
                        Name = clip.Name,
                        StartFrame = clip.StartFrame,
                        DurationFrames = clip.DurationFrames,
                        SourceInFrame = clip.SourceInFrame,
                        PlaybackRate = clip.PlaybackRate,
                        IsReversed = clip.IsReversed,
                        Gain = 1.0,
                        Pan = 0.0,
                        FadeInFrames = clip.FadeInFrames,
                        FadeOutFrames = clip.FadeOutFrames
                    };

                    audioTrack.Clips.Add(newAudio);
                    createdClips.Add(newAudio);
                }
                else if (track.Kind == TrackKind.Audio)
                {
                    if (!media.HasVideo) continue;

                    var hasVideo = Project.Tracks
                        .Where(t => t.Kind == TrackKind.Video)
                        .SelectMany(t => t.Clips)
                        .Any(c => (clip.GroupId != null && c.GroupId == clip.GroupId) ||
                                  (c.MediaId == clip.MediaId && c.StartFrame == clip.StartFrame && c.DurationFrames == clip.DurationFrames));
                    if (hasVideo) continue;

                    var trackIdx = Project.Tracks.IndexOf(track);
                    var videoTrack = Project.Tracks
                        .Take(trackIdx)
                        .Reverse()
                        .FirstOrDefault(t => t.Kind == TrackKind.Video && !t.IsLocked && !t.Clips.Any(c => c.StartFrame < clip.EndFrame && c.EndFrame > clip.StartFrame));

                    if (videoTrack == null)
                    {
                        videoTrack = new TimelineTrack
                        {
                            Id = Guid.NewGuid(),
                            Kind = TrackKind.Video,
                            Name = $"Video {Project.Tracks.Count(t => t.Kind == TrackKind.Video) + 1}"
                        };
                        Project.Tracks.Insert(trackIdx, videoTrack);
                    }

                    clip.GroupId ??= Guid.NewGuid();

                    var newVideo = new TimelineClip
                    {
                        Id = Guid.NewGuid(),
                        GroupId = clip.GroupId,
                        MediaId = clip.MediaId,
                        Name = clip.Name,
                        StartFrame = clip.StartFrame,
                        DurationFrames = clip.DurationFrames,
                        SourceInFrame = clip.SourceInFrame,
                        PlaybackRate = clip.PlaybackRate,
                        IsReversed = clip.IsReversed,
                        Opacity = 1.0,
                        MaintainAspectRatio = true,
                        FadeInFrames = clip.FadeInFrames,
                        FadeOutFrames = clip.FadeOutFrames
                    };

                    videoTrack.Clips.Add(newVideo);
                    createdClips.Add(newVideo);
                }
            }
        });

        return createdClips;
    }

    private static bool AreClipsHealable(TimelineClip left, TimelineClip right, TimelineTrack track)
    {
        if (track.IsLocked || left.IsLocked || right.IsLocked) return false;
        if (left.StartFrame + left.DurationFrames != right.StartFrame) return false;

        // Adjustment events
        if (left.IsAdjustmentEvent && right.IsAdjustmentEvent)
        {
            return left.FrameWidth == right.FrameWidth &&
                   left.FrameHeight == right.FrameHeight &&
                   Math.Abs(left.PixelAspectRatio - right.PixelAspectRatio) < 1e-4;
        }

        // Must share same source media asset
        if (left.MediaId == Guid.Empty || left.MediaId != right.MediaId) return false;

        // Playback rate and direction
        if (Math.Abs(left.PlaybackRate - right.PlaybackRate) > 1e-6) return false;
        if (left.IsReversed != right.IsReversed) return false;

        // Expected source frame advancement
        var effRate = double.IsFinite(left.PlaybackRate) && left.PlaybackRate != 0 ? left.PlaybackRate : 1.0;
        long expectedSourceIn;
        if (left.Envelopes?.FirstOrDefault(e => e.Type == EnvelopeType.Velocity && e.IsEnabled) is { } velEnv)
        {
            expectedSourceIn = velEnv.CalculateAccumulatedSourceFrame(left.SourceInFrame, left.DurationFrames, effRate, left.IsReversed, left.DurationFrames);
        }
        else
        {
            expectedSourceIn = left.IsReversed
                ? left.SourceInFrame
                : checked(left.SourceInFrame + (long)Math.Round(left.DurationFrames * Math.Abs(effRate)));
        }

        return Math.Abs(right.SourceInFrame - expectedSourceIn) <= 1;
    }

    private static void MergeClipInto(TimelineClip left, TimelineClip right)
    {
        var leftOrigDuration = left.DurationFrames;
        left.DurationFrames += right.DurationFrames;

        // Fades: left provides FadeIn, right provides FadeOut
        left.FadeOutFrames = right.FadeOutFrames;
        left.FadeOutCurve = right.FadeOutCurve;
        left.FadeOutReciprocalCurve = right.FadeOutReciprocalCurve;
        left.FadeOutTransition = right.FadeOutTransition?.DeepClone();

        // Envelopes: merge right envelopes shifted by leftOrigDuration
        if (right.Envelopes != null && right.Envelopes.Count > 0)
        {
            left.Envelopes ??= [];
            foreach (var rEnv in right.Envelopes)
            {
                var lEnv = left.Envelopes.FirstOrDefault(e => e.Type == rEnv.Type);
                if (lEnv == null)
                {
                    lEnv = new ClipEnvelope { Type = rEnv.Type, IsEnabled = rEnv.IsEnabled };
                    left.Envelopes.Add(lEnv);
                }
                foreach (var pt in rEnv.Points)
                {
                    var targetFrame = pt.Frame + leftOrigDuration;
                    var existing = lEnv.Points.FirstOrDefault(p => p.Frame == targetFrame);
                    if (existing != null)
                    {
                        existing.Value = pt.Value;
                        existing.Curve = pt.Curve;
                    }
                    else
                    {
                        lEnv.Points.Add(new EnvelopePoint(targetFrame, pt.Value, pt.Curve));
                    }
                }
                lEnv.Points.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            }
        }

        // Crop: merge keyframes shifted by leftOrigDuration
        if (right.Crop?.Keyframes != null && right.Crop.Keyframes.Count > 0)
        {
            if (left.Crop == null)
            {
                left.Crop = right.Crop.DeepClone();
                foreach (var kf in left.Crop.Keyframes)
                {
                    kf.Frame += leftOrigDuration;
                }
            }
            else
            {
                foreach (var kf in right.Crop.Keyframes)
                {
                    var targetFrame = kf.Frame + leftOrigDuration;
                    var existing = left.Crop.Keyframes.FirstOrDefault(k => k.Frame == targetFrame);
                    if (existing != null)
                    {
                        existing.State = kf.State;
                        existing.Interpolation = kf.Interpolation;
                        existing.Smoothness = kf.Smoothness;
                    }
                    else
                    {
                        var clone = kf.DeepClone();
                        clone.Frame = targetFrame;
                        left.Crop.Keyframes.Add(clone);
                    }
                }
                left.Crop.Keyframes.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            }
        }

        // Mask: merge keyframes shifted by leftOrigDuration
        if (right.Mask?.Keyframes != null && right.Mask.Keyframes.Count > 0)
        {
            if (left.Mask == null)
            {
                left.Mask = right.Mask.DeepClone();
                foreach (var kf in left.Mask.Keyframes)
                {
                    kf.Frame += leftOrigDuration;
                }
            }
            else
            {
                foreach (var kf in right.Mask.Keyframes)
                {
                    var targetFrame = kf.Frame + leftOrigDuration;
                    var existing = left.Mask.Keyframes.FirstOrDefault(k => k.Frame == targetFrame);
                    if (existing != null)
                    {
                        existing.Shape = kf.Shape.DeepClone();
                    }
                    else
                    {
                        var clone = kf.DeepClone();
                        clone.Frame = targetFrame;
                        left.Mask.Keyframes.Add(clone);
                    }
                }
                left.Mask.Keyframes.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            }
        }

        // Transcript words:
        if (right.Transcript?.Words != null && right.Transcript.Words.Count > 0)
        {
            if (left.Transcript == null)
            {
                left.Transcript = right.Transcript.DeepClone();
                foreach (var w in left.Transcript.Words)
                {
                    w.StartFrame += leftOrigDuration;
                }
            }
            else
            {
                foreach (var w in right.Transcript.Words)
                {
                    var clone = w.DeepClone();
                    clone.StartFrame += leftOrigDuration;
                    left.Transcript.Words.Add(clone);
                }
                left.Transcript.Words.Sort((a, b) => a.StartFrame.CompareTo(b.StartFrame));
            }
        }
    }
}
