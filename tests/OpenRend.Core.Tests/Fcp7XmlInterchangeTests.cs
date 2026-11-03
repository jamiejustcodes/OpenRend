using System;
using System.Collections.Generic;
using System.IO;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class Fcp7XmlInterchangeTests
{
    [Fact]
    public void ExportAndImportRoundTripPreservesProjectAttributes()
    {
        var project = new ProjectDocument
        {
            Name = "Feature Film Sequence",
            Width = 3840,
            Height = 2160,
            FrameRate = new Rational(30000, 1001) // 29.970 fps NTSC
        };

        var xml = Fcp7XmlInterchange.ExportToXml(project);
        Assert.NotNull(xml);
        Assert.Contains("<xmeml version=\"4\">", xml);
        Assert.Contains("<name>Feature Film Sequence</name>", xml);
        Assert.Contains("<width>3840</width>", xml);
        Assert.Contains("<height>2160</height>", xml);
        Assert.Contains("<timebase>30</timebase>", xml);
        Assert.Contains("<ntsc>TRUE</ntsc>", xml);

        var imported = Fcp7XmlInterchange.ImportFromXml(xml);
        Assert.Equal("Feature Film Sequence", imported.Name);
        Assert.Equal(3840, imported.Width);
        Assert.Equal(2160, imported.Height);
        Assert.Equal(30000, imported.FrameRate.Numerator);
        Assert.Equal(1001, imported.FrameRate.Denominator);
    }

    [Fact]
    public void ExportAndImportPreservesTracksClipsAndTiming()
    {
        var asset1 = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = @"C:\Media\A001_C001.mov",
            DurationSeconds = 120.0,
            Width = 1920,
            Height = 1080,
            FrameRate = 24.0,
            HasVideo = true,
            HasAudio = true
        };

        var videoClip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = asset1.Id,
            Name = "Take 1 Video",
            StartFrame = 48,
            DurationFrames = 96,
            SourceInFrame = 24,
            Opacity = 0.8
        };

        var audioClip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = asset1.Id,
            Name = "Take 1 Audio",
            StartFrame = 48,
            DurationFrames = 96,
            SourceInFrame = 24,
            Gain = 0.5
        };

        var project = new ProjectDocument
        {
            Name = "Clip Timing Project",
            Width = 1920,
            Height = 1080,
            FrameRate = new Rational(24, 1),
            Media = [asset1],
            Tracks =
            [
                new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Video, Name = "Video 1", Clips = [videoClip] },
                new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Audio, Name = "Audio 1", Clips = [audioClip] }
            ]
        };

        var xml = Fcp7XmlInterchange.ExportToXml(project);
        var imported = Fcp7XmlInterchange.ImportFromXml(xml);

        Assert.Equal(2, imported.Tracks.Count);
        var importedVideoTrack = Assert.Single(imported.Tracks, t => t.Kind == TrackKind.Video);
        var importedAudioTrack = Assert.Single(imported.Tracks, t => t.Kind == TrackKind.Audio);

        var impVClip = Assert.Single(importedVideoTrack.Clips);
        Assert.Equal("Take 1 Video", impVClip.Name);
        Assert.Equal(48, impVClip.StartFrame);
        Assert.Equal(96, impVClip.DurationFrames);
        Assert.Equal(24, impVClip.SourceInFrame);
        Assert.InRange(impVClip.Opacity, 0.79, 0.81);

        var impAClip = Assert.Single(importedAudioTrack.Clips);
        Assert.Equal("Take 1 Audio", impAClip.Name);
        Assert.Equal(48, impAClip.StartFrame);
        Assert.Equal(96, impAClip.DurationFrames);
        Assert.Equal(24, impAClip.SourceInFrame);
        Assert.InRange(impAClip.Gain, 0.49, 0.51);

        var impAsset = Assert.Single(imported.Media);
        Assert.Equal(Path.GetFullPath(@"C:\Media\A001_C001.mov"), Path.GetFullPath(impAsset.Path));
    }

    [Fact]
    public void ExportAndImportPreservesSequenceMarkers()
    {
        var project = new ProjectDocument
        {
            Name = "Markers Project",
            Width = 1920,
            Height = 1080,
            FrameRate = new Rational(30, 1),
            Markers =
            [
                new TimelineMarker { Id = Guid.NewGuid(), Index = 1, Frame = 30, Label = "Scene 1 Start" },
                new TimelineMarker { Id = Guid.NewGuid(), Index = 2, Frame = 150, Label = "Cut Here" }
            ]
        };

        var xml = Fcp7XmlInterchange.ExportToXml(project);
        var imported = Fcp7XmlInterchange.ImportFromXml(xml);

        Assert.Equal(2, imported.Markers.Count);
        Assert.Equal(30, imported.Markers[0].Frame);
        Assert.Equal("Scene 1 Start", imported.Markers[0].Label);
        Assert.Equal(150, imported.Markers[1].Frame);
        Assert.Equal("Cut Here", imported.Markers[1].Label);
    }

    [Fact]
    public void ImportRejectsInvalidXmlWithoutSequence()
    {
        var invalidXml = "<xmeml version=\"4\"><project><name>Empty</name></project></xmeml>";
        Assert.Throws<InvalidDataException>(() => Fcp7XmlInterchange.ImportFromXml(invalidXml));
    }
}
