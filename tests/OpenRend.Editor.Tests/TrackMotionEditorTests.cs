using System;
using System.IO;
using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class TrackMotionEditorTests
{
    private static (ProjectDocument doc, TimelineEditor editor, TimelineTrack videoTrack) CreateTestProject()
    {
        var doc = new ProjectDocument
        {
            Width = 1920,
            Height = 1080,
            FrameRate = new Rational(30, 1)
        };
        var track = new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Video, Name = "Video 1" };
        doc.Tracks.Add(track);
        var editor = new TimelineEditor(doc);
        return (doc, editor, track);
    }

    [Fact]
    public void TimelineEditor_TrackMotion_SetAndGet()
    {
        var (doc, editor, track) = CreateTestProject();

        Assert.Null(editor.GetTrackMotion(track.Id));

        var key = new TrackMotionKeyframe
        {
            Frame = 0,
            PositionX = 150,
            PositionY = -75,
            Width = 1280,
            Height = 720,
            RotationZ = 45
        };

        editor.SetTrackMotionKeyframe(track.Id, key);

        var motion = editor.GetTrackMotion(track.Id);
        Assert.NotNull(motion);
        Assert.Single(motion.MotionKeyframes);
        Assert.Equal(150.0, motion.MotionKeyframes[0].PositionX);
        Assert.Equal(-75.0, motion.MotionKeyframes[0].PositionY);
        Assert.Equal(1280.0, motion.MotionKeyframes[0].Width);
        Assert.Equal(720.0, motion.MotionKeyframes[0].Height);
        Assert.Equal(45.0, motion.MotionKeyframes[0].RotationZ);
    }

    [Fact]
    public void TimelineEditor_TrackMotion_SupportsUndoAndRedo()
    {
        var (doc, editor, track) = CreateTestProject();

        var key = TrackMotionKeyframe.Identity(1920, 1080);
        key.PositionX = 300;
        editor.SetTrackMotionKeyframe(track.Id, key);

        Assert.NotNull(editor.GetTrackMotion(track.Id));
        Assert.Equal(300.0, editor.GetTrackMotion(track.Id)!.MotionKeyframes[0].PositionX);

        Assert.True(editor.CanUndo);
        editor.Undo();
        Assert.Null(editor.GetTrackMotion(track.Id));

        Assert.True(editor.CanRedo);
        editor.Redo();
        Assert.NotNull(editor.GetTrackMotion(track.Id));
        Assert.Equal(300.0, editor.GetTrackMotion(track.Id)!.MotionKeyframes[0].PositionX);
    }

    [Fact]
    public void TimelineEditor_TrackMotion_RemoveKeyframeAndReset()
    {
        var (doc, editor, track) = CreateTestProject();

        var k1 = TrackMotionKeyframe.Identity(1920, 1080);
        k1.Frame = 0;
        var k2 = TrackMotionKeyframe.Identity(1920, 1080);
        k2.Frame = 30;
        k2.PositionX = 100;

        editor.SetTrackMotionKeyframe(track.Id, k1);
        editor.SetTrackMotionKeyframe(track.Id, k2);

        var motion = editor.GetTrackMotion(track.Id);
        Assert.Equal(2, motion!.MotionKeyframes.Count);

        editor.RemoveTrackMotionKeyframe(track.Id, 30);
        motion = editor.GetTrackMotion(track.Id);
        Assert.Single(motion!.MotionKeyframes);

        editor.ResetTrackMotion(track.Id);
        Assert.Null(editor.GetTrackMotion(track.Id));
    }

    [Fact]
    public void TimelineEditor_TrackMotion_TogglesShadowAndGlow()
    {
        var (doc, editor, track) = CreateTestProject();

        editor.SetTrackShadowEnabled(track.Id, true);
        var m1 = editor.GetTrackMotion(track.Id);
        Assert.NotNull(m1);
        Assert.True(m1.ShadowEnabled);

        editor.SetTrackGlowEnabled(track.Id, true);
        var m2 = editor.GetTrackMotion(track.Id);
        Assert.NotNull(m2);
        Assert.True(m2.GlowEnabled);

        editor.SetTrackShadowEnabled(track.Id, false);
        var m3 = editor.GetTrackMotion(track.Id);
        Assert.False(m3!.ShadowEnabled);
        Assert.True(m3.GlowEnabled);
    }

    [Fact]
    public void TimelineEditor_TrackMotion_RejectsAudioTracks()
    {
        var doc = new ProjectDocument();
        var audioTrack = new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Audio };
        doc.Tracks.Add(audioTrack);
        var editor = new TimelineEditor(doc);

        Assert.Throws<InvalidOperationException>(() => editor.SetTrackMotion(audioTrack.Id, new TrackMotion()));
        Assert.Throws<InvalidOperationException>(() => editor.SetTrackMotionKeyframe(audioTrack.Id, TrackMotionKeyframe.Identity(1920, 1080)));
    }
}

