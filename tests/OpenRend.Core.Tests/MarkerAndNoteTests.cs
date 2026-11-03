using System.IO;
using System.Text.Json;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class MarkerAndNoteTests
{
    [Fact]
    public void TimelineMarker_ValidatesAndClones()
    {
        var marker = new TimelineMarker
        {
            Index = 3,
            Frame = 120,
            Label = "Intro End",
            ColorId = 2
        };

        marker.Validate();
        Assert.Equal("#43AA8B", marker.ColorHex);

        var clone = marker.DeepClone();
        Assert.Equal(marker.Id, clone.Id);
        Assert.Equal(3, clone.Index);
        Assert.Equal(120, clone.Frame);
        Assert.Equal("Intro End", clone.Label);
        Assert.Equal(2, clone.ColorId);

        var invalidId = new TimelineMarker { Id = Guid.Empty };
        Assert.Throws<InvalidDataException>(() => invalidId.Validate());

        var negativeFrame = new TimelineMarker { Frame = -5 };
        Assert.Throws<InvalidDataException>(() => negativeFrame.Validate());

        var zeroIndex = new TimelineMarker { Index = 0 };
        Assert.Throws<InvalidDataException>(() => zeroIndex.Validate());
    }

    [Fact]
    public void ProjectNoteItem_ValidatesAndClones()
    {
        var note = new ProjectNoteItem
        {
            Note = "Adjust audio levels here",
            Frame = 250,
            Timestamp = "2026-11-03 04:30:00",
            TimecodeMarker = "00:00:08:10",
            UserOverrideLabel = true,
            OverrideLabel = "Sound Designer",
            Status = ProjectNoteStatus.Active,
            OverrideColorId = 1
        };

        note.Validate();
        var clone = note.DeepClone();
        Assert.Equal(note.Id, clone.Id);
        Assert.Equal("Adjust audio levels here", clone.Note);
        Assert.Equal(250, clone.Frame);
        Assert.Equal("Sound Designer", clone.OverrideLabel);
        Assert.Equal(ProjectNoteStatus.Active, clone.Status);

        var invalidStatus = new ProjectNoteItem { Status = (ProjectNoteStatus)99 };
        Assert.Throws<InvalidDataException>(() => invalidStatus.Validate());

        var emptyId = new ProjectNoteItem { Id = Guid.Empty };
        Assert.Throws<InvalidDataException>(() => emptyId.Validate());
    }

    [Fact]
    public async Task ProjectStore_RoundtripsMarkersAndNotes()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"marker_test_{Guid.NewGuid():N}.openrend");
        var mediaFile = Path.Combine(Path.GetTempPath(), $"dummy_media_{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(mediaFile, [0x00, 0x00, 0x00, 0x18]);

        try
        {
            var asset = new MediaAsset
            {
                Path = mediaFile,
                DurationSeconds = 10.0,
                Width = 1920,
                Height = 1080,
                FrameRate = 30.0,
                HasVideo = true
            };

            var project = new ProjectDocument
            {
                Name = "MarkerParityProject",
                Width = 1920,
                Height = 1080,
                FrameRate = new Rational(30, 1),
                Media = [asset],
                Tracks = [new TimelineTrack { Kind = TrackKind.Video, Clips = [new TimelineClip { MediaId = asset.Id, DurationFrames = 300 }] }],
                Markers =
                [
                    new TimelineMarker { Index = 1, Frame = 45, Label = "Scene 1", ColorId = 0 },
                    new TimelineMarker { Index = 2, Frame = 150, Label = "Beat Drop", ColorId = 3 }
                ],
                Notes =
                [
                    new ProjectNoteItem
                    {
                        Note = "Check color balance",
                        Frame = 45,
                        Timestamp = "2026-11-03 04:30:00",
                        TimecodeMarker = "00:00:01:15",
                        Status = ProjectNoteStatus.Active
                    },
                    new ProjectNoteItem
                    {
                        Note = "Approved cut",
                        Frame = 150,
                        Timestamp = "2026-11-03 04:35:00",
                        TimecodeMarker = "00:00:05:00",
                        Status = ProjectNoteStatus.Resolved
                    }
                ]
            };

            await ProjectStore.SaveAsync(project, tempFile);
            var loaded = await ProjectStore.LoadAsync(tempFile);

            Assert.Equal(2, loaded.Markers.Count);
            Assert.Equal("Scene 1", loaded.Markers[0].Label);
            Assert.Equal(45, loaded.Markers[0].Frame);
            Assert.Equal(1, loaded.Markers[0].Index);
            Assert.Equal("Beat Drop", loaded.Markers[1].Label);
            Assert.Equal(150, loaded.Markers[1].Frame);
            Assert.Equal(2, loaded.Markers[1].Index);

            Assert.Equal(2, loaded.Notes.Count);
            Assert.Equal("Check color balance", loaded.Notes[0].Note);
            Assert.Equal(ProjectNoteStatus.Active, loaded.Notes[0].Status);
            Assert.Equal("Approved cut", loaded.Notes[1].Note);
            Assert.Equal(ProjectNoteStatus.Resolved, loaded.Notes[1].Status);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (File.Exists(mediaFile)) File.Delete(mediaFile);
        }
    }

    [Fact]
    public void ProjectDocument_DuplicateMarkerIds_ThrowsValidation()
    {
        var dupId = Guid.NewGuid();
        var project = new ProjectDocument
        {
            Markers =
            [
                new TimelineMarker { Id = dupId, Frame = 10, Index = 1 },
                new TimelineMarker { Id = dupId, Frame = 20, Index = 2 }
            ]
        };
        Assert.Throws<InvalidDataException>(() => project.Validate());
    }
}

