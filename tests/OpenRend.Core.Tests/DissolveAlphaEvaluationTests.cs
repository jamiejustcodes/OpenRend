using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class DissolveAlphaEvaluationTests
{
    [Fact]
    public void PairMetadataPreservesProgressAndParticipantsAcrossBothEventSplits()
    {
        var asset = new MediaAsset { Path = "video.mp4", HasVideo = true, DurationSeconds = 5 };
        var editor = new TimelineEditor(new ProjectDocument());
        editor.AddMedia(asset);
        var track = editor.AddTrack(TrackKind.Video);
        var outgoing = editor.PlaceClip(asset.Id, track.Id, 0, 30);
        var incoming = editor.PlaceClip(asset.Id, track.Id, 10, 30);
        editor.ApplyDissolve(outgoing.Id, incoming.Id);
        editor.SplitClip(incoming.Id, 15);
        editor.SplitClip(outgoing.Id, 23);
        for (var frame = 10; frame < 30; frame++)
        {
            var layers = TimelineEvaluator.Evaluate(editor.Project, frame).Video;
            Assert.Equal(2, layers.Count);
            var pair = Assert.IsType<EvaluatedDissolve>(layers[0].Dissolve);
            Assert.Same(pair, layers[1].Dissolve);
            Assert.Equal(layers[0].Clip.Id, pair.OutgoingClipId);
            Assert.Equal(layers[1].Clip.Id, pair.IncomingClipId);
            Assert.Equal((frame - 10) / 20.0, pair.Progress, 12);
        }
        Assert.Null(Assert.Single(TimelineEvaluator.Evaluate(editor.Project, 30).Video).Dissolve);
    }
}
