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
    public async Task ExportGradingLutWritesOnlyTheChosenTarget()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "LUT export.openrend");
        var outputPath = Path.Combine(directory, "Output Grade.cube");
        try
        {
            var asset = Solid("Gray", "#808080");
            var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 90, VideoEffects = [Grade(.3)] };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 640, Height = 360, Media = [asset], VideoOutputEffects = [Grade(.1)],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [clip], VideoEffects = [Grade(.2)] }]
            }, projectPath);
            using var running = Launch(executable, projectPath, directory);
            var main = running.Window;
            Chord(main, VirtualKeyShort.ALT, VirtualKeyShort.KEY_G);
            WaitFor(() => DockedPane(main, "Color Grading")?.IsSelected == true, "Color Grading did not open.");
            var export = Find(main, "ColorGrading_ExportLut").AsButton();
            Assert.False(export.IsEnabled);
            var scope = Find(main, "ColorGrading_Scope").AsComboBox();
            scope.Select(3);
            scope.Patterns.ExpandCollapse.Pattern.Collapse();
            Assert.True(export.IsEnabled);
            Mouse.Click(export.GetClickablePoint());
            Window? dialog = null;
            var desktop = main.Automation.GetDesktop();
            try
            {
                WaitFor(() => (dialog = main.ModalWindows.FirstOrDefault(IsExportDialog) ??
                    desktop.FindFirstChild(condition => condition.ByName("Export Color Grading LUT")
                        .And(condition.ByProcessId(main.Properties.ProcessId.Value)))?.AsWindow()) is not null,
                    "The native LUT save dialog did not open.");
            }
            catch
            {
                var diagnostic = Path.Combine(root, ".local", "desktop-validation");
                Directory.CreateDirectory(diagnostic);
                using var screenshot = Capture.Screen(0);
                screenshot.ToFile(Path.Combine(diagnostic, "grading-lut-export-diagnostic.png"));
                throw;
            }
            var name = dialog!.FindFirstDescendant(condition => condition.ByControlType(ControlType.Edit).And(condition.ByAutomationId("1001")));
            Assert.NotNull(name);
            name.AsTextBox().Text = outputPath;
            var save = dialog.FindFirstDescendant(condition => condition.ByControlType(ControlType.Button).And(condition.ByAutomationId("1")));
            Assert.NotNull(save);
            save.AsButton().Invoke();
            WaitFor(() => File.Exists(outputPath), "Saving did not produce a CUBE file.");
            var lut = CubeLutParser.Load(outputPath);
            Assert.Equal(33, lut.Size);
            Assert.Equal(new[] { .1, .1, .1 }, lut.Samples[..3]);
            Assert.DoesNotContain('*', main.Title);
            running.CloseGracefully();
        }
        finally { Directory.Delete(directory, recursive: true); }

        static VideoEffect Grade(double brightness)
        {
            var grade = VideoEffect.Create(VideoEffectKind.ColorGrading);
            grade.SetParameter("BrightnessAdjust", brightness);
            return grade;
        }

        static bool IsExportDialog(Window window) => window.Title == "Export Color Grading LUT";
    }
}
