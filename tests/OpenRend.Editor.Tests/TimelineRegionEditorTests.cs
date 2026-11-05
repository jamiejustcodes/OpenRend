using System.Text.Json;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class TimelineRegionEditorTests
{
    private static ProjectDocument CreateTestProject() => new()
    {
        Name = "Test",
        Width = 1920,
        Height = 1080,
        FrameRate = new Rational(30, 1),
        Tracks = [new TimelineTrack { Kind = TrackKind.Video }]
    };

    [Fact]
    public void AddRegion_AssignsIncrementingIndex_AndSortsByStartFrame()
    {
        var editor = new TimelineEditor(CreateTestProject());

        var r1 = editor.AddRegion(100, 200, "Middle");
        var r2 = editor.AddRegion(30, 90, "Beginning");
        var r3 = editor.AddRegion(250, 400, "Ending");

        Assert.Equal(1, r1.Index);
        Assert.Equal(2, r2.Index);
        Assert.Equal(3, r3.Index);

        Assert.Equal(3, editor.Project.Regions.Count);
        Assert.Equal(30, editor.Project.Regions[0].StartFrame);
        Assert.Equal(90, editor.Project.Regions[0].EndFrame);
        Assert.Equal(60, editor.Project.Regions[0].DurationFrames);

        Assert.Equal(100, editor.Project.Regions[1].StartFrame);
        Assert.Equal(250, editor.Project.Regions[2].StartFrame);
    }

    [Fact]
    public void Region_UndoRedo_RestoresPreviousState()
    {
        var editor = new TimelineEditor(CreateTestProject());

        var region = editor.AddRegion(60, 150, "Draft");
        Assert.Single(editor.Project.Regions);

        editor.SetRegionLabel(region.Id, "Final");
        Assert.Equal("Final", editor.Project.Regions[0].Label);

        // Undo label edit
        Assert.True(editor.Undo());
        Assert.Equal("Draft", editor.Project.Regions[0].Label);

        // Undo region creation
        Assert.True(editor.Undo());
        Assert.Empty(editor.Project.Regions);

        // Redo region creation
        Assert.True(editor.Redo());
        Assert.Single(editor.Project.Regions);
        Assert.Equal("Draft", editor.Project.Regions[0].Label);
    }

    [Fact]
    public void Region_ValidationAndJsonRoundtrip_PreservesData()
    {
        var project = CreateTestProject();
        var region = new TimelineRegion
        {
            Index = 1,
            StartFrame = 120,
            EndFrame = 300,
            Label = "Intro Section",
            ColorId = 2
        };
        region.Validate();
        project.Regions.Add(region);
        project.Validate();

        var json = JsonSerializer.Serialize(project);
        var restored = JsonSerializer.Deserialize<ProjectDocument>(json);
        Assert.NotNull(restored);
        restored.Validate();

        Assert.Single(restored.Regions);
        var r = restored.Regions[0];
        Assert.Equal(1, r.Index);
        Assert.Equal(120, r.StartFrame);
        Assert.Equal(300, r.EndFrame);
        Assert.Equal(180, r.DurationFrames);
        Assert.Equal("Intro Section", r.Label);
        Assert.Equal(2, r.ColorId);
    }

    [Fact]
    public void InsertTime_And_DeleteTime_RipplesRegionsAccurately()
    {
        var project = CreateTestProject();
        var editor = new TimelineEditor(project);

        var r1 = editor.AddRegion(30, 90, "First");
        var r2 = editor.AddRegion(150, 250, "Second");
        var r3 = editor.AddRegion(350, 450, "Third");

        // Insert 50 frames at frame 100 with ripple
        editor.InsertTime(100, 50, AutoRippleMode.AllTracksAndMarkers);

        // r1 (30..90) unaffected
        Assert.Equal(30, editor.Project.Regions[0].StartFrame);
        Assert.Equal(90, editor.Project.Regions[0].EndFrame);

        // r2 (150..250) shifted by +50 -> (200..300)
        Assert.Equal(200, editor.Project.Regions[1].StartFrame);
        Assert.Equal(300, editor.Project.Regions[1].EndFrame);

        // r3 (350..450) shifted by +50 -> (400..500)
        Assert.Equal(400, editor.Project.Regions[2].StartFrame);
        Assert.Equal(500, editor.Project.Regions[2].EndFrame);

        // Delete 50 frames from frame 100 with ripple
        editor.DeleteTime(100, 50, AutoRippleMode.AllTracksAndMarkers);

        // r2 and r3 shifted back by -50
        Assert.Equal(150, editor.Project.Regions[1].StartFrame);
        Assert.Equal(250, editor.Project.Regions[1].EndFrame);
        Assert.Equal(350, editor.Project.Regions[2].StartFrame);
        Assert.Equal(450, editor.Project.Regions[2].EndFrame);
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new System.Threading.Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AggregateException(error);
    }

    [Fact]
    public void EditDetails_ShowsRegions_AndFormatsTsv()
    {
        RunSta(() =>
        {
            var mediaService = new OpenRend.Media.MediaService();
            var renderer = new OpenRend.Media.TimelineRenderService(mediaService);
            var vm = new EditorViewModel(mediaService, renderer, new OpenRend.Media.ExportService(renderer));

            vm.Editor.AddRegion(0, 90, "Intro", 0);
            vm.Editor.AddRegion(90, 240, "Main Content", 1);

            var window = new EditDetailsWindow(vm, () => { });

            // Switch Show: to Regions
            window.ShowComboBox.SelectedIndex = 2; // Regions is index 2
            Assert.Equal("Regions", (window.ShowComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString());

            Assert.Equal(2, window.CurrentRegionRows.Count);
            Assert.Equal("Intro", window.CurrentRegionRows[0].Label);
            Assert.Equal("Main Content", window.CurrentRegionRows[1].Label);

            var tsv = window.GetTableText();
            Assert.Contains("Region\tStart\tEnd\tLength\tName\tColor", tsv);
            Assert.Contains("Intro", tsv);
            Assert.Contains("Main Content", tsv);
        });
    }
}
