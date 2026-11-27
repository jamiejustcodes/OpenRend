using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class MediaBatchPlacementTests
{
    [Fact]
    public void PlacingLinkedSourcesCreatesOneUndoAndPairedFadesWithoutTripleCoverage()
    {
        using var media = new MediaService();
        using var render = new TimelineRenderService(media, false);
        var vm = new EditorViewModel(media, render, new ExportService(render));
        var assets = new[] { Asset("first", 30), Asset("short", 5), Asset("last", 30) };
        vm.Replace(new ProjectDocument { FrameRate = new(30, 1), Media = [.. assets] }, "batch.openrend");
        vm.PlaceMedia(assets.Select(item => item.Id).ToArray(), null, 12, advanceCursor: true, requestedOverlap: 9);

        var picture = vm.Project.Tracks.Single(track => track.Kind == TrackKind.Video).Clips;
        var sound = vm.Project.Tracks.Single(track => track.Kind == TrackKind.Audio).Clips;
        Assert.Equal(new long[] { 12, 38, 42 }, picture.Select(clip => clip.StartFrame));
        Assert.Equal(new long[] { 0, 4, 1 }, picture.Select(clip => clip.FadeInFrames));
        Assert.Equal(new long[] { 4, 1, 0 }, picture.Select(clip => clip.FadeOutFrames));
        Assert.Equal(picture.Select(clip => (clip.StartFrame, clip.EndFrame, clip.FadeInFrames, clip.FadeOutFrames, clip.GroupId)),
            sound.Select(clip => (clip.StartFrame, clip.EndFrame, clip.FadeInFrames, clip.FadeOutFrames, clip.GroupId)));
        Assert.Equal(72, vm.Position);
        Assert.Equal(6, vm.Selection.Count);
        Assert.All(TimelineEvaluator.Evaluate(vm.Project, 40).Video, item => Assert.NotNull(item.Dissolve));
        Assert.True(vm.Editor.Undo());
        Assert.Empty(vm.Project.Tracks);
        Assert.False(vm.Editor.CanUndo);
        Assert.True(vm.Editor.Redo());
        Assert.Equal(6, vm.Project.Tracks.Sum(track => track.Clips.Count));
    }

    [Fact]
    public void PlacingBelowTheTracksUsesOneNewTrackPerStreamForTheBatch()
    {
        using var media = new MediaService();
        using var render = new TimelineRenderService(media, false);
        var vm = new EditorViewModel(media, render, new ExportService(render));
        var asset = Asset("linked", 30);
        var existing = new TimelineTrack { Kind = TrackKind.Video };
        vm.Replace(new ProjectDocument { Media = [asset], Tracks = [existing] }, "batch.openrend");

        vm.PlaceMedia([asset.Id, asset.Id], null, 2, false, 5, placeOnNewTracks: true);

        Assert.Equal(3, vm.Project.Tracks.Count);
        Assert.Empty(existing.Clips);
        Assert.All(vm.Project.Tracks.Where(track => track != existing), track => Assert.Equal(2, track.Clips.Count));
        Assert.True(vm.Editor.Undo());
        Assert.Equal(existing.Id, Assert.Single(vm.Project.Tracks).Id);
    }

    [Fact]
    public void IncompatibleBatchRollsBackAndKeepsTheExistingSelection()
    {
        using var media = new MediaService();
        using var render = new TimelineRenderService(media, false);
        var vm = new EditorViewModel(media, render, new ExportService(render));
        var video = Asset("picture", 30);
        var audio = new MediaAsset { Path = "tone.wav", DurationSeconds = 1, HasAudio = true };
        var existing = new TimelineClip { MediaId = video.Id, DurationFrames = 15 };
        var track = new TimelineTrack { Kind = TrackKind.Video, Clips = [existing] };
        vm.Replace(new ProjectDocument { Media = [video, audio], Tracks = [track] }, "batch.openrend");
        vm.Selection.Add(existing.Id);

        Assert.Throws<InvalidOperationException>(() => vm.PlaceMedia([video.Id, audio.Id], track.Id, 15, true, 5));
        Assert.Equal(existing.Id, Assert.Single(Assert.Single(vm.Project.Tracks).Clips).Id);
        Assert.Equal(existing.Id, Assert.Single(vm.Selection));
        Assert.False(vm.Editor.CanUndo);
        Assert.False(vm.Dirty);
    }

    private static MediaAsset Asset(string name, int frames) => new()
    {
        Path = name + ".mp4", DurationSeconds = frames / 30d, HasVideo = true, HasAudio = true
    };
}
