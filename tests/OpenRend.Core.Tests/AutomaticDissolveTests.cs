using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class AutomaticDissolveTests
{
    private static (TimelineEditor Editor, TimelineClip Outgoing, TimelineClip Incoming, TimelineClip Later, TimelineClip Other) Create(Rational? rate = null)
    {
        var media = new MediaAsset { Path = "source.mp4", HasVideo = true, HasAudio = true, DurationSeconds = 100 };
        var outgoing = new TimelineClip { MediaId = media.Id, StartFrame = 40, DurationFrames = 55 };
        var incoming = new TimelineClip { MediaId = media.Id, StartFrame = 100, SourceInFrame = 50, DurationFrames = 50 };
        var later = new TimelineClip { MediaId = media.Id, StartFrame = 160, DurationFrames = 30 };
        var other = new TimelineClip { MediaId = media.Id, StartFrame = 100, DurationFrames = 20 };
        var project = new ProjectDocument { FrameRate = rate ?? new Rational(30, 1), Media = [media],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [outgoing, incoming, later] }, new() { Kind = TrackKind.Video, Clips = [other] }] };
        return (new(project), outgoing, incoming, later, other);
    }

    [Theory]
    [InlineData(30, 1)]
    [InlineData(30000, 1001)]
    public void NonRippleCreatesActualGapAdjustedOverlapAndPreservesOriginalSourceTiming(int numerator, int denominator)
    {
        var (editor, outgoing, incoming, later, other) = Create(new(numerator, denominator));
        incoming.Crop = new() { Keyframes = [new() { Frame = 0, State = CropState.Identity(640, 360) }] };
        var oldSourceAt110 = incoming.SourceInFrame + 110 - incoming.StartFrame;
        var oldCropAt110 = 110 - incoming.StartFrame + incoming.CropOffsetFrames;
        Assert.Equal(1, editor.ApplyDissolveToSelection([incoming.Id], 20, false));
        Assert.Equal(80, incoming.StartFrame); Assert.Equal(50, incoming.DurationFrames); Assert.Equal(30, incoming.SourceInFrame);
        Assert.Equal(oldSourceAt110, incoming.SourceInFrame + 110 - incoming.StartFrame);
        Assert.Equal(oldCropAt110, 110 - incoming.StartFrame + incoming.CropOffsetFrames);
        Assert.Equal(15, outgoing.FadeOutFrames); Assert.Equal(15, incoming.FadeInFrames); Assert.NotNull(incoming.FadeInTransition);
        Assert.Equal(160, later.StartFrame); Assert.Equal(100, other.StartFrame);
        Assert.True(editor.Undo()); Assert.Equal(100, editor.Project.Tracks[0].Clips.Single(clip => clip.Id == incoming.Id).StartFrame); Assert.False(editor.CanUndo);
        Assert.True(editor.Redo()); Assert.Equal(80, editor.Project.Tracks[0].Clips.Single(clip => clip.Id == incoming.Id).StartFrame);
    }

    [Fact]
    public void RippleMovesOnlySameTrackEventsAtOrAfterTheOriginalStartAndIgnoresGroups()
    {
        var (editor, outgoing, incoming, later, other) = Create();
        incoming.GroupId = later.GroupId = other.GroupId = Guid.NewGuid();
        Assert.Equal(1, editor.ApplyDissolveToSelection([incoming.Id], 20, true));
        Assert.Equal(80, incoming.StartFrame); Assert.Equal(50, incoming.SourceInFrame); Assert.Equal(140, later.StartFrame);
        Assert.Equal(40, outgoing.StartFrame); Assert.Equal(100, other.StartFrame);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(4)]
    public void ExactOrLargerGapDoesNotMoveAnythingOrAddUndo(long amount)
    {
        var (editor, _, incoming, _, _) = Create();
        Assert.Equal(0, editor.ApplyDissolveToSelection([incoming.Id], amount, false));
        Assert.Equal(100, incoming.StartFrame); Assert.False(editor.CanUndo);
    }

    [Fact]
    public void MissingSourceHandlesRejectTheWholeOperationWithoutNativeSourceWrapping()
    {
        var (editor, outgoing, incoming, later, _) = Create(); incoming.SourceInFrame = 10;
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.ApplyDissolveToSelection([incoming.Id], 20, false));
        Assert.Equal(100, incoming.StartFrame); Assert.Equal(0, outgoing.FadeOutFrames); Assert.Equal(160, later.StartFrame); Assert.False(editor.CanUndo);
    }

    [Fact]
    public void LockedTrackAndThreeWayOverlapRejectBeforeMutation()
    {
        var (editor, outgoing, incoming, later, _) = Create(); editor.Project.Tracks[0].IsLocked = true;
        Assert.Throws<InvalidOperationException>(() => editor.ApplyDissolveToSelection([incoming.Id], 20, true));
        editor.Project.Tracks[0].IsLocked = false;
        var third = later.DeepClone(); third.Id = Guid.NewGuid(); third.StartFrame = 45; third.DurationFrames = 45;
        editor.Project.Tracks[0].Clips.Add(third);
        Assert.Throws<InvalidOperationException>(() => editor.ApplyDissolveToSelection([incoming.Id], 20, true));
        Assert.Equal(100, incoming.StartFrame); Assert.Equal(0, outgoing.FadeOutFrames); Assert.False(editor.CanUndo);
    }

    [Fact]
    public void ZeroClampRejectsAnUnsupportedSameStartPairInsteadOfGoingNegative()
    {
        var (editor, outgoing, incoming, _, _) = Create(); outgoing.StartFrame = 0; outgoing.DurationFrames = 5; incoming.StartFrame = 8;
        Assert.Throws<InvalidOperationException>(() => editor.ApplyDissolveToSelection([incoming.Id], 20, false));
        Assert.Equal(8, incoming.StartFrame); Assert.False(editor.CanUndo);
    }

    [Fact]
    public async Task AutomaticPlacementPersistsItsTimelineAndFadeDecisions()
    {
        var (editor, _, incoming, _, _) = Create(); editor.ApplyDissolveToSelection([incoming.Id], 20, false);
        var folder = Path.Combine(Path.GetTempPath(), "OpenRendTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "automatic.openrend"); await ProjectStore.SaveAsync(editor.Project, path);
            var saved = (await ProjectStore.LoadAsync(path)).Tracks[0].Clips.Single(clip => clip.Id == incoming.Id);
            Assert.Equal(80, saved.StartFrame); Assert.Equal(30, saved.SourceInFrame); Assert.Equal(15, saved.FadeInFrames); Assert.NotNull(saved.FadeInTransition);
        }
        finally { Directory.Delete(folder, true); }
    }
}
