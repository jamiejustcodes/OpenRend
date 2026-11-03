using System;
using System.Linq;
using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class TimelineTakesEditorTests
{
    private static (TimelineEditor editor, MediaAsset mediaA, MediaAsset mediaB, MediaAsset mediaC, TimelineTrack track, TimelineClip clip) CreateContext()
    {
        var mediaA = new MediaAsset { Id = Guid.NewGuid(), Path = "a.mp4", HasVideo = true, DurationSeconds = 10, FrameRate = 30, Width = 1920, Height = 1080 };
        var mediaB = new MediaAsset { Id = Guid.NewGuid(), Path = "b.mp4", HasVideo = true, DurationSeconds = 10, FrameRate = 30, Width = 1920, Height = 1080 };
        var mediaC = new MediaAsset { Id = Guid.NewGuid(), Path = "c.mp4", HasVideo = true, DurationSeconds = 10, FrameRate = 30, Width = 1920, Height = 1080 };

        var clip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = mediaA.Id,
            Name = "Clip A",
            StartFrame = 0,
            SourceInFrame = 0,
            DurationFrames = 60
        };

        var track = new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Video, Clips = [clip] };
        var project = new ProjectDocument { Media = [mediaA, mediaB, mediaC], Tracks = [track] };
        var editor = new TimelineEditor(project);

        return (editor, mediaA, mediaB, mediaC, track, clip);
    }

    [Fact]
    public void AddTakeConvertsSingleClipToMultiTakeAndActivatesNewTake()
    {
        var (editor, mediaA, mediaB, _, _, clip) = CreateContext();

        editor.AddTake(clip.Id, mediaB.Id, sourceInFrame: 30, name: "Angle B", setActive: true);

        var updated = editor.Project.Tracks[0].Clips[0];
        Assert.NotNull(updated.Takes);
        Assert.Equal(2, updated.Takes.Count);
        Assert.Equal(1, updated.ActiveTakeIndex);

        // Take 0 should be original mediaA
        Assert.Equal(mediaA.Id, updated.Takes[0].MediaId);
        Assert.Equal("Clip A", updated.Takes[0].Name);

        // Take 1 should be mediaB
        Assert.Equal(mediaB.Id, updated.Takes[1].MediaId);
        Assert.Equal("Angle B", updated.Takes[1].Name);
        Assert.Equal(30, updated.Takes[1].SourceInFrame);

        // Clip itself reflects active take
        Assert.Equal(mediaB.Id, updated.MediaId);
        Assert.Equal("Angle B", updated.Name);
        Assert.Equal(30, updated.SourceInFrame);
    }

    [Fact]
    public void NextTakeAndPreviousTakeCycleThroughTakes()
    {
        var (editor, _, mediaB, mediaC, _, clip) = CreateContext();

        editor.AddTake(clip.Id, mediaB.Id, sourceInFrame: 10, name: "Take B", setActive: false);
        editor.AddTake(clip.Id, mediaC.Id, sourceInFrame: 20, name: "Take C", setActive: false);

        var target = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(3, target.Takes!.Count);
        Assert.Equal(0, target.ActiveTakeIndex);
        Assert.Equal("Clip A", target.Name);

        // Next Take (T)
        editor.NextTake(clip.Id);
        Assert.Equal(1, target.ActiveTakeIndex);
        Assert.Equal("Take B", target.Name);

        // Next Take (T) again
        editor.NextTake(clip.Id);
        Assert.Equal(2, target.ActiveTakeIndex);
        Assert.Equal("Take C", target.Name);

        // Wrap around to 0
        editor.NextTake(clip.Id);
        Assert.Equal(0, target.ActiveTakeIndex);
        Assert.Equal("Clip A", target.Name);

        // Previous Take (Shift+T) wraps to last
        editor.PreviousTake(clip.Id);
        Assert.Equal(2, target.ActiveTakeIndex);
        Assert.Equal("Take C", target.Name);
    }

    [Fact]
    public void DeleteActiveTakeRemovesTakeAndMaintainsValidIndex()
    {
        var (editor, mediaA, mediaB, mediaC, _, clip) = CreateContext();

        editor.AddTake(clip.Id, mediaB.Id, name: "Take B", setActive: true);
        editor.AddTake(clip.Id, mediaC.Id, name: "Take C", setActive: true);

        var target = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(3, target.Takes!.Count);
        Assert.Equal(2, target.ActiveTakeIndex);
        Assert.Equal("Take C", target.Name);

        // Delete active take (Take C)
        editor.DeleteActiveTake(clip.Id);
        Assert.Equal(2, target.Takes.Count);
        Assert.Equal(1, target.ActiveTakeIndex);
        Assert.Equal("Take B", target.Name);

        // Delete active take (Take B)
        editor.DeleteActiveTake(clip.Id);
        Assert.Single(target.Takes);
        Assert.Equal(0, target.ActiveTakeIndex);
        Assert.Equal("Clip A", target.Name);
    }

    [Fact]
    public void UndoAndRedoRestoreTakeTransitionsCompletely()
    {
        var (editor, mediaA, mediaB, _, _, clip) = CreateContext();

        editor.AddTake(clip.Id, mediaB.Id, sourceInFrame: 45, name: "Take 2", setActive: true);
        var target = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(2, target.Takes!.Count);
        Assert.Equal(1, target.ActiveTakeIndex);
        Assert.Equal("Take 2", target.Name);

        // Undo add take
        editor.Undo();
        Assert.Null(editor.Project.Tracks[0].Clips[0].Takes);
        Assert.Equal(mediaA.Id, editor.Project.Tracks[0].Clips[0].MediaId);
        Assert.Equal("Clip A", editor.Project.Tracks[0].Clips[0].Name);

        // Redo add take
        editor.Redo();
        Assert.Equal(2, editor.Project.Tracks[0].Clips[0].Takes!.Count);
        Assert.Equal(1, editor.Project.Tracks[0].Clips[0].ActiveTakeIndex);
        Assert.Equal("Take 2", editor.Project.Tracks[0].Clips[0].Name);
    }

    [Fact]
    public void NextTakeOnSelectionCyclesMultipleSelectedClipsSimultaneously()
    {
        var (editor, mediaA, mediaB, mediaC, track, clip1) = CreateContext();

        var clip2 = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = mediaA.Id,
            Name = "Clip 2",
            StartFrame = 100,
            DurationFrames = 60
        };
        track.Clips.Add(clip2);

        editor.AddTake(clip1.Id, mediaB.Id, name: "Clip1 Take B", setActive: false);
        editor.AddTake(clip2.Id, mediaC.Id, name: "Clip2 Take C", setActive: false);

        Assert.Equal(0, clip1.ActiveTakeIndex);
        Assert.Equal(0, clip2.ActiveTakeIndex);

        // Batch cycle on selection
        editor.NextTakeOnSelection([clip1.Id, clip2.Id]);
        Assert.Equal(1, clip1.ActiveTakeIndex);
        Assert.Equal(1, clip2.ActiveTakeIndex);
        Assert.Equal("Clip1 Take B", clip1.Name);
        Assert.Equal("Clip2 Take C", clip2.Name);
    }
}
