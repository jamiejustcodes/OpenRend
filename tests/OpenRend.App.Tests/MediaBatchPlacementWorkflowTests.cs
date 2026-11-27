using System.Drawing;
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
    public async Task BatchMediaPlacementUsesTheOverlapPreferenceAcrossTheLibraryAndFileDrops()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "batch.openrend");
        try
        {
            var original = Path.Combine(root, "tests", "native", "fixture.mp4");
            var mediaFolder = Path.Combine(directory, "drop");
            Directory.CreateDirectory(mediaFolder);
            File.Copy(original, Path.Combine(mediaFolder, "first.mp4"));
            File.Copy(original, Path.Combine(mediaFolder, "second.mp4"));
            var first = new MediaAsset { Path = original, DisplayName = "A", DurationSeconds = 3,
                Width = 320, Height = 180, FrameRate = 30, HasVideo = true, HasAudio = true };
            var copied = Path.Combine(directory, "other.mp4");
            File.Copy(original, copied);
            var second = first.DeepClone();
            second.Id = Guid.NewGuid(); second.DisplayName = "B"; second.Path = copied;
            var picture = new TimelineTrack { Kind = TrackKind.Video };
            var sound = new TimelineTrack { Kind = TrackKind.Audio };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1), Media = [first, second], Tracks = [picture, sound]
            }, path);

            using var running = Launch(executable, path, directory);
            var main = running.Window;
            var options = main.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName("Options")));
            Assert.NotNull(options);
            options.Patterns.ExpandCollapse.Pattern.Expand();
            Find(main, "EditingPreferencesMenu").AsMenuItem().Invoke();
            Window? preferences = null;
            WaitFor(() => (preferences = main.ModalWindows.SingleOrDefault(dialog => dialog.Title == "Preferences")) is not null,
                "Editing preferences did not open.");
            Find(preferences!, "OverlapNewMedia").Patterns.Toggle.Pattern.Toggle();
            Find(preferences!, "CutOverlapSeconds").AsTextBox().Text = "0.4";
            Find(preferences!, "EditingPreferencesOk").AsButton().Invoke();
            WaitFor(() => main.ModalWindows.Length == 0, "Saving overlap preferences did not close the dialog.");
            Assert.True(EditingPreferencesStore.Load(Path.Combine(directory, "editing.json")).OverlapNewMedia);

            SelectBrowserTab(main, "Project Media");
            var tiles = Find(main, "MediaList").AsListBox().Items;
            Assert.Equal(2, tiles.Length);
            Mouse.Click(tiles[0].GetClickablePoint());
            using (Keyboard.Pressing(VirtualKeyShort.CONTROL)) Keyboard.Type(VirtualKeyShort.KEY_A);
            WaitFor(() => tiles.All(item => item.Patterns.SelectionItem.Pattern.IsSelected.Value),
                "Ctrl+A did not select every item in Project Media.");
            Mouse.Click(Find(main, "MediaViewDetails").GetClickablePoint());
            var details = Find(main, "MediaDetailsList").AsListBox().Items;
            WaitFor(() => details.All(item => item.Patterns.SelectionItem.Pattern.IsSelected.Value),
                "Changing the media view lost part of the selected batch.");
            Find(main, "AddSelectedMedia").AsButton().Invoke();
            await CheckPlacement(12, 2, [first.Id, second.Id]);
            CaptureBatch("project-media");
            await UndoPlacement();
            Assert.False(Find(main, "UndoButton").IsEnabled);

            Chord(main, VirtualKeyShort.KEY_X);
            DragToTrack(VisibleRowPoint(Find(main, "MediaDetailsList"), details[0]));
            await CheckPlacement(0, 2, [first.Id, second.Id]);
            await UndoPlacement();
            Chord(main, VirtualKeyShort.KEY_X);

            Find(main, "ExplorerTab").AsTabItem().Select();
            EditText(main, "ExplorerPathBox", mediaFolder);
            ListBoxItem[] files = [];
            WaitFor(() => (files = Find(main, "ExplorerList").AsListBox().Items.Where(item => item.Name.EndsWith(".mp4", StringComparison.Ordinal)).ToArray()).Length == 2,
                "Explorer did not show both source files.");
            var explorer = Find(main, "ExplorerList");
            var firstFile = VisibleRowPoint(explorer, files[0]);
            Mouse.Click(firstFile);
            using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
            {
                Wait.UntilInputIsProcessed();
                Mouse.Click(VisibleRowPoint(explorer, files[1]));
                Wait.UntilInputIsProcessed();
            }
            try
            {
                WaitFor(() => files.All(item => item.Patterns.SelectionItem.Pattern.IsSelected.Value),
                    $"Ctrl-click did not select both files in Explorer at {firstFile} and {VisibleRowPoint(explorer, files[1])}.");
            }
            catch { CaptureBatch("explorer-selection-failure"); throw; }
            DragToTrack(firstFile);
            await CheckPlacement(12, 4);
            Assert.Equal(4, (await UndoPlacement()).Media.Count);
            DragToTrack(firstFile);
            await CheckPlacement(12, 4);
            Assert.Equal(4, (await UndoPlacement()).Media.Count);
            running.CloseGracefully(waitForRender: false, useCaptionButton: true);

            async Task CheckPlacement(long fade, int mediaCount, Guid[]? expectedSources = null)
            {
                try
                {
                    WaitFor(() => main.FindFirstDescendant(c => c.ByControlType(ControlType.Button).And(c.ByName("Cut")))?.IsEnabled == true,
                        "The placement did not select the new timeline events.");
                }
                catch { CaptureBatch($"{mediaCount}-{fade}-failure"); throw; }
                var saved = await SaveGrade(main, path);
                Assert.True(mediaCount == saved.Media.Count,
                    $"Expected {mediaCount} media sources. Saved: {string.Join(", ", saved.Media.Select(asset => asset.Name))}.");
                var video = saved.Tracks.Single(track => track.Id == picture.Id).Clips;
                var audio = saved.Tracks.Single(track => track.Id == sound.Id).Clips;
                Assert.Equal(2, video.Count);
                Assert.Equal(video.Select(clip => clip.MediaId), audio.Select(clip => clip.MediaId));
                if (expectedSources is not null) Assert.Equal(expectedSources, video.Select(clip => clip.MediaId));
                else Assert.Equal(new[] { "first.mp4", "second.mp4" }, video.Select(clip => saved.Media.Single(asset => asset.Id == clip.MediaId).Name));
                Assert.Equal(video[0].EndFrame - fade, video[1].StartFrame);
                Assert.Equal(new[] { fade, 0 }, video.Select(clip => clip.FadeOutFrames));
                Assert.Equal(new[] { 0, fade }, video.Select(clip => clip.FadeInFrames));
                Assert.Equal(video.Select(clip => (clip.StartFrame, clip.EndFrame, clip.FadeInFrames, clip.FadeOutFrames, clip.GroupId)),
                    audio.Select(clip => (clip.StartFrame, clip.EndFrame, clip.FadeInFrames, clip.FadeOutFrames, clip.GroupId)));
            }

            async Task<ProjectDocument> UndoPlacement()
            {
                Find(main, "UndoButton").AsButton().Invoke();
                WaitFor(() => main.Title.Contains('*'), "Undo did not edit the saved placement.");
                var restored = await SaveGrade(main, path);
                Assert.All(restored.Tracks, track => Assert.Empty(track.Clips));
                return restored;
            }

            void DragToTrack(Point source)
            {
                var row = Find(main, $"Track_{picture.Id:N}").BoundingRectangle;
                var viewport = Find(main, "Timeline").BoundingRectangle;
                var target = new Point(viewport.Left + viewport.Width / 3, row.Top + row.Height / 2);
                Mouse.MoveTo(source);
                Mouse.Down(MouseButton.Left);
                try
                {
                    Mouse.MoveTo(source.X + 12, source.Y);
                    Wait.UntilInputIsProcessed();
                    Mouse.MoveTo(target);
                }
                finally { Mouse.Up(MouseButton.Left); }
            }

            void CaptureBatch(string view)
            {
                var captureFolder = Path.Combine(root, ".local", "desktop-validation");
                Directory.CreateDirectory(captureFolder);
                using var capture = Capture.Element(main);
                capture.ToFile(Path.Combine(captureFolder, $"batch-placement-{view}.png"));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }

        static Point VisibleRowPoint(AutomationElement list, ListBoxItem item)
        {
            var visible = Rectangle.Intersect(list.BoundingRectangle, item.BoundingRectangle);
            Assert.True(visible.Width >= 52 && visible.Height > 0, "The source row must be visible to start a physical drag.");
            return new Point(visible.Left + 38, visible.Top + visible.Height / 2);
        }
    }
}
