using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed record EvaluatedClip(TimelineTrack Track, TimelineClip Clip, MediaAsset Media, long SourceFrame, double Opacity, double Gain, double Pan)
{
    public CropState? Crop { get; init; }
    public double? SourceTimeSeconds { get; init; }
    // Dissolve participants form one composite group; scalar opacity alone cannot describe source coverage.
    public EvaluatedDissolve? Dissolve { get; init; }
}
public sealed record EvaluatedDissolve(Guid OutgoingClipId, Guid IncomingClipId, double Progress);
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
            var ordered = track.Kind == TrackKind.Video && track.Clips.Any(c => c.FadeInTransition is not null || c.FadeInFrames > 0)
                ? track.Clips.OrderBy(c => c.StartFrame - c.FadeOffsetFrames).ThenBy(c => c.StartFrame).ThenBy(c => c.Id).ToArray()
                : track.Clips.ToArray();
            var transitionOpacity = new Dictionary<Guid, double>();
            var dissolves = new Dictionary<Guid, EvaluatedDissolve>();
            if (track.Kind == TrackKind.Video)
            {
                foreach (var incoming in ordered.Where(c => c.FadeInFrames > 0 && c.StartFrame <= frame && frame < c.EndFrame))
                {
                    var incomingPosition = frame - incoming.StartFrame + incoming.FadeOffsetFrames;
                    if (incomingPosition >= incoming.FadeInFrames) continue;
                    var outgoing = ordered.Where(c => c.Id != incoming.Id && c.FadeOutFrames > 0 &&
                        c.StartFrame <= frame && frame < c.EndFrame &&
                        c.StartFrame - c.FadeOffsetFrames < incoming.StartFrame - incoming.FadeOffsetFrames &&
                        frame - c.StartFrame + c.FadeOffsetFrames >= (c.FadeDurationFrames ?? c.DurationFrames) - c.FadeOutFrames)
                        .OrderByDescending(c => c.StartFrame).FirstOrDefault();
                    if (outgoing is null || (incoming.FadeInTransition is null && !IsCanonicalCrossfade(outgoing, incoming, ordered)) ||
                        !media.TryGetValue(outgoing.MediaId, out var outgoingAsset) || !outgoingAsset.HasVideo ||
                        !media.TryGetValue(incoming.MediaId, out var incomingAsset) || !incomingAsset.HasVideo ||
                        ordered.Any(c => c.Id != outgoing.Id && c.Id != incoming.Id && c.StartFrame <= frame && frame < c.EndFrame)) continue;
                    var progress = (double)incomingPosition / incoming.FadeInFrames;
                    var incomingAlpha = track.Opacity * incoming.Opacity * progress;
                    // Compensate for the incoming alpha-over pass so lower tracks remain visible at the linear blend weight.
                    transitionOpacity[outgoing.Id] = (1 - progress) / (1 - incomingAlpha);
                    transitionOpacity[incoming.Id] = progress;
                    var dissolve = new EvaluatedDissolve(outgoing.Id, incoming.Id, progress);
                    dissolves[outgoing.Id] = dissolve;
                    dissolves[incoming.Id] = dissolve;
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
                if (track.Kind == TrackKind.Video && asset.HasVideo) video.Add(item with
                    { Crop = CropAt(clip, asset, local), Dissolve = dissolves.GetValueOrDefault(clip.Id),
                        SourceTimeSeconds = VideoSourceTime.Seconds(project.FrameRate, item.SourceFrame, asset.DurationSeconds, clip.VideoSourceMode) });
                if (track.Kind == TrackKind.Audio && asset.HasAudio) audio.Add(item);
            }
        }
        return new(video, audio);
    }

    private static bool IsCanonicalCrossfade(TimelineClip outgoing, TimelineClip incoming, IReadOnlyList<TimelineClip> clips)
    {
        if (outgoing.FadeOutFrames != incoming.FadeInFrames) return false;
        var start = (decimal)incoming.StartFrame - incoming.FadeOffsetFrames;
        var end = (decimal)outgoing.StartFrame - outgoing.FadeOffsetFrames + (outgoing.FadeDurationFrames ?? outgoing.DurationFrames);
        var incomingEnd = start + (incoming.FadeDurationFrames ?? incoming.DurationFrames);
        if (end - start != incoming.FadeInFrames || incomingEnd < end) return false;
        // Split pieces share an envelope. Check every start boundary in it, including parts outside this frame.
        var overlap = clips.Where(clip => clip.StartFrame < end && clip.EndFrame > start).ToArray();
        return !overlap.Select(clip => Math.Max(start, clip.StartFrame)).Any(position =>
            overlap.Count(clip => clip.StartFrame <= position && position < clip.EndFrame) > 2);
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
