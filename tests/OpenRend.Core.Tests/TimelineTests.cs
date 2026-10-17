using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class TimelineTests
{
    private static (TimelineEditor Editor, MediaAsset Media, TimelineTrack Track) Create()
    {
        var editor = new TimelineEditor(new ProjectDocument());
        var media = new MediaAsset { Path = "test.mp4", DurationSeconds = 10, HasVideo = true, HasAudio = true };
        editor.AddMedia(media);
        var track = editor.AddTrack(TrackKind.Video);
        return (editor, media, track);
    }

    [Fact]
    public void RationalNormalizesAndDoesNotDriftAcrossLongTimelines()
    {
        var rate = new Rational(60000, 2002);
        Assert.Equal(new Rational(30000, 1001), rate);
        Assert.Equal(1_000_000, rate.Frames(rate.Seconds(1_000_000)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rational(30, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rate.Frames(double.PositiveInfinity));
    }

    [Fact]
    public void PlacementUsesHalfOpenIntervalsAndSourceOffset()
    {
        var (editor, media, track) = Create();
        var clip = editor.PlaceClip(media.Id, track.Id, 15, 60);
        editor.TrimClip(clip.Id, 30, 45);
        Assert.Empty(TimelineEvaluator.Evaluate(editor.Project, 14).Video);
        Assert.Equal(30, Assert.Single(TimelineEvaluator.Evaluate(editor.Project, 15).Video).SourceFrame);
        Assert.Equal(74, Assert.Single(TimelineEvaluator.Evaluate(editor.Project, 59).Video).SourceFrame);
        Assert.Empty(TimelineEvaluator.Evaluate(editor.Project, 60).Video);
    }

    [Fact]
    public void FailedTransactionsRollbackWithoutAddingHistory()
    {
        var (editor, media, track) = Create();
        var clip = editor.PlaceClip(media.Id, track.Id, 0, 60);
        Assert.Throws<InvalidDataException>(() => editor.MoveClip(clip.Id, track.Id, -1));
        Assert.Equal(0, editor.Project.Tracks[0].Clips[0].StartFrame);
        Assert.True(editor.Undo());
        Assert.Empty(editor.Project.Tracks[0].Clips);
    }

    [Fact]
    public void SplitPreservesSourceAndFadeEnvelope()
    {
        var (editor, media, track) = Create();
        var clip = editor.PlaceClip(media.Id, track.Id, 0, 90);
        editor.SetClipLevels(clip.Id, fadeInFrames: 40, fadeOutFrames: 30);
        var expected = Enumerable.Range(0, 90).Select(f => Assert.Single(TimelineEvaluator.Evaluate(editor.Project, f).Video).Opacity).ToArray();
        var right = editor.SplitClip(clip.Id, 20);
        editor.SplitClip(right.Id, 75);
        for (var frame = 0; frame < 90; frame++)
        {
            var value = Assert.Single(TimelineEvaluator.Evaluate(editor.Project, frame).Video);
            Assert.Equal(frame, value.SourceFrame);
            Assert.Equal(expected[frame], value.Opacity, 12);
        }
    }

    [Fact]
    public void SplitAtEdgesRejectedAndLockedTracksCannotChange()
    {
        var (editor, media, track) = Create();
        var clip = editor.PlaceClip(media.Id, track.Id, 10, 60);
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SplitClip(clip.Id, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SplitClip(clip.Id, 70));
        editor.SetTrackState(track.Id, isLocked: true);
        Assert.Throws<InvalidOperationException>(() => editor.DeleteClip(clip.Id));
        Assert.Throws<InvalidOperationException>(() => editor.TrimClip(clip.Id, 0, 30));
    }

    [Fact]
    public void DissolveAttachesPresetToIncomingFadeAndUndoRestoresIt()
    {
        var (editor, media, track) = Create();
        var outgoing = editor.PlaceClip(media.Id, track.Id, 0, 60);
        var incoming = editor.PlaceClip(media.Id, track.Id, 45, 60);

        editor.ApplyDissolve(outgoing.Id, incoming.Id);

        Assert.Equal(15, outgoing.FadeOutFrames);
        Assert.Equal(15, incoming.FadeInFrames);
        Assert.Null(outgoing.FadeOutTransition);
        Assert.Equal("Default", incoming.FadeInTransition!.Preset);
        Assert.True(editor.Undo());
        Assert.Null(editor.Project.Tracks[0].Clips[0].FadeOutTransition);
        Assert.Null(editor.Project.Tracks[0].Clips[1].FadeInTransition);
        Assert.True(editor.Redo());
        Assert.Equal(15, editor.Project.Tracks[0].Clips[1].FadeInFrames);
    }

    [Fact]
    public void DissolveRejectsNonOverlappingAndThreeWayEvents()
    {
        var (editor, media, track) = Create();
        var outgoing = editor.PlaceClip(media.Id, track.Id, 0, 60);
        var incoming = editor.PlaceClip(media.Id, track.Id, 60, 60);
        Assert.Throws<InvalidOperationException>(() => editor.ApplyDissolve(outgoing.Id, incoming.Id));
        editor.MoveClip(incoming.Id, track.Id, 45);
        editor.PlaceClip(media.Id, track.Id, 50, 30);
        Assert.Throws<InvalidOperationException>(() => editor.ApplyDissolve(outgoing.Id, incoming.Id));
        Assert.Null(outgoing.FadeOutTransition);
    }

    [Fact]
    public void DissolveEvaluatesOverlapWithoutDimmingTheOutgoingEvent()
    {
        var (editor, media, track) = Create();
        var outgoing = editor.PlaceClip(media.Id, track.Id, 0, 60);
        var incoming = editor.PlaceClip(media.Id, track.Id, 45, 60);
        editor.ApplyDissolve(outgoing.Id, incoming.Id);

        var start = TimelineEvaluator.Evaluate(editor.Project, 45).Video;
        Assert.Equal(new[] { outgoing.Id, incoming.Id }, start.Select(layer => layer.Clip.Id));
        Assert.Equal(1, start[0].Opacity);
        Assert.Equal(0, start[1].Opacity);
        var middle = TimelineEvaluator.Evaluate(editor.Project, 52).Video;
        Assert.Equal(1, middle[0].Opacity);
        Assert.Equal(7.0 / 15, middle[1].Opacity, 12);
        Assert.Equal(1, Assert.Single(TimelineEvaluator.Evaluate(editor.Project, 60).Video).Opacity);
    }

    [Fact]
    public void MovingAndTrimmingOverlapKeepsDissolveFadeFramesAligned()
    {
        var (editor, media, track) = Create();
        var outgoing = editor.PlaceClip(media.Id, track.Id, 0, 60);
        var incoming = editor.PlaceClip(media.Id, track.Id, 45, 60);
        editor.ApplyDissolve(outgoing.Id, incoming.Id);

        editor.MoveClip(incoming.Id, track.Id, 40);
        Assert.Equal(20, outgoing.FadeOutFrames);
        Assert.Equal(20, incoming.FadeInFrames);
        Assert.Equal(.5, TimelineEvaluator.Evaluate(editor.Project, 50).Video[1].Opacity, 12);

        editor.TrimClip(outgoing.Id, 0, 55);
        Assert.Equal(15, outgoing.FadeOutFrames);
        Assert.Equal(15, incoming.FadeInFrames);
        Assert.Equal(10.0 / 15, TimelineEvaluator.Evaluate(editor.Project, 50).Video[1].Opacity, 12);
    }

    [Fact]
    public void OneSelectedEventAppliesDissolveToBothAdjacentOverlapsAtomically()
    {
        var (editor, media, track) = Create();
        var first = editor.PlaceClip(media.Id, track.Id, 0, 30);
        var middle = editor.PlaceClip(media.Id, track.Id, 20, 30);
        var last = editor.PlaceClip(media.Id, track.Id, 40, 30);

        Assert.Equal(2, editor.ApplyDissolvesToAdjacent([middle.Id]));
        Assert.Equal(10, first.FadeOutFrames);
        Assert.Equal(10, middle.FadeInFrames);
        Assert.Equal(10, middle.FadeOutFrames);
        Assert.Equal(10, last.FadeInFrames);
        Assert.True(editor.Undo());
        Assert.All(editor.Project.Tracks[0].Clips, clip =>
        {
            Assert.Null(clip.FadeInTransition);
            Assert.Null(clip.FadeOutTransition);
        });
    }

    [Fact]
    public void TwoSelectedEventsApplyDissolveOnlyBetweenThatPair()
    {
        var (editor, media, track) = Create();
        var first = editor.PlaceClip(media.Id, track.Id, 0, 30);
        var second = editor.PlaceClip(media.Id, track.Id, 20, 30);
        var third = editor.PlaceClip(media.Id, track.Id, 40, 30);
        var last = editor.PlaceClip(media.Id, track.Id, 60, 30);

        Assert.Equal(1, editor.ApplyDissolveToSelection([second.Id, third.Id]));

        Assert.Null(first.FadeOutTransition);
        Assert.Equal(0, first.FadeOutFrames);
        Assert.Equal(0, second.FadeInFrames);
        Assert.Null(second.FadeInTransition);
        Assert.Equal(10, second.FadeOutFrames);
        Assert.Equal(VideoTransitionKind.Dissolve, third.FadeInTransition!.Kind);
        Assert.Equal(0, third.FadeOutFrames);
        Assert.Null(last.FadeInTransition);
        Assert.Equal(0, last.FadeInFrames);
    }

    [Fact]
    public void UndoRedoAndBranchingRestoreWholeProject()
    {
        var (editor, media, track) = Create();
        var clip = editor.PlaceClip(media.Id, track.Id, 10, 60);
        editor.SplitClip(clip.Id, 40);
        Assert.True(editor.Undo());
        Assert.Equal(60, Assert.Single(editor.Project.Tracks[0].Clips).DurationFrames);
        Assert.True(editor.Redo());
        Assert.Equal(2, editor.Project.Tracks[0].Clips.Count);
        editor.Undo();
        editor.MoveClip(clip.Id, track.Id, 30);
        Assert.False(editor.CanRedo);
        Assert.Equal(30, Assert.Single(editor.Project.Tracks[0].Clips).StartFrame);
    }

    [Fact]
    public void RippleDeletionShiftsLaterClipsOnEditedTrackOnly()
    {
        var (editor, media, track) = Create();
        var first = editor.PlaceClip(media.Id, track.Id, 10, 30);
        editor.PlaceClip(media.Id, track.Id, 60, 30);
        var other = editor.AddTrack(TrackKind.Video);
        editor.PlaceClip(media.Id, other.Id, 60, 30);
        editor.DeleteClip(first.Id, ripple: true);
        Assert.Equal(30, Assert.Single(editor.Project.Tracks[0].Clips).StartFrame);
        Assert.Equal(60, Assert.Single(editor.Project.Tracks[1].Clips).StartFrame);
    }

    [Fact]
    public void EvaluationHonorsSoloMuteVisibilityAndLayerOrder()
    {
        var (editor, media, front) = Create();
        var back = editor.AddTrack(TrackKind.Video);
        var audio = editor.AddTrack(TrackKind.Audio);
        editor.PlaceClip(media.Id, front.Id, 0, 30);
        editor.PlaceClip(media.Id, back.Id, 0, 30);
        editor.PlaceClip(media.Id, audio.Id, 0, 30);
        Assert.Equal(new[] { back.Id, front.Id }, TimelineEvaluator.Evaluate(editor.Project, 0).Video.Select(v => v.Track.Id));
        editor.SetTrackState(front.Id, isSolo: true);
        var frame = TimelineEvaluator.Evaluate(editor.Project, 0);
        Assert.Equal(front.Id, Assert.Single(frame.Video).Track.Id);
        Assert.Single(frame.Audio);
        editor.SetTrackState(front.Id, isVisible: false);
        Assert.Empty(TimelineEvaluator.Evaluate(editor.Project, 0).Video);
        editor.SetTrackState(audio.Id, isMuted: true);
        Assert.Empty(TimelineEvaluator.Evaluate(editor.Project, 0).Audio);
    }

    [Fact]
    public void SourceBoundsAndStreamCompatibilityAreEnforced()
    {
        var (editor, media, track) = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.PlaceClip(media.Id, track.Id, 0, 301));
        var clip = editor.PlaceClip(media.Id, track.Id, 0, 30);
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.TrimClip(clip.Id, 280, 30));
        var audio = new MediaAsset { Path = "audio.wav", DurationSeconds = 2, HasAudio = true };
        editor.AddMedia(audio);
        Assert.Throws<InvalidOperationException>(() => editor.PlaceClip(audio.Id, track.Id, 0));
    }

    [Fact]
    public void DirectTransactionsCannotBypassSourceValidation()
    {
        var (editor, media, track) = Create();
        editor.PlaceClip(media.Id, track.Id, 0, 30);
        Assert.Throws<InvalidDataException>(() => editor.Execute(p => p.Tracks[0].Clips[0].SourceInFrame = 290));
        Assert.Equal(0, editor.Project.Tracks[0].Clips[0].SourceInFrame);
        Assert.Throws<InvalidDataException>(() => editor.Execute(p => p.Tracks[0].Kind = TrackKind.Audio + 1));
        Assert.Equal(TrackKind.Video, editor.Project.Tracks[0].Kind);
    }

    [Fact]
    public void TrimResetsSplitEnvelopeAndClampsFadeDurations()
    {
        var (editor, media, track) = Create();
        var clip = editor.PlaceClip(media.Id, track.Id, 0, 90);
        editor.SetClipLevels(clip.Id, fadeInFrames: 40, fadeOutFrames: 30);
        var right = editor.SplitClip(clip.Id, 20);
        editor.TrimClip(right.Id, 30, 30);
        Assert.Equal(0, right.FadeOffsetFrames);
        Assert.Null(right.FadeDurationFrames);
        Assert.Equal(30, right.FadeInFrames);
        Assert.Equal(0, right.FadeOutFrames);
        Assert.Equal(0, TimelineEvaluator.FadeAt(right, 0));
        Assert.Equal(0.5, TimelineEvaluator.FadeAt(right, 15));
    }

    [Fact]
    public void SnappingExcludesDraggedClipAndBreaksTiesConsistently()
    {
        var (editor, media, track) = Create();
        var clip = editor.PlaceClip(media.Id, track.Id, 20, 30);
        editor.PlaceClip(media.Id, track.Id, 80, 30);
        Assert.Equal(50, editor.SnapFrame(52, 3));
        Assert.Equal(52, editor.SnapFrame(52, 3, clip.Id));
        Assert.Equal(80, editor.SnapFrame(78, 3, clip.Id));
        Assert.Equal(0, editor.SnapFrame(-2, 3));
    }
}
