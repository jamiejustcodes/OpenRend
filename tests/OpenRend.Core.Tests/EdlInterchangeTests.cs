using System;
using System.IO;
using System.Linq;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class EdlInterchangeTests
{
    [Fact]
    public void ExportToEdl_ProducesValidCmx3600HeaderAndEvents()
    {
        var project = new ProjectDocument
        {
            Name = "Feature Film Sequence",
            Width = 1920,
            Height = 1080,
            FrameRate = new Rational(24, 1)
        };

        var asset1 = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = @"C:\Media\Scene1_Take1.mov",
            DurationSeconds = 60.0,
            FrameRate = 24.0,
            Width = 1920,
            Height = 1080,
            HasVideo = true,
            HasAudio = true
        };
        var asset2 = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = @"C:\Media\Scene1_Take2.mov",
            DurationSeconds = 60.0,
            FrameRate = 24.0,
            Width = 1920,
            Height = 1080,
            HasVideo = true,
            HasAudio = false
        };
        project.Media.AddRange([asset1, asset2]);

        var vTrack = new TimelineTrack
        {
            Id = Guid.NewGuid(),
            Name = "Track 1",
            Kind = TrackKind.Video,
            Opacity = 1.0
        };
        vTrack.Clips.Add(new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = asset1.Id,
            Name = "Shot 1",
            StartFrame = 0,
            DurationFrames = 72, // 3 seconds at 24fps
            SourceInFrame = 24
        });
        vTrack.Clips.Add(new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = asset2.Id,
            Name = "Shot 2",
            StartFrame = 72,
            DurationFrames = 120, // 5 seconds at 24fps
            SourceInFrame = 0,
            FadeInFrames = 24,
            FadeInTransition = new VideoTransition { Kind = VideoTransitionKind.Dissolve }
        });
        project.Tracks.Add(vTrack);

        var aTrack = new TimelineTrack
        {
            Id = Guid.NewGuid(),
            Name = "Audio Track",
            Kind = TrackKind.Audio,
            Gain = 1.0
        };
        aTrack.Clips.Add(new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = asset1.Id,
            Name = "Audio 1",
            StartFrame = 0,
            DurationFrames = 72,
            SourceInFrame = 24
        });
        project.Tracks.Add(aTrack);

        var edl = EdlInterchange.ExportToEdl(project);

        Assert.Contains("TITLE: Feature Film Sequence", edl);
        Assert.Contains("FCM: NON-DROP FRAME", edl);
        Assert.Contains("001", edl);
        Assert.Contains("002", edl);
        Assert.Contains("003", edl);
        Assert.Contains(" C ", edl); // Cut
        Assert.Contains(" D    024", edl); // Dissolve of 24 frames
        Assert.Contains("* FROM CLIP NAME: Shot 1", edl);
        Assert.Contains("* FROM CLIP NAME: Shot 2", edl);
        Assert.Contains(@"* SOURCE FILE: C:\Media\Scene1_Take1.mov", edl);
    }

    [Fact]
    public void ImportFromEdl_ReconstructsTracksAndClipsCorrectly()
    {
        var edlContent = @"TITLE: Vegas Project Interchange
FCM: NON-DROP FRAME

001  REEL01   V     C        00:00:01:00 00:00:04:00 00:00:00:00 00:00:03:00
* FROM CLIP NAME: ActionScene.mp4
* SOURCE FILE: C:\Videos\ActionScene.mp4

002  REEL02   V     D    030 00:00:00:00 00:00:05:00 00:00:03:00 00:00:08:00
* FROM CLIP NAME: Landscape.mp4

003  REEL01   A     C        00:00:01:00 00:00:04:00 00:00:00:00 00:00:03:00
* FROM CLIP NAME: ActionSceneAudio.wav
";

        var project = EdlInterchange.ImportFromEdl(edlContent, new Rational(30, 1));

        Assert.Equal("Vegas Project Interchange", project.Name);
        Assert.Equal(30, project.FrameRate.Numerator);
        Assert.Equal(1, project.FrameRate.Denominator);

        Assert.Equal(2, project.Tracks.Count);
        var vTrack = project.Tracks.First(t => t.Kind == TrackKind.Video);
        var aTrack = project.Tracks.First(t => t.Kind == TrackKind.Audio);

        Assert.Equal(2, vTrack.Clips.Count);
        Assert.Single(aTrack.Clips);

        var clip1 = vTrack.Clips[0];
        Assert.Equal("ActionScene.mp4", clip1.Name);
        Assert.Equal(0, clip1.StartFrame);
        Assert.Equal(90, clip1.DurationFrames); // 3 seconds = 90 frames
        Assert.Equal(30, clip1.SourceInFrame); // 00:00:01:00 = 30 frames

        var clip2 = vTrack.Clips[1];
        Assert.Equal("Landscape.mp4", clip2.Name);
        Assert.Equal(90, clip2.StartFrame);
        Assert.Equal(150, clip2.DurationFrames); // 5 seconds = 150 frames
        Assert.Equal(30, clip2.FadeInFrames); // Dissolve of 30 frames
        Assert.NotNull(clip2.FadeInTransition);
        Assert.Equal(VideoTransitionKind.Dissolve, clip2.FadeInTransition.Kind);

        // Media assets created
        Assert.Equal(3, project.Media.Count);
    }

    [Fact]
    public void DropFrameTimecode_CalculatesAccurately()
    {
        var rate = new Rational(30000, 1001); // 29.97 fps

        // Frame 0 is 00:00:00;00
        var tc0 = EdlInterchange.FormatTimecode(0, rate, dropFrame: true);
        Assert.Equal("00:00:00;00", tc0);
        Assert.Equal(0, EdlInterchange.ParseTimecode(tc0, rate, dropFrame: true));

        // Frame 1800 (1 minute in NTSC drop-frame: skips frames 0 and 1 at start of minute 1)
        long frame1Min = 1800;
        var tc1Min = EdlInterchange.FormatTimecode(frame1Min, rate, dropFrame: true);
        Assert.Equal("00:01:00;02", tc1Min); // First frame of minute 1 is ;02 due to drop
        Assert.Equal(frame1Min, EdlInterchange.ParseTimecode(tc1Min, rate, dropFrame: true));

        // 10 minutes: 17982 frames
        long frame10Min = 17982;
        var tc10Min = EdlInterchange.FormatTimecode(frame10Min, rate, dropFrame: true);
        Assert.Equal("00:10:00;00", tc10Min); // Tenth minute has no drop!
        Assert.Equal(frame10Min, EdlInterchange.ParseTimecode(tc10Min, rate, dropFrame: true));
    }

    [Fact]
    public void ImportFromEdl_ThrowsOnEmptyDocument()
    {
        var emptyEdl = "TITLE: Empty List\nFCM: NON-DROP FRAME\n";
        Assert.Throws<InvalidDataException>(() => EdlInterchange.ImportFromEdl(emptyEdl));
    }
}
