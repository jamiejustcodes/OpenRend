using System.Drawing;
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
    public async Task MarkersAndRegionsPersistAndDoubleClickRestoresTheRegionSelection()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "markers-notes.openrend");
        var asset = new MediaAsset
        {
            Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
            Width = 320,
            Height = 180,
            DurationSeconds = 3,
            FrameRate = 30,
            HasVideo = true
        };
        var clip = new TimelineClip { MediaId = asset.Id, Name = "Event 1", DurationFrames = 90 };
        await ProjectStore.SaveAsync(new()
        {
            Name = "UI integration",
            Width = 320,
            Height = 180,
            FrameRate = new(30, 1),
            Media = [asset],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
        }, path);

        try
        {
            using (var running = Launch(executable, path, temporary))
            {
                var window = running.Window;

                Chord(window, VirtualKeyShort.KEY_M);
                Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
                var clipBounds = Find(window, $"Clip_{clip.Id:N}").BoundingRectangle;
                var scale = clipBounds.Height / 67;
                Point OnRegionBar(int frame, double offset) => new(
                    (int)Math.Round(clipBounds.Left + clipBounds.Width * frame / 90.0),
                    (int)Math.Round(clipBounds.Top - (46 - offset) * scale));
                Mouse.Drag(OnRegionBar(18, 12), OnRegionBar(60, 12), MouseButton.Left);
                WaitFor(() => Find(window, "WorkspaceSelectionDuration").Name == "Selection 00:00:01.12",
                    "Dragging the range did not select frames 18 through 59.");
                Chord(window, VirtualKeyShort.KEY_R);
                WaitFor(() => window.ModalWindows.Any(dialog => dialog.Title == "Edit Region 1"), "R did not create a region.");
                window.ModalWindows.Single(dialog => dialog.Title == "Edit Region 1").Close();
                WaitFor(() => window.ModalWindows.Length == 0, "The region editor did not close.");

                Mouse.Click(OnRegionBar(75, 12));
                WaitFor(() => window.FindFirstDescendant(c => c.ByAutomationId("WorkspaceSelectionDuration")) is null,
                    "Clicking outside the region did not clear the time selection.");
                Mouse.DoubleClick(OnRegionBar(38, 6));
                WaitFor(() => Find(window, "WorkspaceSelectionDuration").Name == "Selection 00:00:01.12",
                    "Double-clicking the region did not restore its range.");
                Assert.Equal("00:00:00.18", Find(window, "Timecode").Name);
                Assert.Empty(window.ModalWindows);

                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "The marker and region were not saved.");

                var savedProject = await ProjectStore.LoadAsync(path);
                Assert.Single(savedProject.Markers);
                Assert.Equal(1, savedProject.Markers[0].Index);
                Assert.Equal(0, savedProject.Markers[0].Frame);
                var region = Assert.Single(savedProject.Regions);
                Assert.Equal((18L, 60L), (region.StartFrame, region.EndFrame));
                running.CloseGracefully();
            }

            using (var running = Launch(executable, path, temporary))
            {
                var reloaded = await ProjectStore.LoadAsync(path);
                Assert.Single(reloaded.Markers);
                Assert.Equal(1, reloaded.Markers[0].Index);
                Assert.Equal(42, Assert.Single(reloaded.Regions).DurationFrames);
                running.CloseGracefully();
            }
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                try { Directory.Delete(temporary, true); } catch { /* Ignore locked temp directory cleanup */ }
            }
        }
    }
}
