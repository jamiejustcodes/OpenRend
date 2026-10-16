using System.Text.Json;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class CropTests
{
    private static readonly CropState Full = CropState.Identity(320, 180);
    private static readonly CropState Zoom = new(160, 90, 180, 100, 90, 150, 80);

    private static (TimelineEditor Editor, TimelineTrack Track, TimelineClip Clip, MediaAsset Media) Create()
    {
        var media = new MediaAsset { Path = Path.GetFullPath("crop-source.avi"), HasVideo = true, HasAudio = true, Width = 320, Height = 180, DurationSeconds = 100 };
        var clip = new TimelineClip { MediaId = media.Id, DurationFrames = 100, SourceInFrame = 20, StartFrame = 10 };
        var track = new TimelineTrack { Kind = TrackKind.Video, Clips = [clip] };
        return (new TimelineEditor(new ProjectDocument { Media = [media], Tracks = [track] }), track, clip, media);
    }

    [Fact]
    public void MissingAnimationUsesSourceDimensionsAndDoesNotAffectAudio()
    {
        var (editor, _, clip, media) = Create();
        Assert.Equal(Full, TimelineEvaluator.CropAt(clip, media, 0));
        var audio = editor.AddTrack(TrackKind.Audio);
        editor.PlaceClip(media.Id, audio.Id, 10, 100);
        var frame = TimelineEvaluator.Evaluate(editor.Project, 10);
        Assert.Equal(Full, Assert.Single(frame.Video).Crop);
        Assert.Null(Assert.Single(frame.Audio).Crop);
    }

    [Fact]
    public void LinearInterpolatesEveryGeometryComponentAndClampsEndpoints()
    {
        var animation = new CropAnimation { Keyframes = [new() { Frame = 10, State = Full }, new() { Frame = 30, State = Zoom }] };
        Assert.Equal(Full, animation.Evaluate(long.MinValue));
        Assert.Equal(Full, animation.Evaluate(10));
        Assert.Equal(new CropState(240, 135, 170, 95, 45, 155, 85), animation.Evaluate(20));
        Assert.Equal(Zoom, animation.Evaluate(30));
        Assert.Equal(Zoom, animation.Evaluate(long.MaxValue));
    }

    [Fact]
    public void HoldChangesAtTheExactNextKeyframe()
    {
        var (editor, _, clip, media) = Create();
        editor.SetCropKeyframe(clip.Id, 0, Full, CropInterpolation.Hold);
        editor.SetCropKeyframe(clip.Id, 20, Zoom);
        Assert.Equal(Full, TimelineEvaluator.CropAt(clip, media, 19));
        Assert.Equal(Zoom, TimelineEvaluator.CropAt(clip, media, 20));
    }

    [Fact]
    public void IntegerSubtractionPreservesAdjacentKeysOnVeryLongCurves()
    {
        var animation = new CropAnimation { Keyframes = [new() { Frame = long.MaxValue - 10, State = Full }, new() { Frame = long.MaxValue, State = Zoom }] };
        Assert.Equal(240, animation.Evaluate(long.MaxValue - 5).Width);
        Assert.Equal(176, animation.Evaluate(long.MaxValue - 1).Width);
    }

    [Fact]
    public void KeyframeInsertionIsOrderedAndRepeatedValuesDoNotAddUndoHistory()
    {
        var (editor, _, clip, _) = Create();
        var changes = 0;
        editor.Changed += (_, _) => changes++;
        editor.ResetCrop(clip.Id);
        editor.RemoveCropKeyframe(clip.Id, 10);
        Assert.False(editor.CanUndo);
        editor.SetCropKeyframe(clip.Id, 50, Zoom);
        editor.SetCropKeyframe(clip.Id, 0, Zoom);
        editor.SetCropKeyframe(clip.Id, 50, Zoom);
        editor.RemoveCropKeyframe(clip.Id, 25);
        Assert.Equal(2, changes);
        Assert.Equal(new long[] { 0, 50 }, clip.Crop!.Keyframes.Select(key => key.Frame));
        Assert.True(editor.Undo());
        Assert.Equal(Full, editor.Project.Tracks[0].Clips[0].Crop!.Keyframes[0].State);
        Assert.True(editor.Undo());
        Assert.Null(editor.Project.Tracks[0].Clips[0].Crop);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void FirstKeyframeAfterTheStartAddsAnIdentityStartInTheSameUndoStep()
    {
        var (editor, _, clip, media) = Create();
        editor.SetCropKeyframe(clip.Id, 40, Zoom);
        Assert.Equal(new long[] { 0, 40 }, clip.Crop!.Keyframes.Select(key => key.Frame));
        Assert.Equal(Full, TimelineEvaluator.CropAt(clip, media, 0));
        Assert.Equal(new CropState(240, 135, 170, 95, 45, 155, 85), TimelineEvaluator.CropAt(clip, media, 20));
        Assert.Equal(Zoom, TimelineEvaluator.CropAt(clip, media, 40));
        Assert.True(editor.Undo());
        Assert.Null(editor.Project.Tracks[0].Clips[0].Crop);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void UndoRedoOwnIndependentKeyframeCopiesAndRemovingLastKeyResets()
    {
        var (editor, _, clip, _) = Create();
        editor.SetCropKeyframe(clip.Id, 0, Full);
        editor.SetCropKeyframe(clip.Id, 0, Zoom, CropInterpolation.Hold);
        var clone = editor.Project.DeepClone();
        clone.Tracks[0].Clips[0].Crop!.Keyframes[0].State = Full;
        Assert.Equal(Zoom, clip.Crop!.Keyframes[0].State);
        Assert.True(editor.Undo());
        Assert.Equal(Full, editor.Project.Tracks[0].Clips[0].Crop!.Keyframes[0].State);
        Assert.True(editor.Redo());
        Assert.Equal(Zoom, editor.Project.Tracks[0].Clips[0].Crop!.Keyframes[0].State);
        editor.RemoveCropKeyframe(clip.Id, 0);
        Assert.Null(editor.Project.Tracks[0].Clips[0].Crop);
        Assert.Equal(0, editor.Project.Tracks[0].Clips[0].CropOffsetFrames);
    }

    [Fact]
    public void TrimSlipExtensionAndMoveKeepTheOriginalCurveCoordinates()
    {
        var (editor, track, clip, media) = Create();
        editor.SetCropKeyframe(clip.Id, 0, Full);
        editor.SetCropKeyframe(clip.Id, 80, Zoom);
        var expected = TimelineEvaluator.CropAt(clip, media, 35);
        editor.TrimClip(clip.Id, 40, 60);
        Assert.Equal(expected, TimelineEvaluator.CropAt(clip, media, 15));
        editor.MoveClip(clip.Id, track.Id, 200);
        Assert.Equal(expected, Assert.Single(TimelineEvaluator.Evaluate(editor.Project, 215).Video).Crop);
        editor.TrimClip(clip.Id, 10, 90);
        Assert.Equal(-10, clip.CropOffsetFrames);
        Assert.Equal(Full, TimelineEvaluator.CropAt(clip, media, 0));
        Assert.Equal(expected, TimelineEvaluator.CropAt(clip, media, 45));
        editor.SetCropKeyframe(clip.Id, 0, Zoom);
        Assert.Equal(0, clip.CropOffsetFrames);
        Assert.Equal(new long[] { 0, 10, 90 }, clip.Crop!.Keyframes.Select(key => key.Frame));
        Assert.Equal(expected, TimelineEvaluator.CropAt(clip, media, 45));
    }

    [Fact]
    public void GroupedTrimChangesVideoCurveOnceAndLeavesAudioUncropped()
    {
        var (editor, _, clip, media) = Create();
        var audioTrack = editor.AddTrack(TrackKind.Audio);
        var audio = editor.PlaceClip(media.Id, audioTrack.Id, 10, 100);
        editor.TrimClip(audio.Id, 20, 100);
        editor.LinkClips([clip.Id, audio.Id]);
        editor.SetCropKeyframe(clip.Id, 0, Full);
        editor.SetCropKeyframe(clip.Id, 80, Zoom);
        var expected = TimelineEvaluator.CropAt(clip, media, 40);
        editor.TrimSelection(clip.Id, 30, 40, 80);
        Assert.Equal(20, clip.CropOffsetFrames);
        Assert.Equal(expected, TimelineEvaluator.CropAt(clip, media, 20));
        Assert.Equal(40, audio.SourceInFrame);
        Assert.Null(audio.Crop);
        Assert.Equal(0, audio.CropOffsetFrames);
        Assert.True(editor.Undo());
        Assert.All(editor.Project.Tracks.SelectMany(track => track.Clips), item => Assert.Equal(20, item.SourceInFrame));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SplitPreservesEveryRenderedFrameAndUsesIndependentCurveCopies(bool grouped)
    {
        var (editor, _, clip, media) = Create();
        editor.SetCropKeyframe(clip.Id, 0, Full);
        editor.SetCropKeyframe(clip.Id, 99, Zoom);
        if (grouped)
        {
            var audioTrack = editor.AddTrack(TrackKind.Audio);
            var audio = editor.PlaceClip(media.Id, audioTrack.Id, 10, 100);
            editor.LinkClips([clip.Id, audio.Id]);
        }
        var expected = Enumerable.Range(0, 100).Select(frame => TimelineEvaluator.CropAt(clip, media, frame)).ToArray();
        var right = grouped ? editor.SplitSelection([clip.Id], 45).Single(item => item.Crop is not null) : editor.SplitClip(clip.Id, 45);
        Assert.Equal(35, right.CropOffsetFrames);
        Assert.NotSame(clip.Crop, right.Crop);
        Assert.NotSame(clip.Crop!.Keyframes[0], right.Crop!.Keyframes[0]);
        for (var frame = 0; frame < 100; frame++) Assert.Equal(expected[frame], Assert.Single(TimelineEvaluator.Evaluate(editor.Project, 10 + frame).Video).Crop);
        Assert.True(editor.Undo());
        Assert.Equal(100, editor.Project.Tracks[0].Clips[0].DurationFrames);
        Assert.Equal(0, editor.Project.Tracks[0].Clips[0].CropOffsetFrames);
    }

    [Fact]
    public void ResetIsOneUndoableEditAndLocksRejectAllCropMutations()
    {
        var (editor, track, clip, _) = Create();
        editor.SetCropKeyframe(clip.Id, 0, Zoom);
        editor.ResetCrop(clip.Id);
        Assert.Null(clip.Crop);
        Assert.True(editor.Undo());
        Assert.Equal(Zoom, editor.Project.Tracks[0].Clips[0].Crop!.Keyframes[0].State);
        editor.SetTrackState(track.Id, isLocked: true);
        Assert.Throws<InvalidOperationException>(() => editor.SetCropKeyframe(clip.Id, 0, Full));
        Assert.Throws<InvalidOperationException>(() => editor.RemoveCropKeyframe(clip.Id, 0));
        Assert.Throws<InvalidOperationException>(() => editor.ResetCrop(clip.Id));
    }

    [Fact]
    public void InvalidGeometryFramesAndAudioCropDoNotMutateTheProject()
    {
        var (editor, _, clip, media) = Create();
        foreach (var state in new[] { Full with { Width = 0 }, Full with { Height = double.NaN }, Full with { CenterX = double.PositiveInfinity }, Full with { PivotY = 1e10 }, Full with { RotationDegrees = double.NaN } })
            Assert.Throws<InvalidDataException>(() => editor.SetCropKeyframe(clip.Id, 0, state));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetCropKeyframe(clip.Id, -1, Full));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetCropKeyframe(clip.Id, 100, Full));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetCropKeyframe(clip.Id, 0, Full, (CropInterpolation)9));
        Assert.False(editor.CanUndo);
        Assert.Null(clip.Crop);
        var audio = editor.AddTrack(TrackKind.Audio);
        var audioClip = editor.PlaceClip(media.Id, audio.Id, 0, 100);
        Assert.Throws<InvalidOperationException>(() => editor.SetCropKeyframe(audioClip.Id, 0, Full));
    }

    [Fact]
    public void ProjectRejectsMalformedKeyOrderAndUnrepresentableCurveRange()
    {
        var (editor, _, clip, _) = Create();
        foreach (var frames in new long[][] { [], [0, 0], [20, 0], [-1] })
        {
            clip.Crop = new() { Keyframes = frames.Select(frame => new CropKeyframe { Frame = frame, State = Full }).ToList() };
            Assert.Throws<InvalidDataException>(() => editor.Project.Validate());
        }
        clip.Crop = new() { Keyframes = [new() { State = Full }] };
        clip.CropOffsetFrames = long.MaxValue - 99;
        Assert.Throws<InvalidDataException>(() => editor.Project.Validate());
        clip.CropOffsetFrames = long.MinValue;
        editor.Project.Validate();
        Assert.Throws<OverflowException>(() => editor.SetCropKeyframe(clip.Id, 0, Zoom));
        Assert.Equal(long.MinValue, clip.CropOffsetFrames);
        Assert.Equal(Full, clip.Crop.Keyframes[0].State);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void TrimRangeAndGroupedOverflowFailuresPreserveCropAndUndoHistory()
    {
        var (editor, track, clip, media) = Create();
        clip.Crop = new() { Keyframes = [new() { State = Full }] };
        clip.CropOffsetFrames = long.MaxValue - 100;
        var group = Guid.NewGuid();
        clip.GroupId = group;
        var audio = new TimelineClip { MediaId = media.Id, GroupId = group, StartFrame = 10, SourceInFrame = 20, DurationFrames = 100 };
        editor.Project.Tracks.Add(new TimelineTrack { Kind = TrackKind.Audio, Clips = [audio] });
        editor.Project.Validate();
        Assert.Throws<InvalidDataException>(() => editor.TrimClip(clip.Id, 21, 100));
        Assert.Throws<OverflowException>(() => editor.TrimSelection(clip.Id, 10, 121, 100));
        Assert.False(editor.CanUndo);
        var restored = editor.Project.Tracks.Single(item => item.Id == track.Id).Clips[0];
        Assert.Equal(20, restored.SourceInFrame);
        Assert.Equal(long.MaxValue - 100, restored.CropOffsetFrames);
        Assert.Equal(Full, restored.Crop!.Keyframes[0].State);
        Assert.Equal(20, editor.Project.Tracks.Single(item => item.Kind == TrackKind.Audio).Clips[0].SourceInFrame);
    }

    [Fact]
    public async Task CropProjectRoundtripRetainsGeometryInterpolationAndSignedOffset()
    {
        var (editor, _, clip, media) = Create();
        editor.SetCropKeyframe(clip.Id, 0, Full, CropInterpolation.Hold);
        editor.SetCropKeyframe(clip.Id, 80, Zoom);
        editor.TrimClip(clip.Id, 10, 110);
        var directory = Path.Combine(Path.GetTempPath(), "OpenRend-crop-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "crop.openrend");
        try
        {
            await ProjectStore.SaveAsync(editor.Project, path);
            var loaded = await ProjectStore.LoadAsync(path);
            var restored = loaded.Tracks[0].Clips[0];
            Assert.Equal(-10, restored.CropOffsetFrames);
            Assert.Equal(CropInterpolation.Hold, restored.Crop!.Keyframes[0].Interpolation);
            Assert.Equal(Zoom, restored.Crop.Keyframes[1].State);
            for (var frame = 0; frame < 110; frame++) Assert.Equal(TimelineEvaluator.CropAt(clip, media, frame), TimelineEvaluator.CropAt(restored, loaded.Media[0], frame));
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }

    [Fact]
    public void LegacyClipWithoutCropFieldsRemainsIdentity()
    {
        var clip = JsonSerializer.Deserialize<TimelineClip>("{\"DurationFrames\":100}")!;
        Assert.Null(clip.Crop);
        Assert.Equal(0, clip.CropOffsetFrames);
        Assert.Equal(Full, TimelineEvaluator.CropAt(clip, new MediaAsset { Width = 320, Height = 180 }, 10));
        var serialized = JsonSerializer.Serialize(clip);
        Assert.DoesNotContain("Crop", serialized);
    }
}
