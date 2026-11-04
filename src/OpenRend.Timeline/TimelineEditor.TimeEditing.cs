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
                        rightClip.SourceInFrame = checked(clip.SourceInFrame + leftDuration);
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
                        var rightSourceIn = clip.SourceInFrame + (endFrame - clip.StartFrame);

                        var rightClip = clip.DeepClone();
                        rightClip.Id = Guid.NewGuid();
                        rightClip.StartFrame = startFrame; // shifted left to start of cut
                        rightClip.SourceInFrame = rightSourceIn;
                        rightClip.DurationFrames = rightDuration;
                        if (rightClip.Crop is not null) rightClip.CropOffsetFrames = checked(rightClip.CropOffsetFrames + (endFrame - clip.StartFrame));
                        rightClip.FadeOffsetFrames = 0;
                        rightClip.FadeDurationFrames = null;
                        rightClip.FadeInFrames = Math.Min(rightClip.FadeInFrames, rightClip.DurationFrames);
                        rightClip.FadeOutFrames = Math.Min(rightClip.FadeOutFrames, checked(rightClip.DurationFrames - rightClip.FadeInFrames));

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
                        clip.DurationFrames = startFrame - clip.StartFrame;
                        clip.FadeOffsetFrames = 0;
                        clip.FadeDurationFrames = null;
                        clip.FadeInFrames = Math.Min(clip.FadeInFrames, clip.DurationFrames);
                        clip.FadeOutFrames = Math.Min(clip.FadeOutFrames, checked(clip.DurationFrames - clip.FadeInFrames));
                    }
                    // Case 4: Clip straddles right boundary (starts inside, ends after) -> trim head and shift
                    else if (clip.StartFrame >= startFrame && clip.StartFrame < endFrame)
                    {
                        var removedFromHead = endFrame - clip.StartFrame;
                        clip.StartFrame = startFrame;
                        clip.SourceInFrame = checked(clip.SourceInFrame + removedFromHead);
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
