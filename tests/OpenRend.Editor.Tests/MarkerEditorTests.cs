using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class MarkerEditorTests
{
    private static ProjectDocument CreateTestProject() => new()
    {
        Name = "Test",
        Width = 1920,
        Height = 1080,
        FrameRate = new Rational(30, 1),
        Tracks = [new TimelineTrack { Kind = TrackKind.Video }]
    };

    [Fact]
    public void AddMarker_AssignsIncrementingIndex_AndSortsByFrame()
    {
        var editor = new TimelineEditor(CreateTestProject());

        var m1 = editor.AddMarker(100, "Middle");
        var m2 = editor.AddMarker(30, "Beginning");
        var m3 = editor.AddMarker(250, "Ending");

        Assert.Equal(1, m1.Index);
        Assert.Equal(2, m2.Index);
        Assert.Equal(3, m3.Index);

        // Verification of sorting by frame
        Assert.Equal(3, editor.Project.Markers.Count);
        Assert.Equal(30, editor.Project.Markers[0].Frame);
        Assert.Equal(100, editor.Project.Markers[1].Frame);
        Assert.Equal(250, editor.Project.Markers[2].Frame);
    }

    [Fact]
    public void Marker_UndoRedo_RestoresPreviousState()
    {
        var editor = new TimelineEditor(CreateTestProject());

        var marker = editor.AddMarker(60, "Draft");
        Assert.Single(editor.Project.Markers);

        editor.SetMarkerLabel(marker.Id, "Final");
        Assert.Equal("Final", editor.Project.Markers[0].Label);

        // Undo label edit
        Assert.True(editor.Undo());
        Assert.Equal("Draft", editor.Project.Markers[0].Label);

        // Undo marker creation
        Assert.True(editor.Undo());
        Assert.Empty(editor.Project.Markers);

        // Redo marker creation
        Assert.True(editor.Redo());
        Assert.Single(editor.Project.Markers);
        Assert.Equal("Draft", editor.Project.Markers[0].Label);

        // Redo label edit
        Assert.True(editor.Redo());
        Assert.Equal("Final", editor.Project.Markers[0].Label);
    }

    [Fact]
    public void RemoveMarker_RemovesTarget_AndSupportsUndo()
    {
        var editor = new TimelineEditor(CreateTestProject());
        var m1 = editor.AddMarker(10, "A");
        var m2 = editor.AddMarker(20, "B");

        Assert.True(editor.RemoveMarker(m1.Id));
        Assert.Single(editor.Project.Markers);
        Assert.Equal(m2.Id, editor.Project.Markers[0].Id);

        Assert.True(editor.Undo());
        Assert.Equal(2, editor.Project.Markers.Count);
    }

    [Fact]
    public void SetMarkerFrame_ReordersMarkers()
    {
        var editor = new TimelineEditor(CreateTestProject());
        var m1 = editor.AddMarker(10, "First");
        var m2 = editor.AddMarker(50, "Second");

        editor.SetMarkerFrame(m1.Id, 100);
        Assert.Equal(50, editor.Project.Markers[0].Frame);
        Assert.Equal(100, editor.Project.Markers[1].Frame);
    }

    [Fact]
    public void Notes_AddStatusAndClear_SupportsUndo()
    {
        var editor = new TimelineEditor(CreateTestProject());
        var note = editor.AddNote("Review audio", 90, "Director", 1);

        Assert.Single(editor.Project.Notes);
        Assert.Equal(ProjectNoteStatus.Active, note.Status);
        Assert.Equal("Director", note.OverrideLabel);

        editor.SetNoteStatus(note.Id, ProjectNoteStatus.Resolved);
        Assert.Equal(ProjectNoteStatus.Resolved, editor.Project.Notes[0].Status);

        editor.ClearNotes();
        Assert.Empty(editor.Project.Notes);

        Assert.True(editor.Undo());
        Assert.Single(editor.Project.Notes);
        Assert.Equal(ProjectNoteStatus.Resolved, editor.Project.Notes[0].Status);
    }
}

