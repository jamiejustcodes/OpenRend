using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using OpenRend.UI;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class TrimAndSlipEditorTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AggregateException(error);
    }

    private static (TimelineEditor Editor, MediaAsset Media, TimelineTrack Track) CreateEditorWithMedia()
    {
        var media = new MediaAsset
        {
            Path = "test_source.mp4",
            DurationSeconds = 60.0,
            HasVideo = true
        };
        var doc = new ProjectDocument();
        doc.Media.Add(media);
        var track = new TimelineTrack { Kind = TrackKind.Video };
        doc.Tracks.Add(track);
        return (new TimelineEditor(doc), media, track);
    }

    [Fact]
    public void SlipClip_ShiftsSourceInWithoutAlteringTimelineStartOrDuration()
    {
        var (editor, media, track) = CreateEditorWithMedia();
        var clip = new TimelineClip
        {
            MediaId = media.Id,
            StartFrame = 100,
            SourceInFrame = 20,
            DurationFrames = 50,
            FadeInFrames = 8,
            FadeOutFrames = 12,
            FadeDurationFrames = 60,
            FadeOffsetFrames = 5
        };
        track.Clips.Add(clip);

        // Slip media by +15 frames
        editor.SlipClip(clip.Id, 15);

        var current = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(100, current.StartFrame);
        Assert.Equal(50, current.DurationFrames);
        Assert.Equal(35, current.SourceInFrame);
        Assert.Equal(8, current.FadeInFrames);
        Assert.Equal(12, current.FadeOutFrames);
        Assert.Equal(60, current.FadeDurationFrames);
        Assert.Equal(5, current.FadeOffsetFrames);

        // Test Undo
        editor.Undo();
        var undone = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(20, undone.SourceInFrame);

        // Test Redo
        editor.Redo();
        var redone = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(35, redone.SourceInFrame);
    }

    [Fact]
    public void SlideClip_AdjustsAdjacentClipsWhileKeepingTargetMediaAndDurationLocked()
    {
        var (editor, media, track) = CreateEditorWithMedia();
        var clipA = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 50 };
        var clipB = new TimelineClip { MediaId = media.Id, StartFrame = 50, SourceInFrame = 10, DurationFrames = 50 };
        var clipC = new TimelineClip { MediaId = media.Id, StartFrame = 100, SourceInFrame = 0, DurationFrames = 50 };
        track.Clips.Add(clipA);
        track.Clips.Add(clipB);
        track.Clips.Add(clipC);

        // Slide clip B to the right by +10 frames
        editor.SlideClip(clipB.Id, 10);

        var clips = editor.Project.Tracks[0].Clips;
        // Predecessor (A) extends by 10 frames
        Assert.Equal(0, clips[0].StartFrame);
        Assert.Equal(60, clips[0].DurationFrames);

        // Clip B moves 10 frames, media in-point and length intact
        Assert.Equal(60, clips[1].StartFrame);
        Assert.Equal(50, clips[1].DurationFrames);
        Assert.Equal(10, clips[1].SourceInFrame);

        // Successor (C) shrinks from head by 10 frames
        Assert.Equal(110, clips[2].StartFrame);
        Assert.Equal(10, clips[2].SourceInFrame);
        Assert.Equal(40, clips[2].DurationFrames);

        // Net timeline endpoint is unchanged (150 frames)
        Assert.Equal(150, clips[2].EndFrame);

        // Test Undo
        editor.Undo();
        var undone = editor.Project.Tracks[0].Clips;
        Assert.Equal(50, undone[0].DurationFrames);
        Assert.Equal(50, undone[1].StartFrame);
        Assert.Equal(100, undone[2].StartFrame);
        Assert.Equal(0, undone[2].SourceInFrame);
        Assert.Equal(50, undone[2].DurationFrames);
    }

    [Fact]
    public void SlideMovesLinkedEventsAndLeavesClipsAcrossGapsUntouched()
    {
        var (editor, media, track) = CreateEditorWithMedia();
        var group = Guid.NewGuid();
        var beforeGap = new TimelineClip { MediaId = media.Id, DurationFrames = 30 };
        var first = new TimelineClip { MediaId = media.Id, GroupId = group, StartFrame = 50, DurationFrames = 50, SourceInFrame = 7 };
        var afterGap = new TimelineClip { MediaId = media.Id, StartFrame = 120, DurationFrames = 35 };
        track.Clips.AddRange([beforeGap, first, afterGap]);
        var linked = new TimelineClip { MediaId = media.Id, GroupId = group, StartFrame = 50, DurationFrames = 50, SourceInFrame = 17 };
        var touching = new TimelineClip { MediaId = media.Id, StartFrame = 100, DurationFrames = 40 };
        editor.Project.Tracks.Add(new TimelineTrack { Kind = TrackKind.Video, Clips = [linked, touching] });

        editor.SlideClip(first.Id, 6);

        Assert.Equal((56L, 7L), (first.StartFrame, first.SourceInFrame));
        Assert.Equal((56L, 17L), (linked.StartFrame, linked.SourceInFrame));
        Assert.Equal((106L, 6L, 34L), (touching.StartFrame, touching.SourceInFrame, touching.DurationFrames));
        Assert.Equal(30, beforeGap.DurationFrames);
        Assert.Equal((120L, 35L), (afterGap.StartFrame, afterGap.DurationFrames));
        Assert.True(editor.Undo());
        Assert.False(editor.CanUndo);
        Assert.Equal(50, editor.Project.Tracks[0].Clips[1].StartFrame);
        Assert.Equal(50, editor.Project.Tracks[1].Clips[0].StartFrame);
    }

    [Fact]
    public void TimeStretchClip_AdjustsDurationAndPlaybackRateWithAtomicUndo()
    {
        var (editor, media, track) = CreateEditorWithMedia();
        var clip = new TimelineClip
        {
            MediaId = media.Id,
            StartFrame = 50,
            DurationFrames = 60,
            PlaybackRate = 1.0
        };
        track.Clips.Add(clip);

        // Stretch right edge: 60 -> 120 frames (0.5x speed)
        editor.TimeStretchClip(clip.Id, 120, 0.5, stretchFromLeft: false);

        var current = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(50, current.StartFrame);
        Assert.Equal(120, current.DurationFrames);
        Assert.Equal(0.5, current.PlaybackRate);

        // Test Undo
        editor.Undo();
        var undone = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(60, undone.DurationFrames);
        Assert.Equal(1.0, undone.PlaybackRate);

        // Test Redo
        editor.Redo();
        var redone = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(120, redone.DurationFrames);
        Assert.Equal(0.5, redone.PlaybackRate);

        // Stretch left edge: 120 -> 40 frames (1.5x speed)
        editor.TimeStretchClip(redone.Id, 40, 1.5, stretchFromLeft: true);
        var stretchedLeft = editor.Project.Tracks[0].Clips[0];
        // Left stretch moved start from 50 to 50 + (120 - 40) = 130
        Assert.Equal(130, stretchedLeft.StartFrame);
        Assert.Equal(40, stretchedLeft.DurationFrames);
        Assert.Equal(1.5, stretchedLeft.PlaybackRate);
    }

    [Fact]
    public void RollEdit_DualEdgeTrim_ShiftsCutBoundaryBetweenTouchingEvents()
    {
        var (editor, media, track) = CreateEditorWithMedia();
        var clipA = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 60 };
        var clipB = new TimelineClip { MediaId = media.Id, StartFrame = 60, SourceInFrame = 0, DurationFrames = 60 };
        track.Clips.Add(clipA);
        track.Clips.Add(clipB);

        // Roll cut by +15 frames
        editor.RollEdit(clipA.Id, clipB.Id, 15);

        var rolled = editor.Project.Tracks[0].Clips;
        Assert.Equal(75, rolled[0].DurationFrames);
        Assert.Equal(75, rolled[1].StartFrame);
        Assert.Equal(15, rolled[1].SourceInFrame);
        Assert.Equal(45, rolled[1].DurationFrames);
        Assert.Equal(120, rolled[1].EndFrame);

        // Test Undo
        editor.Undo();
        var undone = editor.Project.Tracks[0].Clips;
        Assert.Equal(60, undone[0].DurationFrames);
        Assert.Equal(60, undone[1].StartFrame);
        Assert.Equal(0, undone[1].SourceInFrame);
        Assert.Equal(60, undone[1].DurationFrames);
    }
}
