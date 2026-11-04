using System;
using System.Collections.Generic;
using System.Linq;
using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    /// <summary>
    /// Inserts empty time at the specified frame position across tracks and ripples downstream events,
    /// markers, track automation envelopes, bus envelopes, and master bus automation curves.
    /// Aligned with Vegas Pro "Insert > Time..." functionality.
    /// </summary>
    public void InsertTime(long atFrame, long durationFrames, AutoRippleMode rippleMode = AutoRippleMode.AllTracksAndMarkers)
    {
        if (durationFrames <= 0) throw new ArgumentOutOfRangeException(nameof(durationFrames), "Duration must be positive.");
        atFrame = Math.Max(0, atFrame);

        Execute("Insert Time", project =>
        {
            var targetTracks = rippleMode switch
            {
                AutoRippleMode.AffectedTracks => project.Tracks.Where(t => t.Clips.Any(c => c.EndFrame > atFrame)).ToList(),
                _ => project.Tracks.ToList()
            };
            if (targetTracks.Count == 0) targetTracks = project.Tracks.ToList();

            foreach (var track in targetTracks)
            {
                EnsureUnlocked(track);
                var clipsToProcess = track.Clips.ToList();
                foreach (var clip in clipsToProcess)
                {
                    // Case 1: Clip starts at or after insertion point -> shift forward
                    if (clip.StartFrame >= atFrame)
                    {
                        clip.StartFrame = checked(clip.StartFrame + durationFrames);
                    }
                    // Case 2: Clip straddles insertion point -> split into left and right halves
                    else if (clip.EndFrame > atFrame)
                    {
                        var leftDuration = atFrame - clip.StartFrame;
                        var rightDuration = clip.DurationFrames - leftDuration;

                        // Create right half pushed forward by durationFrames
                        var rightClip = clip.DeepClone();
                        rightClip.Id = Guid.NewGuid();
                        rightClip.StartFrame = checked(atFrame + durationFrames);
                        // Calculate source frame with velocity modulation
                        var effPlaybackRate = double.IsFinite(clip.PlaybackRate) && clip.PlaybackRate != 0 ? clip.PlaybackRate : 1.0;
                        if (clip.Envelopes?.FirstOrDefault(e => e.Type == EnvelopeType.Velocity && e.IsEnabled) is { } velEnv)
                        {
                            rightClip.SourceInFrame = velEnv.CalculateAccumulatedSourceFrame(clip.SourceInFrame, leftDuration, effPlaybackRate, clip.IsReversed, clip.DurationFrames);
                        }
                        else
                        {
                            rightClip.SourceInFrame = clip.IsReversed
                                ? clip.SourceInFrame
                                : checked(clip.SourceInFrame + (long)Math.Round(leftDuration * Math.Abs(effPlaybackRate)));
                        }

                        if (clip.Envelopes != null)
                        {
                            var leftEnvelopes = new List<ClipEnvelope>();
                            var rightEnvelopes = new List<ClipEnvelope>();
                            foreach (var env in clip.Envelopes)
                            {
                                var (leftEnv, rightEnv) = env.SplitAt(leftDuration);
                                leftEnvelopes.Add(leftEnv);
                                rightEnvelopes.Add(rightEnv);
                            }
                            clip.Envelopes = leftEnvelopes;
                            rightClip.Envelopes = rightEnvelopes;
                        }
                        rightClip.DurationFrames = rightDuration;
                        if (rightClip.Crop is not null) rightClip.CropOffsetFrames = checked(rightClip.CropOffsetFrames + leftDuration);
                        rightClip.FadeOffsetFrames = 0;
                        rightClip.FadeDurationFrames = null;
                        rightClip.FadeInFrames = Math.Min(rightClip.FadeInFrames, rightClip.DurationFrames);
                        rightClip.FadeOutFrames = Math.Min(rightClip.FadeOutFrames, checked(rightClip.DurationFrames - rightClip.FadeInFrames));

                        // Adjust left half
                        clip.DurationFrames = leftDuration;
                        clip.FadeOffsetFrames = 0;
                        clip.FadeDurationFrames = null;
                        clip.FadeInFrames = Math.Min(clip.FadeInFrames, clip.DurationFrames);
                        clip.FadeOutFrames = Math.Min(clip.FadeOutFrames, checked(clip.DurationFrames - clip.FadeInFrames));

                        track.Clips.Add(rightClip);
                    }
                }

                if (track.Envelopes != null)
                {
                    foreach (var env in track.Envelopes)
                    {
                        env.ShiftPoints(atFrame, durationFrames);
                    }
                }
            }

            if (rippleMode is AutoRippleMode.AffectedTracksAndMarkers or AutoRippleMode.AllTracksAndMarkers)
            {
                if (project.AudioBuses != null)
                {
                    foreach (var bus in project.AudioBuses)
                    {
                        if (bus.Envelopes != null)
                        {
                            foreach (var env in bus.Envelopes)
                            {
                                env.ShiftPoints(atFrame, durationFrames);
                            }
                        }
                    }
                }

                foreach (var marker in project.Markers)
                {
                    if (marker.Frame >= atFrame)
                    {
                        marker.Frame = checked(marker.Frame + durationFrames);
                    }
                }
            }

            if (rippleMode == AutoRippleMode.AllTracksAndMarkers)
            {
                if (project.MasterAudioEnvelopes != null)
                {
                    foreach (var env in project.MasterAudioEnvelopes)
                    {
                        env.ShiftPoints(atFrame, durationFrames);
                    }
                }
            }
        });
    }

    /// <summary>
    /// Deletes a time range across tracks, cutting through events, removing interior automation points
    /// and markers, and rippling all downstream material leftward to close the gap.
    /// Aligned with Vegas Pro "Edit > Delete Time" / "Ripple Delete".
    /// </summary>
    public void DeleteTime(long startFrame, long durationFrames, AutoRippleMode rippleMode = AutoRippleMode.AllTracksAndMarkers)
    {
        if (durationFrames <= 0) throw new ArgumentOutOfRangeException(nameof(durationFrames), "Duration must be positive.");
        startFrame = Math.Max(0, startFrame);
        var endFrame = checked(startFrame + durationFrames);
        var deleteSpan = (Start: startFrame, End: endFrame);

        Execute("Delete Time", project =>
        {
            var targetTracks = rippleMode switch
            {
                AutoRippleMode.AffectedTracks => project.Tracks.Where(t => t.Clips.Any(c => c.StartFrame < endFrame && c.EndFrame > startFrame)).ToList(),
                _ => project.Tracks.ToList()
            };
            if (targetTracks.Count == 0) targetTracks = project.Tracks.ToList();

            foreach (var track in targetTracks)
            {
                EnsureUnlocked(track);
                var clipsToProcess = track.Clips.ToList();
                foreach (var clip in clipsToProcess)
                {
                    // Case 1: Clip completely inside deletion span -> remove
                    if (clip.StartFrame >= startFrame && clip.EndFrame <= endFrame)
                    {
                        track.Clips.Remove(clip);
                    }
                    // Case 2: Clip completely spans across deletion span -> cut out the middle chunk
                    else if (clip.StartFrame < startFrame && clip.EndFrame > endFrame)
                    {
                        var leftDuration = startFrame - clip.StartFrame;
                        var rightDuration = clip.EndFrame - endFrame;
                        var cutDuration = endFrame - clip.StartFrame;

                        var effPlaybackRate = double.IsFinite(clip.PlaybackRate) && clip.PlaybackRate != 0 ? clip.PlaybackRate : 1.0;
                        long rightSourceIn;
                        if (clip.Envelopes?.FirstOrDefault(e => e.Type == EnvelopeType.Velocity && e.IsEnabled) is { } velEnv)
                        {
                            rightSourceIn = velEnv.CalculateAccumulatedSourceFrame(clip.SourceInFrame, cutDuration, effPlaybackRate, clip.IsReversed, clip.DurationFrames);
                        }
                        else
                        {
                            rightSourceIn = clip.IsReversed
                                ? clip.SourceInFrame
                                : checked(clip.SourceInFrame + (long)Math.Round(cutDuration * Math.Abs(effPlaybackRate)));
                        }

                        var rightClip = clip.DeepClone();
                        rightClip.Id = Guid.NewGuid();
                        rightClip.StartFrame = startFrame; // shifted left to start of cut
                        rightClip.SourceInFrame = rightSourceIn;
                        rightClip.DurationFrames = rightDuration;
                        if (rightClip.Crop is not null) rightClip.CropOffsetFrames = checked(rightClip.CropOffsetFrames + cutDuration);
                        rightClip.FadeOffsetFrames = 0;
                        rightClip.FadeDurationFrames = null;
                        rightClip.FadeInFrames = Math.Min(rightClip.FadeInFrames, rightClip.DurationFrames);
                        rightClip.FadeOutFrames = Math.Min(rightClip.FadeOutFrames, checked(rightClip.DurationFrames - rightClip.FadeInFrames));

                        if (clip.Envelopes != null)
                        {
                            var leftEnvelopes = new List<ClipEnvelope>();
                            var rightEnvelopes = new List<ClipEnvelope>();
                            foreach (var env in clip.Envelopes)
                            {
                                var (leftEnv, _) = env.SplitAt(leftDuration);
                                var (_, rightEnv) = env.SplitAt(cutDuration);
                                leftEnvelopes.Add(leftEnv);
                                rightEnvelopes.Add(rightEnv);
                            }
                            clip.Envelopes = leftEnvelopes;
                            rightClip.Envelopes = rightEnvelopes;
                        }

                        clip.DurationFrames = leftDuration;
                        clip.FadeOffsetFrames = 0;
                        clip.FadeDurationFrames = null;
                        clip.FadeInFrames = Math.Min(clip.FadeInFrames, clip.DurationFrames);
                        clip.FadeOutFrames = Math.Min(clip.FadeOutFrames, checked(clip.DurationFrames - clip.FadeInFrames));

                        track.Clips.Add(rightClip);
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
                    }
                    // Case 5: Clip strictly after endFrame -> shift left by durationFrames
                    else if (clip.StartFrame >= endFrame)
                    {
                        clip.StartFrame = checked(clip.StartFrame - durationFrames);
                    }
                }

                if (track.Envelopes != null)
                {
                    foreach (var env in track.Envelopes)
                    {
                        env.RippleForSpans([deleteSpan]);
                    }
                }
            }

            if (rippleMode is AutoRippleMode.AffectedTracksAndMarkers or AutoRippleMode.AllTracksAndMarkers)
            {
                if (project.AudioBuses != null)
                {
                    foreach (var bus in project.AudioBuses)
                    {
                        if (bus.Envelopes != null)
                        {
                            foreach (var env in bus.Envelopes)
                            {
                                env.RippleForSpans([deleteSpan]);
                            }
                        }
                    }
                }

                project.Markers.RemoveAll(m => m.Frame > startFrame && m.Frame < endFrame);
                foreach (var marker in project.Markers)
                {
                    if (marker.Frame >= endFrame)
                    {
                        marker.Frame = Math.Max(0, checked(marker.Frame - durationFrames));
                    }
                }
            }

            if (rippleMode == AutoRippleMode.AllTracksAndMarkers)
            {
                if (project.MasterAudioEnvelopes != null)
                {
                    foreach (var env in project.MasterAudioEnvelopes)
                    {
                        env.RippleForSpans([deleteSpan]);
                    }
                }
            }
        });
    }
}
