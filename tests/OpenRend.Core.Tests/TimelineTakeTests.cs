using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class TimelineTakeTests
{
    [Fact]
    public void TimelineTakeDeepCloneProducesIndependentCopy()
    {
        var original = new TimelineTake
        {
            Id = Guid.NewGuid(),
            MediaId = Guid.NewGuid(),
            Name = "Vocal Take 3",
            SourceInFrame = 120,
            VideoSourceMode = VideoSourceMode.Loop
        };

        var clone = original.DeepClone();
        Assert.Equal(original.Id, clone.Id);
        Assert.Equal(original.MediaId, clone.MediaId);
        Assert.Equal(original.Name, clone.Name);
        Assert.Equal(original.SourceInFrame, clone.SourceInFrame);
        Assert.Equal(original.VideoSourceMode, clone.VideoSourceMode);

        original.Name = "Modified";
        original.SourceInFrame = 999;
        Assert.Equal("Vocal Take 3", clone.Name);
        Assert.Equal(120, clone.SourceInFrame);
    }

    [Fact]
    public void ClipDeepCloneClonesTakesCollectionIndependently()
    {
        var clip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = Guid.NewGuid(),
            Name = "Clip A",
            DurationFrames = 300,
            Takes =
            [
                new TimelineTake { Id = Guid.NewGuid(), MediaId = Guid.NewGuid(), Name = "Take 1", SourceInFrame = 0 },
                new TimelineTake { Id = Guid.NewGuid(), MediaId = Guid.NewGuid(), Name = "Take 2", SourceInFrame = 50 }
            ],
            ActiveTakeIndex = 1
        };

        var clone = clip.DeepClone();
        Assert.NotNull(clone.Takes);
        Assert.Equal(2, clone.Takes.Count);
        Assert.Equal(1, clone.ActiveTakeIndex);
        Assert.Equal("Take 2", clone.Takes[1].Name);

        // Mutating original takes list does not mutate clone
        clip.Takes.Add(new TimelineTake { Id = Guid.NewGuid(), MediaId = Guid.NewGuid(), Name = "Take 3" });
        Assert.Equal(2, clone.Takes.Count);
    }

    [Fact]
    public void ProjectValidationValidatesTakeIntegrityAndMediaReferences()
    {
        var media1 = new MediaAsset { Id = Guid.NewGuid(), Path = "test1.mp4", HasVideo = true, DurationSeconds = 10, FrameRate = 30, Width = 1920, Height = 1080 };
        var media2 = new MediaAsset { Id = Guid.NewGuid(), Path = "test2.mp4", HasVideo = true, DurationSeconds = 10, FrameRate = 30, Width = 1920, Height = 1080 };

        var clip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = media1.Id,
            Name = "Clip",
            DurationFrames = 60,
            Takes =
            [
                new TimelineTake { Id = Guid.NewGuid(), MediaId = media1.Id, Name = "Take 1" },
                new TimelineTake { Id = Guid.NewGuid(), MediaId = media2.Id, Name = "Take 2" }
            ],
            ActiveTakeIndex = 0
        };

        var track = new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Video, Clips = [clip] };
        var project = new ProjectDocument { Media = [media1, media2], Tracks = [track] };

        project.Validate(); // Valid

        // Invalid active take index
        clip.ActiveTakeIndex = 2;
        Assert.Throws<InvalidDataException>(() => project.Validate());

        // Negative active take index
        clip.ActiveTakeIndex = -1;
        Assert.Throws<InvalidDataException>(() => project.Validate());

        // Unknown media ID in take
        clip.ActiveTakeIndex = 0;
        clip.Takes[1].MediaId = Guid.NewGuid();
        Assert.Throws<InvalidDataException>(() => project.Validate());

        // Empty take ID
        clip.Takes[1].MediaId = media2.Id;
        clip.Takes[1].Id = Guid.Empty;
        Assert.Throws<InvalidDataException>(() => project.Validate());
    }

    [Fact]
    public void SerializationRoundTripPreservesMultiTakeEvents()
    {
        var media1 = new MediaAsset { Id = Guid.NewGuid(), Path = "test1.mp4", HasVideo = true, DurationSeconds = 10, FrameRate = 30, Width = 1920, Height = 1080 };
        var media2 = new MediaAsset { Id = Guid.NewGuid(), Path = "test2.mp4", HasVideo = true, DurationSeconds = 10, FrameRate = 30, Width = 1920, Height = 1080 };

        var clip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = media2.Id,
            Name = "Take 2",
            StartFrame = 0,
            DurationFrames = 90,
            SourceInFrame = 45,
            Takes =
            [
                new TimelineTake { Id = Guid.NewGuid(), MediaId = media1.Id, Name = "Take 1", SourceInFrame = 0 },
                new TimelineTake { Id = Guid.NewGuid(), MediaId = media2.Id, Name = "Take 2", SourceInFrame = 45 }
            ],
            ActiveTakeIndex = 1
        };

        var track = new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Video, Clips = [clip] };
        var project = new ProjectDocument { Media = [media1, media2], Tracks = [track] };

        var json = JsonSerializer.Serialize(project);
        var restored = JsonSerializer.Deserialize<ProjectDocument>(json)!;

        var restoredClip = restored.Tracks[0].Clips[0];
        Assert.NotNull(restoredClip.Takes);
        Assert.Equal(2, restoredClip.Takes.Count);
        Assert.Equal(1, restoredClip.ActiveTakeIndex);
        Assert.Equal("Take 2", restoredClip.Name);
        Assert.Equal(media2.Id, restoredClip.MediaId);
        Assert.Equal(45, restoredClip.SourceInFrame);
    }
}
