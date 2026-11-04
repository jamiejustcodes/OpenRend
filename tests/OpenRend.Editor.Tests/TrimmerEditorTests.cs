using System;
using System.IO;
using System.Linq;
using System.Threading;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class TrimmerEditorTests
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

    private static EditorViewModel CreateViewModel()
    {
        var media = new MediaService();
        var renderer = new TimelineRenderService(media);
        return new EditorViewModel(media, renderer, new ExportService(renderer));
    }

    [Fact]
    public void EditorViewModel_PlaceSubclip_InsertsClipWithSourceInAndDuration_SupportsUndoRedo()
    {
        var doc = new ProjectDocument();
        var media = new MediaAsset { Path = "test_video.mp4", DurationSeconds = 10, HasVideo = true, HasAudio = true };
        doc.Media.Add(media);
        var track = new TimelineTrack { Kind = TrackKind.Video, Name = "Video 1" };
        doc.Tracks.Add(track);

        var vm = CreateViewModel();
        vm.Replace(doc, null);
        vm.Position = 60; // timeline project frame 60

        // Place subclip with SourceIn = 30, Duration = 45 frames
        vm.PlaceSubclip(media.Id, sourceIn: 30, duration: 45, trackId: track.Id, at: 60);

        var placedVideo = vm.Project.Tracks[0].Clips.Single();
        Assert.Equal(media.Id, placedVideo.MediaId);
        Assert.Equal(60, placedVideo.StartFrame);
        Assert.Equal(30, placedVideo.SourceInFrame);
        Assert.Equal(45, placedVideo.DurationFrames);

        // Verify linked audio track clip was created with identical subclip bounds
        var audioTrack = vm.Project.Tracks.Single(t => t.Kind == TrackKind.Audio);
        var placedAudio = audioTrack.Clips.Single();
        Assert.Equal(30, placedAudio.SourceInFrame);
        Assert.Equal(45, placedAudio.DurationFrames);
        Assert.Equal(placedVideo.GroupId, placedAudio.GroupId);

        // Verify Undo restores project state
        Assert.True(vm.Editor.CanUndo);
        vm.Editor.Undo();
        Assert.Empty(vm.Project.Tracks[0].Clips);

        // Verify Redo re-places subclip
        Assert.True(vm.Editor.CanRedo);
        vm.Editor.Redo();
        Assert.Single(vm.Project.Tracks[0].Clips);
        Assert.Equal(30, vm.Project.Tracks[0].Clips[0].SourceInFrame);
        Assert.Equal(45, vm.Project.Tracks[0].Clips[0].DurationFrames);
    }

    [Fact]
    public void TrimmerControl_LoadMedia_CalculatesTotalFramesAndInitializesPoints() => RunSta(() =>
    {
        var trimmer = new TrimmerControl();
        var doc = new ProjectDocument { FrameRate = new Rational(30, 1) };
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 5.0, HasVideo = true };
        doc.Media.Add(media);

        var vm = CreateViewModel();
        vm.Replace(doc, null);
        trimmer.Initialize(vm);

        trimmer.LoadMedia(media);

        Assert.NotNull(trimmer.ActiveMedia);
        Assert.Equal(150, trimmer.OutFrame); // 5.0s * 30fps = 150 frames
        Assert.Equal(0, trimmer.InFrame);
        Assert.Equal(0, trimmer.CurrentFrame);
    });

    [Fact]
    public void TrimmerControl_MarkInAndOut_UpdatesAndClampsBounds() => RunSta(() =>
    {
        var trimmer = new TrimmerControl();
        var doc = new ProjectDocument { FrameRate = new Rational(30, 1) };
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 10.0, HasVideo = true };
        doc.Media.Add(media);

        var vm = CreateViewModel();
        vm.Replace(doc, null);
        trimmer.Initialize(vm);
        trimmer.LoadMedia(media);

        // Total frames: 300
        trimmer.SeekTo(45);
        trimmer.MarkIn();
        Assert.Equal(45, trimmer.InFrame);

        trimmer.SeekTo(120);
        trimmer.MarkOut();
        Assert.Equal(120, trimmer.OutFrame);

        // In cannot exceed Out
        trimmer.SeekTo(200);
        trimmer.MarkIn();
        Assert.Equal(120, trimmer.InFrame); // clamped to outFrame

        // Clear resets
        trimmer.ClearPoints();
        Assert.Equal(0, trimmer.InFrame);
        Assert.Equal(300, trimmer.OutFrame);
    });

    [Fact]
    public void TrimmerControl_CreateSubclip_AddsAssetToMediaPool() => RunSta(() =>
    {
        var trimmer = new TrimmerControl();
        var doc = new ProjectDocument { FrameRate = new Rational(30, 1) };
        var media = new MediaAsset { Path = "interview.mp4", DurationSeconds = 20.0, HasVideo = true };
        doc.Media.Add(media);

        var vm = CreateViewModel();
        vm.Replace(doc, null);
        trimmer.Initialize(vm);
        trimmer.LoadMedia(media);

        trimmer.SeekTo(30);
        trimmer.MarkIn();
        trimmer.SeekTo(90);
        trimmer.MarkOut();

        trimmer.CreateSubclip();

        Assert.Equal(2, vm.Project.Media.Count);
        var subclip = vm.Project.Media.Last();
        Assert.NotEqual(media.Id, subclip.Id);
        Assert.Equal(2.0, subclip.DurationSeconds); // (90 - 30) / 30 = 2.0s
    });

    [Fact]
    public void TrimmerControl_PlacementStreamModes_VideoOnlyAndAudioOnly() => RunSta(() =>
    {
        var trimmer = new TrimmerControl();
        var doc = new ProjectDocument { FrameRate = new Rational(30, 1) };
        var media = new MediaAsset { Path = "av_clip.mp4", DurationSeconds = 10.0, HasVideo = true, HasAudio = true };
        doc.Media.Add(media);

        var vm = CreateViewModel();
        vm.Replace(doc, null);
        trimmer.Initialize(vm);
        trimmer.LoadMedia(media);

        trimmer.SeekTo(15);
        trimmer.MarkIn();
        trimmer.SeekTo(45);
        trimmer.MarkOut();

        // 1. Video Only placement
        trimmer.SetPlacementStream(TrimmerPlacementStream.VideoOnly);
        Assert.Equal(TrimmerPlacementStream.VideoOnly, trimmer.StreamMode);
        trimmer.AddSubclipToTimeline();

        Assert.Single(vm.Project.Tracks);
        Assert.Equal(TrackKind.Video, vm.Project.Tracks[0].Kind);
        Assert.Single(vm.Project.Tracks[0].Clips);
        Assert.Equal(15, vm.Project.Tracks[0].Clips[0].SourceInFrame);
        Assert.Equal(30, vm.Project.Tracks[0].Clips[0].DurationFrames);

        // 2. Audio Only placement
        trimmer.SetPlacementStream(TrimmerPlacementStream.AudioOnly);
        Assert.Equal(TrimmerPlacementStream.AudioOnly, trimmer.StreamMode);
        trimmer.AddSubclipToTimeline();

        Assert.Equal(2, vm.Project.Tracks.Count);
        var audioTrack = vm.Project.Tracks.Single(t => t.Kind == TrackKind.Audio);
        Assert.Single(audioTrack.Clips);
        Assert.Equal(15, audioTrack.Clips[0].SourceInFrame);
        Assert.Equal(30, audioTrack.Clips[0].DurationFrames);

        // 3. Both (Video and Audio) placement
        trimmer.SetPlacementStream(TrimmerPlacementStream.VideoAndAudio);
        Assert.Equal(TrimmerPlacementStream.VideoAndAudio, trimmer.StreamMode);
        trimmer.AddSubclipToTimeline();

        var videoTrack = vm.Project.Tracks.Single(t => t.Kind == TrackKind.Video);
        Assert.Equal(2, videoTrack.Clips.Count);
        Assert.Equal(2, audioTrack.Clips.Count);
        // Linked in a group
        Assert.Equal(videoTrack.Clips[1].GroupId, audioTrack.Clips[1].GroupId);
    });

    [Fact]
    public void TrimmerControl_PlaceSubclipUpToCursor_PlacesEndingAtPosition() => RunSta(() =>
    {
        var trimmer = new TrimmerControl();
        var doc = new ProjectDocument { FrameRate = new Rational(30, 1) };
        var media = new MediaAsset { Path = "test.mp4", DurationSeconds = 10.0, HasVideo = true };
        doc.Media.Add(media);
        var track = new TimelineTrack { Kind = TrackKind.Video, Name = "Video 1" };
        track.Clips.Add(new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 200 });
        doc.Tracks.Add(track);

        var vm = CreateViewModel();
        vm.Replace(doc, null);
        trimmer.Initialize(vm);
        trimmer.LoadMedia(media);

        trimmer.SeekTo(20);
        trimmer.MarkIn();
        trimmer.SeekTo(50);
        trimmer.MarkOut(); // duration 30

        vm.Position = 100; // Place ending at frame 100
        trimmer.AddMediaUpToCursor();

        var clip = vm.Project.Tracks[0].Clips.Last();
        Assert.Equal(70, clip.StartFrame); // 100 - 30 = 70
        Assert.Equal(30, clip.DurationFrames);
        Assert.Equal(20, clip.SourceInFrame);
        Assert.Equal(100, vm.Position); // cursor does not advance past endpoint
    });

    [Fact]
    public void TrimmerControl_FitToFill_CalculatesPlaybackRateAndFillsTimelineRange() => RunSta(() =>
    {
        var trimmer = new TrimmerControl();
        var doc = new ProjectDocument { FrameRate = new Rational(30, 1) };
        var media = new MediaAsset { Path = "fit_test.mp4", DurationSeconds = 10.0, HasVideo = true, HasAudio = true };
        doc.Media.Add(media);

        var vm = CreateViewModel();
        vm.Replace(doc, null);
        trimmer.Initialize(vm);
        trimmer.LoadMedia(media);

        // Source: 120 frames
        trimmer.SeekTo(0);
        trimmer.MarkIn();
        trimmer.SeekTo(120);
        trimmer.MarkOut();

        // Target: 60 frames on timeline (half duration -> 2x speed)
        vm.LoopRegion = new FrameRange(30, 60);

        trimmer.FitToFill();

        var videoClip = vm.Project.Tracks[0].Clips.Single();
        Assert.Equal(30, videoClip.StartFrame);
        Assert.Equal(60, videoClip.DurationFrames);
        Assert.Equal(0, videoClip.SourceInFrame);
        Assert.Equal(2.0, videoClip.PlaybackRate, 2); // 120 / 60 = 2.0x speed

        var audioClip = vm.Project.Tracks[1].Clips.Single();
        Assert.Equal(2.0, audioClip.PlaybackRate, 2);
    });

    [Fact]
    public void TrimmerControl_ShuttleNavigationAndShortcuts_UpdatesPlaybackRateAndDirection() => RunSta(() =>
    {
        var trimmer = new TrimmerControl();
        var doc = new ProjectDocument { FrameRate = new Rational(30, 1) };
        var media = new MediaAsset { Path = "shuttle_test.mp4", DurationSeconds = 10.0, HasVideo = true };
        doc.Media.Add(media);

        var vm = CreateViewModel();
        vm.Replace(doc, null);
        trimmer.Initialize(vm);
        trimmer.LoadMedia(media);

        Assert.Equal(0, trimmer.ShuttleSpeed);

        // Forward Shuttle cycles 1x -> 2x -> 4x -> 8x
        trimmer.ShuttleForward();
        Assert.Equal(1, trimmer.ShuttleSpeed);

        trimmer.ShuttleForward();
        Assert.Equal(2, trimmer.ShuttleSpeed);

        trimmer.ShuttleForward();
        Assert.Equal(4, trimmer.ShuttleSpeed);

        trimmer.ShuttleForward();
        Assert.Equal(8, trimmer.ShuttleSpeed);

        // Stop resets
        trimmer.StopPlayback();
        Assert.Equal(0, trimmer.ShuttleSpeed);

        // Reverse Shuttle cycles -1x -> -2x -> -4x -> -8x
        trimmer.ShuttleReverse();
        Assert.Equal(-1, trimmer.ShuttleSpeed);

        trimmer.ShuttleReverse();
        Assert.Equal(-2, trimmer.ShuttleSpeed);

        trimmer.StopPlayback();
        Assert.Equal(0, trimmer.ShuttleSpeed);
    });
}
