using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public class PanCropMaskEditorTests
{
    private static (TimelineEditor editor, Guid clipId) CreateTestEditor()
    {
        var project = new ProjectDocument { Width = 1920, Height = 1080, FrameRate = new Rational(30, 1) };
        var media = new MediaAsset { Path = "test.mp4", Width = 1920, Height = 1080, DurationSeconds = 10, HasVideo = true };
        project.Media.Add(media);

        var track = new TimelineTrack { Id = Guid.NewGuid(), Name = "Video 1", Kind = TrackKind.Video };
        var clip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = media.Id,
            Name = "Clip 1",
            StartFrame = 0,
            DurationFrames = 150
        };
        track.Clips.Add(clip);
        project.Tracks.Add(track);

        var editor = new TimelineEditor(project);
        return (editor, clip.Id);
    }

    [Fact]
    public void SetMaskKeyframe_CreatesMaskAnimationAndKeyframe()
    {
        var (editor, clipId) = CreateTestEditor();
        var shape = MaskShape.CreateRectangle(100, 100, 400, 300);

        editor.SetMaskKeyframe(clipId, 0, shape, CropInterpolation.Linear);

        var clip = editor.Project.Tracks[0].Clips[0];
        Assert.NotNull(clip.Mask);
        Assert.True(clip.Mask.IsEnabled);
        Assert.Single(clip.Mask.Keyframes);
        Assert.Equal(0, clip.Mask.Keyframes[0].Frame);
        Assert.Equal(4, clip.Mask.Keyframes[0].Shape.Paths[0].Points.Count);
    }

    [Fact]
    public void SetMaskKeyframe_MultipleFramesAndMoveKeyframe_WorksAndSupportsUndo()
    {
        var (editor, clipId) = CreateTestEditor();
        var shape0 = MaskShape.CreateRectangle(0, 0, 100, 100);
        var shape1 = MaskShape.CreateOval(500, 500, 100, 100);

        editor.SetMaskKeyframe(clipId, 0, shape0, CropInterpolation.Linear);
        editor.SetMaskKeyframe(clipId, 30, shape1, CropInterpolation.Fast);

        var clip = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(2, clip.Mask!.Keyframes.Count);
        Assert.Equal(CropInterpolation.Fast, clip.Mask.Keyframes[1].Interpolation);

        // Move keyframe from 30 to 45
        editor.MoveMaskKeyframe(clipId, 30, 45);
        Assert.Equal(45, clip.Mask.Keyframes[1].Frame);

        // Undo move
        editor.Undo();
        clip = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(30, clip.Mask!.Keyframes[1].Frame);

        // Undo keyframe 1
        editor.Undo();
        clip = editor.Project.Tracks[0].Clips[0];
        Assert.Single(clip.Mask!.Keyframes);

        // Redo restores
        editor.Redo();
        clip = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(2, clip.Mask!.Keyframes.Count);
    }

    [Fact]
    public void SetMaskEnabled_TogglesMascState()
    {
        var (editor, clipId) = CreateTestEditor();
        var shape = MaskShape.CreateRectangle(0, 0, 100, 100);
        editor.SetMaskKeyframe(clipId, 0, shape, CropInterpolation.Linear);

        Assert.True(editor.Project.Tracks[0].Clips[0].Mask!.IsEnabled);

        editor.SetMaskEnabled(clipId, false);
        Assert.False(editor.Project.Tracks[0].Clips[0].Mask!.IsEnabled);

        editor.Undo();
        Assert.True(editor.Project.Tracks[0].Clips[0].Mask!.IsEnabled);
    }

    [Fact]
    public void ResetMask_RemovesMaskCompletely()
    {
        var (editor, clipId) = CreateTestEditor();
        var shape = MaskShape.CreateRectangle(0, 0, 100, 100);
        editor.SetMaskKeyframe(clipId, 0, shape, CropInterpolation.Linear);

        editor.ResetMask(clipId);
        Assert.Null(editor.Project.Tracks[0].Clips[0].Mask);

        editor.Undo();
        Assert.NotNull(editor.Project.Tracks[0].Clips[0].Mask);
    }

    [Fact]
    public void SetMaskKeyframeSmoothness_UpdatesKeyframeSmoothness()
    {
        var (editor, clipId) = CreateTestEditor();
        var shape = MaskShape.CreateRectangle(0, 0, 100, 100);
        editor.SetMaskKeyframe(clipId, 0, shape, CropInterpolation.Smooth, 75.0);

        Assert.Equal(75.0, editor.Project.Tracks[0].Clips[0].Mask!.Keyframes[0].Smoothness);

        editor.SetMaskKeyframeSmoothness(clipId, 0, 42.5);
        Assert.Equal(42.5, editor.Project.Tracks[0].Clips[0].Mask!.Keyframes[0].Smoothness);

        editor.Undo();
        Assert.Equal(75.0, editor.Project.Tracks[0].Clips[0].Mask!.Keyframes[0].Smoothness);
    }

    [Fact]
    public void RemoveMaskKeyframe_RemovesSingleKeyframeAndNullsWhenEmpty()
    {
        var (editor, clipId) = CreateTestEditor();
        var shape0 = MaskShape.CreateRectangle(0, 0, 100, 100);
        var shape1 = MaskShape.CreateRectangle(10, 10, 50, 50);

        editor.SetMaskKeyframe(clipId, 0, shape0, CropInterpolation.Linear);
        editor.SetMaskKeyframe(clipId, 20, shape1, CropInterpolation.Linear);

        editor.RemoveMaskKeyframe(clipId, 20);
        Assert.Single(editor.Project.Tracks[0].Clips[0].Mask!.Keyframes);

        editor.RemoveMaskKeyframe(clipId, 0);
        Assert.Null(editor.Project.Tracks[0].Clips[0].Mask);

        editor.Undo();
        Assert.Single(editor.Project.Tracks[0].Clips[0].Mask!.Keyframes);
    }
}
