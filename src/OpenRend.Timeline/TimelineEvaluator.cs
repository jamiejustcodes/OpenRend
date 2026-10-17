using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed record EvaluatedClip(TimelineTrack Track, TimelineClip Clip, MediaAsset Media, long SourceFrame, double Opacity, double Gain, double Pan)
{
    public CropState? Crop { get; init; }
}
public sealed record EvaluatedFrame(IReadOnlyList<EvaluatedClip> Video, IReadOnlyList<EvaluatedClip> Audio);

public static class TimelineEvaluator
{
    public static EvaluatedFrame Evaluate(ProjectDocument project, long frame)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (frame < 0) return new EvaluatedFrame([], []);
        var video = new List<EvaluatedClip>();
        var audio = new List<EvaluatedClip>();
        var media = project.Media.ToDictionary(m => m.Id);
        var soloVideo = project.Tracks.Any(t => t.Kind == TrackKind.Video && t.IsSolo);
        var soloAudio = project.Tracks.Any(t => t.Kind == TrackKind.Audio && t.IsSolo);
        // Return back-to-front layers so callers can composite in enumeration order.
        foreach (var track in project.Tracks.AsEnumerable().Reverse())
        {
            if (track.IsMuted || (track.Kind == TrackKind.Video && !track.IsVisible)) continue;
            if ((track.Kind == TrackKind.Video ? soloVideo : soloAudio) && !track.IsSolo) continue;
            var ordered = track.Kind == TrackKind.Video && track.Clips.Any(c => c.FadeInTransition is not null)
                ? track.Clips.OrderBy(c => c.StartFrame).ThenBy(c => c.Id).ToArray() : track.Clips.ToArray();
            var transitionOpacity = new Dictionary<Guid, double>();
            if (track.Kind == TrackKind.Video)
            {
                foreach (var incoming in ordered.Where(c => c.FadeInTransition is not null && c.StartFrame <= frame && frame < c.EndFrame))
                {
                    var outgoing = ordered.Where(c => c.Id != incoming.Id && c.StartFrame < incoming.StartFrame &&
                        c.EndFrame > incoming.StartFrame && c.EndFrame <= incoming.EndFrame &&
                        c.FadeOutTransition?.Kind == incoming.FadeInTransition?.Kind &&
                        c.FadeOutTransition?.Preset == incoming.FadeInTransition?.Preset)
                        .OrderByDescending(c => c.StartFrame).FirstOrDefault();
                    if (outgoing is null || frame >= outgoing.EndFrame ||
                        ordered.Any(c => c.Id != outgoing.Id && c.Id != incoming.Id && c.StartFrame <= frame && frame < c.EndFrame)) continue;
                    var progress = (double)(frame - incoming.StartFrame) / (outgoing.EndFrame - incoming.StartFrame);
                    var incomingAlpha = track.Opacity * incoming.Opacity * progress;
                    // Compensate for the incoming alpha-over pass so lower tracks remain visible at the linear blend weight.
                    transitionOpacity[outgoing.Id] = (1 - progress) / (1 - incomingAlpha);
                    transitionOpacity[incoming.Id] = progress;
                }
            }
            foreach (var clip in ordered)
            {
                if (frame < clip.StartFrame || frame >= clip.EndFrame || !media.TryGetValue(clip.MediaId, out var asset)) continue;
                var local = frame - clip.StartFrame;
                var fade = transitionOpacity.TryGetValue(clip.Id, out var blend) ? blend : FadeAt(clip, local);
                var (left, right) = BalanceStages(track, clip);
                var peak = Math.Max(left, right);
                var pan = peak == 0 ? 0 : left < right ? 1 - left / right : right / left - 1;
                var item = new EvaluatedClip(track, clip, asset, clip.SourceInFrame + local, clip.Opacity * track.Opacity * fade, clip.Gain * track.Gain * fade * peak, pan);
                if (track.Kind == TrackKind.Video && asset.HasVideo) video.Add(item with { Crop = CropAt(clip, asset, local) });
                if (track.Kind == TrackKind.Audio && asset.HasAudio) audio.Add(item);
            }
        }
        return new(video, audio);
    }

    public static double FadeAt(TimelineClip clip, double localFrame)
    {
        var envelope = clip.FadeDurationFrames ?? clip.DurationFrames;
        var position = localFrame + clip.FadeOffsetFrames;
        var fade = 1.0;
        if (clip.FadeInFrames > 0) fade = Math.Min(fade, position / clip.FadeInFrames);
        if (clip.FadeOutFrames > 0) fade = Math.Min(fade, (envelope - position) / clip.FadeOutFrames);
        return Math.Clamp(fade, 0, 1);
    }

    public static CropState CropAt(TimelineClip clip, MediaAsset media, long localFrame)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(media);
        return clip.Crop?.Evaluate(checked(localFrame + clip.CropOffsetFrames)) ?? CropState.Identity(media.Width, media.Height);
    }

    public static (double Left, double Right) AudioGains(TimelineTrack track, TimelineClip clip, double localFrame)
    {
        var gain = track.Gain * clip.Gain * FadeAt(clip, localFrame);
        var (left, right) = BalanceStages(track, clip);
        return (gain * left, gain * right);
    }

    private static (double Left, double Right) BalanceStages(TimelineTrack track, TimelineClip clip)
    {
        // Opposing balance stages attenuate both channels rather than cancelling their pans.
        return ((clip.Pan <= 0 ? 1 : 1 - clip.Pan) * (track.Pan <= 0 ? 1 : 1 - track.Pan),
            (clip.Pan >= 0 ? 1 : 1 + clip.Pan) * (track.Pan >= 0 ? 1 : 1 + track.Pan));
    }
}
