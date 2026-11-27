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
    /// Slides each selected event window while its underlying media stays fixed on the timeline.
    /// </summary>
    public void SlideClip(Guid clipId, long delta, bool ignoreGroups = false)
    {
        if (delta == 0) return;
        var selected = SelectedClips([clipId], ignoreGroups);
        if (selected.Count == 0) throw new KeyNotFoundException("Clip was not found.");
        var changes = new List<(TimelineClip Clip, TimelineClip Slid)>(selected.Count);
        foreach (var (track, clip) in selected)
        {
            EnsureUnlocked(track, clip);
            var slid = clip.DeepClone();
            slid.StartFrame = checked(clip.StartFrame + delta);
            slid.SourceInFrame = checked(clip.SourceInFrame + SlideSourceDelta(track, clip, delta));
            EnsureTimelineBounds(slid);
            var media = Project.Media.Single(asset => asset.Id == slid.MediaId);
            EnsureSourceBounds(slid, media);
            if (track.Kind == TrackKind.Audio && slid.SourceInFrame < 0)
                throw new InvalidOperationException("The slide would place audio before the start of its source.");
            var available = Project.FrameRate.Frames(media.DurationSeconds);
            if (track.Kind == TrackKind.Video && clip.VideoSourceMode == VideoSourceMode.Bounded && available > 0)
            {
                var span = Math.Round((slid.DurationFrames - 1) * Math.Abs(slid.PlaybackRate));
                var earlier = slid.PlaybackRate < 0 && !slid.IsReversed ? -span : 0;
                var later = earlier < 0 ? 0 : span;
                if (slid.SourceInFrame + earlier < 0 || slid.SourceInFrame + later >= available)
                    throw new InvalidOperationException("The slide would extend the window beyond its source.");
            }
            changes.Add((clip, slid));
        }

        Execute(_ =>
        {
            foreach (var (clip, slid) in changes)
            {
                clip.StartFrame = slid.StartFrame;
                clip.SourceInFrame = slid.SourceInFrame;
            }
        });
    }

    private static long SlideSourceDelta(TimelineTrack track, TimelineClip clip, long delta)
    {
        if (clip.Envelopes?.Any(envelope => envelope.Type == EnvelopeType.Velocity && envelope.IsEnabled) == true)
            throw new InvalidOperationException("Slide requires constant source speed. The event has a velocity envelope.");
        if (track.Kind == TrackKind.Audio && (clip.PlaybackRate != 1 || clip.IsReversed))
            throw new InvalidOperationException("Slide requires forward audio at normal speed.");
        if (delta % VideoSourcePhase.SampleStepFor(clip) != 0)
            throw new InvalidOperationException("Slide must align with the event's undersampled frame grid.");

        var rate = clip.IsReversed ? -Math.Abs(clip.PlaybackRate) : clip.PlaybackRate;
        if (rate == Math.Truncate(rate)) return checked(delta * (long)rate);
        var sourceDelta = delta * rate;
        // An even integer offset also preserves the evaluator's round-to-even half-frame ties.
        if (sourceDelta % 2 != 0)
            throw new InvalidOperationException("Slide cannot align this edit to the event's source frame grid.");
        return checked((long)sourceDelta);
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

