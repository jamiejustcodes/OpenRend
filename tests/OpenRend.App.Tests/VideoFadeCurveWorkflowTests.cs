using FlaUI.Core.AutomationElements;
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
    public async Task VideoFadeCurveMenuPersistsCurvesAndSurvivesSaveReopen()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "fade-curves.openrend");
        var asset = new MediaAsset { Path = Path.Combine(root, "tests", "native", "fixture.mp4"), Width = 320, Height = 180,
            DurationSeconds = 3, FrameRate = 30, HasVideo = true };
        var clip = new TimelineClip { MediaId = asset.Id, Name = "Picture", DurationFrames = 90, FadeInFrames = 15, FadeOutFrames = 15 };
        await ProjectStore.SaveAsync(new() { Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1), Media = [asset],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] }, path);
        try
        {
            using (var running = Launch(executable, path, temporary))
            {
                var window = running.Window;

                void PickFadeInCurve(string curveName)
                {
                    Find(window, $"Clip_{clip.Id:N}_More").AsButton().Invoke();
                    AutomationElement? submenu = null;
                    WaitFor(() => (submenu = window.Automation.GetDesktop().FindFirstDescendant(c =>
                        c.ByAutomationId("FadeInCurveMenu").And(c.ByProcessId(window.Properties.ProcessId.Value)))) is not null,
                        "The Fade In Curve menu did not open.");
                    submenu!.Patterns.ExpandCollapse.Pattern.Expand();
                    AutomationElement? item = null;
                    WaitFor(() => (item = window.Automation.GetDesktop().FindFirstDescendant(c =>
                        c.ByAutomationId($"FadeInCurve_{curveName}").And(c.ByProcessId(window.Properties.ProcessId.Value)))) is not null,
                        $"The {curveName} option was not exposed.");
                    item!.AsMenuItem().Invoke();
                }

                PickFadeInCurve("Fast");
                WaitFor(() => window.Title.Contains('*'), "Changing the fade-in curve did not mark the project dirty.");
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "The project was not saved after setting fade-in curve.");
                Assert.Equal(VideoFadeCurve.Fast, (await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0].FadeInCurve);

                Find(window, "UndoButton").AsButton().Invoke();
                WaitFor(() => window.Title.Contains('*'), "Undo did not mark project dirty.");
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "The project was not saved after undo.");
                Assert.Equal(VideoFadeCurve.Linear, (await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0].FadeInCurve);
            }
        }
        finally { Directory.Delete(temporary, true); }
    }
}
