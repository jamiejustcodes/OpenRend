using System.Windows;
using OpenRend.Core;

namespace OpenRend.UI;

public sealed partial class TimelineView
{
    public const string TransitionDataFormat = "OpenRend.Transition";

    private void RequestOverlapToCut()
    {
        if (Project.Tracks.Count == 0) return;
        var track = Project.Tracks[ActiveTrackIndex];
        if (track.Kind != TrackKind.Video || track.IsLocked) return;
        // The cursor and active track choose the overlap, independently of event selection.
        var events = track.Clips.Where(clip => clip.StartFrame <= Position && Position < clip.EndFrame)
            .OrderBy(clip => clip.StartFrame).ToArray();
        if (events.Length != 2 || events[0].StartFrame >= events[1].StartFrame ||
            events[0].EndFrame >= events[1].EndFrame) return;
        OverlapToCutRequested?.Invoke(events[0].Id, events[1].Id);
    }

    internal TransitionGesture? TransitionAt(Point point, VideoTransition transition)
    {
        if (point.X < HeaderWidth || point.Y < RulerHeight ||
            !Enum.IsDefined(transition.Kind) || string.IsNullOrWhiteSpace(transition.Preset)) return null;
        var track = TrackAt(point.Y);
        if (track is null || track.Kind != TrackKind.Video || track.IsLocked) return null;
        var frame = FrameAt(point.X);
        var events = track.Clips.Where(clip => clip.StartFrame <= frame && frame < clip.EndFrame)
            .OrderBy(clip => clip.StartFrame).ToArray();
        if (events.Length > 2) return null;
        if (events.Length < 2)
        {
            var ordered = track.Clips.OrderBy(clip => clip.StartFrame).ToArray();
            for (var i = 1; i < ordered.Length; i++)
            {
                var left = ordered[i - 1];
                var right = ordered[i];
                if (left.EndFrame != right.StartFrame || Math.Abs(X(right.StartFrame) - point.X) > 6) continue;
                if (ordered.Any(clip => clip.Id != left.Id && clip.Id != right.Id &&
                    clip.StartFrame <= right.StartFrame && clip.EndFrame >= right.StartFrame)) continue;
                return new TransitionGesture(left.Id, right.Id, transition.DeepClone(), IsCut: true);
            }
            return null;
        }
        var outgoing = events[0];
        var incoming = events[1];
        if (outgoing.StartFrame == incoming.StartFrame || outgoing.EndFrame > incoming.EndFrame ||
            outgoing.FadeInFrames > outgoing.DurationFrames - (outgoing.EndFrame - incoming.StartFrame) ||
            incoming.FadeOutFrames > incoming.DurationFrames - (outgoing.EndFrame - incoming.StartFrame)) return null;
        if (track.Clips.Any(clip => clip.Id != outgoing.Id && clip.Id != incoming.Id &&
            clip.StartFrame < outgoing.EndFrame && clip.EndFrame > incoming.StartFrame)) return null;
        return new TransitionGesture(outgoing.Id, incoming.Id, transition.DeepClone());
    }

    private (Guid Id, bool FadeOut)? TransitionEdgeAt(Point point)
    {
        if (point.X < HeaderWidth || point.Y < RulerHeight ||
            TrackAt(point.Y) is not { Kind: TrackKind.Video, IsLocked: false } track) return null;
        var frame = FrameAt(point.X);
        if (track.Clips.Count(clip => clip.StartFrame <= frame && frame < clip.EndFrame) > 1) return null;
        foreach (var clip in track.Clips.AsEnumerable().Reverse())
        {
            if (clip.IsLocked || clip.IsAdjustmentEvent) continue;
            var bounds = ClipBounds(track.Id, clip);
            if (bounds.IsEmpty || point.Y < bounds.Top || point.Y > bounds.Bottom) continue;
            var left = Math.Abs(X(clip.StartFrame) - point.X);
            var right = Math.Abs(X(clip.EndFrame) - point.X);
            if (Math.Min(left, right) <= 8) return (clip.Id, right < left);
            var head = clip.FadeBounds(false);
            var tail = clip.FadeBounds(true);
            if (head.FrameCount > 0 && frame >= head.StartFrame && frame < head.EndFrame) return (clip.Id, false);
            if (tail.FrameCount > 0 && frame >= tail.StartFrame && frame < tail.EndFrame) return (clip.Id, true);
        }
        return null;
    }
}
