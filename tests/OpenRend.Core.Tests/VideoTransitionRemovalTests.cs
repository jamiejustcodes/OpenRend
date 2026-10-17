using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class VideoTransitionRemovalTests
{
    [Fact]
    public void RemoveClearsOnlyTheChosenPluginAndUndoRedoRestoreIt()
    {
        var project = CreateProject();
        var incoming = project.Tracks[0].Clips[1];
        incoming.FadeOutTransition = new VideoTransition();
        var editor = new TimelineEditor(project);
        var before = incoming.DeepClone();
        editor.RemoveVideoTransition(incoming.Id);
        var changed = project.Tracks[0].Clips[1];
        Assert.Null(changed.FadeInTransition);
        Assert.NotNull(changed.FadeOutTransition);
        Assert.Equal((before.StartFrame, before.SourceInFrame, before.DurationFrames, before.FadeInFrames, before.FadeOutFrames),
            (changed.StartFrame, changed.SourceInFrame, changed.DurationFrames, changed.FadeInFrames, changed.FadeOutFrames));
        Assert.True(editor.Undo());
        Assert.NotNull(project.Tracks[0].Clips[1].FadeInTransition);
        Assert.True(editor.Redo());
        Assert.Null(project.Tracks[0].Clips[1].FadeInTransition);
        editor.RemoveVideoTransition(incoming.Id, fadeOut: true);
        Assert.Null(project.Tracks[0].Clips[1].FadeOutTransition);
        Assert.True(editor.Undo());
        Assert.NotNull(project.Tracks[0].Clips[1].FadeOutTransition);
    }

    [Fact]
    public void NoPluginIsANoopButLockedAndAudioTargetsAreRejected()
    {
        var project = CreateProject();
        var editor = new TimelineEditor(project);
        var outgoing = project.Tracks[0].Clips[0];
        var changed = 0;
        editor.Changed += (_, _) => changed++;
        editor.RemoveVideoTransition(outgoing.Id);
        Assert.Equal(0, changed);
        Assert.False(editor.CanUndo);
        project.Tracks[0].IsLocked = true;
        Assert.Throws<InvalidOperationException>(() => editor.RemoveVideoTransition(outgoing.Id));
        project.Tracks[0].IsLocked = false;
        project.Tracks[0].Kind = TrackKind.Audio;
        Assert.Throws<InvalidOperationException>(() => editor.RemoveVideoTransition(outgoing.Id));
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public async Task RemovedSplitPluginsSaveAsPlainCrossfadesWithTheirOriginalEnvelopes()
    {
        var project = CreateProject();
        var editor = new TimelineEditor(project);
        var incoming = project.Tracks[0].Clips[1];
        var incomingRight = editor.SplitClip(incoming.Id, 15);
        editor.SplitClip(project.Tracks[0].Clips[0].Id, 23);
        editor.RemoveVideoTransition(incoming.Id);
        editor.RemoveVideoTransition(incomingRight.Id);
        var path = Path.Combine(Path.GetTempPath(), $"openrend-removed-transition-{Guid.NewGuid():N}.orproj");
        try
        {
            await ProjectStore.SaveAsync(project, path);
            var loaded = await ProjectStore.LoadAsync(path);
            Assert.All(loaded.Tracks[0].Clips, clip => Assert.Null(clip.FadeInTransition));
            foreach (var frame in Enumerable.Range(10, 20))
            {
                var layers = TimelineEvaluator.Evaluate(loaded, frame).Video;
                var pair = Assert.IsType<EvaluatedDissolve>(layers[0].Dissolve);
                Assert.Same(pair, layers[1].Dissolve);
                Assert.Equal((frame - 10) / 20.0, pair.Progress, 12);
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PlainPairRequiresMatchingGlobalFadeIntervalAndNoThreeWayOverlap(bool unequalFades)
    {
        var project = CreateProject();
        project.Tracks[0].Clips[1].FadeInTransition = null;
        if (unequalFades) project.Tracks[0].Clips[0].FadeOutFrames--;
        else project.Tracks[0].Clips.Add(new TimelineClip { MediaId = project.Media[0].Id, StartFrame = 25, DurationFrames = 10 });
        Assert.All(TimelineEvaluator.Evaluate(project, 15).Video, layer => Assert.Null(layer.Dissolve));
    }

    [Fact]
    public void MatchingFadeLengthsWithoutMatchingOverlapAreNotAPair()
    {
        var project = CreateProject();
        var clips = project.Tracks[0].Clips;
        clips[1].FadeInTransition = null;
        clips[0].FadeOutFrames = clips[1].FadeInFrames = 15;
        Assert.All(TimelineEvaluator.Evaluate(project, 20).Video, layer => Assert.Null(layer.Dissolve));
    }

    private static ProjectDocument CreateProject()
    {
        var asset = new MediaAsset { Path = "openrend://color?hex=#FF0000", DurationSeconds = 5, HasVideo = true };
        var editor = new TimelineEditor(new ProjectDocument());
        editor.AddMedia(asset);
        var track = editor.AddTrack(TrackKind.Video);
        var outgoing = editor.PlaceClip(asset.Id, track.Id, 0, 30);
        var incoming = editor.PlaceClip(asset.Id, track.Id, 10, 30);
        editor.ApplyDissolve(outgoing.Id, incoming.Id);
        return editor.Project;
    }
}
