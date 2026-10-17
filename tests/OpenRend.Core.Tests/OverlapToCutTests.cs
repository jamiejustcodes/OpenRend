using System.Text.Json;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class OverlapToCutTests
{
    [Theory]
    [InlineData(CutTransitionAlignment.BeforeCut, 120, 60)]
    [InlineData(CutTransitionAlignment.CenteredOnCut, 105, 45)]
    [InlineData(CutTransitionAlignment.AfterCut, 90, 30)]
    public async Task ConversionPreservesSurvivingContentAndRoundTrips(CutTransitionAlignment alignment, long cut, long source)
    {
        var editor = Create();
        var pair = editor.Project.Tracks[0].Clips;
        var outgoing = pair[0]; var incoming = pair[1];
        var previous = editor.Project.DeepClone();
        var changes = 0; editor.Changed += (_, _) => changes++;

        editor.ConvertOverlapToCut(outgoing.Id, incoming.Id, alignment);

        Assert.Equal(1, changes);
        Assert.Equal((0L, cut, 0L), (outgoing.StartFrame, outgoing.EndFrame, outgoing.SourceInFrame));
        Assert.Equal((cut, 210L, source), (incoming.StartFrame, incoming.EndFrame, incoming.SourceInFrame));
        Assert.Equal(0, outgoing.FadeOutFrames); Assert.Null(outgoing.FadeOutTransition);
        Assert.Equal(0, incoming.FadeInFrames); Assert.Null(incoming.FadeInTransition);
        Assert.Equal(6, outgoing.FadeInFrames); Assert.Equal(8, incoming.FadeOutFrames);
        Assert.Equal(120L, pair[2].StartFrame);
        Assert.Equal(JsonSerializer.Serialize(previous.Tracks[1]), JsonSerializer.Serialize(editor.Project.Tracks[1]));
        Assert.Equal(previous.Tracks[0].Clips[1].GroupId, incoming.GroupId);
        Assert.Equal(JsonSerializer.Serialize(previous.Tracks[0].Clips[1].VideoEffects), JsonSerializer.Serialize(incoming.VideoEffects));
        // Frames outside the removed overlap retain their source and animated crop positions.
        foreach (var frame in new long[] { 25, 80, 121, 160, 199 })
        {
            var before = TimelineEvaluator.Evaluate(previous, frame).Video.Single(item => item.Clip.Id == (frame < 90 ? outgoing.Id : incoming.Id));
            var after = TimelineEvaluator.Evaluate(editor.Project, frame).Video.Single(item => item.Clip.Id == before.Clip.Id);
            Assert.Equal(before.SourceFrame, after.SourceFrame);
            Assert.Equal(before.Crop, after.Crop);
            Assert.Equal(before.Opacity, after.Opacity);
        }
        Assert.Equal(outgoing.Id, Assert.Single(TimelineEvaluator.Evaluate(editor.Project, cut - 1).Video.Where(item => item.Clip.Id == outgoing.Id || item.Clip.Id == incoming.Id)).Clip.Id);
        Assert.Equal(incoming.Id, Assert.Single(TimelineEvaluator.Evaluate(editor.Project, cut).Video.Where(item => item.Clip.Id == outgoing.Id || item.Clip.Id == incoming.Id)).Clip.Id);
        var converted = JsonSerializer.Serialize(editor.Project);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".openrend");
        try
        {
            await ProjectStore.SaveAsync(editor.Project, path);
            Assert.Equal(converted, JsonSerializer.Serialize(await ProjectStore.LoadAsync(path)));
        }
        finally { File.Delete(path); }
        Assert.True(editor.Undo()); Assert.False(editor.CanUndo);
        Assert.Equal(JsonSerializer.Serialize(previous), JsonSerializer.Serialize(editor.Project));
        Assert.True(editor.Redo()); Assert.Equal(converted, JsonSerializer.Serialize(editor.Project));
    }

    [Theory]
    [InlineData(CutTransitionAlignment.BeforeCut, 120, 60)]
    [InlineData(CutTransitionAlignment.AfterCut, 89, 29)]
    public void OddOverlapSupportsEndpointAlignmentsWithoutAPlugin(CutTransitionAlignment alignment, long cut, long source)
    {
        var editor = Create(31, plain: true);
        var pair = editor.Project.Tracks[0].Clips;
        editor.ConvertOverlapToCut(pair[0].Id, pair[1].Id, alignment);
        Assert.Equal(cut, pair[0].EndFrame); Assert.Equal(cut, pair[1].StartFrame);
        Assert.Equal(source, pair[1].SourceInFrame); Assert.Equal(210, pair[1].EndFrame);
        Assert.Null(Assert.Single(TimelineEvaluator.Evaluate(editor.Project, cut).Video.Where(item => item.Clip.Id == pair[1].Id)).Dissolve);
    }

    [Theory]
    [InlineData("half-frame")]
    [InlineData("locked")]
    [InlineData("three-way")]
    [InlineData("split")]
    [InlineData("mismatched-fade")]
    [InlineData("wrong-track")]
    [InlineData("same-event")]
    [InlineData("invalid-alignment")]
    public void UnsupportedConversionIsAtomicAndKeepsRedo(string scenario)
    {
        var editor = Create(scenario == "half-frame" ? 31 : 30);
        editor.Execute(project => project.Name = "Changed"); Assert.True(editor.Undo());
        var pair = editor.Project.Tracks[0].Clips;
        if (scenario == "locked") editor.Project.Tracks[0].IsLocked = true;
        if (scenario == "three-way") pair.Add(new() { MediaId = pair[0].MediaId, StartFrame = 100, DurationFrames = 3 });
        if (scenario == "split") { pair[1].FadeDurationFrames = 125; pair[1].FadeOffsetFrames = 5; }
        if (scenario == "mismatched-fade") pair[0].FadeOutFrames--;
        var outgoing = pair[0].Id;
        var incoming = scenario == "same-event" ? outgoing : scenario == "wrong-track" ? editor.Project.Tracks[1].Clips[0].Id : pair[1].Id;
        var snapshot = JsonSerializer.Serialize(editor.Project);
        var changes = 0; editor.Changed += (_, _) => changes++;
        var error = Record.Exception(() => editor.ConvertOverlapToCut(outgoing, incoming,
            scenario == "invalid-alignment" ? (CutTransitionAlignment)999 : CutTransitionAlignment.CenteredOnCut));
        Assert.True(error is ArgumentException or InvalidOperationException, error?.ToString() ?? "The invalid conversion was accepted.");
        if (scenario == "half-frame") Assert.Contains("half-frame", error!.Message);
        Assert.Equal(snapshot, JsonSerializer.Serialize(editor.Project));
        Assert.False(editor.CanUndo); Assert.True(editor.CanRedo); Assert.Equal(0, changes);
    }

    private static TimelineEditor Create(long overlap = 30, bool plain = false)
    {
        var asset = new MediaAsset { Path = Path.GetFullPath("generated.mp4"), DurationSeconds = 12, Width = 320, Height = 180, HasVideo = true };
        var group = Guid.NewGuid();
        var outgoing = new TimelineClip { MediaId = asset.Id, DurationFrames = 120, FadeInFrames = 6, FadeOutFrames = overlap };
        var incoming = new TimelineClip { MediaId = asset.Id, StartFrame = 120 - overlap, DurationFrames = 90 + overlap,
            SourceInFrame = 60 - overlap, FadeInFrames = overlap, FadeInTransition = plain ? null : new(), FadeOutFrames = 8, GroupId = group,
            VideoEffects = [VideoEffect.Create(VideoEffectKind.Invert)],
            Crop = new() { Keyframes = [new() { State = CropState.Identity(320, 180) },
                new() { Frame = 150, State = new(160, 90, 130, 80, 20, 160, 90) }] }, CropOffsetFrames = 7 };
        // This grouped event starts at the old overlap end and must not ripple.
        var later = new TimelineClip { MediaId = asset.Id, StartFrame = 120, DurationFrames = 10, GroupId = group };
        // Overlap validation must ignore the other video track.
        var other = new TimelineClip { MediaId = asset.Id, StartFrame = 50, DurationFrames = 180, GroupId = group };
        return new(new() { FrameRate = new(30000, 1001), Media = [asset], Tracks = [
            new() { Kind = TrackKind.Video, Clips = [outgoing, incoming, later] },
            new() { Kind = TrackKind.Video, Clips = [other] }] });
    }
}
