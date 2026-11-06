using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class FcpxmlInterchangeTests
{
    [Fact]
    public void ExportToXml_CreatesValidFcpxmlDocument()
    {
        var media1 = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = @"C:\Media\clip1.mp4",
            DurationSeconds = 10.0,
            Width = 1920,
            Height = 1080,
            HasVideo = true,
            HasAudio = true
        };

        var project = new ProjectDocument
        {
            Name = "Test FCPXML Project",
            Width = 1920,
            Height = 1080,
            FrameRate = new Rational(30, 1),
            Media = [media1],
            Tracks =
            [
                new TimelineTrack
                {
                    Id = Guid.NewGuid(),
                    Kind = TrackKind.Video,
                    Name = "Video 1",
                    Clips =
                    [
                        new TimelineClip
                        {
                            Id = Guid.NewGuid(),
                            MediaId = media1.Id,
                            Name = "Clip 1",
                            StartFrame = 0,
                            DurationFrames = 60, // 2s
                            SourceInFrame = 30   // 1s
                        },
                        new TimelineClip
                        {
                            Id = Guid.NewGuid(),
                            MediaId = media1.Id,
                            Name = "Clip 2",
                            StartFrame = 90,     // 3s (gap from 2s to 3s)
                            DurationFrames = 60, // 2s
                            SourceInFrame = 0
                        }
                    ]
                },
                new TimelineTrack
                {
                    Id = Guid.NewGuid(),
                    Kind = TrackKind.Audio,
                    Name = "Audio 1",
                    Clips =
                    [
                        new TimelineClip
                        {
                            Id = Guid.NewGuid(),
                            MediaId = media1.Id,
                            Name = "Audio Clip",
                            StartFrame = 0,
                            DurationFrames = 90,
                            SourceInFrame = 0,
                            Gain = 0.5 // -6dB approx
                        }
                    ]
                }
            ],
            Markers =
            [
                new TimelineMarker { Id = Guid.NewGuid(), Index = 1, Frame = 45, Label = "Intro Marker" }
            ]
        };

        var xml = FcpxmlInterchange.ExportToXml(project);
        Assert.NotNull(xml);

        var doc = XDocument.Parse(xml);
        Assert.Equal("fcpxml", doc.Root?.Name.LocalName);
        Assert.Equal("1.9", doc.Root?.Attribute("version")?.Value);

        // Resources verification
        var resources = doc.Root?.Element("resources");
        Assert.NotNull(resources);
        var format = resources.Element("format");
        Assert.NotNull(format);
        Assert.Equal("1920", format.Attribute("width")?.Value);
        Assert.Equal("1080", format.Attribute("height")?.Value);
        Assert.Equal("1/30s", format.Attribute("frameDuration")?.Value);

        var asset = resources.Element("asset");
        Assert.NotNull(asset);
        Assert.Equal("clip1.mp4", asset.Attribute("name")?.Value);

        // Spine and Gaps
        var spine = doc.Descendants("spine").FirstOrDefault();
        Assert.NotNull(spine);

        var elements = spine.Elements().ToList();
        // First clip (offset 0, duration 2s = 60/30s)
        var clip1Elem = elements.FirstOrDefault(e => e.Name.LocalName == "asset-clip" && e.Attribute("offset")?.Value == "0s");
        Assert.NotNull(clip1Elem);
        Assert.Equal("2s", clip1Elem.Attribute("duration")?.Value);

        // Gap between 60 (2s) and 90 (3s): duration = 30 frames = 1s
        var gapElem = elements.FirstOrDefault(e => e.Name.LocalName == "gap" && e.Attribute("offset")?.Value == "2s");
        Assert.NotNull(gapElem);
        Assert.Equal("1s", gapElem.Attribute("duration")?.Value);

        // Second clip at offset 3s
        var clip2Elem = elements.FirstOrDefault(e => e.Name.LocalName == "asset-clip" && e.Attribute("offset")?.Value == "3s");
        Assert.NotNull(clip2Elem);

        // Audio clip with negative lane and adjust-volume
        var audioClipElem = elements.FirstOrDefault(e => e.Name.LocalName == "asset-clip" && e.Attribute("lane")?.Value == "-1");
        Assert.NotNull(audioClipElem);
        var adjustVol = audioClipElem.Element("adjust-volume");
        Assert.NotNull(adjustVol);
        Assert.Contains("dB", adjustVol.Attribute("amount")?.Value);

        // Marker on spine
        var markerElem = spine.Elements("marker").FirstOrDefault();
        Assert.NotNull(markerElem);
        Assert.Equal("Intro Marker", markerElem.Attribute("value")?.Value);
    }

    [Fact]
    public void RoundTrip_PreservesTimelineStructureAndTiming()
    {
        var media1 = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = @"C:\Media\sceneA.mp4",
            DurationSeconds = 20.0,
            Width = 3840,
            Height = 2160,
            HasVideo = true,
            HasAudio = true
        };

        var originalProject = new ProjectDocument
        {
            Name = "4K Cinema Timeline",
            Width = 3840,
            Height = 2160,
            FrameRate = new Rational(24000, 1001), // 23.976 fps
            Media = [media1],
            Tracks =
            [
                new TimelineTrack
                {
                    Id = Guid.NewGuid(),
                    Kind = TrackKind.Video,
                    Name = "Video 1",
                    Clips =
                    [
                        new TimelineClip
                        {
                            Id = Guid.NewGuid(),
                            MediaId = media1.Id,
                            Name = "Scene 1",
                            StartFrame = 0,
                            DurationFrames = 120,
                            SourceInFrame = 24
                        }
                    ]
                },
                new TimelineTrack
                {
                    Id = Guid.NewGuid(),
                    Kind = TrackKind.Audio,
                    Name = "Audio 1",
                    Clips =
                    [
                        new TimelineClip
                        {
                            Id = Guid.NewGuid(),
                            MediaId = media1.Id,
                            Name = "Dialogue",
                            StartFrame = 0,
                            DurationFrames = 120,
                            SourceInFrame = 24,
                            Gain = 1.0
                        }
                    ]
                }
            ],
            Markers =
            [
                new TimelineMarker { Id = Guid.NewGuid(), Index = 1, Frame = 48, Label = "Action Start" }
            ]
        };

        var xml = FcpxmlInterchange.ExportToXml(originalProject);
        var imported = FcpxmlInterchange.ImportFromXml(xml);

        Assert.Equal(originalProject.Width, imported.Width);
        Assert.Equal(originalProject.Height, imported.Height);
        Assert.Equal(originalProject.FrameRate.Numerator, imported.FrameRate.Numerator);
        Assert.Equal(originalProject.FrameRate.Denominator, imported.FrameRate.Denominator);

        Assert.Single(imported.Media);
        Assert.Equal(Path.GetFullPath(media1.Path), Path.GetFullPath(imported.Media[0].Path));

        Assert.Equal(2, imported.Tracks.Count);
        var vTrack = imported.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Video);
        var aTrack = imported.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Audio);
        Assert.NotNull(vTrack);
        Assert.NotNull(aTrack);

        Assert.Single(vTrack.Clips);
        var vClip = vTrack.Clips[0];
        Assert.Equal(0, vClip.StartFrame);
        Assert.Equal(120, vClip.DurationFrames);
        Assert.Equal(24, vClip.SourceInFrame);

        Assert.Single(aTrack.Clips);
        var aClip = aTrack.Clips[0];
        Assert.Equal(0, aClip.StartFrame);
        Assert.Equal(120, aClip.DurationFrames);

        Assert.Single(imported.Markers);
        Assert.Equal(48, imported.Markers[0].Frame);
        Assert.Equal("Action Start", imported.Markers[0].Label);
    }

    [Fact]
    public void ImportFromXml_HandlesDaVinciResolveStyleFlatFcpxml()
    {
        var sampleXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE fcpxml>
<fcpxml version=""1.9"">
    <resources>
        <format id=""r1"" name=""FFVideoFormat1080p25"" frameDuration=""1/25s"" width=""1920"" height=""1080"" />
        <asset id=""r2"" name=""drone_shot.mov"" src=""file://localhost/D:/Footage/drone_shot.mov"" duration=""250/25s"" hasVideo=""1"" hasAudio=""0"" format=""r1"" />
    </resources>
    <library>
        <event name=""Commercial"">
            <project name=""Cut_v01"">
                <sequence format=""r1"" duration=""200/25s"" tcStart=""0s"" tcFormat=""NDF"">
                    <spine>
                        <gap name=""Lead In"" offset=""0s"" duration=""25/25s"" start=""0s"" />
                        <asset-clip ref=""r2"" offset=""25/25s"" name=""Drone Shot"" duration=""100/25s"" start=""50/25s"">
                            <adjust-volume amount=""-3.0dB"" />
                            <marker start=""60/25s"" duration=""1/25s"" value=""Key Moment"" />
                        </asset-clip>
                    </spine>
                </sequence>
            </project>
        </event>
    </library>
</fcpxml>";

        var project = FcpxmlInterchange.ImportFromXml(sampleXml);

        Assert.Equal(1920, project.Width);
        Assert.Equal(1080, project.Height);
        Assert.Equal(25, project.FrameRate.Numerator);
        Assert.Equal(1, project.FrameRate.Denominator);

        Assert.Single(project.Media);
        Assert.Contains("drone_shot.mov", project.Media[0].Path);

        var vTrack = project.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Video);
        Assert.NotNull(vTrack);
        Assert.Single(vTrack.Clips);

        var clip = vTrack.Clips[0];
        Assert.Equal(25, clip.StartFrame);       // 1 second in (after 25 frame gap)
        Assert.Equal(100, clip.DurationFrames);   // 4 seconds = 100 frames
        Assert.Equal(50, clip.SourceInFrame);     // 2 seconds source in = 50 frames
        Assert.True(clip.Gain < 0.9 && clip.Gain > 0.6); // -3dB approx ~0.707

        Assert.Single(project.Markers);
        // marker start was at 60/25s relative to source start 50/25s -> offset + 10 frames = 35 frames
        Assert.Equal(35, project.Markers[0].Frame);
        Assert.Equal("Key Moment", project.Markers[0].Label);
    }

    [Theory]
    [InlineData("10s", 10.0)]
    [InlineData("0s", 0.0)]
    [InlineData("1001/30000s", 1001.0 / 30000.0)]
    [InlineData("1/24s", 1.0 / 24.0)]
    [InlineData("2.5s", 2.5)]
    [InlineData("250/25s", 10.0)]
    public void TryParseFcpxmlSeconds_ParsesStandardTimeStrings(string input, double expected)
    {
        var success = FcpxmlInterchange.TryParseFcpxmlSeconds(input, out var seconds);
        Assert.True(success);
        Assert.Equal(expected, seconds, precision: 6);
    }

    [Theory]
    [InlineData(0, 30, 1, "0s")]
    [InlineData(30, 30, 1, "1s")]
    [InlineData(15, 30, 1, "1/2s")]
    [InlineData(30, 30000, 1001, "1001/1000s")]
    public void FormatTimeRational_FormatsExactFractionalDurations(long frames, int num, int den, string expected)
    {
        var rate = new Rational(num, den);
        var formatted = FcpxmlInterchange.FormatTimeRational(frames, rate);
        Assert.Equal(expected, formatted);
    }
}

