using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed record SceneDetectionResult(
    int CutCount,
    int SceneCount,
    IReadOnlyList<TimelineClip> ResultClips,
    IReadOnlyList<TimelineRegion> ResultRegions)
{
    public IReadOnlyList<MediaAsset> ResultSubclips { get; init; } = [];
}

public sealed partial class TimelineEditor
{
    public SceneDetectionResult ApplySceneDetection(
        Guid clipId,
        IReadOnlyList<SceneCut> cuts,
        SceneDetectionMode mode,
        SceneDetectionOptions? options = null,
        string baseName = "Scene")
    {
        ArgumentNullException.ThrowIfNull(cuts);
        var clip = Project.Tracks.Where(track => track.Kind == TrackKind.Video)
            .SelectMany(track => track.Clips).FirstOrDefault(candidate => candidate.Id == clipId);
        if (clip is null) return new(0, 0, [], []);

        var boundaries = cuts.Where(cut => cut.AbsoluteFrame > clip.StartFrame && cut.AbsoluteFrame < clip.EndFrame)
            .DistinctBy(cut => cut.AbsoluteFrame).OrderBy(cut => cut.AbsoluteFrame)
            .Select(cut => cut with { FrameOffset = cut.AbsoluteFrame - clip.StartFrame }).ToArray();
        if (boundaries.Length == 0) return new(0, 1, [clip], []);
        var spans = SceneDetectionEngine.BuildSceneSpans(boundaries, clip.DurationFrames, clip.StartFrame, baseName);

        switch (mode)
        {
            case SceneDetectionMode.SplitOnTimeline:
                var pieces = new List<TimelineClip> { clip };
                Execute(_ =>
                {
                    // Track the right-hand descendant by identity. Other events can share the same source.
                    var target = clip;
                    var ignoreGroups = options?.IncludeGroupedTracks == false;
                    foreach (var cut in boundaries)
                    {
                        var splits = PrepareSplits(SelectedClips([target.Id], ignoreGroups), cut.AbsoluteFrame);
                        ApplySplits(splits);
                        foreach (var split in splits) SetSceneSplitTakeState(split);
                        target = splits.Single(split => split.Clip.Id == target.Id).Right;
                        pieces.Add(target);
                    }

                    for (var index = 0; index < pieces.Count; index++)
                    {
                        var piece = pieces[index];
                        piece.Name = spans[index].Name;
                        if (piece.Takes is { Count: > 0 } takes) takes[piece.ActiveTakeIndex].Name = piece.Name;
                    }
                });
                return new(boundaries.Length, spans.Count, pieces, []);
            case SceneDetectionMode.AddRegionMarkers:
                var regions = new List<TimelineRegion>();
                Execute(project =>
                {
                    var number = project.Regions.Count == 0 ? 0 : project.Regions.Max(region => region.Index);
                    foreach (var span in spans)
                    {
                        regions.Add(new TimelineRegion
                        {
                            Index = checked(++number),
                            StartFrame = span.StartAbsoluteFrame,
                            EndFrame = span.EndAbsoluteFrame,
                            Label = span.Name,
                            ColorId = (span.Index - 1) % TimelineMarker.MarkerColors.Length
                        });
                    }
                    project.Regions.AddRange(regions);
                    project.Regions.Sort((left, right) => left.StartFrame.CompareTo(right.StartFrame));
                });
                return new(boundaries.Length, spans.Count, [clip], regions);
            case SceneDetectionMode.CreateSubclips:
                if (clip.IsReversed || clip.PlaybackRate != 1 || clip.UnderSampleRate != 1 ||
                    clip.Envelopes?.Any(envelope => envelope.Type == EnvelopeType.Velocity && envelope.IsEnabled) == true)
                    throw new InvalidOperationException("Scene subclips require a forward, unretimed event.");
                var source = Project.Media.Single(asset => asset.Id == clip.MediaId);
                var subclips = spans.Select(span => MakeSubclip(source,
                    new FrameRange(checked(clip.SourceInFrame + span.StartOffsetFrames), span.DurationFrames), span.Name)).ToArray();
                Execute("Create scene subclips", project => project.Media.AddRange(subclips));
                return new(boundaries.Length, spans.Count, [clip], []) { ResultSubclips = subclips };
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    private static void SetSceneSplitTakeState(ClipSplit split)
    {
        var previousSnap = split.Clip.SnapOffsetFrames;
        split.Right.GroupId = split.Clip.GroupId;
        split.Clip.SnapOffsetFrames = Math.Min(previousSnap, split.LeftDuration - 1);
        split.Right.SnapOffsetFrames = Math.Clamp(previousSnap - split.LeftDuration, 0, split.Right.DurationFrames - 1);
        SyncTake(split.Clip);
        SyncTake(split.Right);

        static void SyncTake(TimelineClip clip)
        {
            if (clip.Takes is not { Count: > 0 } takes) return;
            var take = takes[clip.ActiveTakeIndex];
            take.SourceInFrame = clip.SourceInFrame;
            take.SnapOffsetFrames = clip.SnapOffsetFrames;
        }
    }
}
