using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.App.Tests;

public sealed partial class EditorWorkflowTests
{
    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task MediaGeneratorBrowserInsertsAndDropsCatalogPresets()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "generators.openrend");
        var asset = new MediaAsset
        {
            Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
            Width = 320, Height = 180, DurationSeconds = 3, FrameRate = 30, HasVideo = true
        };
        var track = new TimelineTrack
        {
            Name = "Target video",
            Kind = TrackKind.Video,
            Clips = [new() { MediaId = asset.Id, Name = "Guide event", StartFrame = 180, DurationFrames = 30 }]
        };
        var lower = new TimelineTrack
        {
            Name = "Drop target",
            Kind = TrackKind.Video,
            Clips = [new() { MediaId = asset.Id, Name = "Drop guide", StartFrame = 15, DurationFrames = 30 }]
        };
        await ProjectStore.SaveAsync(new ProjectDocument
        {
            Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1), Media = [asset],
            Tracks = [new() { Name = "Audio", Kind = TrackKind.Audio }, track, lower]
        }, path);

        try
        {
            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
            Find(window, $"Clip_{track.Clips[0].Id:N}").Patterns.SelectionItem.Pattern.Select();
            Chord(window, VirtualKeyShort.HOME);
            SelectBrowserTab(window, "Media Generator");
            var search = Find(window, "MediaGenSearch").AsTextBox();
            var categories = Find(window, "MediaGenCategories");
            var generators = Find(window, "MediaGenPlugIns");
            var presets = Find(window, "MediaGenPresets");
            var insert = Find(window, "InsertMediaGenButton").AsButton();
            SelectBrowserTab(categories, "Patterns");
            search.Text = "Blue";
            WaitFor(() => !insert.IsEnabled, "A solid color escaped the selected Patterns category.");
            SelectBrowserTab(categories, "Backgrounds");
            WaitFor(() => VisibleNamedItem(presets, "Blue") is not null && insert.IsEnabled, "The Blue preset did not appear.");
            var generator = VisibleNamedItem(generators, "Solid Color");
            Assert.NotNull(generator);
            var star = generator.FindFirstDescendant(c => c.ByName("Favorite Solid Color"));
            Assert.NotNull(star);
            star.Patterns.Toggle.Pattern.Toggle();
            Find(window, "FavoriteMediaGen").Patterns.SelectionItem.Pattern.Select();
            WaitFor(() => VisibleNamedItem(presets, "Blue") is not null, "A favorite did not retain the matching preset.");
            using (var thumbnail = Capture.Element(VisibleNamedItem(presets, "Blue")!))
            {
                var color = thumbnail.Bitmap.GetPixel(thumbnail.Bitmap.Width / 2, thumbnail.Bitmap.Height / 3);
                Assert.Equal((0x1E, 0x88, 0xE5), ((int)color.R, (int)color.G, (int)color.B));
            }
            Assert.DoesNotContain('*', window.Title);

            insert.Invoke();
            WaitFor(() => window.Title.Contains('*'), "Insert did not add a generated event.");
            SaveButton(window).Invoke();
            WaitFor(() => !window.Title.Contains('*'), "The generated event was not saved.");
            var saved = await ProjectStore.LoadAsync(path);
            var solid = Assert.Single(saved.Media, media => media.Generator?.Kind == MediaGeneratorKind.SolidColor);
            Assert.Equal("Blue", solid.Generator!.PresetName);
            Assert.Equal("#1E88E5", solid.Generator.Parameters["hex"]);
            var placed = Assert.Single(saved.Tracks.Single(t => t.Id == track.Id).Clips, clip => clip.MediaId == solid.Id);
            Assert.Equal((0L, 150L), (placed.StartFrame, placed.DurationFrames));

            Find(window, "AllMediaGenPlugins").Patterns.SelectionItem.Pattern.Select();
            search.Text = "Transparent Checkers";
            WaitFor(() => VisibleNamedItem(presets, "Transparent Checkers") is not null, "The transparent checker preset did not match.");
            var dropGuide = Find(window, $"Clip_{lower.Clips[0].Id:N}").BoundingRectangle;
            var preset = VisibleNamedItem(presets, "Transparent Checkers")!;
            Mouse.Drag(preset.GetClickablePoint(), new System.Drawing.Point(dropGuide.Right + dropGuide.Width, dropGuide.Top + dropGuide.Height / 2), MouseButton.Left);
            WaitFor(() => window.Title.Contains('*'), "Dragging the preset did not add a generated event.");
            SaveButton(window).Invoke();
            WaitFor(() => !window.Title.Contains('*'), "The placed checker preset was not saved.");
            saved = await ProjectStore.LoadAsync(path);
            var checkers = Assert.Single(saved.Media, media => media.Generator?.Kind == MediaGeneratorKind.Checkerboard);
            Assert.Equal("Transparent Checkers", checkers.Generator!.PresetName);
            Assert.Equal("#00000000", checkers.Generator.Parameters["color2"]);
            var dropped = Assert.Single(saved.Tracks.Single(t => t.Id == lower.Id).Clips, clip => clip.MediaId == checkers.Id);
            Assert.InRange(dropped.StartFrame, 74, 76);
            Assert.Equal(150, dropped.DurationFrames);

            search.Text = "Titles";
            WaitFor(() => VisibleNamedItem(presets, "Default Title") is not null, "The title entry did not appear.");
            insert.Invoke();
            Window? titles = null;
            WaitFor(() => (titles = window.ModalWindows.SingleOrDefault(dialog => dialog.Title == "Titles & Text")) is not null,
                "The browser did not open the title editor.");
            Assert.NotNull(titles);
            Find(titles, "TitleTextEditor").AsTextBox().Text = "Opening title";
            var acceptTitle = titles.FindFirstDescendant(c => c.ByControlType(ControlType.Button).And(c.ByName("OK")));
            Assert.NotNull(acceptTitle);
            acceptTitle.AsButton().Invoke();
            WaitFor(() => window.ModalWindows.Length == 0, "The title editor did not close.");
            SaveButton(window).Invoke();
            WaitFor(() => !window.Title.Contains('*'), "The title event was not saved.");
            saved = await ProjectStore.LoadAsync(path);
            var title = Assert.Single(saved.Media, media => media.Path.StartsWith("openrend://text?", StringComparison.Ordinal));
            var titleEvent = Assert.Single(saved.Tracks.SelectMany(t => t.Clips), clip => clip.MediaId == title.Id);
            Assert.Equal("Opening title", titleEvent.Name);
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
