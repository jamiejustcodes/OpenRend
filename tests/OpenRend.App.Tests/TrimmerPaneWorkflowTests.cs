using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
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
    public async Task TrimmerFloatsReopensAndRestores()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var projectPath = Path.Combine(temporary, "trimmer.openrend");
        try
        {
            var media = new MediaAsset
            {
                Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
                Width = 320, Height = 180, DurationSeconds = 3, FrameRate = 30, HasVideo = true
            };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration",
                Media = [media],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [new() { MediaId = media.Id, DurationFrames = 90 }] }]
            }, projectPath);
            Rectangle floatingBounds;
            using (var running = Launch(executable, projectPath, temporary))
            {
                var window = running.Window;
                FocusTrimmerWindow(window);
                WaitFor(() => DockedTrimmer(window) is not null, "The Trimmer tab was not exposed.");
                Chord(window, VirtualKeyShort.ALT, VirtualKeyShort.KEY_2);
                WaitFor(() => DockedTrimmer(window)?.Patterns.SelectionItem.Pattern.IsSelected.Value == true,
                    "Alt+2 did not select the docked Trimmer.");
                Chord(window, VirtualKeyShort.ALT, VirtualKeyShort.KEY_2);
                WaitFor(() => DockedTrimmer(window) is null, "Alt+2 did not hide the docked Trimmer.");
                Chord(window, VirtualKeyShort.ALT, VirtualKeyShort.KEY_2);
                WaitFor(() => DockedTrimmer(window) is not null, "Alt+2 did not show the docked Trimmer.");
                var tab = DockedTrimmer(window)!.BoundingRectangle;
                var dpi = GetDpiForWindow(window.Properties.NativeWindowHandle.Value) / 96.0;
                var start = new Point(tab.Left + (int)(24 * dpi), tab.Top + tab.Height / 2);
                DragTrimmer(start, new Point(start.X, start.Y - (int)(20 * dpi)));
                Assert.Null(FloatingTrimmer(window));

                var target = new Point(start.X + (int)(120 * dpi), start.Y - (int)(180 * dpi));
                DragTrimmer(start, target);
                WaitFor(() => FloatingTrimmer(window) is not null, "Dragging the tab did not float the Trimmer.");
                var floating = FloatingTrimmer(window)!;
                floatingBounds = floating.BoundingRectangle;
                var close = floating.FindFirstDescendant(c => c.ByName("Close Trimmer"));
                Assert.NotNull(close);
                close.AsButton().Invoke();
                WaitFor(() => FloatingTrimmer(window) is null, "The Trimmer window did not close.");
                Chord(window, VirtualKeyShort.ALT, VirtualKeyShort.KEY_2);
                WaitFor(() => FloatingTrimmer(window)?.BoundingRectangle == floatingBounds,
                    "Alt+2 did not reopen the Trimmer at its saved bounds.");
                Chord(FloatingTrimmer(window)!, VirtualKeyShort.ALT, VirtualKeyShort.KEY_2);
                WaitFor(() => FloatingTrimmer(window) is null, "Alt+2 did not hide the active floating Trimmer.");
                Chord(window, VirtualKeyShort.ALT, VirtualKeyShort.KEY_2);
                WaitFor(() => FloatingTrimmer(window)?.BoundingRectangle == floatingBounds,
                    "Alt+2 did not restore the floating Trimmer after hiding it.");
                running.CloseGracefully();
            }

            using (var running = Launch(executable, projectPath, temporary))
            {
                var window = running.Window;
                WaitFor(() => FloatingTrimmer(window)?.BoundingRectangle == floatingBounds,
                    "Restart did not restore the floating Trimmer bounds.");
                var preview = window.FindFirstDescendant(c => c.ByControlType(ControlType.TabItem).And(c.ByName("Video Preview")));
                Assert.NotNull(preview);
                var previewTab = preview.BoundingRectangle;
                var caption = new Point(floatingBounds.Left + 60, floatingBounds.Top + 16);
                var target = new Point(previewTab.Left + 20, previewTab.Top - 40);
                using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                    DragTrimmer(caption, target);
                WaitFor(() => FloatingTrimmer(window) is null && DockedTrimmer(window) is not null,
                    "Ctrl-drag did not redock the Trimmer.");
                Chord(window, VirtualKeyShort.ALT, VirtualKeyShort.KEY_D);
                Chord(window, VirtualKeyShort.KEY_S);
                WaitFor(() => DockedTrimmer(window)?.BoundingRectangle.Right < preview.BoundingRectangle.Left,
                    "Source Window Layout did not place the Trimmer alongside Video Preview.");
                running.CloseGracefully();
            }

            var workspacePath = Path.Combine(temporary, "workspace.json");
            var workspace = JsonNode.Parse(await File.ReadAllTextAsync(workspacePath))!;
            var trimmer = workspace["Trimmer"]!;
            Assert.True(trimmer["SeparateDock"]!.GetValue<bool>());
            trimmer["Floating"] = true;
            trimmer["Bounds"] = new JsonObject
            {
                ["Left"] = 100_000, ["Top"] = -100_000, ["Width"] = 600, ["Height"] = 400
            };
            await File.WriteAllTextAsync(workspacePath, workspace.ToJsonString());

            using (var running = Launch(executable, projectPath, temporary))
            {
                var window = running.Window;
                WaitFor(() => FloatingTrimmer(window) is not null, "The saved floating Trimmer did not reopen.");
                var floating = FloatingTrimmer(window)!;
                var handle = floating.Properties.NativeWindowHandle.Value;
                var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                Assert.True(GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor));
                var workArea = Rectangle.FromLTRB(monitor.Work.Left, monitor.Work.Top, monitor.Work.Right, monitor.Work.Bottom);
                Assert.True(workArea.Contains(floating.BoundingRectangle), "The Trimmer was restored outside the monitor work area.");
                running.CloseGracefully();
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static Window? FloatingTrimmer(Window owner) => owner
        .FindFirstDescendant(c => c.ByControlType(ControlType.Window).And(c.ByName("Trimmer")))?.AsWindow();

    private static TabItem? DockedTrimmer(Window owner) => owner
        .FindFirstDescendant(c => c.ByControlType(ControlType.TabItem).And(c.ByName("Trimmer")))?.AsTabItem();

    private static void DragTrimmer(Point start, Point end)
    {
        Assert.True(SetCursorPos(start.X, start.Y));
        Mouse.Down(MouseButton.Left);
        try
        {
            Wait.UntilInputIsProcessed();
            Assert.True(SetCursorPos(end.X, end.Y));
            Wait.UntilInputIsProcessed();
        }
        finally { Mouse.Up(MouseButton.Left); }
        Wait.UntilInputIsProcessed();
    }

    private static void FocusTrimmerWindow(Window window)
    {
        var handle = window.Properties.NativeWindowHandle.Value;
        if (GetForegroundWindow() != handle)
        {
            Keyboard.Type(VirtualKeyShort.ALT);
            SetForegroundWindow(handle);
        }
        WaitFor(() => GetForegroundWindow() == handle, "The Trimmer test window did not receive foreground input.");
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
}
