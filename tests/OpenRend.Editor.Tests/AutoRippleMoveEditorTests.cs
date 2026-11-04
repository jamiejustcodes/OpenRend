using System.Threading;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class AutoRippleMoveEditorTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AggregateException(error);
    }

    [Fact]
    public void MoveSelection_AutoRippleAffectedTracks_ShiftsDownstreamClipsOnAffectedTracks_PreservesOtherTracksAndMarkers()
    {
        var doc = new ProjectDocument();
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 10, HasVideo = true };
        doc.Media.Add(media);

        var track1 = new TimelineTrack { Kind = TrackKind.Video };
        var clipA = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 30 };
        var clipB = new TimelineClip { MediaId = media.Id, StartFrame = 30, DurationFrames = 30 };
        track1.Clips.Add(clipA);
        track1.Clips.Add(clipB);
        doc.Tracks.Add(track1);

        var track2 = new TimelineTrack { Kind = TrackKind.Video };
        var clipC = new TimelineClip { MediaId = media.Id, StartFrame = 30, DurationFrames = 30 };
        track2.Clips.Add(clipC);
        doc.Tracks.Add(track2);

        var marker = new TimelineMarker { Index = 1, Label = "Marker 1", Frame = 40 };
        doc.Markers.Add(marker);

        var editor = new TimelineEditor(doc);

        // Move Clip A forward by 20 frames (start at 20) with AutoRipple on AffectedTracks
        editor.MoveSelection([clipA.Id], clipA.Id, track1.Id, 20, ripple: true, rippleMode: AutoRippleMode.AffectedTracks);

        var cA = editor.Project.Tracks[0].Clips.First(c => c.Id == clipA.Id);
        var cB = editor.Project.Tracks[0].Clips.First(c => c.Id == clipB.Id);
        var cC = editor.Project.Tracks[1].Clips.First(c => c.Id == clipC.Id);
        var m = editor.Project.Markers[0];

        Assert.Equal(20, cA.StartFrame);
        Assert.Equal(50, cB.StartFrame); // Shifted by +20
        Assert.Equal(30, cC.StartFrame); // Unaffected track untouched
        Assert.Equal(40, m.Frame);       // Marker untouched in AffectedTracks mode

        // Verify atomic undo
        editor.Undo();
        cA = editor.Project.Tracks[0].Clips.First(c => c.Id == clipA.Id);
        cB = editor.Project.Tracks[0].Clips.First(c => c.Id == clipB.Id);
        Assert.Equal(0, cA.StartFrame);
        Assert.Equal(30, cB.StartFrame);
    }

    [Fact]
    public void MoveSelection_AutoRippleAffectedTracksAndMarkers_ShiftsClipsAndMarkers()
    {
        var doc = new ProjectDocument();
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 10, HasVideo = true };
        doc.Media.Add(media);

        var track1 = new TimelineTrack { Kind = TrackKind.Video };
        var clipA = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 30 };
        var clipB = new TimelineClip { MediaId = media.Id, StartFrame = 30, DurationFrames = 30 };
        track1.Clips.Add(clipA);
        track1.Clips.Add(clipB);
        doc.Tracks.Add(track1);

        var track2 = new TimelineTrack { Kind = TrackKind.Video };
        var clipC = new TimelineClip { MediaId = media.Id, StartFrame = 30, DurationFrames = 30 };
        track2.Clips.Add(clipC);
        doc.Tracks.Add(track2);

        var marker = new TimelineMarker { Index = 1, Label = "Scene Cut", Frame = 40 };
        doc.Markers.Add(marker);

        var editor = new TimelineEditor(doc);

        editor.MoveSelection([clipA.Id], clipA.Id, track1.Id, 25, ripple: true, rippleMode: AutoRippleMode.AffectedTracksAndMarkers);

        var cA = editor.Project.Tracks[0].Clips.First(c => c.Id == clipA.Id);
        var cB = editor.Project.Tracks[0].Clips.First(c => c.Id == clipB.Id);
        var cC = editor.Project.Tracks[1].Clips.First(c => c.Id == clipC.Id);
        var m = editor.Project.Markers[0];

        Assert.Equal(25, cA.StartFrame);
        Assert.Equal(55, cB.StartFrame); // Shifted by +25
        Assert.Equal(30, cC.StartFrame); // Track 2 untouched
        Assert.Equal(65, m.Frame);       // Marker shifted by +25 (40 + 25 = 65)
    }

    [Fact]
    public void MoveSelection_AutoRippleAllTracksAndMarkers_ShiftsAllTracksAndMarkers()
    {
        var doc = new ProjectDocument();
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 10, HasVideo = true };
        doc.Media.Add(media);

        var track1 = new TimelineTrack { Kind = TrackKind.Video };
        var clipA = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 30 };
        var clipB = new TimelineClip { MediaId = media.Id, StartFrame = 30, DurationFrames = 30 };
        track1.Clips.Add(clipA);
        track1.Clips.Add(clipB);
        doc.Tracks.Add(track1);

        var track2 = new TimelineTrack { Kind = TrackKind.Video };
        var clipC = new TimelineClip { MediaId = media.Id, StartFrame = 30, DurationFrames = 30 };
        track2.Clips.Add(clipC);
        doc.Tracks.Add(track2);

        var marker = new TimelineMarker { Index = 1, Label = "Beat 1", Frame = 45 };
        doc.Markers.Add(marker);

        var editor = new TimelineEditor(doc);

        editor.MoveSelection([clipA.Id], clipA.Id, track1.Id, 15, ripple: true, rippleMode: AutoRippleMode.AllTracksAndMarkers);

        var cA = editor.Project.Tracks[0].Clips.First(c => c.Id == clipA.Id);
        var cB = editor.Project.Tracks[0].Clips.First(c => c.Id == clipB.Id);
        var cC = editor.Project.Tracks[1].Clips.First(c => c.Id == clipC.Id);
        var m = editor.Project.Markers[0];

        Assert.Equal(15, cA.StartFrame);
        Assert.Equal(45, cB.StartFrame); // Shifted by +15
        Assert.Equal(45, cC.StartFrame); // Track 2 clip shifted by +15
        Assert.Equal(60, m.Frame);       // Marker shifted by +15 (45 + 15 = 60)
    }

    [Fact]
    public void MoveSelectionToNewTrack_AutoRipple_CreatesTrackAndRipples()
    {
        var doc = new ProjectDocument();
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 10, HasVideo = true };
        doc.Media.Add(media);

        var track1 = new TimelineTrack { Kind = TrackKind.Video };
        var clipA = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 30 };
        var clipB = new TimelineClip { MediaId = media.Id, StartFrame = 30, DurationFrames = 30 };
        track1.Clips.Add(clipA);
        track1.Clips.Add(clipB);
        doc.Tracks.Add(track1);

        var marker = new TimelineMarker { Index = 1, Label = "Cue", Frame = 35 };
        doc.Markers.Add(marker);

        var editor = new TimelineEditor(doc);

        var newTrack = editor.MoveSelectionToNewTrack([clipA.Id], clipA.Id, 0, TrackKind.Video, 20, ripple: true, rippleMode: AutoRippleMode.AffectedTracksAndMarkers);

        Assert.Equal(2, editor.Project.Tracks.Count);
        Assert.Contains(newTrack, editor.Project.Tracks);

        var cB = track1.Clips.First(c => c.Id == clipB.Id);
        var m = editor.Project.Markers[0];

        Assert.Equal(50, cB.StartFrame); // Shifted by +20
        Assert.Equal(55, m.Frame);       // Marker shifted by +20 (35 + 20 = 55)
    }

    [Fact]
    public void Paste_AutoRipple_ShiftsDownstreamClipsAndMarkers()
    {
        RunSta(() =>
        {
            var mediaService = new MediaService();
            var renderer = new TimelineRenderService(mediaService);
            var vm = new EditorViewModel(mediaService, renderer, new ExportService(renderer));

            var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 10, HasVideo = true };
            vm.Project.Media.Add(media);

            vm.Project.Tracks.Clear();
            var track = new TimelineTrack { Kind = TrackKind.Video };
            var clipA = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 20 };
            var clipB = new TimelineClip { MediaId = media.Id, StartFrame = 50, DurationFrames = 20 };
            track.Clips.Add(clipA);
            track.Clips.Add(clipB);
            vm.Project.Tracks.Add(track);

            var marker = new TimelineMarker { Index = 1, Label = "Pasted Cue", Frame = 60 };
            vm.Project.Markers.Add(marker);

            vm.Ripple = true;
            vm.RippleMode = AutoRippleMode.AllTracksAndMarkers;

            // Copy clip A
            vm.Selection.Add(clipA.Id);
            vm.Copy();

            // Position at 30 and paste
            vm.Position = 30;
            vm.Paste();

            // Clip B was at 50 (>= 30). Pasted clip A has duration 20.
            // With ripple, Clip B shifts forward to 70!
            // Marker was at 60 (>= 30). With ripple, marker shifts forward to 80!
            var cB = vm.Project.Tracks[0].Clips.First(c => c.Id == clipB.Id);
            var m = vm.Project.Markers[0];

            Assert.Equal(70, cB.StartFrame);
            Assert.Equal(80, m.Frame);
        });
    }
}
