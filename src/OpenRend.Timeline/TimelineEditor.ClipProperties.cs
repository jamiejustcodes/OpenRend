using OpenRend.Core;

namespace OpenRend.Timeline;

public readonly record struct ClipPropertyValues(long StartFrame, long SourceInFrame, long DurationFrames,
    double Opacity, double Gain, double Pan, long FadeInFrames, long FadeOutFrames)
{
    internal static ClipPropertyValues FromClip(TimelineClip clip) => new(clip.StartFrame, clip.SourceInFrame,
        clip.DurationFrames, clip.Opacity, clip.Gain, clip.Pan, clip.FadeInFrames, clip.FadeOutFrames);
}

public sealed partial class TimelineEditor
{
    public bool SetClipProperties(Guid clipId, ClipPropertyValues values, bool ignoreGroups)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        var old = ClipPropertyValues.FromClip(clip);
        if (old == values) return false;

        if (!double.IsFinite(values.Gain) || values.Gain < 0
            || !double.IsFinite(values.Opacity) || values.Opacity is < 0 or > 1
            || !double.IsFinite(values.Pan) || values.Pan is < -1 or > 1)
            throw new InvalidDataException("Event levels are outside their allowed ranges.");

        var durationChanged = values.DurationFrames != old.DurationFrames;
        var timingChanged = values.StartFrame != old.StartFrame || values.SourceInFrame != old.SourceInFrame || durationChanged;
        var resetSelectedFades = durationChanged || values.FadeInFrames != old.FadeInFrames || values.FadeOutFrames != old.FadeOutFrames;
        if (resetSelectedFades && (values.FadeInFrames < 0 || values.FadeOutFrames < 0
            || values.FadeInFrames > values.DurationFrames || values.FadeOutFrames > values.DurationFrames - values.FadeInFrames))
            throw new InvalidDataException("Fade in and fade out must fit within the event duration.");

        List<(TimelineTrack Track, TimelineClip Clip)> members = timingChanged ? SelectedClips([clipId], ignoreGroups) : [(track, clip)];
        var ranges = new List<(TimelineClip Clip, long Start, long SourceIn, long Duration)>(members.Count);
        foreach (var (memberTrack, member) in members)
        {
            EnsureUnlocked(memberTrack, member);
            var (start, sourceIn, duration) = member.Id == clipId
                ? (values.StartFrame, values.SourceInFrame, values.DurationFrames)
                : (checked(member.StartFrame + checked(values.StartFrame - old.StartFrame)),
                    checked(member.SourceInFrame + checked(values.SourceInFrame - old.SourceInFrame)),
                    checked(member.DurationFrames + checked(values.DurationFrames - old.DurationFrames)));
            if (timingChanged)
            {
                var range = new TimelineClip
                {
                    StartFrame = start, SourceInFrame = sourceIn, DurationFrames = duration,
                    VideoSourceMode = member.VideoSourceMode, AudioSourceMode = member.AudioSourceMode
                };
                EnsureTimelineBounds(range);
                EnsureSourceBounds(range, Project.Media.Single(asset => asset.Id == member.MediaId));
            }
            ranges.Add((member, start, sourceIn, duration));
        }

        Execute(_ =>
        {
            foreach (var (member, start, sourceIn, duration) in ranges)
            {
                member.StartFrame = start;
                member.SourceInFrame = sourceIn;
                member.DurationFrames = duration;
                if (durationChanged)
                {
                    member.SnapOffsetFrames = Math.Min(member.SnapOffsetFrames, duration - 1);
                    SetPropertyFades(member, Math.Min(member.FadeInFrames, duration), Math.Min(member.FadeOutFrames, Math.Max(0, duration - member.FadeInFrames)));
                }
                if (member.Takes is { Count: > 0 } takes && member.ActiveTakeIndex >= 0 && member.ActiveTakeIndex < takes.Count)
                {
                    var take = takes[member.ActiveTakeIndex];
                    if (values.SourceInFrame != old.SourceInFrame) take.SourceInFrame = member.SourceInFrame;
                    if (durationChanged) take.SnapOffsetFrames = member.SnapOffsetFrames;
                }
            }

            clip.Opacity = values.Opacity;
            clip.Gain = values.Gain;
            clip.Pan = values.Pan;
            if (resetSelectedFades) SetPropertyFades(clip, values.FadeInFrames, values.FadeOutFrames);
        });
        return true;
    }

    private static void SetPropertyFades(TimelineClip clip, long fadeIn, long fadeOut)
    {
        clip.FadeInFrames = fadeIn;
        clip.FadeOutFrames = fadeOut;
        clip.FadeOffsetFrames = 0;
        clip.FadeDurationFrames = null;
        if (fadeIn == 0) clip.FadeInTransition = null;
        if (fadeOut == 0) clip.FadeOutTransition = null;
    }
}
