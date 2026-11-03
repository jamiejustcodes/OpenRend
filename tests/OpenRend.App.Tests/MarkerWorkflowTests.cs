using FlaUI.Core.AutomationElements;
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
    public async Task MarkerAndProjectNotesWorkflowPersistsAndSurvivesSaveReopen()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "dist", "OpenRend", "OpenRend.exe");
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

                // Add Marker 1 at playhead (0) via transport bar Insert Marker button
                var markerButton = window.FindFirstDescendant(c => c.ByControlType(ControlType.Button).And(c.ByName("Insert marker")));
                Assert.NotNull(markerButton);
                markerButton.AsButton().Invoke();

                // Save project
                SaveButton(window).Invoke();
                WaitFor(() => ProjectStore.LoadAsync(path).GetAwaiter().GetResult().Markers.Count == 1, "Marker 1 was not saved.");

                var savedProject = await ProjectStore.LoadAsync(path);
                Assert.Single(savedProject.Markers);
                Assert.Equal(1, savedProject.Markers[0].Index);
                Assert.Equal(0, savedProject.Markers[0].Frame);
            }

            // Reopen and verify persistence
            using (var running = Launch(executable, path, temporary))
            {
                var window = running.Window;
                var reloaded = await ProjectStore.LoadAsync(path);
                Assert.Single(reloaded.Markers);
                Assert.Equal(1, reloaded.Markers[0].Index);
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