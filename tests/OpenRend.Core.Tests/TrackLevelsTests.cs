using System.Text.Json.Nodes;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class TrackLevelsTests
{
    private static (ProjectDocument Project, TimelineTrack Track, TimelineClip Clip) Create(TrackKind kind = TrackKind.Audio)
    {
        var media = new MediaAsset { Path = "levels.mp4", DurationSeconds = 10, HasVideo = true, HasAudio = true };
        var clip = new TimelineClip { MediaId = media.Id, DurationFrames = 120 };
        var track = new TimelineTrack { Kind = kind, Clips = [clip] };
        return (new ProjectDocument { Media = [media], Tracks = [track] }, track, clip);
    }

    [Fact]
    public void TrackLevelsAreOneUndoableTransactionAndClonesOwnTheirState()
    {
        var (project, track, _) = Create();
        var editor = new TimelineEditor(project);
        editor.SetTrackLevels(track.Id, gain: 2, opacity: .35, pan: -.4);
        Assert.Equal((2, .35, -.4), (project.Tracks[0].Gain, project.Tracks[0].Opacity, project.Tracks[0].Pan));
        var clone = project.DeepClone();
        Assert.Equal((2, .35, -.4), (clone.Tracks[0].Gain, clone.Tracks[0].Opacity, clone.Tracks[0].Pan));
        clone.Tracks[0].Gain = .1;
        Assert.Equal(2, project.Tracks[0].Gain);
        Assert.True(editor.Undo());
        Assert.False(editor.CanUndo);
        Assert.Equal((1, 1, 0), (project.Tracks[0].Gain, project.Tracks[0].Opacity, project.Tracks[0].Pan));
        Assert.True(editor.Redo());
        Assert.Equal((2, .35, -.4), (project.Tracks[0].Gain, project.Tracks[0].Opacity, project.Tracks[0].Pan));
    }

    [Fact]
    public void FiniteEditsClampToValidRangesAndRetainUnspecifiedLevels()
    {
        var (project, track, _) = Create();
        var editor = new TimelineEditor(project);
        editor.SetTrackLevels(track.Id, gain: -3, opacity: -2, pan: 4);
        Assert.Equal((0, 0, 1), (track.Gain, track.Opacity, track.Pan));
        editor.SetTrackLevels(track.Id, gain: 4, opacity: 5, pan: -3);
        Assert.Equal((4, 1, -1), (track.Gain, track.Opacity, track.Pan));
        editor.SetTrackLevels(track.Id, pan: .2);
        Assert.Equal((4, 1, .2), (track.Gain, track.Opacity, track.Pan));
        project.Validate();
    }

    [Fact]
    public void UnchangedNormalizedLevelsDoNotCreateUndoEntries()
    {
        var (project, track, _) = Create();
        var editor = new TimelineEditor(project);
        editor.SetTrackLevels(track.Id);
        editor.SetTrackLevels(track.Id, gain: 1, opacity: 2, pan: 0);
        Assert.False(editor.CanUndo);
        Assert.False(editor.CanRedo);
    }

    [Theory]
    [InlineData("gain", double.NaN)]
    [InlineData("gain", double.PositiveInfinity)]
    [InlineData("gain", double.NegativeInfinity)]
    [InlineData("opacity", double.NaN)]
    [InlineData("opacity", double.PositiveInfinity)]
    [InlineData("pan", double.NaN)]
    [InlineData("pan", double.NegativeInfinity)]
    public void NonfiniteEditsAreRejectedWithoutChangingOtherLevels(string field, double value)
    {
        var (project, track, _) = Create();
        var editor = new TimelineEditor(project);
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetTrackLevels(track.Id,
            gain: field == "gain" ? value : 2, opacity: field == "opacity" ? value : .5, pan: field == "pan" ? value : .5));
        Assert.Equal((1, 1, 0), (track.Gain, track.Opacity, track.Pan));
        Assert.False(editor.CanUndo);
    }

    [Theory]
    [InlineData("gain", -1)]
    [InlineData("gain", double.NaN)]
    [InlineData("opacity", -1)]
    [InlineData("opacity", 2)]
    [InlineData("pan", -2)]
    [InlineData("pan", 2)]
    [InlineData("pan", double.PositiveInfinity)]
    public void DirectTransactionsCannotBypassTrackValidation(string field, double value)
    {
        var (project, track, _) = Create();
        var editor = new TimelineEditor(project);
        Assert.Throws<InvalidDataException>(() => editor.Execute(p =>
        {
            if (field == "gain") p.Tracks[0].Gain = value;
            else if (field == "opacity") p.Tracks[0].Opacity = value;
            else p.Tracks[0].Pan = value;
        }));
        Assert.Equal((1, 1, 0), (project.Tracks[0].Gain, project.Tracks[0].Opacity, project.Tracks[0].Pan));
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void LockedTracksRejectLevelChangesAndUnknownTracksAreNotCreated()
    {
        var (project, track, _) = Create();
        track.IsLocked = true;
        var editor = new TimelineEditor(project);
        Assert.Throws<InvalidOperationException>(() => editor.SetTrackLevels(track.Id, gain: .2, opacity: .3, pan: .4));
        Assert.Throws<KeyNotFoundException>(() => editor.SetTrackLevels(Guid.NewGuid(), gain: .2));
        Assert.Equal((1, 1, 0), (track.Gain, track.Opacity, track.Pan));
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void EvaluatedVideoOpacityMultipliesTrackClipAndFadeWithoutChangingDuration()
    {
        var (project, track, clip) = Create(TrackKind.Video);
        track.Opacity = .25; clip.Opacity = .8; clip.FadeInFrames = 60;
        var evaluated = Assert.Single(TimelineEvaluator.Evaluate(project, 30).Video);
        Assert.Equal(.1, evaluated.Opacity, 12);
        Assert.Equal(120, project.DurationFrames);
        track.Opacity = 0;
        Assert.Equal(0, Assert.Single(TimelineEvaluator.Evaluate(project, 30).Video).Opacity);
        Assert.Equal(.8, clip.Opacity);
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(.5, .5, .25, 1)]
    [InlineData(-.5, -.5, 1, .25)]
    [InlineData(.5, -.5, .5, .5)]
    [InlineData(-.5, .5, .5, .5)]
    [InlineData(1, -1, 0, 0)]
    [InlineData(-1, 1, 0, 0)]
    [InlineData(1, 0, 0, 1)]
    [InlineData(0, -1, 1, 0)]
    public void AudioBalanceStagesAndCollapsedEvaluationProduceIdenticalChannelGains(double clipPan, double trackPan, double left, double right)
    {
        var (project, track, clip) = Create();
        track.Gain = 2; clip.Gain = .5; track.Pan = trackPan; clip.Pan = clipPan; clip.FadeInFrames = 60;
        var gains = TimelineEvaluator.AudioGains(track, clip, 30);
        Assert.Equal(.5 * left, gains.Left, 12);
        Assert.Equal(.5 * right, gains.Right, 12);
        var evaluated = Assert.Single(TimelineEvaluator.Evaluate(project, 30).Audio);
        var collapsedLeft = evaluated.Gain * (evaluated.Pan <= 0 ? 1 : 1 - evaluated.Pan);
        var collapsedRight = evaluated.Gain * (evaluated.Pan >= 0 ? 1 : 1 + evaluated.Pan);
        Assert.Equal(gains.Left, collapsedLeft, 12);
        Assert.Equal(gains.Right, collapsedRight, 12);
        Assert.True(double.IsFinite(evaluated.Pan));
    }

    [Fact]
    public void FractionalSampleFadesUseBothTrackAndClipGainsWithoutApplyingVideoOpacityToAudio()
    {
        var (_, track, clip) = Create();
        track.Gain = 1.5; track.Pan = -.5; track.Opacity = 0;
        clip.Gain = .8; clip.Pan = .25; clip.FadeInFrames = 20;
        var gains = TimelineEvaluator.AudioGains(track, clip, 5.5);
        Assert.Equal(.2475, gains.Left, 12);
        Assert.Equal(.165, gains.Right, 12);
    }

    [Fact]
    public async Task TrackLevelsRoundTripAndOlderManifestsKeepUnityDefaults()
    {
        var (project, track, _) = Create();
        track.Gain = 1.75; track.Opacity = .45; track.Pan = -.2;
        var path = Path.Combine(Path.GetTempPath(), $"openrend-track-levels-{Guid.NewGuid():N}.openrend");
        try
        {
            await ProjectStore.SaveAsync(project, path);
            var loaded = await ProjectStore.LoadAsync(path);
            Assert.Equal((1.75, .45, -.2), (loaded.Tracks[0].Gain, loaded.Tracks[0].Opacity, loaded.Tracks[0].Pan));
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            var saved = json["Project"]!["Tracks"]![0]!.AsObject();
            saved.Remove("Gain"); saved.Remove("Opacity"); saved.Remove("Pan");
            await File.WriteAllTextAsync(path, json.ToJsonString());
            loaded = await ProjectStore.LoadAsync(path);
            Assert.Equal((1, 1, 0), (loaded.Tracks[0].Gain, loaded.Tracks[0].Opacity, loaded.Tracks[0].Pan));
        }
        finally { File.Delete(path); }
    }
}
