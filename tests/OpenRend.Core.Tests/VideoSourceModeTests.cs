using System.Text.Json.Nodes;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class VideoSourceModeTests
{
    private static (TimelineEditor Editor, TimelineClip Clip) Create(VideoSourceMode mode = VideoSourceMode.Bounded,
        double sourceDuration = 8, long sourcePhase = 0, long duration = 20)
    {
        var media = new MediaAsset { Path = Path.GetFullPath("source.mp4"), HasVideo = true, HasAudio = true, DurationSeconds = sourceDuration };
        var clip = new TimelineClip { MediaId = media.Id, StartFrame = 10, SourceInFrame = sourcePhase, DurationFrames = duration, VideoSourceMode = mode };
        var project = new ProjectDocument { FrameRate = new(30, 1), Media = [media], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] };
        return (new(project), clip);
    }

    [Theory]
    [InlineData(VideoSourceMode.Bounded, 0, 20)]
    [InlineData(VideoSourceMode.Loop, -7, 300)]
    [InlineData(VideoSourceMode.HoldLastFrame, 400, 300)]
    public async Task SchemaSevenPreservesSourceModesAndSignedLoopPhase(VideoSourceMode mode, long phase, long duration)
    {
        var (editor, clip) = Create(mode, sourcePhase: phase, duration: duration);
        var folder = Path.Combine(Path.GetTempPath(), "OpenRendTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "source-mode.openrend"); await ProjectStore.SaveAsync(editor.Project, path);
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            Assert.Equal(7, (int)json["Version"]!);
            var restored = (await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0];
            Assert.Equal(mode, restored.VideoSourceMode); Assert.Equal(phase, restored.SourceInFrame); Assert.Equal(duration, restored.DurationFrames);
            var clone = editor.Project.DeepClone(); clone.Tracks[0].Clips[0].VideoSourceMode = VideoSourceMode.Bounded;
            Assert.Equal(mode, clip.VideoSourceMode);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task LegacyProjectWithoutTheFieldKeepsBoundedPlayback()
    {
        var (editor, _) = Create();
        var folder = Path.Combine(Path.GetTempPath(), "OpenRendTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "legacy.openrend"); await ProjectStore.SaveAsync(editor.Project, path);
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!; json["Version"] = 6;
            json["Project"]!["Tracks"]![0]!["Clips"]![0]!.AsObject().Remove("VideoSourceMode");
            await File.WriteAllTextAsync(path, json.ToJsonString());
            Assert.Equal(VideoSourceMode.Bounded, (await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0].VideoSourceMode);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(30001)]
    public void RationalLoopMappingUsesActualSecondsRatherThanRoundedFramePeriods(long phase)
    {
        var rate = new Rational(30000, 1001); const double duration = .95;
        decimal time = (decimal)phase * 1001 / 30000;
        decimal expected = time % .95m; if (expected < 0) expected += .95m;
        Assert.Equal((double)expected, VideoSourceTime.Seconds(rate, phase, duration, VideoSourceMode.Loop), 12);
        Assert.Equal(Math.Clamp(rate.Seconds(phase), 0, duration), VideoSourceTime.Seconds(rate, phase, duration, VideoSourceMode.HoldLastFrame), 12);
        Assert.Equal(rate.Seconds(phase), VideoSourceTime.Seconds(rate, phase, 0, VideoSourceMode.Bounded));
    }

    [Fact]
    public void EvaluatorMapsOnlyVideoAndRetainsContinuousSignedLoopPhase()
    {
        var (editor, clip) = Create(VideoSourceMode.Loop, .95, -1, 60);
        editor.Project.FrameRate = new(30000, 1001);
        var atStart = TimelineEvaluator.Evaluate(editor.Project, clip.StartFrame).Video.Single();
        Assert.Equal(-1, atStart.SourceFrame);
        Assert.Equal(VideoSourceTime.Seconds(editor.Project.FrameRate, -1, .95, VideoSourceMode.Loop), atStart.SourceTimeSeconds);
        var asset = editor.Project.Media[0];
        editor.Project.Tracks.Add(new() { Kind = TrackKind.Audio, Clips = [new() { MediaId = asset.Id, DurationFrames = 10 }] });
        Assert.Null(TimelineEvaluator.Evaluate(editor.Project, 0).Audio.Single().SourceTimeSeconds);
    }

    [Theory]
    [InlineData("audio")]
    [InlineData("generator")]
    [InlineData("enum")]
    [InlineData("unknown-duration")]
    public void ProjectRejectsUnsupportedSourceModeStates(string invalid)
    {
        var (editor, clip) = Create(); clip.VideoSourceMode = VideoSourceMode.Loop;
        if (invalid == "audio") editor.Project.Tracks[0].Kind = TrackKind.Audio;
        if (invalid == "generator") editor.Project.Media[0].Path = "openrend://color?hex=#FF0000";
        if (invalid == "enum") clip.VideoSourceMode = (VideoSourceMode)99;
        if (invalid == "unknown-duration") editor.Project.Media[0].DurationSeconds = 0;
        Assert.Throws<InvalidDataException>(editor.Project.Validate);
    }

    [Fact]
    public void ModeChangeIsUndoableAndSameModeDoesNotAddHistory()
    {
        var (editor, clip) = Create(); editor.SetVideoSourceMode(clip.Id, VideoSourceMode.Bounded); Assert.False(editor.CanUndo);
        editor.SetVideoSourceMode(clip.Id, VideoSourceMode.Loop); Assert.Equal(VideoSourceMode.Loop, clip.VideoSourceMode);
        Assert.True(editor.Undo()); Assert.Equal(VideoSourceMode.Bounded, editor.Project.Tracks[0].Clips[0].VideoSourceMode); Assert.False(editor.CanUndo);
        Assert.True(editor.Redo()); Assert.Equal(VideoSourceMode.Loop, editor.Project.Tracks[0].Clips[0].VideoSourceMode);
    }

    [Fact]
    public void RemovingLoopNormalizesRepresentablePhaseWithoutMovingCropAnimation()
    {
        var (editor, clip) = Create(VideoSourceMode.Loop, 8, -7, 60);
        clip.Crop = new() { Keyframes = [new() { Frame = 0, State = CropState.Identity(640, 360) }] }; clip.CropOffsetFrames = 12;
        editor.SetVideoSourceMode(clip.Id, VideoSourceMode.HoldLastFrame);
        Assert.Equal(233, clip.SourceInFrame); Assert.Equal(12, clip.CropOffsetFrames); Assert.Equal(0, clip.Crop.Keyframes[0].Frame);
        Assert.Equal(VideoSourceMode.HoldLastFrame, clip.VideoSourceMode);
        Assert.True(editor.Undo()); var restored = editor.Project.Tracks[0].Clips[0]; Assert.Equal(-7, restored.SourceInFrame); Assert.Equal(12, restored.CropOffsetFrames);
        Assert.True(editor.Redo()); Assert.Equal(233, editor.Project.Tracks[0].Clips[0].SourceInFrame);
    }

    [Fact]
    public void RemovingAFractionalLoopPhaseRejectsAtomicallyAndPreservesRedo()
    {
        var (editor, clip) = Create(VideoSourceMode.Loop, .95, -1, 60);
        editor.Execute(_ => clip.Name = "changed"); Assert.True(editor.Undo()); Assert.True(editor.CanRedo);
        var error = Assert.Throws<InvalidOperationException>(() => editor.SetVideoSourceMode(clip.Id, VideoSourceMode.HoldLastFrame));
        Assert.Contains("fractional", error.Message, StringComparison.OrdinalIgnoreCase);
        var unchanged = editor.Project.Tracks[0].Clips[0]; Assert.Equal(-1, unchanged.SourceInFrame); Assert.Equal(VideoSourceMode.Loop, unchanged.VideoSourceMode);
        Assert.False(editor.CanUndo); Assert.True(editor.CanRedo);
    }

    [Fact]
    public void InvalidLockedAudioGeneratorAndBoundedRangeChangesFailWithoutEditing()
    {
        var (editor, clip) = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetVideoSourceMode(clip.Id, (VideoSourceMode)99));
        editor.Project.Tracks[0].IsLocked = true;
        Assert.Throws<InvalidOperationException>(() => editor.SetVideoSourceMode(clip.Id, VideoSourceMode.Loop));
        editor.Project.Tracks[0].IsLocked = false; editor.Project.Tracks[0].Kind = TrackKind.Audio;
        Assert.Throws<InvalidOperationException>(() => editor.SetVideoSourceMode(clip.Id, VideoSourceMode.Loop));
        editor.Project.Tracks[0].Kind = TrackKind.Video; editor.Project.Media[0].Path = "openrend://color?hex=#FF0000";
        Assert.Throws<InvalidOperationException>(() => editor.SetVideoSourceMode(clip.Id, VideoSourceMode.Loop));
        Assert.Equal(VideoSourceMode.Bounded, clip.VideoSourceMode); Assert.Equal(0, clip.SourceInFrame); Assert.False(editor.CanUndo);
        var (extended, longClip) = Create(VideoSourceMode.HoldLastFrame, 8, 230, 30);
        Assert.Throws<ArgumentOutOfRangeException>(() => extended.SetVideoSourceMode(longClip.Id, VideoSourceMode.Bounded));
        Assert.Equal(VideoSourceMode.HoldLastFrame, longClip.VideoSourceMode); Assert.Equal(230, longClip.SourceInFrame); Assert.False(extended.CanUndo);
    }

    [Theory]
    [InlineData(VideoSourceMode.Loop)]
    [InlineData(VideoSourceMode.HoldLastFrame)]
    public void SplitAndTrimRetainTheModeAndSurvivingSourcePhase(VideoSourceMode mode)
    {
        var (editor, clip) = Create(mode, .95, mode == VideoSourceMode.Loop ? -3 : 3, 100);
        var oldPhase = clip.SourceInFrame;
        var right = editor.SplitClip(clip.Id, 50);
        Assert.Equal(mode, clip.VideoSourceMode); Assert.Equal(mode, right.VideoSourceMode); Assert.Equal(oldPhase + 40, right.SourceInFrame);
        editor.TrimClip(right.Id, right.SourceInFrame + 5, 80);
        Assert.Equal(mode, right.VideoSourceMode); Assert.Equal(oldPhase + 45, right.SourceInFrame); Assert.Equal(80, right.DurationFrames);
        editor.Project.Validate();
    }

    [Fact]
    public void AutomaticLoopOverlapKeepsSignedPhaseAndSurvivingContentThroughUndo()
    {
        var (editor, incoming) = Create(VideoSourceMode.Loop, 8, 5, 50); incoming.StartFrame = 100;
        incoming.Crop = new() { Keyframes = [new() { Frame = 0, State = CropState.Identity(640, 360) }] };
        var outgoing = new TimelineClip { MediaId = incoming.MediaId, StartFrame = 40, DurationFrames = 55 };
        editor.Project.Tracks[0].Clips.Insert(0, outgoing);
        var before = TimelineEvaluator.Evaluate(editor.Project, 110).Video.Single();
        Assert.Equal(1, editor.ApplyDissolveToSelection([incoming.Id], 20, false));
        Assert.Equal(-15, incoming.SourceInFrame); Assert.Equal(80, incoming.StartFrame); Assert.Equal(-20, incoming.CropOffsetFrames);
        var after = TimelineEvaluator.Evaluate(editor.Project, 110).Video.Single();
        Assert.Equal(before.SourceFrame, after.SourceFrame); Assert.Equal(before.SourceTimeSeconds, after.SourceTimeSeconds); Assert.Equal(before.Crop, after.Crop);
        Assert.True(editor.Undo()); var restored = editor.Project.Tracks[0].Clips.Single(clip => clip.Id == incoming.Id);
        Assert.Equal(100, restored.StartFrame); Assert.Equal(5, restored.SourceInFrame); Assert.Equal(VideoSourceMode.Loop, restored.VideoSourceMode); Assert.False(editor.CanUndo);
        Assert.True(editor.Redo()); Assert.Equal(-15, editor.Project.Tracks[0].Clips.Single(clip => clip.Id == incoming.Id).SourceInFrame);
    }

    [Theory]
    [InlineData(VideoSourceMode.Bounded)]
    [InlineData(VideoSourceMode.HoldLastFrame)]
    public void AutomaticHeadExtensionClampsHoldButKeepsBoundedRejectionAtomic(VideoSourceMode mode)
    {
        var (editor, incoming) = Create(mode, 8, 5, 50); incoming.StartFrame = 100;
        var outgoing = new TimelineClip { MediaId = incoming.MediaId, StartFrame = 40, DurationFrames = 55 };
        editor.Project.Tracks[0].Clips.Insert(0, outgoing);
        if (mode == VideoSourceMode.Bounded)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => editor.ApplyDissolveToSelection([incoming.Id], 20, false));
            Assert.Equal(100, incoming.StartFrame); Assert.Equal(5, incoming.SourceInFrame); Assert.Equal(0, outgoing.FadeOutFrames); Assert.False(editor.CanUndo);
        }
        else
        {
            Assert.Equal(1, editor.ApplyDissolveToSelection([incoming.Id], 20, false));
            Assert.Equal(80, incoming.StartFrame); Assert.Equal(0, incoming.SourceInFrame); Assert.Equal(50, incoming.DurationFrames);
            Assert.Equal(VideoSourceMode.HoldLastFrame, incoming.VideoSourceMode); Assert.True(editor.Undo()); Assert.False(editor.CanUndo);
        }
    }

    [Fact]
    public void CutHeadExtensionCanRevealNegativeLoopPhaseWithoutMovingExistingSourceOrCropKeys()
    {
        var (editor, incoming) = Create(VideoSourceMode.Loop, 8, 5, 50); incoming.StartFrame = 40;
        incoming.Crop = new() { Keyframes = [new() { Frame = 0, State = CropState.Identity(640, 360) }] };
        var outgoing = new TimelineClip { MediaId = incoming.MediaId, DurationFrames = 40 }; editor.Project.Tracks[0].Clips.Insert(0, outgoing);
        var before = TimelineEvaluator.Evaluate(editor.Project, 50).Video.Single();
        editor.ConvertCutToDissolve(outgoing.Id, incoming.Id, 20, CutTransitionAlignment.BeforeCut);
        Assert.Equal(20, incoming.StartFrame); Assert.Equal(70, incoming.DurationFrames); Assert.Equal(-15, incoming.SourceInFrame); Assert.Equal(-20, incoming.CropOffsetFrames);
        var after = TimelineEvaluator.Evaluate(editor.Project, 50).Video.Single();
        Assert.Equal(before.SourceTimeSeconds, after.SourceTimeSeconds); Assert.Equal(before.Crop, after.Crop);
        editor.Project.Validate(); Assert.True(editor.Undo()); Assert.Equal(40, editor.Project.Tracks[0].Clips.Single(clip => clip.Id == incoming.Id).StartFrame); Assert.False(editor.CanUndo);
    }

    [Theory]
    [InlineData(VideoSourceMode.Bounded)]
    [InlineData(VideoSourceMode.Loop)]
    [InlineData(VideoSourceMode.HoldLastFrame)]
    public void CutTailExtensionUsesTheOutgoingSourceMode(VideoSourceMode mode)
    {
        var (editor, incoming) = Create(sourcePhase: 30, duration: 50); incoming.StartFrame = 40;
        var outgoing = new TimelineClip { MediaId = incoming.MediaId, SourceInFrame = 200, DurationFrames = 40, VideoSourceMode = mode };
        editor.Project.Tracks[0].Clips.Insert(0, outgoing);
        if (mode == VideoSourceMode.Bounded)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => editor.ConvertCutToDissolve(outgoing.Id, incoming.Id, 20, CutTransitionAlignment.AfterCut));
            Assert.Equal(40, outgoing.DurationFrames); Assert.Equal(0, incoming.FadeInFrames); Assert.False(editor.CanUndo);
        }
        else
        {
            editor.ConvertCutToDissolve(outgoing.Id, incoming.Id, 20, CutTransitionAlignment.AfterCut);
            Assert.Equal(60, outgoing.DurationFrames); Assert.Equal(200, outgoing.SourceInFrame); Assert.Equal(mode, outgoing.VideoSourceMode);
            editor.Project.Validate(); Assert.True(editor.Undo()); var restored = editor.Project.Tracks[0].Clips.Single(clip => clip.Id == outgoing.Id);
            Assert.Equal(40, restored.DurationFrames); Assert.Equal(mode, restored.VideoSourceMode); Assert.False(editor.CanUndo);
        }
    }

    [Fact]
    public void CutHeadExtensionClampsTheHoldSourceOffsetAtZero()
    {
        var (editor, incoming) = Create(VideoSourceMode.HoldLastFrame, 8, 5, 50); incoming.StartFrame = 40;
        var outgoing = new TimelineClip { MediaId = incoming.MediaId, DurationFrames = 40 }; editor.Project.Tracks[0].Clips.Insert(0, outgoing);
        editor.ConvertCutToDissolve(outgoing.Id, incoming.Id, 20, CutTransitionAlignment.BeforeCut);
        Assert.Equal(0, incoming.SourceInFrame); Assert.Equal(20, incoming.StartFrame); Assert.Equal(70, incoming.DurationFrames);
        Assert.Equal(VideoSourceMode.HoldLastFrame, incoming.VideoSourceMode); Assert.True(editor.Undo()); Assert.False(editor.CanUndo);
    }
}
