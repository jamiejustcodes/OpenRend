namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    /// <summary>
    /// Performs a Vegas Pro 1:1 Slip Edit: shifts media in-point without altering timeline placement or length.
    /// </summary>
    public void SlipClip(Guid clipId, long sourceDelta, bool ignoreGroups = false)
    {
        var (track, clip) = FindClip(clipId);
        TrimSelection(clipId, clip.StartFrame, clip.SourceInFrame + sourceDelta, clip.DurationFrames, ignoreGroups);
    }

    /// <summary>
    /// Performs a Vegas Pro 1:1 Slide Edit: moves event along timeline while simultaneously trimming
    /// adjacent predecessor (tail) and successor (head) events on the same track.
    /// </summary>
    public void SlideClip(Guid clipId, long delta, bool ignoreGroups = false)
    {
        if (delta == 0) return;
        var (track, primary) = FindClip(clipId);
        EnsureUnlocked(track, primary);

        var ordered = track.Clips.OrderBy(c => c.StartFrame).ToList();
        var idx = ordered.IndexOf(primary);
        var predecessor = idx > 0 ? ordered[idx - 1] : null;
        var successor = idx < ordered.Count - 1 ? ordered[idx + 1] : null;

        var predClone = predecessor?.DeepClone();
        var succClone = successor?.DeepClone();
        var primClone = primary.DeepClone();

        primClone.StartFrame += delta;
        EnsureTimelineBounds(primClone);

        if (predClone != null)
        {
            EnsureUnlocked(track, predecessor!);
            predClone.DurationFrames += delta;
            if (predClone.DurationFrames < 1) throw new InvalidOperationException("Slide exceeds predecessor event length.");
            EnsureSourceBounds(predClone, Project.Media.Single(m => m.Id == predClone.MediaId));
        }

        if (succClone != null)
        {
            EnsureUnlocked(track, successor!);
            succClone.StartFrame += delta;
            succClone.SourceInFrame += delta;
            succClone.DurationFrames -= delta;
            if (succClone.DurationFrames < 1) throw new InvalidOperationException("Slide exceeds successor event length.");
            EnsureTimelineBounds(succClone);
            EnsureSourceBounds(succClone, Project.Media.Single(m => m.Id == succClone.MediaId));
        }

        Execute(_ =>
        {
            primary.StartFrame = primClone.StartFrame;
            if (predecessor != null && predClone != null)
            {
                predecessor.DurationFrames = predClone.DurationFrames;
            }
            if (successor != null && succClone != null)
            {
                successor.StartFrame = succClone.StartFrame;
                successor.SourceInFrame = succClone.SourceInFrame;
                successor.DurationFrames = succClone.DurationFrames;
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

