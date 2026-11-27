using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.App.Tests;

public sealed partial class EditorWorkflowTests
{
    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task ProjectSettingsTabsAppearAboveTheFormAndWorkspaceTabsStayBelowTheirPane()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "tab-placement.openrend");
        var asset = new MediaAsset
        {
            Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
            Width = 320, Height = 180, DurationSeconds = 3, FrameRate = 30, HasVideo = true
        };
        await ProjectStore.SaveAsync(new()
        {
            Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1),
            Media = [asset], Tracks = [new() { Kind = TrackKind.Video }]
        }, path);

        try
        {
            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            var mediaTab = window.FindFirstDescendant(c => c.ByControlType(ControlType.TabItem).And(c.ByName("Project Media")));
            Assert.NotNull(mediaTab);
            mediaTab.Patterns.SelectionItem.Pattern.Select();
            var mediaList = Find(window, "MediaList");
            WaitFor(() => mediaList.BoundingRectangle.Height > 0, "Project Media did not show its list.");
            Assert.True(mediaTab.BoundingRectangle.Top >= mediaList.BoundingRectangle.Bottom);

            Chord(window, VirtualKeyShort.ALT, VirtualKeyShort.RETURN);
            WaitFor(() => window.ModalWindows.Any(dialog => dialog.Title == "Project Properties"), "Project Properties did not open.");
            var settings = window.ModalWindows.Single(dialog => dialog.Title == "Project Properties");
            var videoTab = settings.FindFirstDescendant(c => c.ByControlType(ControlType.TabItem).And(c.ByName("Video")));
            Assert.NotNull(videoTab);
            Assert.True(videoTab.BoundingRectangle.Bottom <= Find(settings, "MatchMediaButton").BoundingRectangle.Top);

            var summaryTab = settings.FindFirstDescendant(c => c.ByControlType(ControlType.TabItem).And(c.ByName("Summary")));
            Assert.NotNull(summaryTab);
            summaryTab.Patterns.SelectionItem.Pattern.Select();
            WaitFor(() => settings.FindFirstDescendant(c => c.ByAutomationId("SummaryEngineerBox"))?.BoundingRectangle.Height > 0,
                "The Summary tab did not show its fields.");
            Assert.True(summaryTab.BoundingRectangle.Bottom <= Find(settings, "SummaryEngineerBox").BoundingRectangle.Top);

            Chord(settings, VirtualKeyShort.ESCAPE);
            WaitFor(() => window.ModalWindows.Length == 0, "Escape did not close Project Properties.");
            Assert.DoesNotContain('*', window.Title);
            running.CloseGracefully(waitForRender: false);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }
}
