using System;
using System.Collections.Generic;
using System.Linq;
using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class SmartSplitAndFreezeFrameTests
{
    private static (ProjectDocument doc, TimelineEditor editor, TimelineClip clip) CreateVideoProject(long startFrame = 0, long durationFrames = 300, long sourceIn = 100)
    {
        var doc = new ProjectDocument
        {
            Width = 1920,
            Height = 1080,
            FrameRate = new Rational(30, 1)
        };

        var media = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = "test_video.mp4",
            HasVideo = true,
            HasAudio = true,
            DurationSeconds = 60.0,
            FrameRate = 30.0,
            Width = 1920,
            Height = 1080
        };
        doc.Media.Add(media);

        var track = new TimelineTrack
        {
            Kind = TrackKind.Video,
            Name = "Video 1"
        };
        doc.Tracks.Add(track);

        var clip = new TimelineClip
        {
            MediaId = media.Id,
            Name = "Clip 1",
            StartFrame = startFrame,
            SourceInFrame = sourceIn,
            DurationFrames = durationFrames,
            PlaybackRate = 1.0
        };
        track.Clips.Add(clip);

        var editor = new TimelineEditor(doc);
        return (doc, editor, clip);
    }

    [Fact]
    public void FreezeFrameAtCursor_VideoClip_InsertsVelocityHoldKeyframeAndFreezesPlayback()
    {
        var (doc, editor, clip) = CreateVideoProject(startFrame: 0, durationFrames: 300, sourceIn: 100);

        // Position cursor at frame 120
        var canFreeze = editor.CanFreezeFrameAtCursor(clip.Id, 120);
        Assert.True(canFreeze);

        var success = editor.FreezeFrameAtCursor(clip.Id, 120);
        Assert.True(success);

        var updatedClip = editor.Project.Tracks[0].Clips[0];
        Assert.NotNull(updatedClip.Envelopes);
        var velEnv = updatedClip.Envelopes.FirstOrDefault(e => e.Type == EnvelopeType.Velocity);
        Assert.NotNull(velEnv);
        Assert.True(velEnv.IsEnabled);

        // Must have 3 points: frame 0 (1.0 Hold), frame 120 (0.0 Hold), frame 300 (0.0 Hold)
        Assert.Equal(3, velEnv.Points.Count);
        Assert.Equal(0, velEnv.Points[0].Frame);
        Assert.Equal(1.0, velEnv.Points[0].Value);
        Assert.Equal(EnvelopeCurveType.Hold, velEnv.Points[0].Curve);

        Assert.Equal(120, velEnv.Points[1].Frame);
        Assert.Equal(0.0, velEnv.Points[1].Value);
        Assert.Equal(EnvelopeCurveType.Hold, velEnv.Points[1].Curve);

        Assert.Equal(300, velEnv.Points[2].Frame);
        Assert.Equal(0.0, velEnv.Points[2].Value);
        Assert.Equal(EnvelopeCurveType.Hold, velEnv.Points[2].Curve);

        // Analytical continuous evaluation verification
        // Before 120: advancing 1:1
        Assert.Equal(100 + 60, velEnv.CalculateAccumulatedSourceFrame(updatedClip.SourceInFrame, 60, updatedClip.PlaybackRate));
        Assert.Equal(100 + 120, velEnv.CalculateAccumulatedSourceFrame(updatedClip.SourceInFrame, 120, updatedClip.PlaybackRate));

        // After 120: completely frozen at 220
        Assert.Equal(100 + 120, velEnv.CalculateAccumulatedSourceFrame(updatedClip.SourceInFrame, 150, updatedClip.PlaybackRate));
        Assert.Equal(100 + 120, velEnv.CalculateAccumulatedSourceFrame(updatedClip.SourceInFrame, 200, updatedClip.PlaybackRate));
        Assert.Equal(100 + 120, velEnv.CalculateAccumulatedSourceFrame(updatedClip.SourceInFrame, 299, updatedClip.PlaybackRate));
    }

    [Fact]
    public void FreezeFrameAtCursor_AtStartOfClip_HoldsEntireDuration()
    {
        var (doc, editor, clip) = CreateVideoProject(startFrame: 50, durationFrames: 200, sourceIn: 500);

        // Cursor at clip start (frame 50)
        var success = editor.FreezeFrameAtCursor(clip.Id, 50);
        Assert.True(success);

        var updatedClip = editor.Project.Tracks[0].Clips[0];
        var velEnv = updatedClip.Envelopes?.FirstOrDefault(e => e.Type == EnvelopeType.Velocity);
        Assert.NotNull(velEnv);

        // Points at frame 0 (0.0 Hold) and 200 (0.0 Hold)
        Assert.Equal(2, velEnv.Points.Count);
        Assert.Equal(0, velEnv.Points[0].Frame);
        Assert.Equal(0.0, velEnv.Points[0].Value);
        Assert.Equal(EnvelopeCurveType.Hold, velEnv.Points[0].Curve);

        // Frozen from start to end
        Assert.Equal(500, velEnv.CalculateAccumulatedSourceFrame(updatedClip.SourceInFrame, 0, updatedClip.PlaybackRate));
        Assert.Equal(500, velEnv.CalculateAccumulatedSourceFrame(updatedClip.SourceInFrame, 100, updatedClip.PlaybackRate));
        Assert.Equal(500, velEnv.CalculateAccumulatedSourceFrame(updatedClip.SourceInFrame, 199, updatedClip.PlaybackRate));
    }

    [Fact]
    public void FreezeFrameAtCursor_UndoRedo_RevertsCleanly()
    {
        var (doc, editor, clip) = CreateVideoProject(startFrame: 0, durationFrames: 300, sourceIn: 100);

        editor.FreezeFrameAtCursor(clip.Id, 100);
        Assert.True(editor.Project.Tracks[0].Clips[0].Envelopes?.Any(e => e.Type == EnvelopeType.Velocity) == true);

        // Undo
        editor.Undo();
        Assert.False(editor.Project.Tracks[0].Clips[0].Envelopes?.Any(e => e.Type == EnvelopeType.Velocity) == true);

        // Redo
        editor.Redo();
        var reClip = editor.Project.Tracks[0].Clips[0];
        var reVel = reClip.Envelopes?.FirstOrDefault(e => e.Type == EnvelopeType.Velocity);
        Assert.NotNull(reVel);
        Assert.Equal(0.0, reVel.Points.First(p => p.Frame == 100).Value);
    }

    [Fact]
    public void FreezeFrameAtCursor_RejectsLockedTrackOrInvalidClip()
    {
        var (doc, editor, clip) = CreateVideoProject(startFrame: 0, durationFrames: 300, sourceIn: 100);
        doc.Tracks[0].IsLocked = true;

        var can = editor.CanFreezeFrameAtCursor(clip.Id, 100);
        Assert.False(can);

        var ok = editor.FreezeFrameAtCursor(clip.Id, 100);
        Assert.False(ok);
    }

    [Fact]
    public void SmartSplit_CutsMiddleOfClip_OverlapsAndCrossfadesSlices()
    {
        var (doc, editor, clip) = CreateVideoProject(startFrame: 0, durationFrames: 300, sourceIn: 100);

        // Time selection: 100 to 150 (50 frames)
        var can = editor.CanSmartSplit(100, 50, new[] { clip.Id });
        Assert.True(can);

        var resulting = editor.SmartSplit(100, 50, overlapFrames: 5, new[] { clip.Id });
        Assert.Equal(2, resulting.Count);

        var track = editor.Project.Tracks[0];
        Assert.Equal(2, track.Clips.Count);

        var left = track.Clips[0];
        var right = track.Clips[1];

        // Left slice: 0 to 100, fade out = 5 Smooth
        Assert.Equal(0, left.StartFrame);
        Assert.Equal(100, left.DurationFrames);
        Assert.Equal(100, left.SourceInFrame);
        Assert.Equal(5, left.FadeOutFrames);
        Assert.Equal(VideoFadeCurve.Smooth, left.FadeOutCurve);

        // Right slice: 95 to 250, duration = (150 + 5) = 155, fade in = 5 Smooth
        Assert.Equal(95, right.StartFrame);
        Assert.Equal(155, right.DurationFrames);
        Assert.Equal(250, right.EndFrame);
        Assert.Equal(5, right.FadeInFrames);
        Assert.Equal(VideoFadeCurve.Smooth, right.FadeInCurve);

        // Both slices grouped together
        Assert.NotNull(left.GroupId);
        Assert.Equal(left.GroupId, right.GroupId);
    }

    [Fact]
    public void SmartSplit_DownstreamClips_ShiftToCloseGap()
    {
        var (doc, editor, clip1) = CreateVideoProject(startFrame: 0, durationFrames: 200, sourceIn: 100);
        var media = doc.Media[0];

        // Add downstream clip from 200 to 400
        var clip2 = new TimelineClip
        {
            MediaId = media.Id,
            Name = "Clip 2",
            StartFrame = 200,
            SourceInFrame = 0,
            DurationFrames = 200,
            PlaybackRate = 1.0
        };
        doc.Tracks[0].Clips.Add(clip2);

        // Smart split on clip1 from 50 to 100 (50 frames removed, 5 frame overlap)
        editor.SmartSplit(50, 50, overlapFrames: 5, new[] { clip1.Id });

        var track = editor.Project.Tracks[0];
        Assert.Equal(3, track.Clips.Count);

        var c1Left = track.Clips[0];
        var c1Right = track.Clips[2]; // newly added right slice
        var c2 = track.Clips[1];      // downstream clip

        // c1Right end: (50 - 5) + (100 + 5) = 150
        Assert.Equal(45, c1Right.StartFrame);
        Assert.Equal(105, c1Right.DurationFrames);
        Assert.Equal(150, c1Right.EndFrame);

        // c2 must have shifted left by 50 to start at 150 with 0 gap!
        Assert.Equal(150, c2.StartFrame);
        Assert.Equal(200, c2.DurationFrames);
        Assert.Equal(350, c2.EndFrame);
    }

    [Fact]
    public void SmartSplit_SynchronouslySplitsGroupedAudioAndVideoClips()
    {
        var (doc, editor, videoClip) = CreateVideoProject(startFrame: 0, durationFrames: 300, sourceIn: 100);
        var media = doc.Media[0];

        // Add companion audio track and clip
        var audioTrack = new TimelineTrack { Kind = TrackKind.Audio, Name = "Audio 1" };
        doc.Tracks.Add(audioTrack);

        var audioClip = new TimelineClip
        {
            MediaId = media.Id,
            Name = "Audio Clip 1",
            StartFrame = 0,
            SourceInFrame = 100,
            DurationFrames = 300,
            PlaybackRate = 1.0
        };
        audioTrack.Clips.Add(audioClip);

        var sharedGroup = Guid.NewGuid();
        videoClip.GroupId = sharedGroup;
        audioClip.GroupId = sharedGroup;

        // Smart split across 100 to 150
        var resulting = editor.SmartSplit(100, 50, overlapFrames: 5, new[] { videoClip.Id }, ignoreGroups: false);

        var vTrack = editor.Project.Tracks[0];
        var aTrack = editor.Project.Tracks[1];

        Assert.Equal(2, vTrack.Clips.Count);
        Assert.Equal(2, aTrack.Clips.Count);

        var vLeft = vTrack.Clips[0];
        var vRight = vTrack.Clips[1];
        var aLeft = aTrack.Clips[0];
        var aRight = aTrack.Clips[1];

        // Both audio and video right slices start at 95 and end at 250
        Assert.Equal(95, vRight.StartFrame);
        Assert.Equal(155, vRight.DurationFrames);
        Assert.Equal(95, aRight.StartFrame);
        Assert.Equal(155, aRight.DurationFrames);

        // All 4 slices share the same group
        Assert.Equal(vLeft.GroupId, vRight.GroupId);
        Assert.Equal(vLeft.GroupId, aLeft.GroupId);
        Assert.Equal(vLeft.GroupId, aRight.GroupId);
    }

    [Fact]
    public void SmartSplit_TrimsTailWhenIntersectsClipBoundary()
    {
        var (doc, editor, clip) = CreateVideoProject(startFrame: 50, durationFrames: 100, sourceIn: 0);

        // Range 120 to 200 intersects tail of clip (starts at 50, ends at 150)
        editor.SmartSplit(120, 80, overlapFrames: 5, new[] { clip.Id });

        var track = editor.Project.Tracks[0];
        Assert.Single(track.Clips);
        var trimmed = track.Clips[0];
        Assert.Equal(50, trimmed.StartFrame);
        Assert.Equal(70, trimmed.DurationFrames); // 120 - 50 = 70
        Assert.Equal(120, trimmed.EndFrame);
    }

    [Fact]
    public void SmartSplit_UndoRedo_RevertsAllTracksAndGroupsCleanly()
    {
        var (doc, editor, clip) = CreateVideoProject(startFrame: 0, durationFrames: 300, sourceIn: 100);

        editor.SmartSplit(100, 50, overlapFrames: 5, new[] { clip.Id });
        Assert.Equal(2, editor.Project.Tracks[0].Clips.Count);

        // Undo
        editor.Undo();
        var undoneTrack = editor.Project.Tracks[0];
        Assert.Single(undoneTrack.Clips);
        Assert.Equal(300, undoneTrack.Clips[0].DurationFrames);
        Assert.Equal(0, undoneTrack.Clips[0].StartFrame);

        // Redo
        editor.Redo();
        var redoneTrack = editor.Project.Tracks[0];
        Assert.Equal(2, redoneTrack.Clips.Count);
        Assert.Equal(95, redoneTrack.Clips[1].StartFrame);
        Assert.Equal(155, redoneTrack.Clips[1].DurationFrames);
    }
}

