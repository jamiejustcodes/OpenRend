using System.Windows;
using OpenRend.Core;

namespace OpenRend.UI;

public sealed record VideoEffectDrag(VideoEffectKind Kind, IReadOnlyDictionary<string, double>? Parameters);

public sealed partial class TimelineView
{
    public const string VideoEffectDataFormat = "OpenRend.VideoEffect";

    private (EffectScope Scope, Guid Id)? VideoEffectTargetAt(Point point)
    {
        if (point.Y < RulerHeight || TrackAt(point.Y) is not { Kind: TrackKind.Video, IsLocked: false } track)
            return null;
        if (point.X >= 0 && point.X < HeaderWidth) return (EffectScope.Track, track.Id);
        return HitClip(point) is { Clip: { IsLocked: false } clip } ? (EffectScope.Event, clip.Id) : null;
    }
}
