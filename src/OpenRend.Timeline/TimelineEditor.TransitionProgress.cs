using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public bool CanEditTransitionProgress(Guid incomingId)
    {
        var track = Project.Tracks.Find(item => item.Kind == TrackKind.Video && item.Clips.Exists(clip => clip.Id == incomingId));
        if (track is null || track.IsLocked) return false;
        var incoming = track.Clips.Single(clip => clip.Id == incomingId);
        if (incoming.FadeInFrames <= incoming.FadeOffsetFrames || ProgressPieces(track, incoming).Any(clip => clip.IsLocked)) return false;
        if (incoming.FadeInTransition is not null && !incoming.IsAdjustmentEvent) return true;
        var end = incoming.StartFrame + Math.Min(incoming.DurationFrames, incoming.FadeInFrames - incoming.FadeOffsetFrames);
        return track.Clips.Exists(outgoing =>
            outgoing != incoming && outgoing.FadeOutFrames > 0 && outgoing.StartFrame < end &&
            (decimal)outgoing.StartFrame - outgoing.FadeOffsetFrames < (decimal)incoming.StartFrame - incoming.FadeOffsetFrames &&
            Math.Max(incoming.StartFrame, (decimal)outgoing.StartFrame - outgoing.FadeOffsetFrames +
                (outgoing.FadeDurationFrames ?? outgoing.DurationFrames) - outgoing.FadeOutFrames) < Math.Min(end, outgoing.EndFrame) &&
            (incoming.FadeInTransition is not null || TimelineEvaluator.IsCanonicalCrossfade(outgoing, incoming, track.Clips)));
    }

    public void SetTransitionProgress(Guid incomingId, TransitionProgressCurve? progress)
    {
        var (track, incoming) = FindClip(incomingId);
        if (!CanEditTransitionProgress(incomingId)) throw new InvalidOperationException("Select an editable video overlap.");
        progress?.Validate();
        var pieces = ProgressPieces(track, incoming);
        if (progress is null && pieces.All(clip => clip.FadeInProgress is null)) return;
        Execute("Edit Transition Progress", _ =>
        {
            foreach (var clip in pieces) clip.FadeInProgress = progress?.DeepClone();
        });
    }

    private static TimelineClip[] ProgressPieces(TimelineTrack track, TimelineClip incoming) => incoming.FadeDurationFrames is null
        ? [incoming]
        : track.Clips.Where(clip => incoming.SharesFadeEnvelope(clip, fadeOut: false)).ToArray();
}
