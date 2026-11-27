using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
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
    public async Task TransitionProgressEditsAPlainCrossfadeAndSurvivesReopen()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "transition-progress.openrend");
        try
        {
            var red = new MediaAsset { Path = "openrend://color?hex=#C80000", HasVideo = true };
            var green = new MediaAsset { Path = "openrend://color?hex=#00C800", HasVideo = true };
            var outgoing = new TimelineClip { MediaId = red.Id, Name = "Outgoing", DurationFrames = 36, FadeOutFrames = 24 };
            var incoming = new TimelineClip { MediaId = green.Id, Name = "Incoming", StartFrame = 12, DurationFrames = 36, FadeInFrames = 24 };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1), Media = [red, green],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [outgoing, incoming] }]
            }, path);

            using (var running = Launch(executable, path, directory))
            {
                var main = running.Window;
                Seek(main, 18);
                ExpectGreen(main, 50);
                Find(main, $"Clip_{incoming.Id:N}_More").AsButton().Invoke();
                var desktop = main.Automation.GetDesktop();
                var processId = main.Properties.ProcessId.Value;
                AutomationElement? envelope = null;
                WaitFor(() => (envelope = MenuItem("InsertRemoveEnvelopeMenu")) is not null, "The event envelope menu did not open.");
                envelope!.Patterns.ExpandCollapse.Pattern.Expand();
                AutomationElement? progress = null;
                WaitFor(() => (progress = MenuItem("EventTransitionProgress")) is not null, "The transition progress command was unavailable.");
                progress!.AsMenuItem().Invoke();
                Window? dialog = null;
                WaitFor(() => (dialog = main.ModalWindows.SingleOrDefault(window => window.Title == "Video event FX")) is not null,
                    "The progress editor did not open.");
                var panel = dialog!;
                var graph = Find(panel, "TransitionProgressGraph");
                Assert.False(graph.IsOffscreen);
                Find(panel, "AddTransitionProgressPoint").AsButton().Invoke();
                EditPoint(6, 75, EnvelopeCurveType.Hold);
                Find(panel, "AddTransitionProgressPoint").AsButton().Invoke();
                EditPoint(16, 25, EnvelopeCurveType.Linear);
                TryAndCancelDrag(panel, graph);

                var captures = Path.Combine(root, ".local", "desktop-validation");
                Directory.CreateDirectory(captures);
                using (var image = Capture.Element(panel)) image.ToFile(Path.Combine(captures, "transition-progress-editor.png"));
                Find(panel, "CloseTransitionProperties").AsButton().Invoke();
                WaitFor(() => main.ModalWindows.Length == 0, "The transition editor did not close.");
                ExpectGreen(main, 150);
                Seek(main, 28);
                ExpectGreen(main, 50);
                var saved = await SaveGrade(main, path);
                Assert.Equal(new[] { (0L, 0d), (6L, .75), (16L, .25) },
                    saved.Tracks[0].Clips[1].FadeInProgress!.Points.Select(point => (point.Frame, point.Value)));
                Assert.Equal(EnvelopeCurveType.Hold, saved.Tracks[0].Clips[1].FadeInProgress!.Points[1].Curve);
                Find(main, "UndoButton").AsButton().Invoke();
                WaitFor(() => main.Title.Contains('*'), "Undo did not mark the progress edit dirty.");
                var undone = await SaveGrade(main, path);
                Assert.Equal(15, undone.Tracks[0].Clips[1].FadeInProgress!.Points[2].Frame);
                Find(main, "RedoButton").AsButton().Invoke();
                WaitFor(() => main.Title.Contains('*'), "Redo did not restore the progress edit.");
                await SaveGrade(main, path);
                running.CloseGracefully();

                AutomationElement? MenuItem(string id) => desktop.FindAllDescendants(c => c.ByAutomationId(id).And(c.ByProcessId(processId)))
                    .LastOrDefault(item => item.IsEnabled && !item.IsOffscreen);

                void EditPoint(int frame, int value, EnvelopeCurveType shape)
                {
                    WaitFor(() => !Find(panel, "TransitionProgressOffset").AsTextBox().IsReadOnly,
                        "Adding a point did not select an editable offset.");
                    Find(panel, "TransitionProgressOffset").AsTextBox().Text = frame.ToString();
                    Find(panel, "TransitionProgressPercent").AsTextBox().Text = value.ToString();
                    Find(panel, "TransitionProgressShape").AsComboBox().Select(shape.ToString());
                    Find(panel, "ApplyTransitionProgressPoint").AsButton().Invoke();
                    try
                    {
                        WaitFor(() => Find(panel, "TransitionProgressPoint").AsComboBox().SelectedItem?.Name == $"Frame {frame}",
                            "The numeric transition point edit did not commit.");
                    }
                    catch
                    {
                        var captureFolder = Path.Combine(root, ".local", "desktop-validation");
                        Directory.CreateDirectory(captureFolder);
                        using var screenshot = Capture.Element(panel);
                        screenshot.ToFile(Path.Combine(captureFolder, "transition-progress-edit-failure.png"));
                        throw;
                    }
                }
            }

            using (var running = Launch(executable, path, directory))
            {
                Seek(running.Window, 18);
                ExpectGreen(running.Window, 150);
                Seek(running.Window, 28);
                ExpectGreen(running.Window, 50);
                running.CloseGracefully();
            }

            void Seek(Window main, int frame)
            {
                Chord(main, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
                var bounds = Find(main, $"Clip_{outgoing.Id:N}").BoundingRectangle;
                Mouse.Click(new Point(bounds.Left + (int)Math.Round(frame * bounds.Width / 36d), bounds.Top - 10));
                WaitFor(() => PresentedFrame(main) == frame, $"The ruler did not seek to frame {frame}.");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }

        static void ExpectGreen(Window main, int green)
        {
            var preview = Assert.Single(main.FindAllDescendants(c => c.ByClassName("OpenRend.Preview")));
            WaitFor(() =>
            {
                using var image = Capture.Element(preview);
                var pixel = image.Bitmap.GetPixel(image.Bitmap.Width / 2, image.Bitmap.Height / 2);
                return Math.Abs(pixel.G - green) <= 1 && Math.Abs(pixel.R - (200 - green)) <= 1;
            }, $"The native preview did not reach the expected mix with green at {green}.");
        }

        static void TryAndCancelDrag(Window dialog, AutomationElement graph)
        {
            var plot = graph.BoundingRectangle;
            var inset = 12 * plot.Height / 142d;
            var key = new Point((int)Math.Round(plot.Left + inset + (plot.Width - 2 * inset) * .25),
                (int)Math.Round(plot.Bottom - inset - (plot.Height - 2 * inset) * .75));
            Mouse.MoveTo(key);
            Mouse.Down(MouseButton.Left);
            try
            {
                WaitFor(() => Find(dialog, "TransitionProgressOffset").AsTextBox().Text == "6", "The plotted point did not select at frame 6.");
                Mouse.MoveTo(key.X + 20, key.Y + 22);
                Wait.UntilInputIsProcessed();
                Keyboard.Type(VirtualKeyShort.ESCAPE);
                Wait.UntilInputIsProcessed();
            }
            finally { Mouse.Up(MouseButton.Left); }
        }
    }
}
