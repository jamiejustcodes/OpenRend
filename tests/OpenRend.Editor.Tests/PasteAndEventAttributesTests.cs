namespace OpenRend.Editor.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

public sealed class PasteAndEventAttributesTests
{
    private static (ProjectDocument doc, TimelineTrack videoTrack, TimelineTrack audioTrack, MediaAsset videoMedia, MediaAsset audioMedia) CreateTestProject()
    {
        var doc = new ProjectDocument
        {
            Width = 1920,
            Height = 1080,
            FrameRate = new Rational(30, 1),
            Tracks = []
        };
        var videoMedia = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = "C:/test/sample_video.mp4",
            HasVideo = true,
            HasAudio = false,
            DurationSeconds = 100.0,
            Width = 1920,
            Height = 1080,
            FrameRate = 30.0
        };
        var audioMedia = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = "C:/test/sample_audio.wav",
            HasVideo = false,
            HasAudio = true,
            DurationSeconds = 100.0
        };
        doc.Media.Add(videoMedia);
        doc.Media.Add(audioMedia);

        var videoTrack = new TimelineTrack
        {
            Id = Guid.NewGuid(),
            Name = "Video 1",
            Kind = TrackKind.Video
        };
        var audioTrack = new TimelineTrack
        {
            Id = Guid.NewGuid(),
            Name = "Audio 1",
            Kind = TrackKind.Audio
        };
        doc.Tracks.Add(videoTrack);
        doc.Tracks.Add(audioTrack);

        return (doc, videoTrack, audioTrack, videoMedia, audioMedia);
    }

    [Fact]
    public void PasteEventAttributes_VideoClip_TransfersEffectsPanCropSwitchesAndFades()
    {
        var (doc, videoTrack, _, videoMedia, _) = CreateTestProject();
        var editor = new TimelineEditor(doc);

        var sourceClip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = videoMedia.Id,
            StartFrame = 0,
            DurationFrames = 30,
            Opacity = 0.65,
            PlaybackRate = 1.5,
            VideoSourceMode = VideoSourceMode.Loop,
            ResampleMode = VideoResampleMode.Disable,
            MaintainAspectRatio = false,
            ReduceInterlace = true,
            FadeInCurve = VideoFadeCurve.Fast,
            FadeOutCurve = VideoFadeCurve.Slow,
            Crop = new CropAnimation
            {
                Keyframes =
                [
                    new CropKeyframe
                    {
                        Frame = 0,
                        State = new CropState { CenterX = 120, CenterY = 80, Width = 800, Height = 600, RotationDegrees = 15 }
                    }
                ]
            },
            Mask = new MaskAnimation
            {
                Keyframes =
                [
                    new MaskKeyframe
                    {
                        Frame = 0,
                        Shape = MaskShape.CreateRectangle(10, 10, 200, 200)
                    }
                ]
            },
            VideoEffects =
            [
                new VideoEffect
                {
                    Id = Guid.NewGuid(),
                    Kind = VideoEffectKind.Invert,
                    Amount = 0.8
                }
            ]
        };

        var targetClip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = videoMedia.Id,
            StartFrame = 60,
            DurationFrames = 30,
            Opacity = 1.0,
            PlaybackRate = 1.0,
            FadeInCurve = VideoFadeCurve.Linear,
            FadeOutCurve = VideoFadeCurve.Linear
        };
        videoTrack.Clips.Add(targetClip);

        var modified = editor.PasteEventAttributes(sourceClip, [targetClip.Id]);
        Assert.Equal(1, modified);

        var updated = editor.Project.Tracks[0].Clips.First(c => c.Id == targetClip.Id);
        Assert.Equal(0.65, updated.Opacity);
        Assert.Equal(1.5, updated.PlaybackRate);
        Assert.Equal(VideoSourceMode.Loop, updated.VideoSourceMode);
        Assert.Equal(VideoResampleMode.Disable, updated.ResampleMode);
        Assert.False(updated.MaintainAspectRatio);
        Assert.True(updated.ReduceInterlace);
        Assert.Equal(VideoFadeCurve.Fast, updated.FadeInCurve);
        Assert.Equal(VideoFadeCurve.Slow, updated.FadeOutCurve);
        Assert.NotNull(updated.Crop);
        Assert.Equal(120, updated.Crop!.Keyframes[0].State.CenterX);
        Assert.NotNull(updated.Mask);
        Assert.Single(updated.VideoEffects);
        Assert.Equal(VideoEffectKind.Invert, updated.VideoEffects[0].Kind);

        // Verify Undo restores original target state
        editor.Undo();
        var undone = editor.Project.Tracks[0].Clips.First(c => c.Id == targetClip.Id);
        Assert.Equal(1.0, undone.Opacity);
        Assert.Equal(1.0, undone.PlaybackRate);
        Assert.Null(undone.Crop);
        Assert.Null(undone.Mask);
        Assert.Empty(undone.VideoEffects);
    }

    [Fact]
    public void PasteEventAttributes_AudioClip_TransfersEffectsGainPanPitchAndChannels()
    {
        var (doc, _, audioTrack, _, audioMedia) = CreateTestProject();
        var editor = new TimelineEditor(doc);

        var sourceClip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = audioMedia.Id,
            StartFrame = 0,
            DurationFrames = 30,
            Gain = 0.45,
            Pan = -0.7,
            PitchSemis = 4.0,
            InvertPhase = true,
            Normalize = true,
            ChannelMode = AudioChannelMode.LeftOnly,
            PlaybackRate = 1.25,
            FadeInFrames = 8,
            FadeOutFrames = 12,
            AudioEffects =
            [
                new AudioEffect
                {
                    Id = Guid.NewGuid(),
                    Kind = AudioEffectKind.TrackCompressor,
                    Parameters = new Dictionary<string, double> { ["ThresholdDb"] = -18.0 }
                }
            ]
        };

        var targetClip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = audioMedia.Id,
            StartFrame = 50,
            DurationFrames = 40,
            Gain = 1.0,
            Pan = 0.0,
            PitchSemis = 0.0,
            InvertPhase = false,
            Normalize = false,
            ChannelMode = AudioChannelMode.Both
        };
        audioTrack.Clips.Add(targetClip);

        var modified = editor.PasteEventAttributes(sourceClip, [targetClip.Id]);
        Assert.Equal(1, modified);

        var updated = editor.Project.Tracks[1].Clips.First(c => c.Id == targetClip.Id);
        Assert.Equal(0.45, updated.Gain);
        Assert.Equal(-0.7, updated.Pan);
        Assert.Equal(4.0, updated.PitchSemis);
        Assert.True(updated.InvertPhase);
        Assert.True(updated.Normalize);
        Assert.Equal(AudioChannelMode.LeftOnly, updated.ChannelMode);
        Assert.Equal(1.25, updated.PlaybackRate);
        Assert.Equal(8, updated.FadeInFrames);
        Assert.Equal(12, updated.FadeOutFrames);
        Assert.Single(updated.AudioEffects);
        Assert.Equal(AudioEffectKind.TrackCompressor, updated.AudioEffects[0].Kind);

        // Undo verification
        editor.Undo();
        var undone = editor.Project.Tracks[1].Clips.First(c => c.Id == targetClip.Id);
        Assert.Equal(1.0, undone.Gain);
        Assert.Equal(0.0, undone.Pan);
        Assert.Equal(0.0, undone.PitchSemis);
        Assert.Equal(0, undone.FadeInFrames);
        Assert.Equal(0, undone.FadeOutFrames);
        Assert.False(undone.InvertPhase);
        Assert.False(undone.Normalize);
        Assert.Equal(AudioChannelMode.Both, undone.ChannelMode);
        Assert.Empty(undone.AudioEffects);
    }

    [Fact]
    public void SelectivelyPasteEventAttributes_RespectsOptionsAndAppendsFX()
    {
        var (doc, videoTrack, _, videoMedia, _) = CreateTestProject();
        var editor = new TimelineEditor(doc);

        var existingFx = new VideoEffect { Id = Guid.NewGuid(), Kind = VideoEffectKind.ColorGrading };
        var targetClip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = videoMedia.Id,
            StartFrame = 0,
            DurationFrames = 50,
            Opacity = 1.0,
            VideoEffects = [existingFx]
        };
        videoTrack.Clips.Add(targetClip);

        var sourceClip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = videoMedia.Id,
            StartFrame = 100,
            DurationFrames = 50,
            Opacity = 0.5,
            VideoEffects =
            [
                new VideoEffect { Id = Guid.NewGuid(), Kind = VideoEffectKind.Invert }
            ],
            Crop = new CropAnimation
            {
                Keyframes = [new CropKeyframe { Frame = 0, State = CropState.Identity(1920, 1080) }]
            }
        };

        // Option: VideoEffects enabled, ReplaceExistingFX = false (append), PanCrop = false
        var options = new PasteEventAttributesOptions
        {
            VideoEffects = true,
            ReplaceExistingFX = false,
            PanCrop = false,
            VideoFadesAndOpacity = false,
            VideoSwitches = false
        };

        var count = editor.PasteEventAttributes(sourceClip, [targetClip.Id], options);
        Assert.Equal(1, count);

        var updated = editor.Project.Tracks[0].Clips.First(c => c.Id == targetClip.Id);
        // Original FX + Appended FX = 2
        Assert.Equal(2, updated.VideoEffects.Count);
        Assert.Equal(VideoEffectKind.ColorGrading, updated.VideoEffects[0].Kind);
        Assert.Equal(VideoEffectKind.Invert, updated.VideoEffects[1].Kind);
        // PanCrop and Opacity untouched
        Assert.Null(updated.Crop);
        Assert.Equal(1.0, updated.Opacity);
    }

    [Fact]
    public void PasteEventAttributes_IgnoresLockedClipsAndLockedTracks()
    {
        var (doc, videoTrack, _, videoMedia, _) = CreateTestProject();
        var editor = new TimelineEditor(doc);

        var clip1 = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = videoMedia.Id,
            StartFrame = 0,
            DurationFrames = 30,
            IsLocked = true,
            Opacity = 1.0
        };
        videoTrack.Clips.Add(clip1);

        var sourceClip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = videoMedia.Id,
            StartFrame = 50,
            DurationFrames = 30,
            Opacity = 0.2
        };

        var count = editor.PasteEventAttributes(sourceClip, [clip1.Id]);
        Assert.Equal(0, count);
        Assert.Equal(1.0, editor.Project.Tracks[0].Clips[0].Opacity);

        // Lock entire track
        videoTrack.IsLocked = true;
        clip1.IsLocked = false;
        count = editor.PasteEventAttributes(sourceClip, [clip1.Id]);
        Assert.Equal(0, count);
        Assert.Equal(1.0, editor.Project.Tracks[0].Clips[0].Opacity);
    }

    private sealed class ViewModelTestSession : IDisposable
    {
        private readonly MediaService media = new();
        private readonly TimelineRenderService renderer;
        public EditorViewModel ViewModel { get; }
        public MediaAsset VideoMedia { get; }
        public TimelineClip Clip1 { get; }
        public TimelineClip Clip2 { get; }

        public ViewModelTestSession()
        {
            renderer = new TimelineRenderService(media);
            ViewModel = new EditorViewModel(media, renderer, new ExportService(renderer));
            VideoMedia = new MediaAsset { Path = "test-video.mp4", DurationSeconds = 60, HasVideo = true };
            Clip1 = new TimelineClip { MediaId = VideoMedia.Id, Name = "Clip1", StartFrame = 0, DurationFrames = 30 };
            Clip2 = new TimelineClip { MediaId = VideoMedia.Id, Name = "Clip2", StartFrame = 60, DurationFrames = 30 };

            ViewModel.Replace(new ProjectDocument
            {
                Media = [VideoMedia],
                Tracks =
                [
                    new TimelineTrack { Name = "Track 1", Kind = TrackKind.Video, Clips = [Clip1, Clip2] }
                ]
            }, "test.openrend");
        }

        public void Dispose()
        {
            renderer.Dispose();
            media.Dispose();
        }
    }

    [Fact]
    public void PasteInsert_ForcesRippleDownstreamAndPastesAtCursor()
    {
        using var session = new ViewModelTestSession();
        var vm = session.ViewModel;

        // Select and copy Clip1 (duration 30)
        vm.Selection.Clear();
        vm.Selection.Add(session.Clip1.Id);
        vm.Copy();

        // Position cursor at 30 (between Clip1 [0,30] and Clip2 [60,90])
        vm.Position = 30;
        vm.Ripple = false; // Even with Ripple toggle off, PasteInsert must ripple downstream!

        vm.PasteInsert();

        var track = vm.Project.Tracks[0];
        Assert.Equal(3, track.Clips.Count);

        // Clip1 stays at 0..30
        var originalClip1 = track.Clips.First(c => c.Id == session.Clip1.Id);
        Assert.Equal(0, originalClip1.StartFrame);

        // Newly pasted clip is at 30..60
        var pastedClip = track.Clips.First(c => c.Id != session.Clip1.Id && c.Id != session.Clip2.Id);
        Assert.Equal(30, pastedClip.StartFrame);
        Assert.Equal(30, pastedClip.DurationFrames);

        // Clip2 was at 60, shifted downstream by 30 to 90
        var rippledClip2 = track.Clips.First(c => c.Id == session.Clip2.Id);
        Assert.Equal(90, rippledClip2.StartFrame);

        // Undo restores Clip2 back to 60 and removes pasted clip
        vm.Editor.Undo();
        track = vm.Project.Tracks[0];
        Assert.Equal(2, track.Clips.Count);
        Assert.Equal(60, track.Clips.First(c => c.Id == session.Clip2.Id).StartFrame);
    }

    [Fact]
    public void PasteRepeat_EndToEnd_PastesNConsecutiveCopiesAtomically()
    {
        using var session = new ViewModelTestSession();
        var vm = session.ViewModel;

        vm.Selection.Clear();
        vm.Selection.Add(session.Clip1.Id);
        vm.Copy();

        vm.Position = 90;
        vm.PasteRepeat(repeatCount: 3, repeatSpacingFrames: null);

        var track = vm.Project.Tracks[0];
        // 2 original + 3 repeated copies = 5 clips
        Assert.Equal(5, track.Clips.Count);

        var newClips = track.Clips.Where(c => c.Id != session.Clip1.Id && c.Id != session.Clip2.Id)
            .OrderBy(c => c.StartFrame).ToList();
        Assert.Equal(3, newClips.Count);

        // Copy 0: 90..120
        Assert.Equal(90, newClips[0].StartFrame);
        Assert.Equal(120, newClips[0].EndFrame);

        // Copy 1: 120..150
        Assert.Equal(120, newClips[1].StartFrame);
        Assert.Equal(150, newClips[1].EndFrame);

        // Copy 2: 150..180
        Assert.Equal(150, newClips[2].StartFrame);
        Assert.Equal(180, newClips[2].EndFrame);

        // Playhead position updated to end
        Assert.Equal(180, vm.Position);

        // Atomic Undo removes all 3 copies in one step
        vm.Editor.Undo();
        Assert.Equal(2, vm.Project.Tracks[0].Clips.Count);
    }

    [Fact]
    public void PasteRepeat_EvenSpacing_PastesSpacedCopiesAtomically()
    {
        using var session = new ViewModelTestSession();
        var vm = session.ViewModel;

        vm.Selection.Clear();
        vm.Selection.Add(session.Clip1.Id);
        vm.Copy();

        vm.Position = 90;
        // Paste 3 copies with 50-frame even spacing interval (duration is 30, so 20 frames gap between each)
        vm.PasteRepeat(repeatCount: 3, repeatSpacingFrames: 50);

        var track = vm.Project.Tracks[0];
        Assert.Equal(5, track.Clips.Count);

        var newClips = track.Clips.Where(c => c.Id != session.Clip1.Id && c.Id != session.Clip2.Id)
            .OrderBy(c => c.StartFrame).ToList();
        Assert.Equal(3, newClips.Count);

        // Copy 0: 90..120
        Assert.Equal(90, newClips[0].StartFrame);
        Assert.Equal(120, newClips[0].EndFrame);

        // Copy 1: 140..170
        Assert.Equal(140, newClips[1].StartFrame);
        Assert.Equal(170, newClips[1].EndFrame);

        // Copy 2: 190..220
        Assert.Equal(190, newClips[2].StartFrame);
        Assert.Equal(220, newClips[2].EndFrame);

        // Playhead position updated to end
        Assert.Equal(220, vm.Position);

        // Atomic Undo removes all 3 copies in one step
        vm.Editor.Undo();
        Assert.Equal(2, vm.Project.Tracks[0].Clips.Count);
    }
}
