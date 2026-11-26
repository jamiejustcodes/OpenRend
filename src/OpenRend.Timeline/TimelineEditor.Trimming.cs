using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    /// <summary>
    /// Shifts the source in-point while keeping event timing and envelopes in place.
    /// </summary>
    public void SlipClip(Guid clipId, long sourceDelta, bool ignoreGroups = false)
    {
        var selected = SelectedClips([clipId], ignoreGroups);
        if (selected.Count == 0) throw new KeyNotFoundException("Clip was not found.");
        if (sourceDelta == 0) return;
        var changes = new Dictionary<TimelineClip, long>();
        foreach (var (track, clip) in selected)
        {
            EnsureUnlocked(track, clip);
            var sourceIn = checked(clip.SourceInFrame + sourceDelta);
            var slipped = clip.DeepClone();
            slipped.SourceInFrame = clip.VideoSourceMode == VideoSourceMode.HoldLastFrame ? Math.Max(0, sourceIn) : sourceIn;
            EnsureSourceBounds(slipped, Project.Media.Single(media => media.Id == slipped.MediaId));
            if (slipped.SourceInFrame != clip.SourceInFrame)
                changes.Add(clip, slipped.SourceInFrame);
        }
        if (changes.Count > 0)
            Execute(_ => { foreach (var (clip, sourceIn) in changes) clip.SourceInFrame = sourceIn; });
    }

    /// <summary>
    /// Moves events together and shifts the shared cuts with touching neighbors.
    /// </summary>
    public void SlideClip(Guid clipId, long delta, bool ignoreGroups = false)
    {
        if (delta == 0) return;
        var selected = SelectedClips([clipId], ignoreGroups);
        if (selected.Count == 0) throw new KeyNotFoundException("Clip was not found.");
        var moving = selected.Select(item => item.Clip.Id).ToHashSet();
        var cuts = new Dictionary<TimelineClip, (long Start, long End)>();
        foreach (var (track, clip) in selected)
        {
            EnsureUnlocked(track, clip);
            cuts.Add(clip, (delta, delta));
            if (track.Clips.LastOrDefault(candidate => !moving.Contains(candidate.Id) && candidate.EndFrame == clip.StartFrame) is { } before)
            {
                EnsureUnlocked(track, before);
                cuts[before] = (cuts.GetValueOrDefault(before).Start, delta);
            }
            if (track.Clips.LastOrDefault(candidate => !moving.Contains(candidate.Id) && candidate.StartFrame == clip.EndFrame) is { } after)
            {
                EnsureUnlocked(track, after);
                cuts[after] = (delta, cuts.GetValueOrDefault(after).End);
            }
        }

        var changes = new Dictionary<TimelineClip, TimelineClip>();
        foreach (var (clip, (start, end)) in cuts)
        {
            var changed = clip.DeepClone();
            changed.StartFrame = checked(changed.StartFrame + start);
            if (!moving.Contains(clip.Id))
            {
                changed.DurationFrames = checked(changed.DurationFrames + end - start);
                var sourceDelta = (clip.IsReversed ? -(double)end : start) * Math.Abs(clip.PlaybackRate);
                changed.SourceInFrame = checked(changed.SourceInFrame + (long)Math.Round(sourceDelta));
                if (clip.VideoSourceMode == VideoSourceMode.HoldLastFrame)
                    changed.SourceInFrame = Math.Max(0, changed.SourceInFrame);
                if (changed.Crop is not null)
                    changed.CropOffsetFrames = checked(changed.CropOffsetFrames + start);

                // Reset the fade envelope just as an ordinary edge trim does.
                changed.FadeInFrames = Math.Min(changed.FadeInFrames, changed.DurationFrames);
                changed.FadeOutFrames = Math.Min(changed.FadeOutFrames, changed.DurationFrames - changed.FadeInFrames);
                changed.FadeDurationFrames = null;
                changed.FadeOffsetFrames = 0;
            }
            EnsureTimelineBounds(changed);
            EnsureSourceBounds(changed, Project.Media.Single(media => media.Id == changed.MediaId));
            changes.Add(clip, changed);
        }

        Execute(_ =>
        {
            foreach (var (clip, changed) in changes)
            {
                clip.StartFrame = changed.StartFrame;
                clip.DurationFrames = changed.DurationFrames;
                clip.SourceInFrame = changed.SourceInFrame;
                clip.CropOffsetFrames = changed.CropOffsetFrames;
                clip.FadeInFrames = changed.FadeInFrames;
                clip.FadeOutFrames = changed.FadeOutFrames;
                clip.FadeDurationFrames = changed.FadeDurationFrames;
                clip.FadeOffsetFrames = changed.FadeOffsetFrames;
            }
        });
    }

    /// <summary>
    /// Performs a Vegas Pro 1:1 Time Stretch / Compress (Ctrl + Edge Drag): rescales event duration
    /// and adjusts playback rate (0.25x - 4.0x) within an atomic undo transaction.
    /// </summary>
    public void TimeStretchClip(Guid clipId, long newDuration, double newPlaybackRate, bool stretchFromLeft = false, bool ignoreGroups = false)
    {
        var (track, primary) = FindClip(clipId);
        EnsureUnlocked(track, primary);

        var duration = Math.Max(1L, newDuration);
        var clampedRate = Math.Clamp(newPlaybackRate, 0.25, 4.0);
        var startDelta = stretchFromLeft ? primary.DurationFrames - duration : 0L;
        var newStart = Math.Max(0L, primary.StartFrame + startDelta);

        var selected = SelectedClips([clipId], ignoreGroups);
        var changes = new Dictionary<Guid, (long Start, long Duration, double Rate)>();
        foreach (var item in selected)
        {
            EnsureUnlocked(item.Track, item.Clip);
            var itemStart = stretchFromLeft ? Math.Max(0L, item.Clip.StartFrame + startDelta) : item.Clip.StartFrame;
            changes[item.Clip.Id] = (itemStart, duration, clampedRate);
        }

        Execute(_ =>
        {
            foreach (var item in selected)
            {
                var change = changes[item.Clip.Id];
                item.Clip.StartFrame = change.Start;
                item.Clip.DurationFrames = change.Duration;
                item.Clip.PlaybackRate = change.Rate;
            }
        });
    }

    /// <summary>
    /// Performs a Vegas Pro 1:1 Roll Edit (Split Edit Trim): rolls cut boundary between two touching events.
    /// </summary>
    public void RollEdit(Guid leftClipId, Guid rightClipId, long delta)
    {
        if (delta == 0) return;
        var (leftTrack, leftClip) = FindClip(leftClipId);
        var (rightTrack, rightClip) = FindClip(rightClipId);

        EnsureUnlocked(leftTrack, leftClip);
        EnsureUnlocked(rightTrack, rightClip);

        var leftClone = leftClip.DeepClone();
        var rightClone = rightClip.DeepClone();

        leftClone.DurationFrames += delta;
        if (leftClone.DurationFrames < 1) throw new InvalidOperationException("Roll edit exceeds left event length.");
        EnsureSourceBounds(leftClone, Project.Media.Single(m => m.Id == leftClone.MediaId));

        rightClone.StartFrame += delta;
        rightClone.SourceInFrame += delta;
        rightClone.DurationFrames -= delta;
        if (rightClone.DurationFrames < 1) throw new InvalidOperationException("Roll edit exceeds right event length.");
        EnsureTimelineBounds(rightClone);
        EnsureSourceBounds(rightClone, Project.Media.Single(m => m.Id == rightClone.MediaId));

        Execute(_ =>
        {
            leftClip.DurationFrames = leftClone.DurationFrames;
            rightClip.StartFrame = rightClone.StartFrame;
            rightClip.SourceInFrame = rightClone.SourceInFrame;
            rightClip.DurationFrames = rightClone.DurationFrames;
        });
    }
}

