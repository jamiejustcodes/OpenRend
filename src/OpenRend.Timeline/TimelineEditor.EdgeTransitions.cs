using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public void ApplyEdgeTransition(Guid clipId, bool fadeOut, VideoTransition transition, long defaultFrames)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defaultFrames);
        transition.Validate();
        var (track, clip) = FindClip(clipId);
        if (clip.IsAdjustmentEvent) throw new InvalidOperationException("An adjustment event cannot use a standalone transition.");
        var pieces = EditableTransitionPieces(track, clip, fadeOut);
        var others = track.Clips.Except(pieces).ToArray();
        var existing = fadeOut ? clip.FadeOutFrames : clip.FadeInFrames;
        var length = existing;
        if (length == 0)
        {
            var envelope = clip.FadeDurationFrames ?? clip.DurationFrames;
            if (fadeOut ? clip.FadeOffsetFrames + clip.DurationFrames != envelope : clip.FadeOffsetFrames != 0)
                throw new InvalidOperationException("Choose the start or end of the split event's fade envelope.");
            var oppositeFade = pieces.Max(piece => fadeOut ? piece.FadeInFrames : piece.FadeOutFrames);
            length = Math.Min(defaultFrames, Math.Min(clip.DurationFrames, envelope - oppositeFade));
            if (length <= 0) throw new InvalidOperationException("The other fade already fills the event.");
            var start = fadeOut ? clip.EndFrame - length : clip.StartFrame;
            var end = fadeOut ? clip.EndFrame : clip.StartFrame + length;
            foreach (var other in others.Where(other => other.StartFrame < end && other.EndFrame > start))
            {
                // Leave the shared part of the track available for an overlap transition.
                var available = fadeOut ? clip.EndFrame - other.EndFrame : other.StartFrame - clip.StartFrame;
                length = Math.Min(length, available);
            }
            if (length <= 0) throw new InvalidOperationException("Use the overlap to apply a transition between events.");
        }
        else
        {
            if (clip.FadeBounds(fadeOut).FrameCount == 0)
                throw new InvalidOperationException("Choose the part of the event containing the fade.");
            if (pieces.Select(piece => piece.FadeBounds(fadeOut)).Any(range => range.FrameCount > 0 &&
                others.Any(other => other.StartFrame < range.EndFrame && other.EndFrame > range.StartFrame)))
                throw new InvalidOperationException("Use the overlap to apply a transition between events.");
        }

        Execute("Apply event transition", _ =>
        {
            foreach (var piece in pieces)
            {
                if (fadeOut)
                {
                    piece.FadeOutFrames = length;
                    piece.FadeOutTransition = transition.DeepClone();
                }
                else
                {
                    piece.FadeInFrames = length;
                    piece.FadeInTransition = transition.DeepClone();
                }
            }
        });
    }

    private static TimelineClip[] EditableTransitionPieces(TimelineTrack track, TimelineClip clip, bool fadeOut)
    {
        if (track.Kind != TrackKind.Video)
            throw new InvalidOperationException("Transitions require a video event.");
        var pieces = clip.FadeDurationFrames is null ? [clip] : track.Clips.Where(piece => clip.SharesFadeEnvelope(piece, fadeOut)).ToArray();
        foreach (var piece in pieces) EnsureUnlocked(track, piece);
        return pieces;
    }
}
