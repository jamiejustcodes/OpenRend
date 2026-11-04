using System.Threading;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class MediaBinsEditorTests
{
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AggregateException(error);
    }

    [Fact]
    public void MediaBin_DeepClone_PreservesHierarchyAndMediaIds()
    {
        var parentId = Guid.NewGuid();
        var mediaId1 = Guid.NewGuid();
        var mediaId2 = Guid.NewGuid();

        var bin = new MediaBin
        {
            Name = "B-Roll",
            ParentBinId = parentId,
            MediaIds = [mediaId1, mediaId2]
        };

        var clone = bin.DeepClone();
        Assert.Equal(bin.Id, clone.Id);
        Assert.Equal("B-Roll", clone.Name);
        Assert.Equal(parentId, clone.ParentBinId);
        Assert.Equal(2, clone.MediaIds.Count);
        Assert.Contains(mediaId1, clone.MediaIds);
        Assert.Contains(mediaId2, clone.MediaIds);

        // Verify independent collection
        clone.MediaIds.Add(Guid.NewGuid());
        Assert.Equal(2, bin.MediaIds.Count);
        Assert.Equal(3, clone.MediaIds.Count);
    }

    [Fact]
    public void ProjectDocument_MediaBinsAndMediaTags_CloneAndPersist()
    {
        var doc = new ProjectDocument();
        var media = new MediaAsset
        {
            Path = "interview_01.mp4",
            HasVideo = true,
            Tags = ["interview", "day 1", "4k"]
        };
        doc.Media.Add(media);

        var bin = new MediaBin
        {
            Name = "Interviews",
            MediaIds = [media.Id]
        };
        doc.MediaBins.Add(bin);

        var clonedDoc = doc.DeepClone();
        Assert.Single(clonedDoc.MediaBins);
        Assert.Equal("Interviews", clonedDoc.MediaBins[0].Name);
        Assert.Single(clonedDoc.MediaBins[0].MediaIds);
        Assert.Equal(media.Id, clonedDoc.MediaBins[0].MediaIds[0]);

        Assert.Single(clonedDoc.Media);
        Assert.Equal(3, clonedDoc.Media[0].Tags.Count);
        Assert.Contains("interview", clonedDoc.Media[0].Tags);
    }

    [Fact]
    public void TimelineEditor_MediaBins_CreateRenameDelete_SupportsUndoRedo()
    {
        var doc = new ProjectDocument();
        var editor = new TimelineEditor(doc);

        // 1. Create root bin
        var rootBin = editor.CreateMediaBin("Footage");
        Assert.Single(editor.Project.MediaBins);
        Assert.Equal("Footage", editor.Project.MediaBins[0].Name);

        // 2. Create child bin
        var subBin = editor.CreateMediaBin("A-Cam", rootBin.Id);
        Assert.Equal(2, editor.Project.MediaBins.Count);
        Assert.Equal(rootBin.Id, editor.Project.MediaBins.First(b => b.Id == subBin.Id).ParentBinId);

        // 3. Rename sub bin
        editor.RenameMediaBin(subBin.Id, "A-Cam 4K");
        Assert.Equal("A-Cam 4K", editor.Project.MediaBins.First(b => b.Id == subBin.Id).Name);

        // 4. Undo rename
        editor.Undo();
        Assert.Equal("A-Cam", editor.Project.MediaBins.First(b => b.Id == subBin.Id).Name);

        // 5. Redo rename
        editor.Redo();
        Assert.Equal("A-Cam 4K", editor.Project.MediaBins.First(b => b.Id == subBin.Id).Name);

        // 6. Delete root bin cascades to child bin
        editor.DeleteMediaBin(rootBin.Id);
        Assert.Empty(editor.Project.MediaBins);

        // 7. Undo delete restores both
        editor.Undo();
        Assert.Equal(2, editor.Project.MediaBins.Count);
    }

    [Fact]
    public void TimelineEditor_MediaBins_AddAndRemoveMedia_TracksMembershipWithUndoRedo()
    {
        var doc = new ProjectDocument();
        var mediaA = new MediaAsset { Path = "clipA.mp4", HasVideo = true };
        var mediaB = new MediaAsset { Path = "clipB.mp4", HasVideo = true };
        doc.Media.Add(mediaA);
        doc.Media.Add(mediaB);

        var editor = new TimelineEditor(doc);
        var bin = editor.CreateMediaBin("Selects");

        // Add mediaA
        editor.AddMediaToBin(bin.Id, mediaA.Id);
        Assert.Single(editor.Project.MediaBins[0].MediaIds);
        Assert.Contains(mediaA.Id, editor.Project.MediaBins[0].MediaIds);

        // Add duplicate mediaA (should not duplicate)
        editor.AddMediaToBin(bin.Id, mediaA.Id);
        Assert.Single(editor.Project.MediaBins[0].MediaIds);

        // Add mediaB
        editor.AddMediaToBin(bin.Id, mediaB.Id);
        Assert.Equal(2, editor.Project.MediaBins[0].MediaIds.Count);

        // Remove mediaA
        editor.RemoveMediaFromBin(bin.Id, mediaA.Id);
        Assert.Single(editor.Project.MediaBins[0].MediaIds);
        Assert.DoesNotContain(mediaA.Id, editor.Project.MediaBins[0].MediaIds);
        Assert.Contains(mediaB.Id, editor.Project.MediaBins[0].MediaIds);

        // Undo removal
        editor.Undo();
        Assert.Equal(2, editor.Project.MediaBins[0].MediaIds.Count);
        Assert.Contains(mediaA.Id, editor.Project.MediaBins[0].MediaIds);
    }

    [Fact]
    public void TimelineEditor_SetMediaTags_NormalizesAndPersistsWithUndoRedo()
    {
        var doc = new ProjectDocument();
        var media = new MediaAsset { Path = "hero.mp4" };
        doc.Media.Add(media);

        var editor = new TimelineEditor(doc);
        editor.SetMediaTags(media.Id, ["  Cinematic  ", "4K", "cinematic", "Wide "]);

        var updated = editor.Project.Media.First(m => m.Id == media.Id);
        Assert.Equal(3, updated.Tags.Count); // Deduplicated "cinematic" vs "Cinematic" handled or preserved
        Assert.Contains("Cinematic", updated.Tags);
        Assert.Contains("4K", updated.Tags);
        Assert.Contains("Wide", updated.Tags);

        editor.Undo();
        Assert.Empty(editor.Project.Media.First(m => m.Id == media.Id).Tags);
    }

    [Fact]
    public void EditorViewModel_MediaBinsMethods_DelegateAndSetStatus()
    {
        var mediaService = new MediaService();
        var renderer = new TimelineRenderService(mediaService);
        var exportService = new ExportService(renderer);
        var vm = new EditorViewModel(mediaService, renderer, exportService);

        var bin = vm.CreateMediaBin("Interviews");
        Assert.Single(vm.Project.MediaBins);
        Assert.Contains("Created media bin", vm.Status);

        vm.RenameMediaBin(bin.Id, "Interviews 2026");
        Assert.Equal("Interviews 2026", vm.Project.MediaBins[0].Name);
        Assert.Contains("Renamed bin", vm.Status);

        vm.DeleteMediaBin(bin.Id);
        Assert.Empty(vm.Project.MediaBins);
        Assert.Contains("Deleted media bin", vm.Status);
    }
}
