using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.WindowsAPI;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.App.Tests;

public sealed partial class EditorWorkflowTests
{
    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task NewGradesUseTheCurrentSequenceAndOlderGradesKeepTheirRenderedResult()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "grading-sequence.openrend");
        try
        {
            var asset = Solid("Gray plate", "#404040");
            var oldGrade = new VideoEffect { Kind = VideoEffectKind.ColorGrading };
            oldGrade.SetParameter("BrightnessAdjust", .1);
            oldGrade.SetParameter("ExposureLnAdjust", 1);
            var first = new TimelineClip { Name = "Saved grade", MediaId = asset.Id, DurationFrames = 1, VideoEffects = [oldGrade] };
            var second = new TimelineClip { Name = "New grade", MediaId = asset.Id, StartFrame = 1, DurationFrames = 90 };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 640, Height = 360, Media = [asset],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [first, second] }]
            }, path);
            Assert.DoesNotContain(nameof(VideoEffect.GradingSequence), await File.ReadAllTextAsync(path));

            using (var running = Launch(executable, path, directory))
            {
                var main = running.Window;
                var preview = Preview(main);
                WaitFor(() => PresentedFrame(main) == 0 && Pixel(preview) is >= 153 and <= 154, "The saved project grade changed on open.");
                Find(main, $"Clip_{second.Id:N}").Patterns.SelectionItem.Pattern.Select();
                Chord(main, VirtualKeyShort.ALT, VirtualKeyShort.RIGHT);
                WaitFor(() => PresentedFrame(main) == 1 && Pixel(preview) == 64, "The new grade test frame did not load.");
                SelectBrowserTab(main, "Color Grading");
                Find(main, "UtilitiesItem").Patterns.SelectionItem.Pattern.Select();
                Find(main, "brightness").Patterns.RangeValue.Pattern.SetValue(.1);
                Find(main, "logExposure").Patterns.RangeValue.Pattern.SetValue(1);
                WaitFor(() => Pixel(preview) == 179, "The new grade did not apply brightness before exposure.");
                var saved = await SaveGrade(main, path);
                var clips = saved.Tracks[0].Clips;
                Assert.Equal(ColorGradingSequence.Legacy, Assert.Single(clips[0].VideoEffects).GradingSequence);
                var newGrade = Assert.Single(clips[1].VideoEffects);
                Assert.Equal(ColorGradingSequence.CurvesBeforeWheels, newGrade.GradingSequence);
                Assert.Equal(.1, newGrade.GetParameter("BrightnessAdjust"));
                Assert.Equal(1, newGrade.GetParameter("ExposureLnAdjust"));
                running.CloseGracefully();
            }
            using (var running = Launch(executable, path, directory))
            {
                var main = running.Window;
                var preview = Preview(main);
                WaitFor(() => PresentedFrame(main) == 0 && Pixel(preview) is >= 153 and <= 154, "The old grade changed after saving the new grade.");
                Chord(main, VirtualKeyShort.ALT, VirtualKeyShort.RIGHT);
                WaitFor(() => PresentedFrame(main) == 1, "The frame-step shortcut did not reach the newly graded clip after reopening.");
                WaitFor(() => Pixel(preview) == 179, "Reopening did not restore the new grade sequence.");
                running.CloseGracefully();
            }
        }
        finally { Directory.Delete(directory, recursive: true); }

        static AutomationElement Preview(Window window) =>
            Assert.Single(window.FindAllDescendants(condition => condition.ByClassName("OpenRend.Preview")));

        static int Pixel(AutomationElement preview)
        {
            using var image = Capture.Element(preview);
            return image.Bitmap.GetPixel(image.Bitmap.Width / 2, image.Bitmap.Height / 2).R;
        }
    }
}
