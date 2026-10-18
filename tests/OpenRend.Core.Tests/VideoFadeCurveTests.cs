using System.Text.Json.Nodes;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class VideoFadeCurveTests
{
    private static (TimelineEditor Editor, TimelineClip Clip) Create(VideoFadeCurve curve)
    {
        var media = new MediaAsset { Path = Path.GetFullPath("fade.avi"), HasVideo = true, HasAudio = true, DurationSeconds = 10 };
        var clip = new TimelineClip { MediaId = media.Id, DurationFrames = 210, FadeInFrames = 30, FadeOutFrames = 30,
            FadeInCurve = curve, FadeOutCurve = curve };
        return (new(new() { Media = [media], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] }), clip);
    }

    [Theory]
    [InlineData(VideoFadeCurve.Linear, 68, 127, 187)]
    [InlineData(VideoFadeCurve.Fast, 118, 191, 237)]
    [InlineData(VideoFadeCurve.Slow, 18, 64, 137)]
    [InlineData(VideoFadeCurve.Smooth, 45, 128, 210)]
    [InlineData(VideoFadeCurve.Sharp, 115, 128, 140)]
    public void HeadAndTailFollowDistinctDirections(VideoFadeCurve curve, int early, int middle, int late)
    {
        var (editor, clip) = Create(curve);
        var points = new[] { 8, 15, 22 };
        var alpha = new[] { early, middle, late };
        for (var i = 0; i < points.Length; i++)
        {
            var head = TimelineEvaluator.Evaluate(editor.Project, points[i]).Video.Single().Opacity;
            var tail = TimelineEvaluator.Evaluate(editor.Project, 180 + points[i]).Video.Single().Opacity;
            Assert.InRange(Math.Abs(head * 255 - alpha[i]), 0, 1);
            Assert.Equal(1 - head, tail, 12);
        }
        Assert.Equal(0, TimelineEvaluator.VideoFadeAt(clip, 0));
        Assert.Equal(1, TimelineEvaluator.VideoFadeAt(clip, 30));
        Assert.Equal(1, TimelineEvaluator.VideoFadeAt(clip, 180));
        Assert.Equal(0, TimelineEvaluator.VideoFadeAt(clip, 210));
        Assert.Empty(TimelineEvaluator.Evaluate(editor.Project, 210).Video);
    }

    [Theory]
    [InlineData(VideoFadeCurve.Linear)]
    [InlineData(VideoFadeCurve.Fast)]
    [InlineData(VideoFadeCurve.Slow)]
    [InlineData(VideoFadeCurve.Smooth)]
    [InlineData(VideoFadeCurve.Sharp)]
    public void SplitsKeepTheWholeVideoEnvelope(VideoFadeCurve curve)
    {
        var (editor, clip) = Create(curve);
        var before = Enumerable.Range(0, 210).Select(frame => TimelineEvaluator.Evaluate(editor.Project, frame).Video.Single().Opacity).ToArray();
        var middle = editor.SplitClip(clip.Id, 11);
        editor.SplitClip(middle.Id, 195);
        for (var frame = 0; frame < 210; frame++)
            Assert.Equal(before[frame], TimelineEvaluator.Evaluate(editor.Project, frame).Video.Single().Opacity, 12);
        Assert.All(editor.Project.Tracks[0].Clips, part => { Assert.Equal(curve, part.FadeInCurve); Assert.Equal(curve, part.FadeOutCurve); });
        Assert.True(editor.Undo()); Assert.True(editor.Undo());
        Assert.Single(editor.Project.Tracks[0].Clips);
    }

    [Fact]
    public async Task ProjectRoundTripKeepsShapesAndLegacyProjectsDefaultToLinear()
    {
        var (editor, _) = Create(VideoFadeCurve.Fast);
        var folder = Path.Combine(Path.GetTempPath(), "OpenRendTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "curves.openrend"); await ProjectStore.SaveAsync(editor.Project, path);
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            Assert.Equal(8, (int)json["Version"]!);
            var restored = (await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0];
            Assert.Equal(VideoFadeCurve.Fast, restored.FadeInCurve); Assert.Equal(VideoFadeCurve.Fast, restored.FadeOutCurve);
            var clip = json["Project"]!["Tracks"]![0]!["Clips"]![0]!.AsObject();
            clip.Remove("FadeInCurve"); clip.Remove("FadeOutCurve"); json["Version"] = 7;
            await File.WriteAllTextAsync(path, json.ToJsonString());
            restored = (await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0];
            Assert.Equal(VideoFadeCurve.Linear, restored.FadeInCurve); Assert.Equal(VideoFadeCurve.Linear, restored.FadeOutCurve);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void AudioKeepsItsExistingLinearEnvelopeAndRejectsVideoCurveMetadata()
    {
        var (editor, clip) = Create(VideoFadeCurve.Fast);
        var audio = new TimelineTrack { Kind = TrackKind.Audio, Clips = [clip.DeepClone()] };
        audio.Clips[0].Id = Guid.NewGuid(); editor.Project.Tracks.Add(audio);
        Assert.Equal(.5, TimelineEvaluator.Evaluate(editor.Project, 15).Audio.Single().Gain);
        Assert.Equal(.5, TimelineEvaluator.AudioGains(audio, audio.Clips[0], 15).Left);
        Assert.Throws<InvalidDataException>(editor.Project.Validate);
        audio.Clips[0].FadeInCurve = audio.Clips[0].FadeOutCurve = VideoFadeCurve.Linear;
        editor.Project.Validate();
        clip.FadeOutCurve = (VideoFadeCurve)99;
        Assert.Throws<InvalidDataException>(editor.Project.Validate);
    }

    [Fact]
    public void SetVideoFadeCurveChangesHeadAndTailWithUndoRedo()
    {
        var (editor, clip) = Create(VideoFadeCurve.Linear);
        editor.SetVideoFadeCurve(clip.Id, VideoFadeCurve.Fast, fadeOut: false);
        Assert.Equal(VideoFadeCurve.Fast, editor.Project.Tracks[0].Clips[0].FadeInCurve);
        Assert.Equal(VideoFadeCurve.Linear, editor.Project.Tracks[0].Clips[0].FadeOutCurve);

        editor.SetVideoFadeCurve(clip.Id, VideoFadeCurve.Slow, fadeOut: true);
        Assert.Equal(VideoFadeCurve.Fast, editor.Project.Tracks[0].Clips[0].FadeInCurve);
        Assert.Equal(VideoFadeCurve.Slow, editor.Project.Tracks[0].Clips[0].FadeOutCurve);

        Assert.True(editor.Undo());
        Assert.Equal(VideoFadeCurve.Linear, editor.Project.Tracks[0].Clips[0].FadeOutCurve);
        Assert.True(editor.Undo());
        Assert.Equal(VideoFadeCurve.Linear, editor.Project.Tracks[0].Clips[0].FadeInCurve);

        Assert.True(editor.Redo());
        Assert.Equal(VideoFadeCurve.Fast, editor.Project.Tracks[0].Clips[0].FadeInCurve);
        Assert.True(editor.Redo());
        Assert.Equal(VideoFadeCurve.Slow, editor.Project.Tracks[0].Clips[0].FadeOutCurve);
    }

    [Fact]
    public void SetVideoFadeCurvePreservesSplitEnvelopes()
    {
        var (editor, clip) = Create(VideoFadeCurve.Linear);
        var middle = editor.SplitClip(clip.Id, 50);
        var right = editor.SplitClip(middle.Id, 150);

        editor.SetVideoFadeCurve(clip.Id, VideoFadeCurve.Smooth, fadeOut: false);
        Assert.All(editor.Project.Tracks[0].Clips, c => Assert.Equal(VideoFadeCurve.Smooth, c.FadeInCurve));

        editor.SetVideoFadeCurve(right.Id, VideoFadeCurve.Sharp, fadeOut: true);
        Assert.All(editor.Project.Tracks[0].Clips, c => Assert.Equal(VideoFadeCurve.Sharp, c.FadeOutCurve));

        Assert.True(editor.Undo());
        Assert.All(editor.Project.Tracks[0].Clips, c => Assert.Equal(VideoFadeCurve.Linear, c.FadeOutCurve));
    }

    [Fact]
    public void SetVideoFadeCurveRemovesActiveTransitionAndValidates()
    {
        var (editor, clip) = Create(VideoFadeCurve.Linear);
        clip.FadeInTransition = new VideoTransition();

        editor.SetVideoFadeCurve(clip.Id, VideoFadeCurve.Fast, fadeOut: false);
        Assert.Equal(VideoFadeCurve.Fast, editor.Project.Tracks[0].Clips[0].FadeInCurve);
        Assert.Null(editor.Project.Tracks[0].Clips[0].FadeInTransition);

        Assert.True(editor.Undo());
        Assert.NotNull(editor.Project.Tracks[0].Clips[0].FadeInTransition);
        Assert.Equal(VideoFadeCurve.Linear, editor.Project.Tracks[0].Clips[0].FadeInCurve);

        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetVideoFadeCurve(clip.Id, (VideoFadeCurve)99));

        var audio = new TimelineTrack { Kind = TrackKind.Audio, Clips = [new TimelineClip { Id = Guid.NewGuid(), MediaId = clip.MediaId, DurationFrames = 100 }] };
        editor.Project.Tracks.Add(audio);
        Assert.Throws<InvalidOperationException>(() => editor.SetVideoFadeCurve(audio.Clips[0].Id, VideoFadeCurve.Fast));

        editor.Project.Tracks[0].IsLocked = true;
        Assert.Throws<InvalidOperationException>(() => editor.SetVideoFadeCurve(clip.Id, VideoFadeCurve.Slow));
    }
}
