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
    public async Task VideoLoopMenuPersistsAndAllowsTrimmingBeyondTheSource()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "source-mode.openrend");
        var asset = new MediaAsset { Path = Path.Combine(root, "tests", "native", "fixture.mp4"), Width = 320, Height = 180,
            DurationSeconds = 3, FrameRate = 30, HasVideo = true };
        var clip = new TimelineClip { MediaId = asset.Id, Name = "Picture", DurationFrames = 90 };
        await ProjectStore.SaveAsync(new() { Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1), Media = [asset],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] }, path);
        try
        {
            using (var running = Launch(executable, path, temporary))
            {
                var window = running.Window;
                Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
                void ToggleLoop()
                {
                    Find(window, $"Clip_{clip.Id:N}_More").AsButton().Invoke();
                    AutomationElement? menu = null;
                    WaitFor(() => (menu = window.Automation.GetDesktop().FindFirstDescendant(c =>
                        c.ByControlType(ControlType.Menu).And(c.ByProcessId(window.Properties.ProcessId.Value)))) is not null,
                        "The video event menu did not open.");
                    menu!.FindFirstDescendant(c => c.ByName("Switches"))!.Patterns.ExpandCollapse.Pattern.Expand();
                    AutomationElement? loop = null;
                    WaitFor(() => (loop = window.Automation.GetDesktop().FindFirstDescendant(c =>
                        c.ByAutomationId("VideoEventLoop").And(c.ByProcessId(window.Properties.ProcessId.Value)))) is not null,
                        "The Loop switch was not exposed.");
                    loop!.AsMenuItem().Invoke();
                }
                ToggleLoop();
                WaitFor(() => window.Title.Contains('*'), "The Loop switch did not edit the event.");
                Find(window, "UndoButton").AsButton().Invoke();
                WaitFor(() => !Find(window, "UndoButton").IsEnabled, "Loop created more than one undo step.");
                Find(window, "RedoButton").AsButton().Invoke();
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "The looping event was not saved.");
                Assert.Equal(VideoSourceMode.Loop, (await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0].VideoSourceMode);
                var bounds = Find(window, $"Clip_{clip.Id:N}").BoundingRectangle;
                Mouse.MoveTo((int)bounds.Right - 1, (int)(bounds.Top + bounds.Height / 2));
                Mouse.Down(MouseButton.Left);
                Mouse.MoveTo((int)(bounds.Right + bounds.Width / 3), (int)(bounds.Top + bounds.Height / 2));
                Mouse.Up(MouseButton.Left);
                WaitFor(() => window.Title.Contains('*'), "The looping event did not extend beyond its source.");
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "The extended event was not saved.");
                Assert.True((await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0].DurationFrames > 90);
                Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
                ToggleLoop();
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "The held event was not saved.");
                Assert.Equal(VideoSourceMode.HoldLastFrame, (await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0].VideoSourceMode);
                running.CloseGracefully();
            }
            using var reopened = Launch(executable, path, temporary);
            Assert.Equal(VideoSourceMode.HoldLastFrame, (await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0].VideoSourceMode);
            reopened.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
