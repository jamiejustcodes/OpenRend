using System;
using System.Collections.Generic;
using System.Linq;
using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class SceneDetectionEditorTests
{
    private static (TimelineEditor Editor, TimelineClip VideoClip, TimelineClip AudioClip) CreateTestProject()
    {
        var mediaId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var videoClip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = mediaId,
            Name = "Interview",
            StartFrame = 0,
            DurationFrames = 300,
            SourceInFrame = 0,
            GroupId = groupId
        };

        var audioClip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = mediaId,
            Name = "Interview Audio",
            StartFrame = 0,
            DurationFrames = 300,
            SourceInFrame = 0,
            GroupId = groupId
        };

        var project = new ProjectDocument
        {
            Name = "Scene Detection Test",
            FrameRate = new Rational(30, 1),
            Media = new List<MediaAsset>
            {
                new()
                {
                    Id = mediaId,
                    Path = "interview.mp4",
                    DurationSeconds = 10.0,
                    HasVideo = true,
                    HasAudio = true
                }
            },
            Tracks = new List<TimelineTrack>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Kind = TrackKind.Video,
                    Name = "Video 1",
                    Clips = new List<TimelineClip> { videoClip }
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Kind = TrackKind.Audio,
                    Name = "Audio 1",
                    Clips = new List<TimelineClip> { audioClip }
                }
            }
        };

        return (new TimelineEditor(project), videoClip, audioClip);
    }

    [Fact]
    public void ApplySceneDetection_SplitOnTimeline_SplitsEventIntoContiguousScenes()
    {
        var (editor, videoClip, _) = CreateTestProject();

        var cuts = new List<SceneCut>
        {
            new(100, 100, 3.33, 75.0, SceneCutType.HardCut),
            new(200, 200, 6.66, 82.0, SceneCutType.HardCut)
        };

        var result = editor.ApplySceneDetection(
            videoClip.Id,
            cuts,
            SceneDetectionMode.SplitOnTimeline,
            new SceneDetectionOptions { IncludeGroupedTracks = false },
            "Interview"
        );

        Assert.Equal(2, result.CutCount);
        Assert.Equal(3, result.SceneCount);

        var videoTrack = editor.Project.Tracks.First(t => t.Kind == TrackKind.Video);
        Assert.Equal(3, videoTrack.Clips.Count);

        var c1 = videoTrack.Clips[0];
        var c2 = videoTrack.Clips[1];
        var c3 = videoTrack.Clips[2];

        Assert.Equal(0, c1.StartFrame);
        Assert.Equal(100, c1.DurationFrames);
        Assert.Equal("Interview [Scene 1]", c1.Name);

        Assert.Equal(100, c2.StartFrame);
        Assert.Equal(100, c2.DurationFrames);
        Assert.Equal("Interview [Scene 2]", c2.Name);

        Assert.Equal(200, c3.StartFrame);
        Assert.Equal(100, c3.DurationFrames);
        Assert.Equal("Interview [Scene 3]", c3.Name);

        // Test Undo
        editor.Undo();
        videoTrack = editor.Project.Tracks.First(t => t.Kind == TrackKind.Video);
        Assert.Single(videoTrack.Clips);
        Assert.Equal(300, videoTrack.Clips[0].DurationFrames);

        // Test Redo
        editor.Redo();
        videoTrack = editor.Project.Tracks.First(t => t.Kind == TrackKind.Video);
        Assert.Equal(3, videoTrack.Clips.Count);
    }

    [Fact]
    public void ApplySceneDetection_SplitOnTimeline_SynchronizesGroupedAudioTrack()
    {
        var (editor, videoClip, audioClip) = CreateTestProject();

        var cuts = new List<SceneCut>
        {
            new(120, 120, 4.0, 78.0, SceneCutType.HardCut)
        };

        var result = editor.ApplySceneDetection(
            videoClip.Id,
            cuts,
            SceneDetectionMode.SplitOnTimeline,
            new SceneDetectionOptions { IncludeGroupedTracks = true },
            "Interview"
        );

        Assert.Equal(1, result.CutCount);
        Assert.Equal(2, result.SceneCount);

        var videoTrack = editor.Project.Tracks.First(t => t.Kind == TrackKind.Video);
        var audioTrack = editor.Project.Tracks.First(t => t.Kind == TrackKind.Audio);

        Assert.Equal(2, videoTrack.Clips.Count);
        Assert.Equal(2, audioTrack.Clips.Count);

        // Verify video clips
        Assert.Equal(0, videoTrack.Clips[0].StartFrame);
        Assert.Equal(120, videoTrack.Clips[0].DurationFrames);
        Assert.Equal(120, videoTrack.Clips[1].StartFrame);
        Assert.Equal(180, videoTrack.Clips[1].DurationFrames);

        // Verify audio clips split at identical frame
        Assert.Equal(0, audioTrack.Clips[0].StartFrame);
        Assert.Equal(120, audioTrack.Clips[0].DurationFrames);
        Assert.Equal(120, audioTrack.Clips[1].StartFrame);
        Assert.Equal(180, audioTrack.Clips[1].DurationFrames);

        // Both parts retain their group relationship
        Assert.Equal(videoClip.GroupId, videoTrack.Clips[0].GroupId);
        Assert.Equal(videoClip.GroupId, audioTrack.Clips[0].GroupId);
        Assert.Equal(videoClip.GroupId, videoTrack.Clips[1].GroupId);
        Assert.Equal(videoClip.GroupId, audioTrack.Clips[1].GroupId);
    }

    [Fact]
    public void ApplySceneDetection_AddRegionMarkers_CreatesTimelineRegionsWithoutSplittingClips()
    {
        var (editor, videoClip, _) = CreateTestProject();

        var cuts = new List<SceneCut>
        {
            new(100, 100, 3.33, 75.0, SceneCutType.HardCut),
            new(220, 220, 7.33, 80.0, SceneCutType.HardCut)
        };

        var result = editor.ApplySceneDetection(
            videoClip.Id,
            cuts,
            SceneDetectionMode.AddRegionMarkers,
            new SceneDetectionOptions(),
            "Interview"
        );

        Assert.Equal(2, result.CutCount);
        Assert.Equal(3, result.SceneCount);
        Assert.Equal(3, result.ResultRegions.Count);

        var videoTrack = editor.Project.Tracks.First(t => t.Kind == TrackKind.Video);
        Assert.Single(videoTrack.Clips); // Clip is NOT split
        Assert.Equal(300, videoTrack.Clips[0].DurationFrames);

        // Verify regions
        Assert.Equal(3, editor.Project.Regions.Count);

        Assert.Equal(0, editor.Project.Regions[0].StartFrame);
        Assert.Equal(100, editor.Project.Regions[0].EndFrame);
        Assert.Equal("Interview [Scene 1]", editor.Project.Regions[0].Label);

        Assert.Equal(100, editor.Project.Regions[1].StartFrame);
        Assert.Equal(220, editor.Project.Regions[1].EndFrame);
        Assert.Equal("Interview [Scene 2]", editor.Project.Regions[1].Label);

        Assert.Equal(220, editor.Project.Regions[2].StartFrame);
        Assert.Equal(300, editor.Project.Regions[2].EndFrame);
        Assert.Equal("Interview [Scene 3]", editor.Project.Regions[2].Label);

        // Undo removes regions
        editor.Undo();
        Assert.Empty(editor.Project.Regions);
    }
}

