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

                    var bounds = floating.BoundingRectangle;
                    var home = Find(window, dock).BoundingRectangle;
                    var caption = new Point(bounds.Left + (int)(60 * scale), bounds.Top + (int)(16 * scale));
                    var target = new Point(home.Left + home.Width / 2, home.Top + home.Height / 2);
                    using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                        DragTrimmer(caption, target);
                    WaitFor(() => FloatingPane(window, name) is null && DockedPane(window, name) is not null,
                        $"Ctrl-drag did not redock {name}.");
                    Find(window, control);
                }

                var projectMedia = FloatPane(window, "Project Media", scale);
                savedMediaBounds = projectMedia.BoundingRectangle;
                running.CloseGracefully();
            }

            using (var running = Launch(executable, projectPath, temporary))
            {
                var window = running.Window;
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

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
}
