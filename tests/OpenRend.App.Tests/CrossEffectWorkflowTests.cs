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
    public async Task CrossEffectPropertiesChangeThePreviewAndRoundTrip()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "cross-effect.openrend");
        try
        {
            var red = new MediaAsset { Path = "openrend://generator?kind=Checkerboard&size=16&color1=C80000&color2=000000", HasVideo = true };
            var green = new MediaAsset { Path = "openrend://color?hex=#00C800", HasVideo = true };
            var outgoing = new TimelineClip { MediaId = red.Id, Name = "Outgoing", DurationFrames = 36, FadeOutFrames = 24 };
            var incoming = new TimelineClip { MediaId = green.Id, Name = "Incoming", StartFrame = 12, DurationFrames = 36, FadeInFrames = 24,
                FadeInTransition = new VideoTransition { Kind = VideoTransitionKind.CrossEffect, Preset = "Cross Zoom A Only" } };
            await ProjectStore.SaveAsync(new ProjectDocument { Name = "UI integration", Width = 320, Height = 180,
                FrameRate = new(30, 1), Media = [red, green], Tracks = [new() { Kind = TrackKind.Video, Clips = [outgoing, incoming] }] }, path);

            using (var running = Launch(executable, path, directory))
            {
                var main = running.Window;
                SeekQuarter(main);
                CheckGreen(main, 50);
                Find(main, $"Clip_{incoming.Id:N}_Transition").AsButton().Invoke();
                Window? dialog = null;
                WaitFor(() => (dialog = main.ModalWindows.SingleOrDefault(window => window.Title == "Video event FX")) is not null,
                    "Cross Effect properties did not open.");
                var properties = dialog!;
                var area = main.BoundingRectangle;
                properties.Patterns.Transform.Pattern.Move(area.Left + 12, area.Bottom - properties.BoundingRectangle.Height - 32);
                Assert.Equal("Source", Find(properties, "TransitionParameter_ApplyTo").AsComboBox().SelectedItem?.Name);
                Find(properties, "TransitionParameter_EffectType").AsComboBox().Select("Pixelate");
                WaitFor(() => properties.FindFirstDescendant(c => c.ByAutomationId("TransitionParameter_MaxX"))?.IsEnabled == true,
                    "Pixelate controls did not appear.");
                Assert.Equal("Source", Find(properties, "TransitionParameter_ApplyTo").AsComboBox().SelectedItem?.Name);
                Find(properties, "TransitionParameter_FadeRange").Patterns.RangeValue.Pattern.SetValue(.2);
                CheckGreen(main, 0);
                Find(properties, "UndoTransitionEdit").AsButton().Invoke();
                CheckGreen(main, 50);
                Find(properties, "RedoTransitionEdit").AsButton().Invoke();
                CheckGreen(main, 0);
                var captureFolder = Path.Combine(root, ".local", "desktop-validation");
                Directory.CreateDirectory(captureFolder);
                using (var capture = Capture.Element(properties)) capture.ToFile(Path.Combine(captureFolder, "cross-effect-properties.png"));
                Find(properties, "CloseTransitionProperties").AsButton().Invoke();
                WaitFor(() => main.ModalWindows.Length == 0, "Cross Effect properties did not close.");
                var saved = (await SaveGrade(main, path)).Tracks[0].Clips[1].FadeInTransition!;
                Assert.Equal("Cross Zoom A Only", saved.Preset);
                Assert.Equal("Pixelate", saved.Parameters!["EffectType"]);
                Assert.Equal("0.20", saved.Parameters["FadeRange"]);
                running.CloseGracefully();
            }

            using (var running = Launch(executable, path, directory))
            {
                SeekQuarter(running.Window);
                CheckGreen(running.Window, 0);
                running.CloseGracefully();
            }

            void SeekQuarter(Window main)
            {
                Chord(main, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
                var bounds = Find(main, $"Clip_{outgoing.Id:N}").BoundingRectangle;
                Mouse.Click(new Point(bounds.Left + bounds.Width / 2, bounds.Top - 10));
                WaitFor(() => PresentedFrame(main) == 18, "The timeline did not seek to one quarter of the transition.");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }

        static void CheckGreen(Window main, int expected)
        {
            var preview = Assert.Single(main.FindAllDescendants(c => c.ByClassName("OpenRend.Preview")));
            WaitFor(() =>
            {
                using var capture = Capture.Element(preview);
                var pixel = capture.Bitmap.GetPixel(capture.Bitmap.Width * 5 / 8, capture.Bitmap.Height / 2);
                return Math.Abs(pixel.G - expected) <= 2;
            }, $"The preview did not reach the expected green level of {expected}.");
        }
    }
}
