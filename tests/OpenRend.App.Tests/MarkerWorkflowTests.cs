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
        var rippleClip = new TimelineClip { MediaId = asset.Id, Name = "Ripple event", DurationFrames = 30 };
        await ProjectStore.SaveAsync(new()
        {
            Name = "UI integration",
            Width = 320,
            Height = 180,
            FrameRate = new(30, 1),
            Media = [asset],
            Tracks =
            [
                new() { Kind = TrackKind.Video, Clips = [clip] },
                new()
                {
                    Kind = TrackKind.Video, Clips = [rippleClip],
                    Envelopes = [new() { Type = EnvelopeType.Composite, IsEnabled = true, Points = [new() { Frame = 65, Value = 1 }] }]
                }
            ]
        }, path);

        try
        {
            using (var running = Launch(executable, path, temporary))
            {
                var window = running.Window;

                FitTimeline(window);
                Chord(window, VirtualKeyShort.KEY_M);
                WaitFor(() => window.Title.Contains('*'), "M did not add a marker to the open project.");
                var clipBounds = Find(window, $"Clip_{clip.Id:N}").BoundingRectangle;
                var scale = clipBounds.Height / 67;
                Point OnRegionBar(int frame, double offset) => new(
                    (int)Math.Round(clipBounds.Left + clipBounds.Width * frame / 90.0),
                    (int)Math.Round(clipBounds.Top - (46 - offset) * scale));
                var markerPoint = OnRegionBar(0, 20);
                markerPoint.Offset(3, 0);
                Mouse.DoubleClick(markerPoint);
                WaitFor(() => window.ModalWindows.Any(dialog => dialog.Title == "Edit Marker 1"),
                    "Double-clicking the marker did not open its properties.");
                var markerDialog = window.ModalWindows.Single(dialog => dialog.Title == "Edit Marker 1");
                var markerLabel = Find(markerDialog, "AnnotationLabel").AsTextBox();
                markerLabel.Text = "Opening beat";
                var markerColor = Find(markerDialog, "AnnotationColor").AsComboBox();
                markerColor.Select(3);
                markerColor.Patterns.ExpandCollapse.Pattern.Collapse();
                markerLabel.Focus();
                Chord(markerDialog, VirtualKeyShort.RETURN);
                WaitFor(() => window.ModalWindows.Length == 0, "The marker editor did not close.");

                Mouse.Drag(OnRegionBar(18, 12), OnRegionBar(60, 12), MouseButton.Left);
                WaitFor(() => Find(window, "WorkspaceSelectionDuration").Name == "Selection 00:00:01.12",
                    "Dragging the range did not select frames 18 through 59.");
                Chord(window, VirtualKeyShort.KEY_R);
                WaitFor(() => window.ModalWindows.Any(dialog => dialog.Title == "Edit Region 1"), "R did not create a region.");
                var regionDialog = window.ModalWindows.Single(dialog => dialog.Title == "Edit Region 1");
                var regionEnd = Find(regionDialog, "RegionEndFrame").AsTextBox();
                regionEnd.Text = "17";
                Chord(regionDialog, VirtualKeyShort.RETURN);
                WaitFor(() => Find(regionDialog, "DialogValidationError").Name.Contains("after the start"),
                    "The region editor did not reject a reversed range.");
                regionEnd.Text = "60";
                Assert.Equal("Length 42 frames", Find(regionDialog, "RegionLength").Name);
                Find(regionDialog, "AnnotationLabel").AsTextBox().Text = "Review";
                Chord(regionDialog, VirtualKeyShort.RETURN);
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
                Assert.Equal(("Opening beat", 3), (savedProject.Markers[0].Label, savedProject.Markers[0].ColorId));
                var region = Assert.Single(savedProject.Regions);
                Assert.Equal((18L, 60L), (region.StartFrame, region.EndFrame));
                Assert.Equal("Review", region.Label);
                running.CloseGracefully();
            }

            using (var running = Launch(executable, path, temporary))
            {
                var reloaded = await ProjectStore.LoadAsync(path);
                Assert.Single(reloaded.Markers);
                Assert.Equal(1, reloaded.Markers[0].Index);
                Assert.Equal(42, Assert.Single(reloaded.Regions).DurationFrames);

                var window = running.Window;
                FitTimeline(window);
                SelectRippleMode("RippleAllTracksMarkersMenu");
                Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
                WaitFor(() => Find(window, "RippleToggle").Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On,
                    "Ctrl+L did not enable Auto Ripple.");

                var rippled = await DragAndSaveRippleEdit();
                Assert.Equal(10, Assert.Single(rippled.Tracks[1].Clips).StartFrame);
                var region = Assert.Single(rippled.Regions);
                Assert.Equal((18L, 70L), (region.StartFrame, region.EndFrame));
                Assert.Equal(0, Assert.Single(rippled.Markers).Frame);
                Assert.Equal(75, rippled.Tracks[1].Envelopes!.Single().Points.Single().Frame);

                SelectRippleMode("RippleAffectedTracksMenu");
                rippled = await DragAndSaveRippleEdit();
                Assert.Equal(20, Assert.Single(rippled.Tracks[1].Clips).StartFrame);
                region = Assert.Single(rippled.Regions);
                Assert.Equal((18L, 70L), (region.StartFrame, region.EndFrame));
                Assert.Equal(85, rippled.Tracks[1].Envelopes!.Single().Points.Single().Frame);
                running.CloseGracefully();

                void SelectRippleMode(string automationId)
                {
                    var options = window.FindFirstDescendant(c => c.ByName("Options").And(c.ByControlType(ControlType.MenuItem)));
                    Assert.NotNull(options);
                    options.AsMenuItem().Expand();
                    Find(window, "AutoRippleModeMenu").AsMenuItem().Expand();
                    Find(window, automationId).AsMenuItem().Invoke();
                }

                async Task<ProjectDocument> DragAndSaveRippleEdit()
                {
                    var bounds = Find(window, $"Clip_{rippleClip.Id:N}").BoundingRectangle;
                    var from = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height * 2 / 3);
                    var to = new Point(from.X + bounds.Width / 3, from.Y);
                    foreach (var point in new[] { from, to })
                        Assert.Equal(window.Properties.ProcessId.Value, window.Automation.FromPoint(point).Properties.ProcessId.Value);
                    DragTrimmer(from, to);
                    WaitFor(() => window.Title.Contains('*'), "The ripple drag did not edit the project.");
                    SaveButton(window).Invoke();
                    WaitFor(() => !window.Title.Contains('*'), "The ripple edit did not save.");
                    return await ProjectStore.LoadAsync(path);
                }
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
