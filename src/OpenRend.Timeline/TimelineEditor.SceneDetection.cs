using System;
using System.Collections.Generic;
using System.Linq;
using OpenRend.Core;

namespace OpenRend.Timeline;

/// <summary>
/// Result summary of an applied scene detection operation.
/// </summary>
public sealed record SceneDetectionResult(
    int CutCount,
    int SceneCount,
    IReadOnlyList<TimelineClip> ResultClips,
    IReadOnlyList<TimelineRegion> ResultRegions
);

public sealed partial class TimelineEditor
{
    /// <summary>
    /// Executes scene detection actions (Split on Timeline or Add Region Markers) matching Vegas Pro 1:1.
    /// Preserves full transaction history so the entire operation can be undone in a single Undo step.
    /// </summary>
    public SceneDetectionResult ApplySceneDetection(
        Guid clipId,
        IReadOnlyList<SceneCut> cuts,
        SceneDetectionMode mode,
        SceneDetectionOptions? options = null,
        string baseName = "Scene")
    {
        ArgumentNullException.ThrowIfNull(cuts);
        options ??= new SceneDetectionOptions();

        var initialClip = Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (initialClip is null)
            return new SceneDetectionResult(0, 0, Array.Empty<TimelineClip>(), Array.Empty<TimelineRegion>());

        var resultClips = new List<TimelineClip>();
        var resultRegions = new List<TimelineRegion>();

        if (cuts.Count == 0)
        {
            resultClips.Add(initialClip);
            return new SceneDetectionResult(0, 1, resultClips, resultRegions);
        }

        var sortedCuts = cuts
            .Where(c => c.AbsoluteFrame > initialClip.StartFrame && c.AbsoluteFrame < initialClip.EndFrame)
            .OrderBy(c => c.AbsoluteFrame)
            .ToList();

        if (sortedCuts.Count == 0)
        {
            resultClips.Add(initialClip);
            return new SceneDetectionResult(0, 1, resultClips, resultRegions);
        }

        var origStartFrame = initialClip.StartFrame;
        var origEndFrame = initialClip.EndFrame;

        switch (mode)
        {
            case SceneDetectionMode.SplitOnTimeline:
                Execute("Detect Scenes and Split", p =>
                {
                    // Sequential split: at each cut point, split the clip currently covering cut.AbsoluteFrame
                    foreach (var cut in sortedCuts)
                    {
                        var frame = cut.AbsoluteFrame;

                        // Find current clip piece on any video track originating from the same media or range
                        var targetClip = p.Tracks
                            .SelectMany(t => t.Clips)
                            .FirstOrDefault(c => c.StartFrame < frame && c.EndFrame > frame &&
                                                 (c.Id == clipId || c.MediaId == initialClip.MediaId));

                        if (targetClip is null) continue;

                        var splitIds = new List<Guid> { targetClip.Id };

                        // If grouped audio tracks should be included (Vegas Pro SplitGroup)
                        if (options.IncludeGroupedTracks && targetClip.GroupId.HasValue)
                        {
                            var groupMembers = p.Tracks
                                .SelectMany(t => t.Clips)
                                .Where(c => c.GroupId == targetClip.GroupId && c.StartFrame < frame && c.EndFrame > frame)
                                .Select(c => c.Id);
                            splitIds.AddRange(groupMembers);
                        }

                        // Perform atomic split on the selected clips at frame
                        var uniqueIds = splitIds.Distinct().ToList();
                        foreach (var id in uniqueIds)
                        {
                            var track = p.Tracks.First(t => t.Clips.Any(c => c.Id == id));
                            var clip = track.Clips.First(c => c.Id == id);
                            var leftDuration = frame - clip.StartFrame;
                            var rightDuration = clip.EndFrame - frame;
                            var leftSourceIn = clip.SourceInFrame;
                            var rightSourceIn = clip.SourceInFrame + leftDuration;

                            var rightClip = new TimelineClip
                            {
                                MediaId = clip.MediaId,
                                StartFrame = frame,
                                DurationFrames = rightDuration,
                                SourceInFrame = rightSourceIn,
                                Name = clip.Name,
                                Gain = clip.Gain,
                                Pan = clip.Pan,
                                Opacity = clip.Opacity,
                                GroupId = clip.GroupId,
                                VideoEffects = clip.VideoEffects.Select(fx => fx.DeepClone()).ToList(),
                                AudioEffects = clip.AudioEffects.Select(fx => fx.DeepClone()).ToList(),
                                FadeInFrames = 0,
                                FadeOutFrames = clip.FadeOutFrames,
                                FadeInTransition = null,
                                FadeOutTransition = clip.FadeOutTransition?.DeepClone()
                            };

                            clip.DurationFrames = leftDuration;
                            clip.FadeOutFrames = 0;
                            clip.FadeOutTransition = null;

                            // Split envelopes cleanly
                            if (clip.Envelopes is { Count: > 0 })
                            {
                                var rightEnvs = new List<ClipEnvelope>();
                                for (var eIdx = 0; eIdx < clip.Envelopes.Count; eIdx++)
                                {
                                    var (leftEnv, rightEnv) = clip.Envelopes[eIdx].SplitAt(leftDuration);
                                    clip.Envelopes[eIdx] = leftEnv;
                                    rightEnvs.Add(rightEnv);
                                }
                                rightClip.Envelopes = rightEnvs;
                            }

                            track.Clips.Add(rightClip);
                            track.Clips.Sort((a, b) => a.StartFrame.CompareTo(b.StartFrame));
                        }
                    }

                    // Rename resulting video clips to Scene 1, Scene 2, etc.
                    var finalClips = p.Tracks
                        .SelectMany(t => t.Clips)
                        .Where(c => c.MediaId == initialClip.MediaId &&
                                    c.StartFrame >= origStartFrame &&
                                    c.EndFrame <= origEndFrame)
                        .OrderBy(c => c.StartFrame)
                        .ToList();

                    var sceneNum = 1;
                    foreach (var c in finalClips)
                    {
                        var track = p.Tracks.First(t => t.Clips.Contains(c));
                        if (track.Kind == TrackKind.Video)
                        {
                            c.Name = $"{baseName} [Scene {sceneNum++}]";
                        }
                    }
                });

                resultClips = Project.Tracks
                    .SelectMany(t => t.Clips)
                    .Where(c => c.MediaId == initialClip.MediaId)
                    .OrderBy(c => c.StartFrame)
                    .ToList();
                break;

            case SceneDetectionMode.AddRegionMarkers:
            case SceneDetectionMode.CreateSubclips:
                var spans = SceneDetectionEngine.BuildSceneSpans(
                    sortedCuts,
                    initialClip.DurationFrames,
                    initialClip.StartFrame,
                    baseName
                );

                if (mode == SceneDetectionMode.AddRegionMarkers)
                {
                    Execute("Add Scene Detection Regions", p =>
                    {
                        var colorIndex = 0;
                        foreach (var span in spans)
                        {
                            var nextIndex = p.Regions.Count == 0 ? 1 : p.Regions.Max(r => r.Index) + 1;
                            var region = new TimelineRegion
                            {
                                Index = nextIndex,
                                StartFrame = span.StartAbsoluteFrame,
                                EndFrame = span.EndAbsoluteFrame,
                                Label = span.Name,
                                ColorId = colorIndex % TimelineMarker.MarkerColors.Length
                            };
                            region.Validate();
                            p.Regions.Add(region);
                            colorIndex++;
                        }
                        p.Regions.Sort((a, b) => a.StartFrame.CompareTo(b.StartFrame));
                    });

                    resultRegions = Project.Regions
                        .Where(r => r.StartFrame >= initialClip.StartFrame && r.EndFrame <= initialClip.EndFrame)
                        .OrderBy(r => r.StartFrame)
                        .ToList();
                }

                resultClips.Add(initialClip);
                break;
        }

        return new SceneDetectionResult(
            sortedCuts.Count,
            sortedCuts.Count + 1,
            resultClips,
            resultRegions
        );
    }
}

