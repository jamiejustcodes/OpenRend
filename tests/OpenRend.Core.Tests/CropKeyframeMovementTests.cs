using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class CropKeyframeMovementTests
{
    private static readonly CropState Start = CropState.Identity(320, 180);
    private static readonly CropState Middle = Start with { Width = 160, Height = 90, CenterX = 120, RotationDegrees = 45 };
    private static readonly CropState End = Start with { Width = 80, Height = 45, CenterX = 80, RotationDegrees = 90 };

    private static (TimelineEditor Editor, TimelineClip Clip, TimelineTrack Track, MediaAsset Media) Create(long offset = 0)
    {
        var media = new MediaAsset { Path = "unchanged-source.avi", HasVideo = true, HasAudio = true, Width = 320, Height = 180, DurationSeconds = 10 };
        var clip = new TimelineClip { MediaId = media.Id, StartFrame = 20, SourceInFrame = 30, DurationFrames = 100, CropOffsetFrames = offset,
            Crop = new() { Keyframes = [new() { Frame = 0, State = Start }, new() { Frame = 20, State = Middle, Interpolation = CropInterpolation.Hold }, new() { Frame = 40, State = End }] } };
        var track = new TimelineTrack { Kind = TrackKind.Video, Clips = [clip] };
        return (new TimelineEditor(new ProjectDocument { Media = [media], Tracks = [track] }), clip, track, media);
    }

    [Fact]
    public void MovingKeyReordersItAndCarriesItsStateAndOutgoingInterpolation()
    {
        var (editor, clip, _, media) = Create();
        editor.MoveCropKeyframe(clip.Id, 20, 60);
        Assert.Equal(new long[] { 0, 40, 60 }, clip.Crop!.Keyframes.Select(key => key.Frame));
        Assert.Equal(Middle, clip.Crop.Keyframes[2].State);
        Assert.Equal(CropInterpolation.Hold, clip.Crop.Keyframes[2].Interpolation);
        Assert.Equal(End, TimelineEvaluator.CropAt(clip, media, 40));
        Assert.Equal((End.Width + Middle.Width) / 2, TimelineEvaluator.CropAt(clip, media, 50).Width);
        Assert.Equal(Middle, TimelineEvaluator.CropAt(clip, media, 60));
        Assert.Equal(Middle, TimelineEvaluator.CropAt(clip, media, 99));
        Assert.Equal(20, clip.StartFrame);
        Assert.Equal(30, clip.SourceInFrame);
        Assert.Equal(100, clip.DurationFrames);
        Assert.Equal("unchanged-source.avi", media.Path);
        Assert.Equal(10, media.DurationSeconds);
        Assert.Equal(0, clip.CropOffsetFrames);
    }

    [Fact]
    public void MovingHoldKeyEarlierChangesItsBoundaryWithoutChangingTheFollowingKey()
    {
        var (editor, clip, _, media) = Create();
        editor.MoveCropKeyframe(clip.Id, 20, 10);
        Assert.Equal(Middle, TimelineEvaluator.CropAt(clip, media, 10));
        Assert.Equal(Middle, TimelineEvaluator.CropAt(clip, media, 39));
        Assert.Equal(End, TimelineEvaluator.CropAt(clip, media, 40));
        Assert.Equal((Start.Width + Middle.Width) / 2, TimelineEvaluator.CropAt(clip, media, 5).Width);
    }

    [Fact]
    public void FirstKeyCanMoveAndIsNotReplacedByAnImplicitIdentity()
    {
        var (editor, clip, _, media) = Create();
        editor.MoveCropKeyframe(clip.Id, 0, 80);
        Assert.Equal(new long[] { 20, 40, 80 }, clip.Crop!.Keyframes.Select(key => key.Frame));
        Assert.Equal(Middle, TimelineEvaluator.CropAt(clip, media, 0));
        Assert.Equal(Start, TimelineEvaluator.CropAt(clip, media, 80));
    }

    [Fact]
    public void NoopRequiresAnExistingKeyAndDoesNotClearRedoOrAddHistory()
    {
        var (editor, clip, _, _) = Create();
        var changes = 0;
        editor.Changed += (_, _) => changes++;
        editor.MoveCropKeyframe(clip.Id, 20, 20);
        Assert.False(editor.CanUndo);
        Assert.Equal(0, changes);
        Assert.Throws<InvalidOperationException>(() => editor.MoveCropKeyframe(clip.Id, 10, 10));
        editor.MoveCropKeyframe(clip.Id, 20, 30);
        Assert.True(editor.Undo());
        editor.MoveCropKeyframe(clip.Id, 20, 20);
        Assert.True(editor.CanRedo);
        Assert.False(editor.CanUndo);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void OccupiedDestinationIsReplacedAndBothKeysReturnOnUndo()
    {
        var (editor, clip, _, media) = Create();
        editor.MoveCropKeyframe(clip.Id, 20, 40);
        Assert.Equal(new long[] { 0, 40 }, clip.Crop!.Keyframes.Select(key => key.Frame));
        Assert.Equal(Middle, clip.Crop.Keyframes[1].State);
        Assert.Equal(CropInterpolation.Hold, clip.Crop.Keyframes[1].Interpolation);
        Assert.Equal(Middle, TimelineEvaluator.CropAt(clip, media, 99));
        Assert.True(editor.Undo());
        var restored = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(new long[] { 0, 20, 40 }, restored.Crop!.Keyframes.Select(key => key.Frame));
        Assert.Equal(End, restored.Crop.Keyframes[2].State);
        Assert.True(editor.Redo());
        Assert.Equal(new long[] { 0, 40 }, editor.Project.Tracks[0].Clips[0].Crop!.Keyframes.Select(key => key.Frame));
    }

    [Fact]
    public void MissingSourceIsRejectedAtomically()
    {
        var (editor, clip, _, _) = Create();
        Assert.Contains("source", Assert.Throws<InvalidOperationException>(() => editor.MoveCropKeyframe(clip.Id, 21, 30)).Message);
        Assert.Equal(new long[] { 0, 20, 40 }, clip.Crop!.Keyframes.Select(key => key.Frame));
        Assert.Equal(Middle, clip.Crop.Keyframes[1].State);
        Assert.False(editor.CanUndo);
        clip.Crop = null;
        Assert.Throws<InvalidOperationException>(() => editor.MoveCropKeyframe(clip.Id, 0, 10));
        Assert.False(editor.CanUndo);
    }

    [Theory]
    [InlineData(-1, 20)]
    [InlineData(100, 20)]
    [InlineData(20, -1)]
    [InlineData(20, 100)]
    public void OnlyHalfOpenEventLocalFramesAreAccepted(long from, long to)
    {
        var (editor, clip, _, _) = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.MoveCropKeyframe(clip.Id, from, to));
        Assert.False(editor.CanUndo);
        Assert.Equal(new long[] { 0, 20, 40 }, clip.Crop!.Keyframes.Select(key => key.Frame));
    }

    [Fact]
    public void LockedAndAudioTracksRejectMovementIncludingNoops()
    {
        var (editor, clip, track, media) = Create();
        track.IsLocked = true;
        Assert.Throws<InvalidOperationException>(() => editor.MoveCropKeyframe(clip.Id, 20, 30));
        Assert.Throws<InvalidOperationException>(() => editor.MoveCropKeyframe(clip.Id, 20, 20));
        Assert.False(editor.CanUndo);
        var sound = new TimelineClip { MediaId = media.Id, DurationFrames = 100 };
        editor.Project.Tracks.Add(new() { Kind = TrackKind.Audio, Clips = [sound] });
        Assert.Throws<InvalidOperationException>(() => editor.MoveCropKeyframe(sound.Id, 0, 20));
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void NegativeOffsetDestinationRebasesAllKeysWithoutChangingOtherLocalTimes()
    {
        var (editor, clip, _, media) = Create(-10);
        editor.MoveCropKeyframe(clip.Id, 30, 2);
        Assert.Equal(-2, clip.CropOffsetFrames);
        Assert.Equal(new long[] { 0, 8, 48 }, clip.Crop!.Keyframes.Select(key => key.Frame));
        Assert.Equal(Middle, TimelineEvaluator.CropAt(clip, media, 2));
        Assert.Equal(Start, TimelineEvaluator.CropAt(clip, media, 10));
        Assert.Equal(End, TimelineEvaluator.CropAt(clip, media, 50));
        Assert.Equal(CropInterpolation.Hold, clip.Crop.Keyframes[0].Interpolation);
        Assert.Equal(30, clip.SourceInFrame);
        Assert.True(editor.Undo());
        var restored = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(-10, restored.CropOffsetFrames);
        Assert.Equal(new long[] { 0, 20, 40 }, restored.Crop!.Keyframes.Select(key => key.Frame));
        Assert.True(editor.Redo());
        Assert.Equal(-2, editor.Project.Tracks[0].Clips[0].CropOffsetFrames);
    }

    [Fact]
    public void PositiveOffsetIsRetainedAndHiddenKeysRemainUnchanged()
    {
        var (editor, clip, _, _) = Create(10);
        editor.MoveCropKeyframe(clip.Id, 10, 25);
        Assert.Equal(10, clip.CropOffsetFrames);
        Assert.Equal(new long[] { 0, 35, 40 }, clip.Crop!.Keyframes.Select(key => key.Frame));
        Assert.Equal(Start, clip.Crop.Keyframes[0].State);
    }

    [Fact]
    public void UndoAndRedoKeepDeepSnapshotsAfterReordering()
    {
        var (editor, clip, _, _) = Create();
        var original = clip.Crop!;
        editor.MoveCropKeyframe(clip.Id, 20, 60);
        Assert.NotSame(original, clip.Crop);
        Assert.Equal(20, original.Keyframes[1].Frame);
        var moved = clip.Crop!;
        Assert.True(editor.Undo());
        Assert.Equal(new long[] { 0, 20, 40 }, editor.Project.Tracks[0].Clips[0].Crop!.Keyframes.Select(key => key.Frame));
        moved.Keyframes[2].State = End;
        Assert.True(editor.Redo());
        Assert.Equal(Middle, editor.Project.Tracks[0].Clips[0].Crop!.Keyframes[2].State);
        Assert.True(editor.Undo());
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void OverflowWhileRebasingHiddenKeysLeavesTheOriginalAnimationIntact()
    {
        var (editor, clip, _, _) = Create(-10);
        clip.Crop!.Keyframes.Add(new() { Frame = long.MaxValue, State = End });
        editor.Project.Validate();
        Assert.Throws<OverflowException>(() => editor.MoveCropKeyframe(clip.Id, 30, 0));
        Assert.Equal(-10, clip.CropOffsetFrames);
        Assert.Equal(new long[] { 0, 20, 40, long.MaxValue }, clip.Crop.Keyframes.Select(key => key.Frame));
        Assert.Equal(Middle, clip.Crop.Keyframes[1].State);
        Assert.False(editor.CanUndo);
    }
}
