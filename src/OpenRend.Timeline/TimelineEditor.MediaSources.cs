using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public MediaAsset CreateSubclip(Guid sourceId, FrameRange frames, string name, bool reverse = false)
    {
        var source = Project.Media.SingleOrDefault(asset => asset.Id == sourceId) ?? throw new KeyNotFoundException("Media was not found.");
        var subclip = MakeSubclip(source, frames, name, reverse);
        Execute("Create subclip", project => project.Media.Add(subclip));
        return subclip;
    }

    private MediaAsset MakeSubclip(MediaAsset source, FrameRange frames, string name, bool reverse = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (source.IsGenerated || source.Path.StartsWith("openrend://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Subclips require file media.");
        frames.Validate(Project.FrameRate.Frames(source.DurationSeconds));

        var subclip = source.DeepClone();
        subclip.Id = Guid.NewGuid();
        subclip.DisplayName = name.Trim();
        var range = MediaSourceRange.FromFrames(frames, Project.FrameRate, source.SourceRange, reverse);
        subclip.SourceRange = range;
        subclip.DurationSeconds = range.DurationSeconds;
        return subclip;
    }

    public void ReplaceMediaSource(Guid mediaId, MediaAsset replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var media = Project.Media.SingleOrDefault(asset => asset.Id == mediaId) ?? throw new KeyNotFoundException("Media was not found.");
        if (media.IsGenerated || media.Path.StartsWith("openrend://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Generated media has no source file to replace.");
        if (media.SourceRange is { } range && range.EndSeconds > replacement.DurationSeconds + Project.FrameRate.Seconds(1) / 2)
            throw new InvalidOperationException("The replacement ends before the subclip's source range.");
        foreach (var track in Project.Tracks)
        {
            var supportsTrack = track.Kind == TrackKind.Video ? replacement.HasVideo : replacement.HasAudio;
            if (!supportsTrack && track.Clips.Any(clip => clip.MediaId == mediaId || clip.Takes?.Any(take => take.MediaId == mediaId) == true))
                throw new InvalidOperationException($"The replacement has no {track.Kind.ToString().ToLowerInvariant()} stream used by this project.");
        }

        Execute("Replace media source", _ =>
        {
            media.Path = replacement.Path;
            if (media.SourceRange is null) media.DurationSeconds = replacement.DurationSeconds;
            media.Width = replacement.Width;
            media.Height = replacement.Height;
            media.FrameRate = replacement.FrameRate;
            media.HasVideo = replacement.HasVideo;
            media.HasAudio = replacement.HasAudio;
            media.AudioChannels = replacement.AudioChannels;
            media.AudioSampleRate = replacement.AudioSampleRate;
        });
    }
}
