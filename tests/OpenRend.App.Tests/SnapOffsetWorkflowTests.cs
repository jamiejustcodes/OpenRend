using System.Drawing;
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
    public async Task EventTrimSnapsToRegionWithinScreenToleranceAndHonorsShift()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var path = Path.Combine(temporary, "edge-snap.openrend");
            var media = new MediaAsset
            {
                Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
                DurationSeconds = 3, Width = 320, Height = 180, FrameRate = 30, HasVideo = true
            };
            const long startFrame = 10, endFrame = 56, regionEnd = 40;
            var clip = new TimelineClip { MediaId = media.Id, StartFrame = startFrame, DurationFrames = endFrame - startFrame };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1),
                Media = [media], Regions = [new() { StartFrame = 6, EndFrame = regionEnd }],
                Tracks =
                [
                    new() { Kind = TrackKind.Video, Clips = [clip] },
                    new() { Kind = TrackKind.Video, Clips = [new() { MediaId = media.Id, StartFrame = 240, DurationFrames = 60 }] }
                ]
            }, path);

            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            FitTimeline(window);
            var dpiScale = GetDpiForWindow(window.Properties.NativeWindowHandle.Value) / 96.0;
            var stride = EdgeBounds().Width / (double)clip.DurationFrames;
            var nearFrame = Math.Max(1, (long)Math.Round(6 * dpiScale / stride));
            Assert.InRange(nearFrame * stride, 1, 9.5 * dpiScale);

            DragRightEdge(regionEnd + nearFrame);
            await SaveEnd(regionEnd);

            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            using (Keyboard.Pressing(VirtualKeyShort.SHIFT))
                DragRightEdge(regionEnd + nearFrame);
            await SaveEnd(regionEnd + nearFrame);

            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            Chord(window, VirtualKeyShort.F8);
            WaitFor(() => Find(window, "SnapToggle").Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.Off,
                "F8 did not disable snapping.");
            using (Keyboard.Pressing(VirtualKeyShort.SHIFT))
                DragRightEdge(regionEnd + nearFrame);
            await SaveEnd(regionEnd);

            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            Chord(window, VirtualKeyShort.F8);
            WaitFor(() => Find(window, "SnapToggle").Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On,
                "F8 did not reenable snapping.");
            for (var zoom = 0; zoom < 12 && EdgeBounds().Width / (double)clip.DurationFrames < 13 * dpiScale; zoom++)
            {
                var beforeWidth = EdgeBounds().Width;
                Find(window, "ZoomInTimelineButton").AsButton().Invoke();
                WaitFor(() => EdgeBounds().Width > beforeWidth, "The timeline did not zoom in.");
            }
            Assert.True(EdgeBounds().Width / (double)clip.DurationFrames >= 13 * dpiScale,
                "One project frame must be wider than the snapping tolerance.");
            DragRightEdge(regionEnd + 1);
            await SaveEnd(regionEnd + 1);
            running.CloseGracefully();

            Rectangle EdgeBounds() => Find(window, $"Clip_{clip.Id:N}").BoundingRectangle;

            void DragRightEdge(long targetFrame)
            {
                var bounds = EdgeBounds();
                var from = new Point(bounds.Right - 1, bounds.Top + bounds.Height / 2);
                var dx = (int)Math.Round((targetFrame - endFrame) * bounds.Width / (double)clip.DurationFrames);
                var to = new Point(from.X + dx, from.Y);
                Assert.Equal(window.Properties.ProcessId.Value, window.Automation.FromPoint(from).Properties.ProcessId.Value);
                DragTrimmer(from, to);
            }

            async Task SaveEnd(long expected)
            {
                WaitFor(() => window.Title.Contains('*'), "Trimming did not edit the project.");
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "The trimmed project did not save.");
                var saved = await ProjectStore.LoadAsync(path);
                Assert.Equal(expected, Assert.Single(saved.Tracks[0].Clips).EndFrame);
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task SnapOffsetDragCommitsOnceAndCancelsWithoutChangingTheTake()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var path = Path.Combine(temporary, "snap-offset.openrend");
            var media = new MediaAsset
            {
                Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
                DurationSeconds = 3, Width = 320, Height = 180, FrameRate = 30, HasVideo = true
            };
            var clip = new TimelineClip
            {
                MediaId = media.Id, StartFrame = 15, DurationFrames = 60, SnapOffsetFrames = 12,
                Takes = [new() { MediaId = media.Id, SnapOffsetFrames = 12 }]
            };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1),
                Media = [media], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
            }, path);

            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            FitTimeline(window);
            DragTrimmer(HandleAt(12), HandleAt(24));
            await SaveAndVerify(24);
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            await SaveAndVerify(12);
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Y);
            await SaveAndVerify(24);

            var cancelStart = HandleAt(24);
            var cancelEnd = HandleAt(36);
            Assert.True(SetCursorPos(cancelStart.X, cancelStart.Y));
            Mouse.Down(MouseButton.Left);
            try
            {
                Wait.UntilInputIsProcessed();
                Assert.True(SetCursorPos(cancelEnd.X, cancelEnd.Y));
                Wait.UntilInputIsProcessed();
                Keyboard.Type(VirtualKeyShort.ESCAPE);
                Wait.UntilInputIsProcessed();
            }
            finally { Mouse.Up(MouseButton.Left); }
            Wait.UntilInputIsProcessed();
            Assert.DoesNotContain('*', window.Title);

            // Saving another edit exposes any untracked snap offset left by cancellation.
            Chord(window, VirtualKeyShort.KEY_M);
            await SaveAndVerify(24);
            running.CloseGracefully();

            Point HandleAt(long offset)
            {
                var bounds = Find(window, $"Clip_{clip.Id:N}").BoundingRectangle;
                Assert.True(bounds.Width > 100);
                var point = new Point(bounds.Left + (int)Math.Round(bounds.Width * offset / 60.0), bounds.Bottom - 5);
                Assert.Equal(window.Properties.ProcessId.Value, window.Automation.FromPoint(point).Properties.ProcessId.Value);
                return point;
            }

            async Task SaveAndVerify(long expected)
            {
                WaitFor(() => window.Title.Contains('*'), "The snap offset workflow did not edit the project.");
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "The snap offset workflow did not save.");
                var document = await ProjectStore.LoadAsync(path);
                var saved = Assert.Single(Assert.Single(document.Tracks).Clips);
                Assert.Equal(expected, saved.SnapOffsetFrames);
                Assert.Equal(expected, Assert.Single(saved.Takes!).SnapOffsetFrames);
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
