using System;
using System.Collections.Generic;
using System.Linq;
using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    /// <summary>
    /// Evaluates whether a Smart Split operation can be performed on the given time range and selected clips.
    /// </summary>
    public bool CanSmartSplit(long startFrame, long durationFrames, IReadOnlyCollection<Guid>? selectedClipIds = null)
    {
        if (durationFrames <= 0) return false;
        var endFrame = checked(startFrame + durationFrames);

        if (selectedClipIds != null && selectedClipIds.Count > 0)
        {
            var targetIds = selectedClipIds.ToHashSet();
            return Project.Tracks.Any(t => !t.IsLocked &&
                t.Clips.Any(c => targetIds.Contains(c.Id) && !c.IsLocked && c.StartFrame < endFrame && c.EndFrame > startFrame));
        }

        return Project.Tracks.Any(t => !t.IsLocked &&
            t.Clips.Any(c => !c.IsLocked && c.StartFrame < endFrame && c.EndFrame > startFrame));
    }

    /// <summary>
    /// Splits events across the specified time selection, removes the interior duration, closes the gap,
    /// overlaps the cut edges with a smooth crossfade transition, and groups the resulting slices.
    /// Handles grouped companion audio/video clips synchronously to maintain sync.
    /// </summary>
    public IReadOnlyList<TimelineClip> SmartSplit(
        long startFrame,
        long durationFrames,
        int overlapFrames = 5,
        IReadOnlyCollection<Guid>? selectedClipIds = null,
        bool ignoreGroups = false,
        AutoRippleMode rippleMode = AutoRippleMode.AffectedTracks)
    {
        if (durationFrames <= 0) throw new ArgumentOutOfRangeException(nameof(durationFrames), "Duration must be positive.");
        startFrame = Math.Max(0, startFrame);
        var endFrame = checked(startFrame + durationFrames);
        var resultingClips = new List<TimelineClip>();

        Execute("Smart Split", project =>
        {
            // Determine targeted clip IDs
            HashSet<Guid> targetClipIds;
            if (selectedClipIds != null && selectedClipIds.Count > 0)
            {
                var expanded = ignoreGroups ? selectedClipIds.ToHashSet() : ExpandSelection(selectedClipIds, false);
                targetClipIds = project.Tracks
                    .Where(t => !t.IsLocked)
                    .SelectMany(t => t.Clips)
                    .Where(c => expanded.Contains(c.Id) && !c.IsLocked && c.StartFrame < endFrame && c.EndFrame > startFrame)
                    .Select(c => c.Id)
                    .ToHashSet();
            }
            else
            {
                targetClipIds = project.Tracks
                    .Where(t => !t.IsLocked)
                    .SelectMany(t => t.Clips)
                    .Where(c => !c.IsLocked && c.StartFrame < endFrame && c.EndFrame > startFrame)
                    .Select(c => c.Id)
                    .ToHashSet();
            }

            if (targetClipIds.Count == 0) return;

            // Determine affected tracks
            var targetTracks = rippleMode switch
            {
                AutoRippleMode.AffectedTracks => project.Tracks.Where(t => !t.IsLocked && t.Clips.Any(c => targetClipIds.Contains(c.Id) || c.EndFrame > startFrame)).ToList(),
                _ => project.Tracks.Where(t => !t.IsLocked).ToList()
            };
            if (targetTracks.Count == 0) targetTracks = project.Tracks.Where(t => !t.IsLocked).ToList();

            // Store new right slices to add after iterating
            var clipsToAdd = new List<(TimelineTrack Track, TimelineClip Clip)>();

            foreach (var track in targetTracks)
            {
                var clipsToProcess = track.Clips.ToList();
                foreach (var clip in clipsToProcess)
                {
                    if (!targetClipIds.Contains(clip.Id))
                    {
                        // Not targeted: if it is downstream and starting at/after endFrame, ripple it leftward
                        if (clip.StartFrame >= endFrame)
                        {
                            clip.StartFrame = Math.Max(0, checked(clip.StartFrame - durationFrames));
                        }
                        continue;
                    }

                    // Case 1: Clip completely inside deletion span -> remove
                    if (clip.StartFrame >= startFrame && clip.EndFrame <= endFrame)
                    {
                        track.Clips.Remove(clip);
                    }
                    // Case 2: Clip straddles across time selection -> Smart Split with transition overlap & grouping
                    else if (clip.StartFrame < startFrame && clip.EndFrame > endFrame)
                    {
                        var leftDuration = startFrame - clip.StartFrame;
                        var rightOrigDuration = clip.EndFrame - endFrame;
                        var cutOffset = endFrame - clip.StartFrame;

                        // Calculate feasible overlap duration
                        var maxOverlap = Math.Min(leftDuration / 2, rightOrigDuration / 2);
                        var effOverlap = Math.Clamp(overlapFrames, 0, (int)maxOverlap);
                        if (effOverlap < 1 && leftDuration >= 2 && rightOrigDuration >= 2) effOverlap = 1;

                        var effPlaybackRate = double.IsFinite(clip.PlaybackRate) && clip.PlaybackRate != 0 ? clip.PlaybackRate : 1.0;
                        long rightSourceIn;
                        if (clip.Envelopes?.FirstOrDefault(e => e.Type == EnvelopeType.Velocity && e.IsEnabled) is { } velEnv)
                        {
                            rightSourceIn = velEnv.CalculateAccumulatedSourceFrame(clip.SourceInFrame, cutOffset, effPlaybackRate, clip.IsReversed, clip.DurationFrames);
                        }
                        else
                        {
                            rightSourceIn = clip.IsReversed
                                ? clip.SourceInFrame
                                : checked(clip.SourceInFrame + (long)Math.Round(cutOffset * Math.Abs(effPlaybackRate)));
                        }

                        // Right slice extends head by effOverlap so it can dissolve across the cut boundary
                        var sourceHeadOffset = (long)Math.Round(effOverlap * Math.Abs(effPlaybackRate));
                        var adjustedRightSourceIn = (rightSourceIn >= sourceHeadOffset && !clip.IsReversed)
                            ? checked(rightSourceIn - sourceHeadOffset)
                            : rightSourceIn;

                        var rightClip = clip.DeepClone();
                        rightClip.Id = Guid.NewGuid();
                        rightClip.StartFrame = checked(startFrame - effOverlap);
                        rightClip.SourceInFrame = adjustedRightSourceIn;
                        rightClip.DurationFrames = checked(rightOrigDuration + effOverlap);
                        if (rightClip.Crop is not null) rightClip.CropOffsetFrames = checked(rightClip.CropOffsetFrames + cutOffset - effOverlap);
                        rightClip.FadeOffsetFrames = 0;
                        rightClip.FadeDurationFrames = null;
                        var fadeCurve = track.Kind == TrackKind.Video ? VideoFadeCurve.Smooth : VideoFadeCurve.Linear;
                        rightClip.FadeInFrames = effOverlap;
                        rightClip.FadeInCurve = fadeCurve;
                        rightClip.FadeOutFrames = Math.Min(rightClip.FadeOutFrames, checked(rightClip.DurationFrames - rightClip.FadeInFrames));

                        // Split envelopes
                        if (clip.Envelopes != null)
                        {
                            var leftEnvelopes = new List<ClipEnvelope>();
                            var rightEnvelopes = new List<ClipEnvelope>();
                            foreach (var env in clip.Envelopes)
                            {
                                var (leftEnv, _) = env.SplitAt(leftDuration);
                                var splitPoint = Math.Max(0, checked(cutOffset - effOverlap));
                                var (_, rightEnv) = env.SplitAt(splitPoint);
                                leftEnvelopes.Add(leftEnv);
                                rightEnvelopes.Add(rightEnv);
                            }
                            clip.Envelopes = leftEnvelopes;
                            rightClip.Envelopes = rightEnvelopes;
                        }

                        // Adjust left slice
                        clip.DurationFrames = leftDuration;
                        clip.FadeOffsetFrames = 0;
                        clip.FadeDurationFrames = null;
                        clip.FadeInFrames = Math.Min(clip.FadeInFrames, clip.DurationFrames);
                        clip.FadeOutFrames = effOverlap;
                        clip.FadeOutCurve = fadeCurve;

                        // Ensure shared group
                        var groupId = clip.GroupId ?? Guid.NewGuid();
                        clip.GroupId = groupId;
                        rightClip.GroupId = groupId;

                        clipsToAdd.Add((track, rightClip));
                        resultingClips.Add(clip);
                        resultingClips.Add(rightClip);
                    }
                    // Case 3: Clip straddles left boundary (starts before, ends inside) -> trim tail
                    else if (clip.StartFrame < startFrame && clip.EndFrame > startFrame)
                    {
                        var leftDuration = startFrame - clip.StartFrame;
                        if (clip.Envelopes != null)
                        {
                            clip.Envelopes = clip.Envelopes.Select(e => e.SplitAt(leftDuration).Left).ToList();
                        }
                        clip.DurationFrames = leftDuration;
                        clip.FadeOffsetFrames = 0;
                        clip.FadeDurationFrames = null;
                        clip.FadeInFrames = Math.Min(clip.FadeInFrames, clip.DurationFrames);
                        clip.FadeOutFrames = Math.Min(clip.FadeOutFrames, checked(clip.DurationFrames - clip.FadeInFrames));
                        resultingClips.Add(clip);
                    }
                    // Case 4: Clip straddles right boundary (starts inside, ends after) -> trim head and shift
                    else if (clip.StartFrame >= startFrame && clip.StartFrame < endFrame)
                    {
                        var removedFromHead = endFrame - clip.StartFrame;
                        var effPlaybackRate = double.IsFinite(clip.PlaybackRate) && clip.PlaybackRate != 0 ? clip.PlaybackRate : 1.0;
                        if (clip.Envelopes?.FirstOrDefault(e => e.Type == EnvelopeType.Velocity && e.IsEnabled) is { } velEnv)
                        {
                            clip.SourceInFrame = velEnv.CalculateAccumulatedSourceFrame(clip.SourceInFrame, removedFromHead, effPlaybackRate, clip.IsReversed, clip.DurationFrames);
                        }
                        else
                        {
                            clip.SourceInFrame = clip.IsReversed
                                ? clip.SourceInFrame
                                : checked(clip.SourceInFrame + (long)Math.Round(removedFromHead * Math.Abs(effPlaybackRate)));
                        }

                        if (clip.Envelopes != null)
                        {
                            clip.Envelopes = clip.Envelopes.Select(e => e.SplitAt(removedFromHead).Right).ToList();
                        }

                        clip.StartFrame = startFrame;
                        clip.DurationFrames = checked(clip.DurationFrames - removedFromHead);
                        if (clip.Crop is not null) clip.CropOffsetFrames = checked(clip.CropOffsetFrames + removedFromHead);
                        clip.FadeOffsetFrames = 0;
                        clip.FadeDurationFrames = null;
                        clip.FadeInFrames = Math.Min(clip.FadeInFrames, clip.DurationFrames);
                        clip.FadeOutFrames = Math.Min(clip.FadeOutFrames, checked(clip.DurationFrames - clip.FadeInFrames));
                        resultingClips.Add(clip);
                    }
                    // Case 5: Clip strictly after endFrame -> shift left by durationFrames
                    else if (clip.StartFrame >= endFrame)
                    {
                        clip.StartFrame = Math.Max(0, checked(clip.StartFrame - durationFrames));
                    }
                }

                if (track.Envelopes != null)
                {
                    var deleteSpan = (Start: startFrame, End: endFrame);
                    foreach (var env in track.Envelopes)
                    {
                        env.RippleForSpans([deleteSpan]);
                    }
                }
            }

            // Add newly created right slices to tracks
            foreach (var (t, c) in clipsToAdd)
            {
                t.Clips.Add(c);
            }

            // Ripple markers and regions if mode specifies
            if (rippleMode is AutoRippleMode.AffectedTracksAndMarkers or AutoRippleMode.AllTracksAndMarkers)
            {
                project.Markers.RemoveAll(m => m.Frame > startFrame && m.Frame < endFrame);
                foreach (var marker in project.Markers)
                {
                    if (marker.Frame >= endFrame)
                    {
                        marker.Frame = Math.Max(0, checked(marker.Frame - durationFrames));
                    }
                }

                project.Regions.RemoveAll(r => r.StartFrame >= startFrame && r.EndFrame <= endFrame);
                foreach (var region in project.Regions)
                {
                    if (region.StartFrame >= endFrame)
                    {
                        region.StartFrame = Math.Max(0, checked(region.StartFrame - durationFrames));
                        region.EndFrame = Math.Max(region.StartFrame + 1, checked(region.EndFrame - durationFrames));
                    }
                    else if (region.EndFrame > startFrame)
                    {
                        region.EndFrame = Math.Max(region.StartFrame + 1, checked(region.EndFrame - durationFrames));
                    }
                }
            }
        });

        return resultingClips;
    }

    /// <summary>
    /// Evaluates whether a Freeze Frame at Cursor operation can be performed on the given clip or cursor position.
    /// </summary>
    public bool CanFreezeFrameAtCursor(Guid? clipId = null, long? cursorFrame = null)
    {
        var cursor = cursorFrame ?? 0;
        if (clipId.HasValue)
        {
            return Project.Tracks.Any(t => t.Kind == TrackKind.Video && !t.IsLocked &&
                t.Clips.Any(c => c.Id == clipId.Value && !c.IsLocked && cursor >= c.StartFrame && cursor < c.EndFrame));
        }

        return Project.Tracks.Any(t => t.Kind == TrackKind.Video && !t.IsLocked &&
            t.Clips.Any(c => !c.IsLocked && cursor >= c.StartFrame && cursor < c.EndFrame));
    }

    /// <summary>
    /// Inserts a velocity envelope point with 0% playback speed (Hold curve) at the cursor position
    /// within selected video clip(s), holding the current image indefinitely for the remaining duration.
    /// </summary>
    public bool FreezeFrameAtCursor(Guid? clipId = null, long? cursorFrame = null)
    {
        var cursor = cursorFrame ?? 0;
        var targets = new List<(TimelineTrack Track, TimelineClip Clip)>();

        if (clipId.HasValue)
        {
            foreach (var t in Project.Tracks)
            {
                if (t.Kind != TrackKind.Video || t.IsLocked) continue;
                var c = t.Clips.FirstOrDefault(clip => clip.Id == clipId.Value && !clip.IsLocked);
                if (c != null && cursor >= c.StartFrame && cursor < c.EndFrame)
                {
                    targets.Add((t, c));
                    break;
                }
            }
        }
        else
        {
            foreach (var t in Project.Tracks)
            {
                if (t.Kind != TrackKind.Video || t.IsLocked) continue;
                foreach (var c in t.Clips)
                {
                    if (!c.IsLocked && cursor >= c.StartFrame && cursor < c.EndFrame)
                    {
                        targets.Add((t, c));
                    }
                }
            }
        }

        if (targets.Count == 0) return false;

        Execute("Freeze Frame at Cursor", project =>
        {
            foreach (var (targetTrack, targetClip) in targets)
            {
                var track = project.Tracks.FirstOrDefault(t => t.Id == targetTrack.Id);
                var clip = track?.Clips.FirstOrDefault(c => c.Id == targetClip.Id);
                if (clip == null) continue;

                var localFrame = Math.Clamp(cursor - clip.StartFrame, 0, clip.DurationFrames);

                clip.Envelopes ??= [];
                var velEnv = clip.Envelopes.FirstOrDefault(e => e.Type == EnvelopeType.Velocity);
                if (velEnv == null)
                {
                    velEnv = new ClipEnvelope
                    {
                        Type = EnvelopeType.Velocity,
                        IsEnabled = true
                    };
                    clip.Envelopes.Add(velEnv);
                }
                else
                {
                    velEnv.IsEnabled = true;
                }

                if (velEnv.Points.Count == 0)
                {
                    if (localFrame > 0)
                    {
                        velEnv.Points.Add(new EnvelopePoint(0, 1.0, EnvelopeCurveType.Hold));
                    }
                    velEnv.Points.Add(new EnvelopePoint(localFrame, 0.0, EnvelopeCurveType.Hold));
                    velEnv.Points.Add(new EnvelopePoint(clip.DurationFrames, 0.0, EnvelopeCurveType.Hold));
                }
                else
                {
                    var prevPoint = velEnv.Points.Where(pt => pt.Frame < localFrame).OrderBy(pt => pt.Frame).LastOrDefault();
                    if (prevPoint == null && localFrame > 0)
                    {
                        velEnv.Points.Add(new EnvelopePoint(0, 1.0, EnvelopeCurveType.Hold));
                    }
                    else if (prevPoint != null)
                    {
                        prevPoint.Curve = EnvelopeCurveType.Hold;
                    }

                    velEnv.Points.RemoveAll(pt => pt.Frame >= localFrame);
                    velEnv.Points.Add(new EnvelopePoint(localFrame, 0.0, EnvelopeCurveType.Hold));
                    velEnv.Points.Add(new EnvelopePoint(clip.DurationFrames, 0.0, EnvelopeCurveType.Hold));
                }

                velEnv.Points.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            }
        });

        return true;
    }
}

