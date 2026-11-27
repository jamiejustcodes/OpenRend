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
    public Task SqueezeOriginChangesPreviewAndRoundTripsTheSelectedDirection() =>
        VerifySpatialTransitionDirection(VideoTransitionKind.Squeeze, "Squeeze Left-Right", startsWithGreen: true, savedDirection: "2");

    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public Task SwapDirectionChangesPreviewAndSurvivesReopen() =>
        VerifySpatialTransitionDirection(VideoTransitionKind.Swap, "Swap Left", startsWithGreen: false, savedDirection: "3");

    private async Task VerifySpatialTransitionDirection(VideoTransitionKind kind, string preset,
        bool startsWithGreen, string savedDirection)
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "spatial-transition.openrend");
        try
        {
            var red = new MediaAsset { Path = "openrend://color?hex=#C80000", HasVideo = true };
            var green = new MediaAsset { Path = "openrend://color?hex=#00C800", HasVideo = true };
            var outgoing = new TimelineClip { MediaId = red.Id, Name = "Outgoing", DurationFrames = 36, FadeOutFrames = 24 };
            var incoming = new TimelineClip { MediaId = green.Id, Name = "Incoming", StartFrame = 12, DurationFrames = 36, FadeInFrames = 24,
                FadeInTransition = new VideoTransition { Kind = kind, Preset = preset } };
            await ProjectStore.SaveAsync(new ProjectDocument { Name = "UI integration", Width = 320, Height = 180,
                FrameRate = new(30, 1), Media = [red, green], Tracks = [new() { Kind = TrackKind.Video, Clips = [outgoing, incoming] }] }, path);

            using (var running = Launch(executable, path, directory))
            {
                var main = running.Window;
                SeekToMiddle(main);
                CheckSide(main, startsWithGreen);
                Find(main, $"Clip_{incoming.Id:N}_Transition").AsButton().Invoke();
                Window? dialog = null;
                WaitFor(() => (dialog = main.ModalWindows.SingleOrDefault(window => window.Title == "Video event FX")) is not null,
                    "Transition properties did not open.");
                var properties = dialog!;
                var area = main.BoundingRectangle;
                properties.Patterns.Transform.Pattern.Move(area.Left + 12, area.Bottom - properties.BoundingRectangle.Height - 32);
                Assert.Equal("Left", Find(properties, "TransitionParameter_Direction").AsComboBox().SelectedItem?.Name);
                Find(properties, "TransitionParameter_Direction").AsComboBox().Select("Right");
                CheckSide(main, !startsWithGreen);
                Find(properties, "UndoTransitionEdit").AsButton().Invoke();
                CheckSide(main, startsWithGreen);
                Find(properties, "RedoTransitionEdit").AsButton().Invoke();
                CheckSide(main, !startsWithGreen);
                Find(properties, "CloseTransitionProperties").AsButton().Invoke();
                WaitFor(() => main.ModalWindows.Length == 0, "Transition properties did not close.");
                var saved = await SaveGrade(main, path);
                var savedTransition = saved.Tracks[0].Clips[1].FadeInTransition;
                Assert.Equal(kind, savedTransition!.Kind);
                Assert.Equal(savedDirection, savedTransition.Parameters!["Direction"]);
                Assert.Equal(preset, savedTransition.Preset);
                running.CloseGracefully();
            }

            using (var running = Launch(executable, path, directory))
            {
                SeekToMiddle(running.Window);
                CheckSide(running.Window, !startsWithGreen);
                running.CloseGracefully();
            }

            void SeekToMiddle(Window main)
            {
                Chord(main, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
                var bounds = Find(main, $"Clip_{outgoing.Id:N}").BoundingRectangle;
                Mouse.Click(new Point(bounds.Left + (int)Math.Round(bounds.Width * 2 / 3d), bounds.Top - 10));
                WaitFor(() => PresentedFrame(main) == 24, "The timeline did not seek to the middle of the effect.");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }

        static void CheckSide(Window main, bool green)
        {
            var preview = Assert.Single(main.FindAllDescendants(c => c.ByClassName("OpenRend.Preview")));
            WaitFor(() =>
            {
                using var capture = Capture.Element(preview);
                var width = Math.Min(capture.Bitmap.Width, capture.Bitmap.Height * 16 / 9);
                var pixel = capture.Bitmap.GetPixel(capture.Bitmap.Width / 2 - width / 4, capture.Bitmap.Height / 2);
                return green ? pixel.G > 190 && pixel.R < 10 : pixel.R > 190 && pixel.G < 10;
            }, $"The transition did not render the expected left-side {(green ? "green" : "red")} image.");
        }
    }
}
