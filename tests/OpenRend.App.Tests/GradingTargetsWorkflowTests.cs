using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
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
    public async Task GradingTargetFollowsTheActiveScopeInDockedAndFloatingPanels()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "grading-targets.openrend");
        try
        {
            var first = Solid("First source", "#6A6A6A");
            var second = Solid("Second source", "#808080");
            var clip = new TimelineClip { Name = "Graded event", MediaId = first.Id, DurationFrames = 90 };
            var primary = new TimelineTrack { Name = "Primary", Kind = TrackKind.Video, Clips = [clip] };
            var secondary = new TimelineTrack { Name = "Secondary", Kind = TrackKind.Video };
            var audio = new TimelineTrack { Name = "Audio", Kind = TrackKind.Audio };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 640, Height = 360, Media = [first, second],
                Tracks = [primary, secondary, audio]
            }, path);

            using var running = Launch(executable, path, temporary);
            var main = running.Window;
            TogglePanel(main);
            WaitFor(() => DockedPane(main, "Color Grading")?.IsSelected == true, "Alt+G did not select Color Grading.");
            Find(main, "UtilitiesItem").Patterns.SelectionItem.Pattern.Select();
            Assert.False(Find(main, "logExposure").IsEnabled);

            Find(main, $"Clip_{clip.Id:N}").Patterns.SelectionItem.Pattern.Select();
            WaitFor(() => Target(main).Contains(clip.Name, StringComparison.Ordinal), "Event grading did not follow the event selection.");
            SetExposure(main, .1);
            ChooseScope(main, 1, "Track FX · Primary");
            SetExposure(main, .2);
            FocusTrack(secondary);
            WaitFor(() => Target(main) == "Track FX · Secondary", "Track grading did not follow the focused track.");
            SetExposure(main, .3);
            FocusTrack(audio);
            WaitFor(() => !Find(main, "logExposure").IsEnabled, "Audio track focus left a video grade enabled.");

            ChooseScope(main, 2, $"Media FX · {first.Name}");
            SelectBrowserTab(main, "Project Media");
            Find(main, "MediaList").AsListBox().Items[0].Select();
            Mouse.Click(Find(main, "MediaViewDetails").GetClickablePoint());
            Find(main, "MediaDetailsList").AsListBox().Items[1].Select();
            SelectBrowserTab(main, "Color Grading");
            WaitFor(() => Target(main) == $"Media FX · {second.Name}", "Media grading did not follow the Project Media selection.");
            SetExposure(main, .4);

            Find(main, "ColorGrading_PopoutButton").AsButton().Invoke();
            var dialog = GradingDialog(main);
            Assert.Equal($"Media FX · {second.Name}", Target(dialog));
            Assert.Equal(.4, Exposure(dialog));
            ChooseScope(dialog, 3, "Video Output FX");
            SetExposure(dialog, .5);
            Find(dialog, "ColorGrading_Close").AsButton().Invoke();
            WaitFor(() => main.ModalWindows.Length == 0, "The floating grading dialog did not close.");

            ChooseScope(main, 3, "Video Output FX");
            Assert.Equal(.5, Exposure(main));
            var pane = DockedPane(main, "Color Grading");
            Assert.NotNull(pane);
            var bounds = pane.BoundingRectangle;
            var start = new Point(bounds.Left + bounds.Width / 4, bounds.Top + bounds.Height / 2);
            FocusTrimmerWindow(main);
            DragTrimmer(start, new Point(start.X, start.Y - 145));
            WaitFor(() => FloatingPane(main, "Color Grading") is not null, "Dragging the grading tab did not float the pane.");
            var floating = FloatingPane(main, "Color Grading")!;
            Assert.Equal("Video Output FX", Target(floating));
            Assert.Equal(.5, Exposure(floating));
            SetExposure(floating, .6);
            var captureDir = Path.Combine(root, ".local", "desktop-validation");
            Directory.CreateDirectory(captureDir);
            using (var capture = Capture.Element(floating))
                capture.ToFile(Path.Combine(captureDir, "color-grading-targets.png"));

            var frame = floating.BoundingRectangle;
            var dock = Find(main, "ToolsTabs").BoundingRectangle;
            using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                DragTrimmer(new Point(frame.Left + 60, frame.Top + 16), new Point(dock.Left + dock.Width / 2, dock.Top + dock.Height / 2));
            WaitFor(() => FloatingPane(main, "Color Grading") is null && DockedPane(main, "Color Grading")?.IsSelected == true,
                "Ctrl-drag did not return Color Grading to Tools.");
            Assert.Equal(.6, Exposure(main));

            var saved = await SaveGrade(main, path);
            Assert.Equal(.1, SavedExposure(saved.Tracks[0].Clips[0].VideoEffects));
            Assert.Equal(.2, SavedExposure(saved.Tracks[0].VideoEffects));
            Assert.Equal(.3, SavedExposure(saved.Tracks[1].VideoEffects));
            Assert.Empty(saved.Tracks[2].VideoEffects);
            Assert.Empty(saved.Media[0].VideoEffects);
            Assert.Equal(.4, SavedExposure(saved.Media[1].VideoEffects));
            Assert.Equal(.6, SavedExposure(saved.VideoOutputEffects));

            Chord(main, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            WaitFor(() => Exposure(main) == .5, "Undo did not restore the previous output grade.");
            TogglePanel(main);
            WaitFor(() => DockedPane(main, "Color Grading")?.IsSelected != true, "Alt+G did not hide Color Grading.");
            TogglePanel(main);
            WaitFor(() => DockedPane(main, "Color Grading")?.IsSelected == true, "Alt+G did not reopen Color Grading.");
            Assert.Equal("Video Output FX", Target(main));
            Assert.Equal(.5, Exposure(main));
            await SaveGrade(main, path);
            running.CloseGracefully();

            void FocusTrack(TimelineTrack track)
            {
                var row = Find(main, $"Track_{track.Id:N}").BoundingRectangle;
                Mouse.Click(new Point(row.Left + 54, row.Top + 16));
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }

        static void TogglePanel(Window window) => Chord(window, VirtualKeyShort.ALT, VirtualKeyShort.KEY_G);
        static string Target(Window window) => Find(window, "ColorGrading_TargetLabel").Name;
        static double Exposure(Window window) => Find(window, "logExposure").Patterns.RangeValue.Pattern.Value.Value;
        static double SavedExposure(List<VideoEffect> effects) => Assert.Single(effects).GetParameter("ExposureLnAdjust");
        static void SetExposure(Window window, double value) => Find(window, "logExposure").Patterns.RangeValue.Pattern.SetValue(value);
        static void ChooseScope(Window window, int index, string target)
        {
            var picker = Find(window, "ColorGrading_Scope").AsComboBox();
            picker.Select(index);
            picker.Patterns.ExpandCollapse.Pattern.Collapse();
            WaitFor(() => Target(window) == target, $"Grading did not bind to {target}.");
        }
    }
}
