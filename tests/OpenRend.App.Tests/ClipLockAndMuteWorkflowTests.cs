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
    public async Task EventLockAndMuteWorkflowPersistsAndSurvivesSaveReopen()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "lock-mute.openrend");
        var asset = new MediaAsset
        {
            Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
            Width = 320,
            Height = 180,
            DurationSeconds = 3,
            FrameRate = 30,
            HasVideo = true
        };
        var clip = new TimelineClip { MediaId = asset.Id, Name = "Picture", DurationFrames = 90 };
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

                void ToggleSwitch(string automationId)
                {
                    Find(window, $"Clip_{clip.Id:N}_More").AsButton().Invoke();
                    AutomationElement? switches = null;
                    WaitFor(() => (switches = window.Automation.GetDesktop().FindAllDescendants(c =>
                        c.ByAutomationId("EventSwitchesMenu").And(c.ByProcessId(window.Properties.ProcessId.Value)))
                        .LastOrDefault(c => c.IsEnabled)) is not null,
                        "The Switches menu did not open.");
                    switches!.Patterns.ExpandCollapse.Pattern.Expand();
                    AutomationElement? item = null;
                    WaitFor(() => (item = window.Automation.GetDesktop().FindAllDescendants(c =>
                        c.ByAutomationId(automationId).And(c.ByProcessId(window.Properties.ProcessId.Value)))
                        .LastOrDefault(c => c.IsEnabled)) is not null,
                        $"The {automationId} switch was not found.");
                    item!.AsMenuItem().Invoke();
                }

                // 1. Lock event via context menu
                ToggleSwitch("EventLock");
                WaitFor(() => window.Title.Contains('*'), "Locking event did not mark project dirty.");
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "Project was not saved after locking event.");
                Assert.True((await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0].IsLocked);

                // 2. Unlock via context menu Switches -> Lock
                ToggleSwitch("EventLock");
                WaitFor(() => window.Title.Contains('*'), "Unlocking event did not mark project dirty.");
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "Project was not saved after unlocking event.");
                Assert.False((await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0].IsLocked);

                // 3. Mute event via context menu Switches -> Mute
                ToggleSwitch("EventMute");
                WaitFor(() => window.Title.Contains('*'), "Muting event did not mark project dirty.");
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "Project was not saved after muting event.");
                Assert.True((await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0].IsMuted);

                // 4. Undo restores unmuted state
                Find(window, "UndoButton").AsButton().Invoke();
                WaitFor(() => window.Title.Contains('*'), "Undo did not mark project dirty.");
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "Project was not saved after undo.");
                Assert.False((await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0].IsMuted);
            }
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }
}
