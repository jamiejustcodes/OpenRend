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
                AssertPerMonitorDpi(window);
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
                AssertPerMonitorDpi(floating);
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

                const uint preserveSizeOrderAndFocus = 0x1 | 0x4 | 0x10;
                Assert.True(SetWindowPos(handle, 0, 100_000, -100_000, 0, 0, preserveSizeOrderAndFocus));
                WaitFor(() => !workArea.Contains(floating.BoundingRectangle), "The Trimmer did not move outside the test work area.");
                var dimensions = (nint)((workArea.Height << 16) | workArea.Width);
                Assert.True(PostMessage(window.Properties.NativeWindowHandle.Value, 0x7e, 32, dimensions));
                WaitFor(() => workArea.Contains(floating.BoundingRectangle), "Display notification did not recover the open Trimmer.");
                running.CloseGracefully();
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task CoreWorkspacePanesFloatAndRedock()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var projectPath = Path.Combine(temporary, "panes.openrend");
        try
        {
            var media = new MediaAsset
            {
                Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
                Width = 320, Height = 180, DurationSeconds = 3, FrameRate = 30, HasVideo = true
            };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Media = [media],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [new() { MediaId = media.Id, DurationFrames = 90 }] }]
            }, projectPath);

            Rectangle savedMediaBounds;
            int savedUpperHeight;
            using (var running = Launch(executable, projectPath, temporary))
            {
                var window = running.Window;
                var scale = GetDpiForWindow(window.Properties.NativeWindowHandle.Value) / 96.0;
                foreach (var (name, dock, control) in new[]
                {
                    ("Video Preview", "PreviewDeckTabs", "PreviewInfo"), ("Explorer", "ToolsTabs", "ExplorerList"),
                    ("Project Media", "ToolsTabs", "MediaList"), ("Video FX", "ToolsTabs", "VideoFxPlugIns"),
                    ("Transitions", "ToolsTabs", "TransitionPlugIns"), ("Master Bus", "MasterBusTabs", "MasterGain")
                })
                {
                    var floating = FloatPane(window, name, scale);
                    var movedControl = Find(floating, control);
                    if (name == "Project Media")
                        Assert.Single(movedControl.AsListBox().Items);
                    if (name == "Video Preview")
                        WaitFor(() => movedControl.Properties.HelpText.ValueOrDefault?.Contains("Direct3D 11") == true,
                            "The native display did not initialize in the floating preview.");

                    DockIn(window, floating, name, dock, scale);
                    Find(window, control);
                }

                DockIn(window, FloatPane(window, "Video FX", scale), "Video FX", "PreviewDeckTabs", scale);
                Find(window, "VideoFxPlugIns");
                DockIn(window, FloatPane(window, "Video Preview", scale), "Video Preview", "ToolsTabs", scale);
                WaitFor(() => Find(window, "PreviewInfo").Properties.HelpText.ValueOrDefault?.Contains("Direct3D 11") == true,
                    "The native preview did not initialize in its new dock.");

                var transitions = FloatPane(window, "Transitions", scale);
                var lower = Find(window, "ToolsTabs").BoundingRectangle;
                var upperTarget = new Point(lower.Left + lower.Width / 2, lower.Top + (int)(12 * scale));
                DockIn(window, transitions, "Transitions", "UpperDockTabs", scale, upperTarget);
                DockIn(window, FloatPane(window, "Explorer", scale), "Explorer", "UpperDockTabs", scale);

                var upper = Find(window, "UpperDockTabs");
                var initialUpperHeight = upper.BoundingRectangle.Height;
                var splitter = Find(window, "UpperDockSplitter").BoundingRectangle;
                var grip = new Point(splitter.Left + splitter.Width / 2, splitter.Top + splitter.Height / 2);
                DragTrimmer(grip, new Point(grip.X, grip.Y + (int)(44 * scale)));
                WaitFor(() => upper.BoundingRectangle.Height > initialUpperHeight + 20,
                    "Dragging the workspace splitter did not grow the upper group.");
                savedUpperHeight = upper.BoundingRectangle.Height;

                var projectMedia = FloatPane(window, "Project Media", scale);
                savedMediaBounds = projectMedia.BoundingRectangle;
                running.CloseGracefully();
            }

            using (var running = Launch(executable, projectPath, temporary))
            {
                var window = running.Window;
                WaitFor(() => DockTab(window, "PreviewDeckTabs", "Video FX")?.Patterns.SelectionItem.Pattern.IsSelected.Value == true,
                    "Restart did not restore Video FX as the active tab in the Preview group.");
                Assert.NotNull(DockTab(window, "ToolsTabs", "Video Preview"));
                Assert.NotNull(DockTab(window, "UpperDockTabs", "Transitions"));
                WaitFor(() => DockTab(window, "UpperDockTabs", "Explorer")?.Patterns.SelectionItem.Pattern.IsSelected.Value == true,
                    "Restart did not restore Explorer as the active tab in the upper group.");
                Assert.InRange(Find(window, "UpperDockTabs").BoundingRectangle.Height, savedUpperHeight - 2, savedUpperHeight + 2);
                WaitFor(() => FloatingPane(window, "Project Media")?.BoundingRectangle == savedMediaBounds,
                    "Restart did not restore the floating Project Media bounds.");
                var floating = FloatingPane(window, "Project Media")!;
                var close = floating.FindFirstDescendant(c => c.ByName("Close Project Media"));
                Assert.NotNull(close);
                close.AsButton().Invoke();
                WaitFor(() => FloatingPane(window, "Project Media") is null, "The Project Media pane did not close.");

                var view = window.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName("View")));
                Assert.NotNull(view);
                view.AsMenuItem().Expand();
                Find(window, "WorkspaceWindowMenu").AsMenuItem().Expand();
                Find(window, "ShowPane_ProjectMedia").AsMenuItem().Invoke();
                WaitFor(() => FloatingPane(window, "Project Media")?.BoundingRectangle == savedMediaBounds,
                    "The Window menu did not reopen Project Media at its prior bounds.");

                var upper = Find(window, "UpperDockTabs");
                var closeExplorer = upper.FindFirstDescendant(c => c.ByName("Close Explorer"));
                Assert.NotNull(closeExplorer);
                closeExplorer.AsButton().Invoke();
                AutomationElement? closeTransitions = null;
                WaitFor(() => (closeTransitions = upper.FindFirstDescendant(c => c.ByName("Close Transitions"))) is not null,
                    "Closing Explorer did not expose the remaining upper tab.");
                closeTransitions!.AsButton().Invoke();
                WaitFor(() => window.FindFirstDescendant(c => c.ByAutomationId("UpperDockTabs"))?.Properties.IsOffscreen.ValueOrDefault != false,
                    "Closing the last upper pane did not collapse the group.");
                running.CloseGracefully();
            }

            static Window FloatPane(Window window, string name, double scale)
            {
                FocusTrimmerWindow(window);
                var tab = DockedPane(window, name);
                Assert.NotNull(tab);
                tab.Select();
                WaitFor(() => !tab.Properties.IsOffscreen.ValueOrDefault, $"The {name} tab was off screen.");
                var tabBounds = tab.BoundingRectangle;
                var start = new Point(tabBounds.Left + (int)(22 * scale), tabBounds.Top + tabBounds.Height / 2);
                DragTrimmer(start, new Point(start.X, start.Y - (int)(140 * scale)));
                WaitFor(() => FloatingPane(window, name) is not null, $"Dragging the tab did not float {name}.");
                return FloatingPane(window, name)!;
            }

            static void DockIn(Window window, Window floating, string name, string group, double scale, Point? drop = null)
            {
                var bounds = floating.BoundingRectangle;
                var caption = new Point(bounds.Left + (int)(60 * scale), bounds.Top + (int)(16 * scale));
                var target = drop ?? CenterOf(Find(window, group).BoundingRectangle);
                using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                    DragTrimmer(caption, target);
                WaitFor(() => FloatingPane(window, name) is null && DockTab(window, group, name)?.Patterns.SelectionItem.Pattern.IsSelected.Value == true,
                    $"Ctrl-drag did not select {name} in {group}.");

                static Point CenterOf(Rectangle dock) => new(dock.Left + dock.Width / 2, dock.Top + dock.Height / 2);
            }

            static TabItem? DockTab(Window window, string group, string name) => Find(window, group)
                .FindFirstDescendant(c => c.ByControlType(ControlType.TabItem).And(c.ByName(name)))?.AsTabItem();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static Window? FloatingTrimmer(Window owner) => FloatingPane(owner, "Trimmer");

    private static Window? FloatingPane(Window owner, string name) => owner
        .FindFirstDescendant(c => c.ByControlType(ControlType.Window).And(c.ByName(name)))?.AsWindow();

    private static TabItem? DockedTrimmer(Window owner) => DockedPane(owner, "Trimmer");

    private static TabItem? DockedPane(Window owner, string name) => owner
        .FindFirstDescendant(c => c.ByControlType(ControlType.TabItem).And(c.ByName(name)))?.AsTabItem();

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

    private static void AssertPerMonitorDpi(Window window)
    {
        var context = GetWindowDpiAwarenessContext(window.Properties.NativeWindowHandle.Value);
        Assert.True(AreDpiAwarenessContextsEqual(context, (nint)(-4)), $"{window.Title} must use per-monitor v2 DPI awareness.");
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetWindowDpiAwarenessContext(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(nint hwnd, uint message, nint wparam, nint lparam);
}
