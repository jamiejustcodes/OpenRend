using System.Windows;
using OpenRend.Core;

namespace OpenRend.UI;

public sealed partial class TimelineView
{
    public const string TransitionDataFormat = "OpenRend.Transition";

    internal TransitionGesture? TransitionAt(Point point, VideoTransition transition)
    {
        if (point.X < HeaderWidth || point.Y < RulerHeight ||
            transition.Kind != VideoTransitionKind.Dissolve || transition.Preset != "Default") return null;
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
}
