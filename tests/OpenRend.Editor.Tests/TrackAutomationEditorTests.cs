using System;
using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class TrackAutomationEditorTests
{
    [Fact]
    public void TimelineEditor_SetTrackState_ArmRecord_TogglesAndSupportsUndoRedo()
    {
        var doc = new ProjectDocument();
        var track = new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Audio };
        doc.Tracks.Add(track);
        var editor = new TimelineEditor(doc);

        Assert.False(editor.Project.Tracks[0].ArmRecord);

        editor.SetTrackState(track.Id, armRecord: true);
        Assert.True(editor.Project.Tracks[0].ArmRecord);

        Assert.True(editor.CanUndo);
        editor.Undo();
        Assert.False(editor.Project.Tracks[0].ArmRecord);

        Assert.True(editor.CanRedo);
        editor.Redo();
        Assert.True(editor.Project.Tracks[0].ArmRecord);
    }

    [Fact]
    public void TimelineEditor_SetTrackColor_UpdatesAndSupportsUndoRedo()
    {
        var doc = new ProjectDocument();
        var track = new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Video };
        doc.Tracks.Add(track);
        var editor = new TimelineEditor(doc);

        Assert.Null(editor.Project.Tracks[0].TrackColor);

        editor.SetTrackColor(track.Id, "#FF857A");
        Assert.Equal("#FF857A", editor.Project.Tracks[0].TrackColor);

        Assert.True(editor.CanUndo);
        editor.Undo();
        Assert.Null(editor.Project.Tracks[0].TrackColor);

        Assert.True(editor.CanRedo);
        editor.Redo();
        Assert.Equal("#FF857A", editor.Project.Tracks[0].TrackColor);

        // Reset to null / default
        editor.SetTrackColor(track.Id, "");
        Assert.Null(editor.Project.Tracks[0].TrackColor);
    }

    [Fact]
    public void TimelineEditor_SetTrackAutomationMode_UpdatesAndSupportsUndoRedo()
    {
        var doc = new ProjectDocument();
        var track = new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Audio };
        doc.Tracks.Add(track);
        var editor = new TimelineEditor(doc);

        Assert.Equal(TrackAutomationMode.AutomationRead, editor.Project.Tracks[0].AutomationMode);

        editor.SetTrackAutomationMode(track.Id, TrackAutomationMode.AutomationTouch);
        Assert.Equal(TrackAutomationMode.AutomationTouch, editor.Project.Tracks[0].AutomationMode);

        Assert.True(editor.CanUndo);
        editor.Undo();
        Assert.Equal(TrackAutomationMode.AutomationRead, editor.Project.Tracks[0].AutomationMode);

        Assert.True(editor.CanRedo);
        editor.Redo();
        Assert.Equal(TrackAutomationMode.AutomationTouch, editor.Project.Tracks[0].AutomationMode);
    }
}

