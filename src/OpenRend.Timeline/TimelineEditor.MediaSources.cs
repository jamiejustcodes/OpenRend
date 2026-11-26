using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public void ReplaceMediaSource(Guid mediaId, MediaAsset replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var media = Project.Media.SingleOrDefault(asset => asset.Id == mediaId) ?? throw new KeyNotFoundException("Media was not found.");
        if (media.IsGenerated || media.Path.StartsWith("openrend://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Generated media has no source file to replace.");
        foreach (var track in Project.Tracks)
        {
            var supportsTrack = track.Kind == TrackKind.Video ? replacement.HasVideo : replacement.HasAudio;
            if (!supportsTrack && track.Clips.Any(clip => clip.MediaId == mediaId || clip.Takes?.Any(take => take.MediaId == mediaId) == true))
                throw new InvalidOperationException($"The replacement has no {track.Kind.ToString().ToLowerInvariant()} stream used by this project.");
        }

        Execute("Replace media source", _ =>
        {
            media.Path = replacement.Path;
            media.DurationSeconds = replacement.DurationSeconds;
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
