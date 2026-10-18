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

    [Theory]
    [InlineData(CutTransitionAlignment.BeforeCut, 15, 0)]
    [InlineData(CutTransitionAlignment.CenteredOnCut, 7, 8)]
    [InlineData(CutTransitionAlignment.AfterCut, 0, 15)]
    public void CutConversionExtendsSourceHandlesWithoutMovingExistingContent(CutTransitionAlignment alignment, long before, long after)
    {
        var (editor, media, track) = Create();
        var outgoing = editor.PlaceClip(media.Id, track.Id, 0, 60);
        editor.TrimClip(outgoing.Id, 20, 60);
        var incoming = editor.PlaceClip(media.Id, track.Id, 60, 60);
        editor.TrimClip(incoming.Id, 30, 60);
        var later = editor.PlaceClip(media.Id, track.Id, 180, 30);

        editor.ConvertCutToDissolve(outgoing.Id, incoming.Id, 15, alignment);

        Assert.Equal(60 + after, outgoing.EndFrame);
        Assert.Equal(60 - before, incoming.StartFrame);
        Assert.Equal(30 - before, incoming.SourceInFrame);
        Assert.Equal(120, incoming.EndFrame);
        Assert.Equal(180, later.StartFrame);
        Assert.Equal(15, outgoing.FadeOutFrames);
        Assert.Equal(15, incoming.FadeInFrames);
        var firstUnblendedFrame = Assert.Single(TimelineEvaluator.Evaluate(editor.Project, 60 + after).Video);
        Assert.Equal(30 + after, firstUnblendedFrame.SourceFrame);
        Assert.True(editor.Undo());
        var restored = editor.Project.Tracks[0].Clips;
        Assert.Equal(60, restored[0].EndFrame);
        Assert.Equal(60, restored[1].StartFrame);
        Assert.Equal(30, restored[1].SourceInFrame);
        Assert.Null(restored[1].FadeInTransition);
    }

    [Fact]
    public void CutConversionRejectsMissingSourceHandlesWithoutChangingTheTimeline()
    {
        var (editor, media, track) = Create();
        var outgoing = editor.PlaceClip(media.Id, track.Id, 0, 60);
        var incoming = editor.PlaceClip(media.Id, track.Id, 60, 60);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            editor.ConvertCutToDissolve(outgoing.Id, incoming.Id, 15, CutTransitionAlignment.BeforeCut));
        Assert.Equal(60, incoming.StartFrame);
        Assert.Null(incoming.FadeInTransition);
        editor.TrimClip(outgoing.Id, 240, 60);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            editor.ConvertCutToDissolve(outgoing.Id, incoming.Id, 15, CutTransitionAlignment.AfterCut));
        Assert.Equal(60, outgoing.EndFrame);
        Assert.Equal(60, incoming.StartFrame);
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

    [Fact]
    public void MoveSelectionToNewTrackCreatesNewLayerAndMovesPrimaryClipWithUndoRedo()
    {
        var (editor, media, track) = Create();
        var clip = editor.PlaceClip(media.Id, track.Id, 10, 60);
        Assert.Single(editor.Project.Tracks);
        Assert.Contains(clip, track.Clips);

        var newTrack = editor.MoveSelectionToNewTrack([clip.Id], clip.Id, 1, TrackKind.Video, 25);
        Assert.NotNull(newTrack);
        Assert.Equal(TrackKind.Video, newTrack.Kind);
        Assert.Equal("Video 2", newTrack.Name);
        Assert.Equal(2, editor.Project.Tracks.Count);
        Assert.Empty(track.Clips);
        Assert.Contains(clip, newTrack.Clips);
        Assert.Equal(25, clip.StartFrame);

        // Undo restores original single track and clip position
        Assert.True(editor.Undo());
        Assert.Single(editor.Project.Tracks);
        Assert.Equal(track.Id, editor.Project.Tracks[0].Id);
        Assert.Single(editor.Project.Tracks[0].Clips);
        Assert.Equal(10, editor.Project.Tracks[0].Clips[0].StartFrame);

        // Redo re-creates the track and moved clip
        Assert.True(editor.Redo());
        Assert.Equal(2, editor.Project.Tracks.Count);
        Assert.Equal(25, editor.Project.Tracks[1].Clips[0].StartFrame);
    }

    [Fact]
    public void MoveSelectionToNewTrackSyncsLinkedClipsHorizontally()
    {
        var editor = new TimelineEditor(new ProjectDocument());
        var media = new MediaAsset { Path = "test.mp4", DurationSeconds = 10, HasVideo = true, HasAudio = true };
        editor.AddMedia(media);
        var videoTrack = editor.AddTrack(TrackKind.Video);
        var audioTrack = editor.AddTrack(TrackKind.Audio);
        var videoClip = editor.PlaceClip(media.Id, videoTrack.Id, 10, 60);
        var audioClip = editor.PlaceClip(media.Id, audioTrack.Id, 10, 60);
        editor.LinkClips([videoClip.Id, audioClip.Id]);

        // Drag video clip down to create a new video layer at index 1 (between video and audio)
        var newTrack = editor.MoveSelectionToNewTrack([videoClip.Id], videoClip.Id, 1, TrackKind.Video, 30);
        Assert.Equal(3, editor.Project.Tracks.Count);
        Assert.Equal(newTrack.Id, editor.Project.Tracks[1].Id);
        Assert.Contains(videoClip, newTrack.Clips);
        Assert.Equal(30, videoClip.StartFrame);

        // Linked audio clip is still on the audio track, but shifted horizontally in sync!
        Assert.Contains(audioClip, audioTrack.Clips);
        Assert.Equal(30, audioClip.StartFrame);
    }

    [Fact]
    public void MoveSelectionToNewTrackRejectsIncompatibleMediaAndLockedClips()
    {
        var editor = new TimelineEditor(new ProjectDocument());
        var videoMedia = new MediaAsset { Path = "test.mp4", DurationSeconds = 10, HasVideo = true, HasAudio = false };
        var audioMedia = new MediaAsset { Path = "test.wav", DurationSeconds = 10, HasVideo = false, HasAudio = true };
        editor.AddMedia(videoMedia);
        editor.AddMedia(audioMedia);
        var videoTrack = editor.AddTrack(TrackKind.Video);
        var audioTrack = editor.AddTrack(TrackKind.Audio);
        var videoClip = editor.PlaceClip(videoMedia.Id, videoTrack.Id, 0, 60);
        var audioClip = editor.PlaceClip(audioMedia.Id, audioTrack.Id, 0, 60);

        // Cannot move audio media to video track or video media to audio track
        Assert.Throws<InvalidOperationException>(() => editor.MoveSelectionToNewTrack([audioClip.Id], audioClip.Id, 0, TrackKind.Video, 10));
        Assert.Throws<InvalidOperationException>(() => editor.MoveSelectionToNewTrack([videoClip.Id], videoClip.Id, 1, TrackKind.Audio, 10));

        // Locked clip rejection
        editor.SetClipLock(videoClip.Id, true);
        Assert.Throws<InvalidOperationException>(() => editor.MoveSelectionToNewTrack([videoClip.Id], videoClip.Id, 1, TrackKind.Video, 10));
    }

    [Fact]
    public void MoveSelectionToNewTrackMovesMultipleSelectedClipsOnSourceTrack()
    {
        var (editor, media, track) = Create();
        var clip1 = editor.PlaceClip(media.Id, track.Id, 10, 30);
        var clip2 = editor.PlaceClip(media.Id, track.Id, 50, 30);
        Assert.Single(editor.Project.Tracks);
        Assert.Equal(2, track.Clips.Count);

        // Select both clips on the source track and move to new track at index 0 (above track 0)
        var newTrack = editor.MoveSelectionToNewTrack([clip1.Id, clip2.Id], clip1.Id, 0, TrackKind.Video, 20);
        Assert.Equal(2, editor.Project.Tracks.Count);
        Assert.Equal(newTrack.Id, editor.Project.Tracks[0].Id);
        Assert.Empty(track.Clips);
        Assert.Equal(2, newTrack.Clips.Count);
        Assert.Equal(20, clip1.StartFrame);
        Assert.Equal(60, clip2.StartFrame); // Shifted by delta = +10

        // Undo restores both clips to original track
        Assert.True(editor.Undo());
        Assert.Single(editor.Project.Tracks);
        var restoredTrack = editor.Project.Tracks[0];
        Assert.Equal(2, restoredTrack.Clips.Count);
        Assert.Equal(10, restoredTrack.Clips.Single(c => c.Id == clip1.Id).StartFrame);
        Assert.Equal(50, restoredTrack.Clips.Single(c => c.Id == clip2.Id).StartFrame);
    }

    [Fact]
    public void MoveSelectionToNewTrackAudioClipCreatesAudioLayerWithLinkedVideoSync()
    {
        var editor = new TimelineEditor(new ProjectDocument());
        var media = new MediaAsset { Path = "test.mp4", DurationSeconds = 10, HasVideo = true, HasAudio = true };
        editor.AddMedia(media);
        var videoTrack = editor.AddTrack(TrackKind.Video);
        var audioTrack = editor.AddTrack(TrackKind.Audio);
        var videoClip = editor.PlaceClip(media.Id, videoTrack.Id, 10, 60);
        var audioClip = editor.PlaceClip(media.Id, audioTrack.Id, 10, 60);
        editor.LinkClips([videoClip.Id, audioClip.Id]);

        // Drag audio clip down to create a new audio layer at index 2 (below all tracks)
        var newAudioTrack = editor.MoveSelectionToNewTrack([audioClip.Id], audioClip.Id, 2, TrackKind.Audio, 40);
        Assert.Equal(3, editor.Project.Tracks.Count);
        Assert.Equal(newAudioTrack.Id, editor.Project.Tracks[2].Id);
        Assert.Equal(TrackKind.Audio, newAudioTrack.Kind);
        Assert.Equal("Audio 2", newAudioTrack.Name);
        Assert.Empty(audioTrack.Clips);
        Assert.Contains(audioClip, newAudioTrack.Clips);
        Assert.Equal(40, audioClip.StartFrame);

        // Linked video clip remains on video track, shifted horizontally in sync
        Assert.Contains(videoClip, videoTrack.Clips);
        Assert.Equal(40, videoClip.StartFrame);

        // Undo restores
        Assert.True(editor.Undo());
        Assert.Equal(2, editor.Project.Tracks.Count);
        var restoredVideoTrack = editor.Project.Tracks.Single(t => t.Id == videoTrack.Id);
        var restoredAudioTrack = editor.Project.Tracks.Single(t => t.Id == audioTrack.Id);
        Assert.Contains(restoredAudioTrack.Clips, c => c.Id == audioClip.Id);
        Assert.Contains(restoredVideoTrack.Clips, c => c.Id == videoClip.Id);
        Assert.Equal(10, restoredAudioTrack.Clips.Single(c => c.Id == audioClip.Id).StartFrame);
        Assert.Equal(10, restoredVideoTrack.Clips.Single(c => c.Id == videoClip.Id).StartFrame);
    }
}
