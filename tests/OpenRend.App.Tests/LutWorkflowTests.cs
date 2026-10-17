using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.App.Tests;

public sealed partial class EditorWorkflowTests
{
    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task ImportedCubeLutSurvivesClearUndoSaveAndReopenWithoutTheSourceFile()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "lut.openrend");
        var lutPath = Path.Combine(temporary, "swap.cube");
        await File.WriteAllTextAsync(lutPath, "TITLE \"Swap\"\nLUT_3D_SIZE 2\n0 0 0\n0 0 1\n0 1 0\n0 1 1\n1 0 0\n1 0 1\n1 1 0\n1 1 1\n");
        var media = new MediaAsset { Path = Path.Combine(root, "tests", "native", "fixture.mp4"), Width = 320, Height = 180, DurationSeconds = 3, HasVideo = true };
        var clip = new TimelineClip { MediaId = media.Id, DurationFrames = 90, VideoEffects = [VideoEffect.Create(VideoEffectKind.Lut3D)] };
        await ProjectStore.SaveAsync(new() { Name = "UI integration", Width = 320, Height = 180, Media = [media],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] }, path);
        Window OpenEffects(Window window)
        {
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
            Find(window, $"Clip_{clip.Id:N}_VideoEffects").AsButton().Invoke();
            Window? effects = null;
            WaitFor(() => (effects = window.ModalWindows.SingleOrDefault(dialog => dialog.Title == "Video event FX")) is not null,
                "Video event FX did not open.");
            return effects!;
        }
        try
        {
            using (var running = Launch(executable, path, temporary))
            {
                var window = running.Window;
                var effects = OpenEffects(window);
                Find(effects, "BrowseLut").AsButton().Invoke();
                Window? browse = null;
                WaitFor(() => (browse = effects.ModalWindows.SingleOrDefault(dialog => dialog.Title == "Import 3D LUT")) is not null,
                    "Import LUT did not open its file dialog.");
                var controls = browse!.FindAllDescendants();
                Assert.Single(controls, element => element.ControlType == ControlType.Edit && element.Name.Contains("File name", StringComparison.OrdinalIgnoreCase))
                    .AsTextBox().Text = lutPath;
                Assert.Single(controls, element => element.ControlType == ControlType.Button && element.Properties.AutomationId.ValueOrDefault == "1")
                    .AsButton().Invoke();
                WaitFor(() => Find(effects, "LutDetails").Name.Contains("Swap"), "The imported LUT was not attached to the event.");
                Find(effects, "ClearLut").AsButton().Invoke();
                Assert.Contains("No LUT loaded", Find(effects, "LutDetails").Name);
                Find(effects, "UndoVideoEffect").AsButton().Invoke();
                Assert.Contains("Swap", Find(effects, "LutDetails").Name);
                var strength = Find(effects, "VideoEffectAmount").AsTextBox(); strength.Focus(); strength.Text = "0.5";
                Chord(effects, VirtualKeyShort.RETURN);
                var interpolation = Find(effects, "VideoEffect_Interpolation").AsComboBox();
                interpolation.Select(1); interpolation.Patterns.ExpandCollapse.Pattern.Collapse();
                var artifacts = Path.Combine(root, ".local", "desktop-validation"); Directory.CreateDirectory(artifacts);
                using (var screenshot = Capture.Element(effects)) screenshot.ToFile(Path.Combine(artifacts, "lut-filter.png"));
                effects.Close(); SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "The imported LUT was not saved.");
                var saved = Assert.Single((await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0].VideoEffects);
                Assert.NotNull(saved.Lut);
                Assert.Equal("Swap", saved.Lut.Title);
                Assert.Equal(.5, saved.Amount); Assert.Equal(1, saved.GetParameter("Interpolation"));
                running.CloseGracefully();
            }
            File.Delete(lutPath);
            using var reopened = Launch(executable, path, temporary);
            var reopenedEffects = OpenEffects(reopened.Window);
            Assert.Contains("Swap", Find(reopenedEffects, "LutDetails").Name);
            Assert.Equal("0.5", Find(reopenedEffects, "VideoEffectAmount").AsTextBox().Text);
            reopenedEffects.Close();
            reopened.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
