using System;
using System.Collections.Generic;
using System.Linq;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class VideoEffectKeyframeEditorTests
{
    private static (EditorViewModel ViewModel, TimelineEditor Editor, ProjectDocument Project, Guid ClipId) CreateEnvironment()
    {
        var media = new MediaService();
        var renderer = new TimelineRenderService(media);
        var exporter = new ExportService(renderer);
        var vm = new EditorViewModel(media, renderer, exporter);
        vm.Project.Tracks.Clear();

        var asset = new MediaAsset { Path = "test.mp4", Width = 1920, Height = 1080, DurationSeconds = 10, HasVideo = true };
        vm.Project.Media.Add(asset);

        var track = new TimelineTrack { Id = Guid.NewGuid(), Name = "Video 1", Kind = TrackKind.Video };
        var clip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = asset.Id,
            Name = "Clip 1",
            StartFrame = 0,
            DurationFrames = 150
        };
        track.Clips.Add(clip);
        vm.Project.Tracks.Add(track);

        return (vm, vm.Editor, vm.Project, clip.Id);
    }

    [Fact]
    public void TimelineEditor_SetAndRemoveScopedVideoEffectKeyframe_SupportsUndo()
    {
        var (_, editor, _, clipId) = CreateEnvironment();

        var effect = editor.AddScopedVideoEffect(EffectScope.Event, clipId, VideoEffectKind.BrightnessContrast);
        VideoEffect GetTargetFx() => editor.Project.Tracks[0].Clips[0].VideoEffects.First(e => e.Id == effect.Id);

        // Add keyframe at frame 0
        editor.SetScopedVideoEffectKeyframe(
            EffectScope.Event,
            clipId,
            effect.Id,
            0,
            new Dictionary<string, double> { ["Brightness"] = -5.0 },
            amount: 0.1);

        var fx0 = GetTargetFx();
        Assert.NotNull(fx0.Animation);
        Assert.True(fx0.Animation.IsEnabled);
        Assert.Single(fx0.Animation.Keyframes);
        Assert.Equal(0, fx0.Animation.Keyframes[0].Frame);
        Assert.Equal(0.1, fx0.Animation.Keyframes[0].Amount);

        // Add keyframe at frame 60
        editor.SetScopedVideoEffectKeyframe(
            EffectScope.Event,
            clipId,
            effect.Id,
            60,
            new Dictionary<string, double> { ["Brightness"] = 5.0 },
            amount: 0.9,
            interpolation: EffectInterpolation.Fast);

        var fx60 = GetTargetFx();
        Assert.Equal(2, fx60.Animation!.Keyframes.Count);
        Assert.Equal(60, fx60.Animation.Keyframes[1].Frame);
        Assert.Equal(EffectInterpolation.Fast, fx60.Animation.Keyframes[1].Interpolation);

        // Remove keyframe at frame 60
        editor.RemoveScopedVideoEffectKeyframe(EffectScope.Event, clipId, effect.Id, 60);
        Assert.Single(GetTargetFx().Animation!.Keyframes);

        // Undo removal
        Assert.True(editor.CanUndo);
        editor.Undo();
        Assert.Equal(2, GetTargetFx().Animation!.Keyframes.Count);

        // Redo removal
        Assert.True(editor.CanRedo);
        editor.Redo();
        Assert.Single(GetTargetFx().Animation!.Keyframes);
    }

    [Fact]
    public void TimelineEditor_MoveAndChangeInterpolation_WorksCorrectly()
    {
        var (_, editor, _, clipId) = CreateEnvironment();

        var effect = editor.AddScopedVideoEffect(EffectScope.Event, clipId, VideoEffectKind.GaussianBlur);
        VideoEffect GetTargetFx() => editor.Project.Tracks[0].Clips[0].VideoEffects.First(e => e.Id == effect.Id);

        editor.SetScopedVideoEffectKeyframe(EffectScope.Event, clipId, effect.Id, 0, amount: 0.0);
        editor.SetScopedVideoEffectKeyframe(EffectScope.Event, clipId, effect.Id, 30, amount: 15.0);

        // Change interpolation of keyframe 0 to Slow
        editor.SetScopedVideoEffectKeyframeInterpolation(EffectScope.Event, clipId, effect.Id, 0, EffectInterpolation.Slow);
        Assert.Equal(EffectInterpolation.Slow, GetTargetFx().Animation!.Keyframes[0].Interpolation);

        // Move keyframe from frame 30 to frame 75
        editor.MoveScopedVideoEffectKeyframe(EffectScope.Event, clipId, effect.Id, 30, 75);
        Assert.Equal(75, GetTargetFx().Animation!.Keyframes[1].Frame);

        // Undo move
        editor.Undo();
        Assert.Equal(30, GetTargetFx().Animation!.Keyframes[1].Frame);

        // Redo move
        editor.Redo();
        Assert.Equal(75, GetTargetFx().Animation!.Keyframes[1].Frame);
    }

    [Fact]
    public void TimelineEditor_SetAnimationEnabled_InitializesFirstKeyframe()
    {
        var (_, editor, _, clipId) = CreateEnvironment();

        var effect = editor.AddScopedVideoEffect(EffectScope.Event, clipId, VideoEffectKind.Saturation);
        effect.Amount = 0.75;
        VideoEffect GetTargetFx() => editor.Project.Tracks[0].Clips[0].VideoEffects.First(e => e.Id == effect.Id);

        // Enable animation
        editor.SetScopedVideoEffectAnimationEnabled(EffectScope.Event, clipId, effect.Id, true);

        var fx = GetTargetFx();
        Assert.NotNull(fx.Animation);
        Assert.True(fx.Animation.IsEnabled);
        Assert.Single(fx.Animation.Keyframes);
        Assert.Equal(0, fx.Animation.Keyframes[0].Frame);
        Assert.Equal(0.75, fx.Animation.Keyframes[0].Amount);

        // Disable animation
        editor.SetScopedVideoEffectAnimationEnabled(EffectScope.Event, clipId, effect.Id, false);
        Assert.False(GetTargetFx().Animation!.IsEnabled);

        // Undo disable
        editor.Undo();
        Assert.True(GetTargetFx().Animation!.IsEnabled);
    }
}
