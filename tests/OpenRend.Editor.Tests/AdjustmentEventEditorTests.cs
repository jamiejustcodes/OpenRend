using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class AdjustmentEventEditorTests
{
    [Fact]
    public void AddAdjustmentTrack_SetsTrackPropertiesAndSupportsUndoRedo()
    {
        var project = new ProjectDocument { Name = "Adj Track Test" };
        var editor = new TimelineEditor(project);

        var track = editor.AddAdjustmentTrack(0);
        Assert.Equal(TrackKind.Video, track.Kind);
        Assert.True(track.IsAdjustmentTrack);
        Assert.Equal("Adjustment Track 1", track.Name);

        var track2 = editor.AddAdjustmentTrack();
        Assert.True(track2.IsAdjustmentTrack);
        Assert.Equal("Adjustment Track 2", track2.Name);

        // Undo track 2
        Assert.True(editor.Undo());
        Assert.Single(editor.Project.Tracks);
        Assert.True(editor.Project.Tracks[0].IsAdjustmentTrack);

        // Redo track 2
        Assert.True(editor.Redo());
        Assert.Equal(2, editor.Project.Tracks.Count);
        Assert.True(editor.Project.Tracks[1].IsAdjustmentTrack);
    }

    [Fact]
    public void InsertAdjustmentEvent_CreatesAdjustmentEventAndSupportsUndoRedo()
    {
        var project = new ProjectDocument
        {
            Name = "Adj Event Test",
            Width = 1920,
            Height = 1080,
            PixelAspectRatio = 1.0,
            FrameRate = new Rational(30, 1)
        };
        var editor = new TimelineEditor(project);
        var track = editor.AddTrack(TrackKind.Video);

        var clip = editor.InsertAdjustmentEvent(track.Id, 30, 150);
        Assert.True(clip.IsAdjustmentEvent);
        Assert.Equal(1920, clip.FrameWidth);
        Assert.Equal(1080, clip.FrameHeight);
        Assert.Equal(1.0, clip.PixelAspectRatio);
        Assert.Equal("Adjustment Event", clip.Name);
        Assert.Equal(30, clip.StartFrame);
        Assert.Equal(150, clip.DurationFrames);

        // Ensure adjustment media asset was created
        var asset = editor.Project.Media.FirstOrDefault(m => m.Id == clip.MediaId);
        Assert.NotNull(asset);
        Assert.Equal("openrend://adjustment", asset.Path);
        Assert.True(asset.HasVideo);

        // Undo
        Assert.True(editor.Undo());
        Assert.Empty(editor.Project.Tracks[0].Clips);

        // Redo
        Assert.True(editor.Redo());
        Assert.Single(editor.Project.Tracks[0].Clips);
        Assert.True(editor.Project.Tracks[0].Clips[0].IsAdjustmentEvent);
    }

    [Fact]
    public void ResizeAdjustmentEventToProject_UpdatesDimensionsAndCrop()
    {
        var project = new ProjectDocument
        {
            Name = "Resize Test",
            Width = 1920,
            Height = 1080,
            PixelAspectRatio = 1.0
        };
        var editor = new TimelineEditor(project);
        var track = editor.AddTrack(TrackKind.Video);
        var clip = editor.InsertAdjustmentEvent(track.Id, 0, 90);

        Assert.Equal(1920, clip.FrameWidth);
        Assert.Equal(1080, clip.FrameHeight);

        // Update project dimensions (e.g. user switched project to 4K UHD 3840x2160)
        project.Width = 3840;
        project.Height = 2160;

        editor.ResizeAdjustmentEventToProject(clip.Id);
        var updated = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(3840, updated.FrameWidth);
        Assert.Equal(2160, updated.FrameHeight);

        // Undo restores 1920x1080
        Assert.True(editor.Undo());
        var undone = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(1920, undone.FrameWidth);
        Assert.Equal(1080, undone.FrameHeight);

        // Redo restores 3840x2160
        Assert.True(editor.Redo());
        var redone = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(3840, redone.FrameWidth);
        Assert.Equal(2160, redone.FrameHeight);
    }

    [Fact]
    public void ResizeSelectedAndAllAdjustmentEventsToProject_BatchUpdates()
    {
        var project = new ProjectDocument
        {
            Name = "Batch Resize Test",
            Width = 1280,
            Height = 720
        };
        var editor = new TimelineEditor(project);
        var track1 = editor.AddTrack(TrackKind.Video);
        var track2 = editor.AddTrack(TrackKind.Video);

        var clip1 = editor.InsertAdjustmentEvent(track1.Id, 0, 60);
        var clip2 = editor.InsertAdjustmentEvent(track1.Id, 70, 60);
        var clip3 = editor.InsertAdjustmentEvent(track2.Id, 0, 60);

        // User changes project to 1920x1080
        project.Width = 1920;
        project.Height = 1080;

        // Resize only clip1
        var resizedSelected = editor.ResizeSelectedAdjustmentEventsToProject([clip1.Id]);
        Assert.Equal(1, resizedSelected);
        Assert.Equal(1920, editor.Project.Tracks[0].Clips[0].FrameWidth);
        Assert.Equal(1280, editor.Project.Tracks[0].Clips[1].FrameWidth);

        // Resize all
        var resizedAll = editor.ResizeAllAdjustmentEventsToProject();
        Assert.Equal(3, resizedAll);
        Assert.Equal(1920, editor.Project.Tracks[0].Clips[0].FrameWidth);
        Assert.Equal(1920, editor.Project.Tracks[0].Clips[1].FrameWidth);
        Assert.Equal(1920, editor.Project.Tracks[1].Clips[0].FrameWidth);
    }

    [Fact]
    public void Validation_RejectsAdjustmentEventOnAudioTrackOrNegativeDimensions()
    {
        var project = new ProjectDocument { Name = "Invalid Test" };
        var media = TimelineEditor.EnsureAdjustmentMediaAsset(project);

        var audioTrack = new TimelineTrack { Kind = TrackKind.Audio };
        var badClip = new TimelineClip
        {
            MediaId = media.Id,
            IsAdjustmentEvent = true,
            FrameWidth = 1920,
            FrameHeight = 1080,
            DurationFrames = 60
        };
        audioTrack.Clips.Add(badClip);
        project.Tracks.Add(audioTrack);

        Assert.Throws<InvalidDataException>(() => project.Validate());

        // Fix track to video
        audioTrack.Kind = TrackKind.Video;
        project.Validate(); // now valid

        // Set negative width
        badClip.FrameWidth = -100;
        Assert.Throws<InvalidDataException>(() => project.Validate());
    }

    [Fact]
    public void Serialization_PreservesAdjustmentEventProperties()
    {
        var project = new ProjectDocument
        {
            Name = "Serialization Test",
            Width = 1920,
            Height = 1080
        };
        var editor = new TimelineEditor(project);
        var track = editor.AddAdjustmentTrack();
        var clip = editor.InsertAdjustmentEvent(track.Id, 15, 120);
        clip.UseAlternateAdjustmentEventBehavior = true;

        project.Validate();

        var json = JsonSerializer.Serialize(project);
        Assert.Contains("\"IsAdjustmentTrack\":true", json);
        Assert.Contains("\"IsAdjustmentEvent\":true", json);
        Assert.Contains("\"UseAlternateAdjustmentEventBehavior\":true", json);

        var deserialized = JsonSerializer.Deserialize<ProjectDocument>(json);
        Assert.NotNull(deserialized);
        deserialized.Validate();

        Assert.True(deserialized.Tracks[0].IsAdjustmentTrack);
        Assert.True(deserialized.Tracks[0].Clips[0].IsAdjustmentEvent);
        Assert.True(deserialized.Tracks[0].Clips[0].UseAlternateAdjustmentEventBehavior);
        Assert.Equal(1920, deserialized.Tracks[0].Clips[0].FrameWidth);
        Assert.Equal(1080, deserialized.Tracks[0].Clips[0].FrameHeight);

        var clone = project.DeepClone();
        Assert.True(clone.Tracks[0].IsAdjustmentTrack);
        Assert.True(clone.Tracks[0].Clips[0].IsAdjustmentEvent);
        Assert.True(clone.Tracks[0].Clips[0].UseAlternateAdjustmentEventBehavior);
    }
}

