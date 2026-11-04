using System.IO;
using OpenRend.App;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class WorkspaceLayoutEditorTests
{
    [Fact]
    public void BuiltInLayouts_ContainsFiveStandardVegasProPresets()
    {
        var builtIns = WorkspaceLayoutManager.CreateBuiltInLayouts();
        Assert.Equal(5, builtIns.Count);

        var defaultLayout = builtIns.First(l => l.Name == "Default Layout");
        Assert.True(defaultLayout.IsBuiltIn);
        Assert.False(defaultLayout.TimelineOnTop);
        Assert.Equal(1, defaultLayout.ToolsTabIndex); // Project Media
        Assert.Equal(0, defaultLayout.PreviewDeckTabIndex); // Video Preview
        Assert.False(defaultLayout.MixingConsoleOpen);
        Assert.False(defaultLayout.VideoScopesOpen);

        var audioMixing = builtIns.First(l => l.Name == "Audio Mixing");
        Assert.True(audioMixing.MixingConsoleOpen);
        Assert.True(audioMixing.WorkspaceHeight < defaultLayout.WorkspaceHeight); // Lower workspace for multi-track audio

        var colorCorrection = builtIns.First(l => l.Name == "Color Correction");
        Assert.True(colorCorrection.VideoScopesOpen);
        Assert.Equal(5, colorCorrection.ToolsTabIndex); // Color Grading tab

        var sourceWindow = builtIns.First(l => l.Name == "Source Window Layout");
        Assert.Equal(1, sourceWindow.PreviewDeckTabIndex); // Trimmer dock tab
        Assert.Equal(0, sourceWindow.ToolsTabIndex); // Explorer tab

        var timelineTop = builtIns.First(l => l.Name == "Timeline Top Layout");
        Assert.True(timelineTop.TimelineOnTop);
    }

    [Fact]
    public void SaveLayout_SerializesAndPersistsToFile_AndLoadsAccurately()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OpenRend_LayoutTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var custom = new WorkspaceLayout
            {
                Name = "Dual Monitor Grading",
                WorkspaceHeight = 550,
                MediaWidth = 420,
                PreviewWidth = 980,
                ToolsTabIndex = 5,
                PreviewDeckTabIndex = 0,
                TimelineOnTop = false,
                VideoScopesOpen = true,
                MixingConsoleOpen = false,
                EditDetailsOpen = true,
                ShortcutSlot = 3
            };

            WorkspaceLayoutManager.SaveLayout(custom, tempDir);

            var loaded = WorkspaceLayoutManager.LoadAllLayouts(tempDir);
            var retrieved = loaded.FirstOrDefault(l => l.Name == "Dual Monitor Grading");
            Assert.NotNull(retrieved);
            Assert.False(retrieved.IsBuiltIn);
            Assert.Equal(550, retrieved.WorkspaceHeight);
            Assert.Equal(420, retrieved.MediaWidth);
            Assert.Equal(980, retrieved.PreviewWidth);
            Assert.Equal(5, retrieved.ToolsTabIndex);
            Assert.True(retrieved.VideoScopesOpen);
            Assert.True(retrieved.EditDetailsOpen);
            Assert.Equal(3, retrieved.ShortcutSlot);

            var slotLayout = WorkspaceLayoutManager.GetLayoutBySlot(3, tempDir);
            Assert.NotNull(slotLayout);
            Assert.Equal("Dual Monitor Grading", slotLayout.Name);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveLayout_ReassignsShortcutSlotExclusively()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OpenRend_LayoutTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var layoutA = new WorkspaceLayout { Name = "Layout Alpha", ShortcutSlot = 2 };
            var layoutB = new WorkspaceLayout { Name = "Layout Beta", ShortcutSlot = 2 };

            WorkspaceLayoutManager.SaveLayout(layoutA, tempDir);
            WorkspaceLayoutManager.SaveLayout(layoutB, tempDir);

            var slot2 = WorkspaceLayoutManager.GetLayoutBySlot(2, tempDir);
            Assert.NotNull(slot2);
            Assert.Equal("Layout Beta", slot2.Name);

            var reloadedA = WorkspaceLayoutManager.LoadAllLayouts(tempDir).First(l => l.Name == "Layout Alpha");
            Assert.Null(reloadedA.ShortcutSlot);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DeleteAndRenameLayout_ModifiesStorageAndSlotIntegrity()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OpenRend_LayoutTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var layout = new WorkspaceLayout { Name = "Initial Layout", ShortcutSlot = 4 };
            WorkspaceLayoutManager.SaveLayout(layout, tempDir);

            var renamed = WorkspaceLayoutManager.RenameLayout("Initial Layout", "Renamed Layout", tempDir);
            Assert.True(renamed);

            var loaded = WorkspaceLayoutManager.LoadAllLayouts(tempDir);
            Assert.DoesNotContain(loaded, l => l.Name == "Initial Layout");
            Assert.Contains(loaded, l => l.Name == "Renamed Layout");

            var deleted = WorkspaceLayoutManager.DeleteLayout("Renamed Layout", tempDir);
            Assert.True(deleted);

            var loadedAfterDelete = WorkspaceLayoutManager.LoadAllLayouts(tempDir);
            Assert.DoesNotContain(loadedAfterDelete, l => l.Name == "Renamed Layout");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Clone_ProducesIndependentInstanceWithCorrectDefaults()
    {
        var original = new WorkspaceLayout
        {
            Name = "Original",
            WorkspaceHeight = 500,
            MediaWidth = 350,
            PreviewWidth = 800,
            ToolsTabIndex = 2,
            PreviewDeckTabIndex = 1,
            TimelineOnTop = true,
            MixingConsoleOpen = true,
            VideoScopesOpen = true,
            EditDetailsOpen = true,
            ShortcutSlot = 5,
            IsBuiltIn = true
        };

        var clone = original.Clone("Cloned Preset");
        Assert.Equal("Cloned Preset", clone.Name);
        Assert.False(clone.IsBuiltIn);
        Assert.Equal(500, clone.WorkspaceHeight);
        Assert.Equal(350, clone.MediaWidth);
        Assert.Equal(800, clone.PreviewWidth);
        Assert.Equal(2, clone.ToolsTabIndex);
        Assert.Equal(1, clone.PreviewDeckTabIndex);
        Assert.True(clone.TimelineOnTop);
        Assert.True(clone.MixingConsoleOpen);
        Assert.True(clone.VideoScopesOpen);
        Assert.True(clone.EditDetailsOpen);
        Assert.Equal(5, clone.ShortcutSlot);
    }
}

