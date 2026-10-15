using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed record EvaluatedClip(TimelineTrack Track, TimelineClip Clip, MediaAsset Media, long SourceFrame, double Opacity, double Gain, double Pan);
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
            foreach (var clip in track.Clips)
            {
                if (frame < clip.StartFrame || frame >= clip.EndFrame || !media.TryGetValue(clip.MediaId, out var asset)) continue;
                var local = frame - clip.StartFrame;
                var fade = FadeAt(clip, local);
                var item = new EvaluatedClip(track, clip, asset, clip.SourceInFrame + local, clip.Opacity * fade, clip.Gain * fade, clip.Pan);
                if (track.Kind == TrackKind.Video && asset.HasVideo) video.Add(item);
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
}
