using System.IO;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class SourceModeClipboardTests
{
    [Theory]
    [InlineData(VideoSourceMode.Loop, 1)]
    [InlineData(VideoSourceMode.Loop, 2)]
    [InlineData(VideoSourceMode.HoldLastFrame, 1)]
    [InlineData(VideoSourceMode.HoldLastFrame, 2)]
    public async Task PasteKeepsExtendedDurationAndSourcePhaseAtMatchingOrDoubledRationalRates(VideoSourceMode mode, int rateMultiplier)
    {
        using var media = new MediaService(); using var renderer = new TimelineRenderService(media, false);
        var vm = new EditorViewModel(media, renderer, new ExportService(renderer));
        var source = new MediaAsset { Path = Path.GetFullPath("clipboard-finite-video.mp4"), DurationSeconds = .95, HasVideo = true };
        var phase = mode == VideoSourceMode.Loop ? -7L : 60L;
        var crop = CropState.Identity(640, 360);
        var clip = new TimelineClip { MediaId = source.Id, StartFrame = 30, SourceInFrame = phase, DurationFrames = 120,
            VideoSourceMode = mode, CropOffsetFrames = 5, Crop = new() { Keyframes = [new() { Frame = 0, State = crop }, new() { Frame = 60, State = crop with { RotationDegrees = 45 } }] } };
        vm.Replace(new ProjectDocument { FrameRate = new(30000, 1001), Media = [source],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] }, "clipboard-source.openrend");
        vm.Selection.Add(clip.Id); vm.Copy(); Assert.False(vm.Dirty); Assert.False(vm.Editor.CanUndo);
        vm.Replace(new ProjectDocument { FrameRate = new(30000 * rateMultiplier, 1001),
            Tracks = [new() { Kind = TrackKind.Video }] }, "clipboard-destination.openrend");
        vm.Paste();
        var pasted = Assert.Single(vm.Project.Tracks.SelectMany(track => track.Clips));
        Assert.Equal(mode, pasted.VideoSourceMode); Assert.Equal(phase * rateMultiplier, pasted.SourceInFrame);
        Assert.Equal(120 * rateMultiplier, pasted.DurationFrames); Assert.Equal(0, pasted.StartFrame);
        Assert.Equal(5 * rateMultiplier, pasted.CropOffsetFrames);
        Assert.Equal(new long[] { 0, 60 * rateMultiplier }, pasted.Crop!.Keyframes.Select(key => key.Frame));
        Assert.NotEqual(clip.Id, pasted.Id); Assert.NotSame(clip.Crop, pasted.Crop); vm.Project.Validate();
        var before = TimelineEvaluator.Evaluate(new ProjectDocument { FrameRate = new(30000, 1001), Media = [source],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] }, 63).Video.Single();
        var after = TimelineEvaluator.Evaluate(vm.Project, 33 * rateMultiplier).Video.Single();
        Assert.Equal(before.SourceTimeSeconds!.Value, after.SourceTimeSeconds!.Value, 12);
        var pastedId = pasted.Id;
        Assert.True(vm.Editor.Undo()); Assert.Empty(vm.Project.Tracks.SelectMany(track => track.Clips)); Assert.Empty(vm.Project.Media); Assert.False(vm.Editor.CanUndo);
        Assert.True(vm.Editor.Redo()); pasted = vm.Project.Tracks.SelectMany(track => track.Clips).Single(item => item.Id == pastedId);
        Assert.Equal(mode, pasted.VideoSourceMode); Assert.Equal(phase * rateMultiplier, pasted.SourceInFrame); Assert.Equal(120 * rateMultiplier, pasted.DurationFrames);
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "source-mode-clipboard.openrend"); await ProjectStore.SaveAsync(vm.Project, path);
            var reopened = (await ProjectStore.LoadAsync(path)).Tracks[0].Clips.Single();
            Assert.Equal(mode, reopened.VideoSourceMode); Assert.Equal(pasted.SourceInFrame, reopened.SourceInFrame); Assert.Equal(pasted.DurationFrames, reopened.DurationFrames);
            Assert.Equal(pasted.CropOffsetFrames, reopened.CropOffsetFrames);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void BoundedClipboardRetainsTheExistingOneFrameSourceEndClamp()
    {
        using var media = new MediaService(); using var renderer = new TimelineRenderService(media, false);
        var vm = new EditorViewModel(media, renderer, new ExportService(renderer));
        var source = new MediaAsset { Path = "bounded-clipboard.mp4", HasVideo = true, DurationSeconds = 1 };
        var clip = new TimelineClip { MediaId = source.Id, SourceInFrame = 59, DurationFrames = 1 };
        vm.Replace(new ProjectDocument { FrameRate = new(60, 1), Media = [source], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] }, "bounded-source.openrend");
        vm.Selection.Add(clip.Id); vm.Copy();
        vm.Replace(new ProjectDocument { FrameRate = new(24, 1), Tracks = [new() { Kind = TrackKind.Video }] }, "bounded-destination.openrend"); vm.Paste();
        var pasted = Assert.Single(vm.Project.Tracks[0].Clips);
        Assert.Equal(VideoSourceMode.Bounded, pasted.VideoSourceMode); Assert.Equal(23, pasted.SourceInFrame); Assert.Equal(1, pasted.DurationFrames); vm.Project.Validate();
    }
}
