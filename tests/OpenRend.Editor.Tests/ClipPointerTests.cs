using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OpenRend.Core;
using OpenRend.UI;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class ClipPointerTests
{
    private static (TimelineView View, TimelineTrack Track, TimelineClip Clip) Create()
    {
        var clip = new TimelineClip { StartFrame = 30, DurationFrames = 60 };
        var track = new TimelineTrack { Kind = TrackKind.Video, Clips = [clip] };
        var view = new TimelineView { Project = new() { FrameRate = new(30, 1), Tracks = [track] }, PixelsPerSecond = 100 };
        view.Measure(new Size(1000, 400)); view.Arrange(new Rect(0, 0, 1000, 400));
        return (view, track, clip);
    }

    [Theory]
    [InlineData(348, "TrimLeft")]
    [InlineData(365, "TrimLeft")]
    [InlineData(547, "TrimRight")]
    [InlineData(564, "TrimRight")]
    [InlineData(456, "Move")]
    public void HoverAndClickShareTheExpandedEdgeTarget(double x, string mode) => Sta(() =>
    {
        var (view, _, clip) = Create();
        var hit = view.HitClip(new Point(x, 80));
        Assert.NotNull(hit); Assert.Same(clip, hit.Value.Clip); Assert.Equal(mode, hit.Value.Mode);
        Assert.Same(mode == "Move" ? Cursors.Arrow : Cursors.SizeWE, view.CursorAt(new Point(x, 80)));
    });

    [Theory]
    [InlineData(345, 80)]
    [InlineData(567, 80)]
    [InlineData(255, 80)]
    [InlineData(356, 45)]
    [InlineData(356, 399)]
    public void EmptySpaceRulerAndHeadersDoNotAdvertiseTrimming(double x, double y) => Sta(() =>
    {
        var (view, _, _) = Create();
        Assert.Null(view.HitClip(new Point(x, y)));
        Assert.Same(Cursors.Arrow, view.CursorAt(new Point(x, y)));
    });

    [Fact]
    public void ShortEventsKeepAMovableMiddleAndUseTheNearestEdge() => Sta(() =>
    {
        var (view, _, clip) = Create(); clip.DurationFrames = 6;
        Assert.Equal("TrimLeft", view.HitClip(new Point(360, 80))!.Value.Mode);
        Assert.Equal("Move", view.HitClip(new Point(366, 80))!.Value.Mode);
        Assert.Equal("TrimRight", view.HitClip(new Point(373, 80))!.Value.Mode);
    });

    [Fact]
    public void AdjacentEventsUseTheSideActuallyUnderThePointer() => Sta(() =>
    {
        var (view, track, first) = Create();
        var second = new TimelineClip { StartFrame = 90, DurationFrames = 60 }; track.Clips.Add(second);
        Assert.Same(first, view.HitClip(new Point(551, 80))!.Value.Clip);
        Assert.Equal("TrimRight", view.HitClip(new Point(551, 80))!.Value.Mode);
        Assert.Same(second, view.HitClip(new Point(556, 80))!.Value.Clip);
        Assert.Equal("TrimLeft", view.HitClip(new Point(561, 80))!.Value.Mode);
    });

    [Fact]
    public void AClippedEventDoesNotTurnTheViewportBoundaryIntoATrimHandle() => Sta(() =>
    {
        var (view, _, clip) = Create(); view.Offset = 150;
        Assert.Equal("Move", view.HitClip(new Point(257, 80))!.Value.Mode);
        Assert.Equal("TrimRight", view.HitClip(new Point(405, 80))!.Value.Mode);
        clip.DurationFrames = 900;
        Assert.Equal("Move", view.HitClip(new Point(999, 80))!.Value.Mode);
    });

    [Fact]
    public void LockedEventsShowTheUnavailableCursorAcrossTheirBodyAndEdges() => Sta(() =>
    {
        var (view, track, _) = Create(); track.IsLocked = true;
        Assert.Same(Cursors.No, view.CursorAt(new Point(348, 80)));
        Assert.Same(Cursors.No, view.CursorAt(new Point(456, 80)));
        Assert.Same(Cursors.No, view.CursorAt(new Point(564, 80)));
    });

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); } catch (Exception error) { failure = error; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Clip pointer check timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void TransitionDropTargetsTheOverlapAndRejectsLockedOrAmbiguousTracks() => Sta(() =>
    {
        var (view, track, outgoing) = Create();
        var incoming = new TimelineClip { StartFrame = 75, DurationFrames = 60 };
        track.Clips.Add(incoming);
        var preset = new VideoTransition();
        var point = new Point(TimelineView.HeaderWidth + 80.0 / 30 * view.PixelsPerSecond, 80);
        var target = view.TransitionAt(point, preset);
        Assert.NotNull(target);
        Assert.Equal(outgoing.Id, target.OutgoingClipId);
        Assert.Equal(incoming.Id, target.IncomingClipId);
        Assert.NotSame(preset, target.Transition);
        Assert.Null(view.TransitionAt(new Point(360, 80), preset));
        Assert.Null(view.TransitionAt(new Point(100, 80), preset));
        track.IsLocked = true;
        Assert.Null(view.TransitionAt(point, preset));
        track.IsLocked = false;
        track.Clips.Add(new TimelineClip { StartFrame = 76, DurationFrames = 3 });
        Assert.Null(view.TransitionAt(point, preset));
    });

    [Fact]
    public void TransitionDropFindsACutOnlyWithinItsEdgeTarget() => Sta(() =>
    {
        var (view, track, outgoing) = Create();
        var incoming = new TimelineClip { StartFrame = outgoing.EndFrame, DurationFrames = 60 };
        track.Clips.Add(incoming);
        var cutX = TimelineView.HeaderWidth + view.Project.FrameRate.Seconds(incoming.StartFrame) * view.PixelsPerSecond;
        var target = view.TransitionAt(new Point(cutX + 5, 80), new VideoTransition());
        Assert.NotNull(target);
        Assert.True(target.IsCut);
        Assert.Equal(outgoing.Id, target.OutgoingClipId);
        Assert.Equal(incoming.Id, target.IncomingClipId);
        Assert.Null(view.TransitionAt(new Point(cutX + 7, 80), new VideoTransition()));
    });

    [Fact]
    public void CtrlShiftMDoesNotSwallowShortcutAllowingAudioMute() => Sta(() =>
    {
        var (view, _, _) = Create();
        // Shift+M without Ctrl triggers Solo on the timeline track
        Assert.True(view.HandleKeyboardInput(Key.M, ModifierKeys.Shift));
        // M without Ctrl triggers Mute on the timeline track
        Assert.True(view.HandleKeyboardInput(Key.M, ModifierKeys.None));
        // Ctrl+Shift+M is not handled by the timeline view, so it propagates to MainWindow for audio mute
        Assert.False(view.HandleKeyboardInput(Key.M, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(view.HandleKeyboardInput(Key.M, ModifierKeys.Control));
    });

    [Fact]
    public void EvaluateDragTargetDifferentiatesBoundaryInsertionsAndExistingTrack() => Sta(() =>
    {
        var (view, _, _) = Create(); // 1 video track at RulerHeight = 50, TrackHeight = 64
        // In ruler / above track 0: insert at top
        var topRuler = view.EvaluateDragTarget(40, TrackKind.Video);
        Assert.True(topRuler.IsNewTrack);
        Assert.Equal(0, topRuler.TrackIndex);

        // Near top boundary of track 0: insert at top (index 0)
        var topBoundary = view.EvaluateDragTarget(55, TrackKind.Video);
        Assert.True(topBoundary.IsNewTrack);
        Assert.Equal(0, topBoundary.TrackIndex);

        // Center of track 0: target existing track 0
        var center = view.EvaluateDragTarget(82, TrackKind.Video);
        Assert.False(center.IsNewTrack);
        Assert.Equal(0, center.TrackIndex);

        // Near bottom boundary of track 0: insert below track 0 (index 1)
        var bottomBoundary = view.EvaluateDragTarget(105, TrackKind.Video);
        Assert.True(bottomBoundary.IsNewTrack);
        Assert.Equal(1, bottomBoundary.TrackIndex);

        // Empty space below all tracks: insert at bottom (index 1)
        var emptyBelow = view.EvaluateDragTarget(200, TrackKind.Video);
        Assert.True(emptyBelow.IsNewTrack);
        Assert.Equal(1, emptyBelow.TrackIndex);
    });

    [Fact]
    public void EvaluateDragTargetInterTrackBoundaryBetweenSameKindTracks() => Sta(() =>
    {
        var (view, track0, _) = Create();
        var track1 = new TimelineTrack { Kind = TrackKind.Video };
        view.Project.Tracks.Add(track1); // Track 0 at 50..114, Track 1 at 114..178

        // Bottom of track 0 indicates insertion between tracks (index 1)
        var bottomTrack0 = view.EvaluateDragTarget(105, TrackKind.Video);
        Assert.True(bottomTrack0.IsNewTrack);
        Assert.Equal(1, bottomTrack0.TrackIndex);

        // Top of track 1 also indicates insertion between tracks (index 1)
        var topTrack1 = view.EvaluateDragTarget(120, TrackKind.Video);
        Assert.True(topTrack1.IsNewTrack);
        Assert.Equal(1, topTrack1.TrackIndex);

        // Center of track 1 targets existing track 1
        var centerTrack1 = view.EvaluateDragTarget(146, TrackKind.Video);
        Assert.False(centerTrack1.IsNewTrack);
        Assert.Equal(1, centerTrack1.TrackIndex);

        // Bottom of track 1 indicates insertion below track 1 (index 2)
        var bottomTrack1 = view.EvaluateDragTarget(170, TrackKind.Video);
        Assert.True(bottomTrack1.IsNewTrack);
        Assert.Equal(2, bottomTrack1.TrackIndex);
    });

    [Fact]
    public void EvaluateDragTargetIncompatibleTrackAlwaysInsertsNewLayer() => Sta(() =>
    {
        var (view, videoTrack, _) = Create();
        var audioTrack = new TimelineTrack { Kind = TrackKind.Audio };
        view.Project.Tracks.Add(audioTrack); // Video Track 0 at 50..114, Audio Track 1 at 114..178

        // Dragging Video over upper half of Audio track: inserts new Video layer above Audio track (index 1)
        var videoOverAudioUpper = view.EvaluateDragTarget(130, TrackKind.Video);
        Assert.True(videoOverAudioUpper.IsNewTrack);
        Assert.Equal(1, videoOverAudioUpper.TrackIndex);
        Assert.Equal(TrackKind.Video, videoOverAudioUpper.NewTrackKind);

        // Dragging Video over lower half of Audio track: inserts new Video layer below Audio track (index 2)
        var videoOverAudioLower = view.EvaluateDragTarget(160, TrackKind.Video);
        Assert.True(videoOverAudioLower.IsNewTrack);
        Assert.Equal(2, videoOverAudioLower.TrackIndex);
        Assert.Equal(TrackKind.Video, videoOverAudioLower.NewTrackKind);

        // Dragging Audio over upper half of Video track: inserts new Audio layer above Video track (index 0)
        var audioOverVideoUpper = view.EvaluateDragTarget(65, TrackKind.Audio);
        Assert.True(audioOverVideoUpper.IsNewTrack);
        Assert.Equal(0, audioOverVideoUpper.TrackIndex);
        Assert.Equal(TrackKind.Audio, audioOverVideoUpper.NewTrackKind);

        // Dragging Audio over lower half of Video track: inserts new Audio layer below Video track (index 1)
        var audioOverVideoLower = view.EvaluateDragTarget(95, TrackKind.Audio);
        Assert.True(audioOverVideoLower.IsNewTrack);
        Assert.Equal(1, audioOverVideoLower.TrackIndex);
        Assert.Equal(TrackKind.Audio, audioOverVideoLower.NewTrackKind);
    });
}
