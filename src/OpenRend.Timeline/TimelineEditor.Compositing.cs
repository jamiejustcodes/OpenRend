using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public void MakeCompositingChild(Guid trackId)
    {
        var idx = Project.Tracks.FindIndex(t => t.Id == trackId);
        if (idx <= 0) return;
        var tr = Project.Tracks[idx];
        if (tr.Kind != TrackKind.Video) return;
        var prevVid = Project.Tracks.Take(idx).LastOrDefault(t => t.Kind == TrackKind.Video);
        if (prevVid == null || tr.CompositeNestingLevel >= prevVid.CompositeNestingLevel + 1) return;

        Execute(p =>
        {
            var index = p.Tracks.FindIndex(t => t.Id == trackId);
            if (index <= 0) return;
            var track = p.Tracks[index];
            if (track.Kind != TrackKind.Video) return;

            var prevVideo = p.Tracks.Take(index).LastOrDefault(t => t.Kind == TrackKind.Video);
            if (prevVideo == null) return;

            var maxAllowed = prevVideo.CompositeNestingLevel + 1;
            if (track.CompositeNestingLevel < maxAllowed)
            {
                track.CompositeNestingLevel++;
            }
        });
    }

    public void MakeCompositingParent(Guid trackId)
    {
        var idx = Project.Tracks.FindIndex(t => t.Id == trackId);
        if (idx < 0) return;
        var tr = Project.Tracks[idx];
        if (tr.Kind != TrackKind.Video || tr.CompositeNestingLevel <= 0) return;

        Execute(p =>
        {
            var index = p.Tracks.FindIndex(t => t.Id == trackId);
            if (index < 0) return;
            var track = p.Tracks[index];
            if (track.Kind != TrackKind.Video || track.CompositeNestingLevel <= 0) return;

            track.CompositeNestingLevel--;

            // Cascade clamp subsequent video tracks to preserve nesting continuity
            var currentLevel = track.CompositeNestingLevel;
            for (var i = index + 1; i < p.Tracks.Count; i++)
            {
                var subsequent = p.Tracks[i];
                if (subsequent.Kind != TrackKind.Video) continue;
                if (subsequent.CompositeNestingLevel > currentLevel + 1)
                {
                    subsequent.CompositeNestingLevel = currentLevel + 1;
                }
                currentLevel = subsequent.CompositeNestingLevel;
            }
        });
    }

    public void SetTrackAdjustment(Guid trackId, bool isAdjustment)
    {
        var existing = Project.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (existing == null || existing.Kind != TrackKind.Video || existing.IsAdjustmentTrack == isAdjustment) return;

        Execute(p =>
        {
            var track = p.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (track == null || track.Kind != TrackKind.Video) return;
            track.IsAdjustmentTrack = isAdjustment;
        });
    }

    public void ToggleTrackAdjustment(Guid trackId)
    {
        var track = Project.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track == null || track.Kind != TrackKind.Video) return;
        SetTrackAdjustment(trackId, !track.IsAdjustmentTrack);
    }

    public bool IsCompositingParent(Guid trackId)
    {
        var index = Project.Tracks.FindIndex(t => t.Id == trackId);
        if (index < 0) return false;
        var track = Project.Tracks[index];
        if (track.Kind != TrackKind.Video) return false;

        for (var i = index + 1; i < Project.Tracks.Count; i++)
        {
            var next = Project.Tracks[i];
            if (next.Kind == TrackKind.Video)
            {
                return next.CompositeNestingLevel > track.CompositeNestingLevel;
            }
        }
        return false;
    }
}
