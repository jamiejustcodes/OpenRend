using System;
using System.IO;
using System.Linq;
using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class SlideshowEditorTests
{
    private static ProjectDocument CreateEmptyProject(double fps = 30.0) => new()
    {
        Name = "Slideshow Test",
        Width = 1920,
        Height = 1080,
        FrameRate = new Rational((int)fps, 1),
        Tracks = [new TimelineTrack { Kind = TrackKind.Video, Name = "Video 1" }]
    };

    [Fact]
    public void CreateSlideshowPlacesFrameAlignedClipsWithOverlaps()
    {
        var project = CreateEmptyProject(30.0);
        var editor = new TimelineEditor(project);

        var settings = new SlideshowSettings
        {
            StartMode = SlideshowStartMode.AtStart,
            SecondsPerSlide = 4.0, // 120 frames at 30 fps
            SecondsOfOverlap = 1.0, // 30 frames at 30 fps
            Effect = SlideshowEffectKind.Crossfade,
            Pictures =
            [
                new SlideshowPicture("C:/photos/slide1.jpg"),
                new SlideshowPicture("C:/photos/slide2.jpg"),
                new SlideshowPicture("C:/photos/slide3.jpg")
            ]
        };

        var clips = editor.CreateSlideshow(settings);

        Assert.Equal(3, clips.Count);

        // Slide 1: 0 to 120 (Duration 120)
        Assert.Equal(0, clips[0].StartFrame);
        Assert.Equal(120, clips[0].DurationFrames);
        Assert.Equal(0, clips[0].FadeInFrames);
        Assert.Null(clips[0].FadeInTransition);

        // Slide 2: starts at 120 - 30 = 90, duration 120, fade in 30
        Assert.Equal(90, clips[1].StartFrame);
        Assert.Equal(120, clips[1].DurationFrames);
        Assert.Equal(30, clips[1].FadeInFrames);
        Assert.NotNull(clips[1].FadeInTransition);
        Assert.Equal(VideoTransitionKind.Dissolve, clips[1].FadeInTransition!.Kind);

        // Slide 3: starts at 90 + (120 - 30) = 180, duration 120, fade in 30
        Assert.Equal(180, clips[2].StartFrame);
        Assert.Equal(120, clips[2].DurationFrames);
        Assert.Equal(30, clips[2].FadeInFrames);
        Assert.NotNull(clips[2].FadeInTransition);
        Assert.Equal(VideoTransitionKind.Dissolve, clips[2].FadeInTransition!.Kind);

        // Total timeline duration: 180 + 120 = 300 frames (10 seconds)
        // 3 slides * 4s - 2 overlaps * 1s = 10s = 300 frames. Exactly matches!
        var track = project.Tracks.First(t => t.Kind == TrackKind.Video);
        Assert.Equal(300, track.Clips.Max(c => c.EndFrame));
    }

    [Fact]
    public void CreateSlideshowGeneratesKenBurnsCropKeyframes()
    {
        var project = CreateEmptyProject(30.0);
        var editor = new TimelineEditor(project);

        var settings = new SlideshowSettings
        {
            StartMode = SlideshowStartMode.AtStart,
            SecondsPerSlide = 5.0, // 150 frames
            SecondsOfOverlap = 1.0,
            Effect = SlideshowEffectKind.PanAndZoom,
            PanScaleAmount = 0.2, // 20% zoom
            Pictures =
            [
                new SlideshowPicture("C:/photos/landscape.jpg") { PanZoom = SlideshowPanZoom.ZoomIn },
                new SlideshowPicture("C:/photos/portrait.jpg") { PanZoom = SlideshowPanZoom.ZoomOut, OrientationDegrees = 90 }
            ]
        };

        var clips = editor.CreateSlideshow(settings);

        // Slide 1 has ZoomIn crop animation
        Assert.NotNull(clips[0].Crop);
        var crop0 = clips[0].Crop!;
        Assert.Equal(2, crop0.Keyframes.Count);
        var kf0 = crop0.Keyframes[0];
        var kf1 = crop0.Keyframes[1];

        Assert.Equal(0, kf0.Frame);
        Assert.Equal(149, kf1.Frame);
        // Start is full width (1920), end is zoomed in by 20% (1920 * 0.8 = 1536)
        Assert.Equal(1920, kf0.State.Width);
        Assert.Equal(1536, kf1.State.Width);

        // Slide 2 has ZoomOut and 90-degree rotation preserved
        Assert.NotNull(clips[1].Crop);
        var crop1 = clips[1].Crop!;
        var kf2_0 = crop1.Keyframes[0];
        var kf2_1 = crop1.Keyframes[1];
        Assert.Equal(90, kf2_0.State.RotationDegrees);
        Assert.Equal(90, kf2_1.State.RotationDegrees);
    }

    [Fact]
    public void CreateSlideshowSupportsAtomicUndoAndRedo()
    {
        var project = CreateEmptyProject(30.0);
        var editor = new TimelineEditor(project);

        var settings = new SlideshowSettings
        {
            StartMode = SlideshowStartMode.AtStart,
            SecondsPerSlide = 3.0,
            SecondsOfOverlap = 1.0,
            Pictures =
            [
                new SlideshowPicture("C:/p1.png"),
                new SlideshowPicture("C:/p2.png")
            ]
        };

        editor.CreateSlideshow(settings);
        var track = editor.Project.Tracks.First(t => t.Kind == TrackKind.Video);
        Assert.Equal(2, track.Clips.Count);
        Assert.Equal(2, editor.Project.Media.Count);

        // Undo removes both clips and media additions atomically
        editor.Undo();
        var undoneTrack = editor.Project.Tracks.First(t => t.Kind == TrackKind.Video);
        Assert.Empty(undoneTrack.Clips);
        Assert.Empty(editor.Project.Media);

        // Redo restores both clips and media additions
        editor.Redo();
        var redoneTrack = editor.Project.Tracks.First(t => t.Kind == TrackKind.Video);
        Assert.Equal(2, redoneTrack.Clips.Count);
        Assert.Equal(2, editor.Project.Media.Count);
    }
}

