using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Project;
using OpenRend.UI;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class EditorRegressionTests
{
    [Fact]
    public void SplitWithoutSelectionTargetsTheTopEventOnTheFocusedTrack()
    {
        using var media = new MediaService(); using var renderer = new TimelineRenderService(media);
        var vm = new EditorViewModel(media, renderer, new ExportService(renderer));
        var asset = new MediaAsset { Path = "source.mp4", DurationSeconds = 5, HasVideo = true };
        var earlier = new TimelineClip { MediaId = asset.Id, DurationFrames = 80 };
        var target = new TimelineClip { MediaId = asset.Id, StartFrame = 20, DurationFrames = 60, SourceInFrame = 10 };
        vm.Replace(new ProjectDocument
        {
            FrameRate = new(30000, 1001), Media = [asset], Tracks =
            [new() { Kind = TrackKind.Video, Clips = [new() { MediaId = asset.Id, DurationFrames = 90 }] },
             new() { Kind = TrackKind.Video, Clips = [earlier, target] }]
        }, "split.openrend");

        vm.Position = 30;
        vm.Split(focusedTrackIndex: 1);

        Assert.Single(vm.Project.Tracks[0].Clips);
        Assert.Equal(80, earlier.DurationFrames);
        Assert.Equal(10, target.DurationFrames);
        var right = Assert.Single(vm.Project.Tracks[1].Clips, clip => clip.StartFrame == 30);
        Assert.Equal((20L, 50L), (right.SourceInFrame, right.DurationFrames));
        Assert.Empty(vm.Selection);
        Assert.True(vm.Editor.Undo());
        Assert.Equal(2, vm.Project.Tracks[1].Clips.Count);
        Assert.False(vm.Editor.CanUndo);

        vm.Position = 0;
        vm.Split(focusedTrackIndex: 1);
        Assert.False(vm.Editor.CanUndo);
    }

    [Fact]
    public void OneFrameClipWithCachedThumbnailRendersAtNormalZoom()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var media = new MediaAsset { Path = "generated.mp4", DurationSeconds = 1, HasVideo = true, Width = 1, Height = 1 };
                var timeline = new TimelineView
                {
                    Project = new ProjectDocument { Media = [media], Tracks = [new TimelineTrack { Clips = [new TimelineClip { MediaId = media.Id, DurationFrames = 1 }] }] }
                };
                var thumbnail = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255 }, 4); thumbnail.Freeze();
                timeline.Thumbnails[media.Id] = thumbnail;
                timeline.Measure(new Size(800, 250)); timeline.Arrange(new Rect(0, 0, 800, 250)); timeline.UpdateLayout();
                var target = new RenderTargetBitmap(800, 250, 96, 96, PixelFormats.Pbgra32); target.Render(timeline);
                var pixels = new byte[800 * 250 * 4]; target.CopyPixels(pixels, 800 * 4, 0);
                Assert.Contains(pixels, p => p > 0);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        Assert.Null(failure);
    }

    [Fact]
    public async Task SaveDoesNotClearNewerEditsOrAssignPathToReplacementProject()
    {
        using var media = new MediaService(); using var renderer = new TimelineRenderService(media);
        var vm = new EditorViewModel(media, renderer, new ExportService(renderer));
        vm.Editor.Execute(p =>
        {
            for (var i = 0; i < 3000; i++) p.Media.Add(new MediaAsset { Path = Path.Combine(Path.GetTempPath(), new string('a', 120) + i + ".mp4"), DurationSeconds = 1, HasVideo = true });
        });
        var path = Path.Combine(Path.GetTempPath(), $"openrend-save-{Guid.NewGuid():N}.openrend");
        try
        {
            var save = vm.SaveAsync(path);
            Assert.False(save.IsCompleted, "Fixture must force asynchronous file I/O.");
            vm.Editor.AddTrack(TrackKind.Audio);
            await save;
            Assert.True(vm.Dirty);
            Assert.Equal(2, (await ProjectStore.LoadAsync(path)).Tracks.Count);
            var previousProject = vm.Project;
            save = vm.SaveAsync(path);
            Assert.False(save.IsCompleted);
            vm.NewProject(); await save;
            Assert.NotSame(previousProject, vm.Project);
            Assert.Null(vm.ProjectPath); Assert.False(vm.Dirty);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task OverlappingSavesKeepTheLatestRequestedRevisionOnDisk()
    {
        using var media = new MediaService(); using var renderer = new TimelineRenderService(media);
        var vm = new EditorViewModel(media, renderer, new ExportService(renderer));
        vm.Editor.Execute(p => p.Name = new string('a', 2_000_000));
        var path = Path.Combine(Path.GetTempPath(), $"openrend-save-order-{Guid.NewGuid():N}.openrend");
        try
        {
            var earlier = vm.SaveAsync(path);
            Assert.False(earlier.IsCompleted);
            vm.Editor.Execute(p => p.Name = "Latest requested revision");
            var latest = vm.SaveAsync(path);
            await Task.WhenAll(earlier, latest);
            Assert.Equal("Latest requested revision", (await ProjectStore.LoadAsync(path)).Name);
            Assert.False(vm.Dirty);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void UndoRelinkRebindsRetainedLibraryItemAndRecoveryNeedsSaving()
    {
        using var media = new MediaService(); using var renderer = new TimelineRenderService(media);
        var vm = new EditorViewModel(media, renderer, new ExportService(renderer));
        var asset = new MediaAsset { Path = "first.mp4", HasVideo = true, DurationSeconds = 1 };
        vm.Editor.AddMedia(asset); var item = Assert.Single(vm.Library);
        vm.Editor.Execute(p => p.Media[0].Path = "second.mp4");
        Assert.Equal("second.mp4", item.Asset.Path);
        vm.Editor.Undo(); Assert.Equal("first.mp4", item.Asset.Path); Assert.Same(vm.Project.Media[0], item.Asset);
        vm.Replace(vm.Project.DeepClone(), null); Assert.True(vm.Dirty);
    }
}
