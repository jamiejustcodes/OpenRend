using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class TransitionProgressTests
{
    [Fact]
    public void CurveUsesWholeFramesAndKeepsHiddenPointsAfterShortening()
    {
        var curve = new TransitionProgressCurve
        {
            Points = [new(0, 0), new(10, .75, EnvelopeCurveType.Hold), new(15, .25)],
            EndValue = .9
        };
        curve.Validate();
        Assert.Equal(.375, curve.ValueAt(5, 20));
        Assert.Equal(.75, curve.ValueAt(14, 20));
        Assert.Equal(.25, curve.ValueAt(15, 20));
        Assert.Equal(.9, curve.ValueAt(12, 12));
        Assert.Equal(.75, curve.ValueAt(11, 12));
        Assert.Equal(.25, curve.ValueAt(15, 20));
        Assert.Equal(new long[] { 0, 5, 7 }, curve.WithFrameRate(frame => frame / 2).Points.Select(point => point.Frame));
        var collapsed = new TransitionProgressCurve { Points = [new(0, 0), new(1, .5), new(4, .25), new(5, .75)] }
            .WithFrameRate(frame => frame / 2);
        Assert.Equal(new[] { (0L, 0d), (2L, .75) }, collapsed.Points.Select(point => (point.Frame, point.Value)));
    }

    [Fact]
    public void EditingSplitProgressUpdatesBothPiecesAndUndoRestoresTheCurve()
    {
        var red = new MediaAsset { Path = "openrend://color?hex=#C80000", HasVideo = true };
        var blue = new MediaAsset { Path = "openrend://color?hex=#0000C8", HasVideo = true };
        var outgoing = new TimelineClip { MediaId = red.Id, DurationFrames = 60, FadeOutFrames = 30 };
        var incoming = new TimelineClip { MediaId = blue.Id, StartFrame = 30, DurationFrames = 60, FadeInFrames = 30 };
        var track = new TimelineTrack { Kind = TrackKind.Video, Clips = [outgoing, incoming] };
        var editor = new TimelineEditor(new ProjectDocument { Media = [red, blue], Tracks = [track] });
        editor.SetTransitionProgress(incoming.Id, new TransitionProgressCurve { Points = [new(0, .5)] });
        var right = editor.SplitClip(incoming.Id, 45);
        Assert.True(editor.CanEditTransitionProgress(right.Id));
        editor.SetTransitionProgress(right.Id, new TransitionProgressCurve { Points = [new(0, .25)] });
        Assert.Equal(.25, incoming.FadeInProgress!.Points[0].Value);
        Assert.Equal(.25, right.FadeInProgress!.Points[0].Value);
        Assert.NotSame(incoming.FadeInProgress, right.FadeInProgress);
        Assert.True(editor.Undo());
        Assert.Equal(.5, editor.FindClip(incoming.Id).Clip.FadeInProgress!.Points[0].Value);
        Assert.Equal(.5, editor.FindClip(right.Id).Clip.FadeInProgress!.Points[0].Value);
        editor.FindClip(incoming.Id).Clip.IsLocked = true;
        Assert.False(editor.CanEditTransitionProgress(right.Id));
        Assert.Throws<InvalidOperationException>(() => editor.SetTransitionProgress(right.Id, null));
    }

    [Fact]
    public void RemovingAnEffectKeepsTheCurveAndConvertingToACutClearsIt()
    {
        var media = new MediaAsset { Path = "openrend://color?hex=#0000C8", HasVideo = true };
        var outgoing = new TimelineClip { MediaId = media.Id, DurationFrames = 60, FadeOutFrames = 30 };
        var incoming = new TimelineClip { MediaId = media.Id, StartFrame = 30, DurationFrames = 60, FadeInFrames = 30, FadeInTransition = new() };
        var project = new ProjectDocument { Media = [media], Tracks = [new() { Kind = TrackKind.Video, Clips = [outgoing, incoming] }] };
        var editor = new TimelineEditor(project);
        editor.SetTransitionProgress(incoming.Id, new TransitionProgressCurve { Points = [new(0, .5, EnvelopeCurveType.Hold)] });
        editor.RemoveVideoTransition(incoming.Id);
        Assert.Null(incoming.FadeInTransition);
        Assert.Equal(.5, TimelineEvaluator.Evaluate(project, 35).Video.Last().Dissolve!.Progress);
        editor.ConvertOverlapToCut(outgoing.Id, incoming.Id, CutTransitionAlignment.CenteredOnCut);
        Assert.Null(incoming.FadeInProgress);
        Assert.True(editor.Undo());
        Assert.Equal(.5, editor.FindClip(incoming.Id).Clip.FadeInProgress!.Points[0].Value);
    }
}
