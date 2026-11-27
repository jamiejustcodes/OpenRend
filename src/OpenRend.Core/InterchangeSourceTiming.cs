namespace OpenRend.Core;

internal static class InterchangeSourceTiming
{
    public static FrameRange FileRange(TimelineClip clip, MediaAsset? asset, Rational rate)
    {
        // Interchange clips reference the original file and use the sequence frame grid.
        var offset = asset?.SourceRange?.OnFrameGrid(rate).StartFrame ?? 0;
        return new(checked(offset + clip.SourceInFrame), clip.DurationFrames);
    }

    public static Dictionary<string, long> FileDurations(ProjectDocument project)
    {
        var rate = project.FrameRate;
        var lengths = project.Media.GroupBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Max(asset =>
                checked((asset.SourceRange?.OnFrameGrid(rate).StartFrame ?? 0) + rate.Frames(asset.DurationSeconds))),
                StringComparer.OrdinalIgnoreCase);

        var mediaById = project.Media.ToDictionary(asset => asset.Id);
        foreach (var clip in project.Tracks.SelectMany(track => track.Clips))
        {
            if (!mediaById.TryGetValue(clip.MediaId, out var asset)) continue;
            var end = FileRange(clip, asset, rate).EndFrame;
            lengths[asset.Path] = Math.Max(lengths[asset.Path], end);
        }
        return lengths;
    }
}
