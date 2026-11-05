using System;
using System.IO;
using System.Text.Json;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Timeline;
using OpenRend.UI;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class ClipSnapOffsetEditorTests
{
    private static (ProjectDocument Project, TimelineTrack Track, TimelineClip Clip) CreateTestProject()
    {
        var project = new ProjectDocument();
        var media = new MediaAsset { Path = "test.mp4", DurationSeconds = 10.0, HasVideo = true, HasAudio = true };
        project.Media.Add(media);

        var track = new TimelineTrack { Name = "Video 1", Kind = TrackKind.Video };
        var clip = new TimelineClip
        {
            MediaId = media.Id,
            Name = "Clip 1",
            StartFrame = 0,
            SourceInFrame = 0,
            DurationFrames = 100
        };
        track.Clips.Add(clip);
        project.Tracks.Add(track);
        return (project, track, clip);
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new System.Threading.Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AggregateException(error);
    }

    [Fact]
    public void SetClipSnapOffset_ClampsToClipDuration_AndSurvivesUndoRedo()
    {
        var (project, track, clip) = CreateTestProject();
        var editor = new TimelineEditor(project);

        Assert.Equal(0, editor.Project.Tracks[0].Clips[0].SnapOffsetFrames);

        // Set valid snap offset
        editor.SetClipSnapOffset(clip.Id, 35);
        Assert.Equal(35, editor.Project.Tracks[0].Clips[0].SnapOffsetFrames);

        // Clamps above duration (duration is 100 -> max is 99)
        editor.SetClipSnapOffset(clip.Id, 250);
        Assert.Equal(99, editor.Project.Tracks[0].Clips[0].SnapOffsetFrames);

        // Clamps below zero
        editor.SetClipSnapOffset(clip.Id, -20);
        Assert.Equal(0, editor.Project.Tracks[0].Clips[0].SnapOffsetFrames);

        // Undo to 99
        editor.Undo();
        Assert.Equal(99, editor.Project.Tracks[0].Clips[0].SnapOffsetFrames);

        // Undo to 35
        editor.Undo();
        Assert.Equal(35, editor.Project.Tracks[0].Clips[0].SnapOffsetFrames);

        // Undo to 0
        editor.Undo();
        Assert.Equal(0, editor.Project.Tracks[0].Clips[0].SnapOffsetFrames);

        // Redo to 35
        editor.Redo();
        Assert.Equal(35, editor.Project.Tracks[0].Clips[0].SnapOffsetFrames);
    }

    [Fact]
    public void SplitClip_PreservesAndShiftsSnapOffset_AccuratelyOnBothSlices()
    {
        // Case 1: Snap offset in the right slice
        var (project1, track1, clip1) = CreateTestProject();
        clip1.SnapOffsetFrames = 75; // 75 frames into the 100-frame clip
        var editor1 = new TimelineEditor(project1);

        var right1 = editor1.SplitClip(clip1.Id, 50);

        Assert.Equal(50, clip1.DurationFrames);
        Assert.Equal(0, clip1.SnapOffsetFrames); // Left slice cleared

        Assert.Equal(50, right1.StartFrame);
        Assert.Equal(50, right1.DurationFrames);
        Assert.Equal(25, right1.SnapOffsetFrames); // 75 - 50 = 25
        // Absolute project frame of snap point is preserved: 50 + 25 = 75!
        Assert.Equal(75, right1.StartFrame + right1.SnapOffsetFrames);

        // Case 2: Snap offset in the left slice
        var (project2, track2, clip2) = CreateTestProject();
        clip2.SnapOffsetFrames = 20; // 20 frames into the 100-frame clip
        var editor2 = new TimelineEditor(project2);

        var right2 = editor2.SplitClip(clip2.Id, 50);

        Assert.Equal(50, clip2.DurationFrames);
        Assert.Equal(20, clip2.SnapOffsetFrames); // Left slice retained: 0 + 20 = 20

        Assert.Equal(50, right2.StartFrame);
        Assert.Equal(50, right2.DurationFrames);
        Assert.Equal(0, right2.SnapOffsetFrames); // Right slice is 0
    }

    [Fact]
    public void SnapOffset_SurvivesProjectValidationAndJsonRoundTrip()
    {
        var (project, track, clip) = CreateTestProject();
        clip.SnapOffsetFrames = 42;

        project.Validate();

        var json = JsonSerializer.Serialize(project);
        var deserialized = JsonSerializer.Deserialize<ProjectDocument>(json);
        Assert.NotNull(deserialized);
        deserialized.Validate();
        Assert.Equal(42, deserialized.Tracks[0].Clips[0].SnapOffsetFrames);

        // Test validation failure on negative snap offset
        clip.SnapOffsetFrames = -5;
        Assert.Throws<InvalidDataException>(() => project.Validate());

        // Test validation failure when snap offset >= duration
        clip.SnapOffsetFrames = 100;
        Assert.Throws<InvalidDataException>(() => project.Validate());
    }

    [Fact]
    public void EditDetailsWindow_IncludesSnapOffsetColumn_InEventsTsvExport()
    {
        RunSta(() =>
        {
            var mediaService = new OpenRend.Media.MediaService();
            var renderer = new OpenRend.Media.TimelineRenderService(mediaService);
            var vm = new EditorViewModel(mediaService, renderer, new OpenRend.Media.ExportService(renderer));

            var track = new TimelineTrack { Name = "Video 1", Kind = TrackKind.Video };
            var clip = new TimelineClip
            {
                Name = "Clip 1",
                StartFrame = 0,
                DurationFrames = 100,
                SnapOffsetFrames = 30
            };
            track.Clips.Add(clip);
            vm.Project.Tracks.Add(track);

            var window = new EditDetailsWindow(vm, () => { });
            var tsv = window.GetTableText(selectedOnly: false);
            Assert.Contains("Snap Offset", tsv);
            Assert.Contains("Media File\tSnap Offset", tsv);
            var expectedTimecode = TimelineView.FormatTimecode(30, vm.Project.FrameRate);
            Assert.Contains(expectedTimecode, tsv);
        });
    }
}
