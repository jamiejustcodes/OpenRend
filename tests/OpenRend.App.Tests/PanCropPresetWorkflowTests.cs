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
    public async Task PanCropPresetNameCanBeTypedAndDeletingItKeepsTheCrop()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "crop-preset.openrend");
        var preferencesPath = Path.Combine(temporary, "pancrop.json");
        var asset = new MediaAsset
        {
            Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
            Width = 320, Height = 180, DurationSeconds = 3, FrameRate = 30, HasVideo = true
        };
        var clip = new TimelineClip { MediaId = asset.Id, Name = "Crop source", DurationFrames = 90 };
        await ProjectStore.SaveAsync(new()
        {
            Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1),
            Media = [asset], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
        }, path);

        const string presetName = "Interview crop";
        try
        {
            using (var running = Launch(executable, path, temporary))
            {
                var crop = OpenCrop(running.Window);
                var width = Find(crop, "CropWidth").AsTextBox();
                width.Focus();
                width.Text = "120";
                Chord(crop, VirtualKeyShort.RETURN);
                WaitFor(() => Find(crop, "CropHeight").AsTextBox().Text == "67.5", "The crop geometry did not change.");

                var input = PresetInput(crop);
                Mouse.Click(input.GetClickablePoint());
                Chord(crop, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                Keyboard.Type(presetName);
                WaitFor(() => input.Text == presetName, "The editable preset field did not accept keyboard input.");
                Find(crop, "SaveCropPreset").AsButton().Invoke();
                WaitFor(() => PanCropPreferencesStore.Load(preferencesPath).CustomPresets.Any(preset => preset.Name == presetName),
                    "The named crop preset was not saved.");
                Assert.Equal(120, Assert.Single(PanCropPreferencesStore.Load(preferencesPath).CustomPresets).Width);
                crop.Close();
                SaveButton(running.Window).Invoke();
                WaitFor(() => !running.Window.Title.Contains('*'), "The crop edit did not save.");
                running.CloseGracefully();
            }

            using (var running = Launch(executable, path, temporary))
            {
                var crop = OpenCrop(running.Window);
                var input = PresetInput(crop);
                Assert.Equal(presetName, input.Text);
                var presets = Find(crop, "CropPresetCombo").AsComboBox();
                var width = Find(crop, "CropWidth").AsTextBox();
                presets.Select("Full frame");
                presets.Patterns.ExpandCollapse.Pattern.Collapse();
                WaitFor(() => width.Text == "320", "Full frame did not reset the crop.");
                presets.Select(presetName);
                presets.Patterns.ExpandCollapse.Pattern.Collapse();
                WaitFor(() => width.Text == "120" && input.Text == presetName, "Selecting the custom preset did not restore the crop.");

                Find(crop, "DeleteCropPreset").AsButton().Invoke();
                WaitFor(() => PanCropPreferencesStore.Load(preferencesPath).CustomPresets.Count == 0, "The custom preset was not removed.");
                Assert.Equal("120", width.Text);
                crop.Close();
                SaveButton(running.Window).Invoke();
                WaitFor(() => !running.Window.Title.Contains('*'), "The reapplied crop did not save.");
                var project = await ProjectStore.LoadAsync(path);
                Assert.Equal(120, project.Tracks[0].Clips[0].Crop!.Keyframes[0].State.Width);
                running.CloseGracefully();
            }
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }

        Window OpenCrop(Window owner)
        {
            FitTimeline(owner);
            Find(owner, $"Clip_{clip.Id:N}_PanCrop").AsButton().Invoke();
            Window? crop = null;
            WaitFor(() => (crop = owner.ModalWindows.SingleOrDefault(window => window.Title == "Video event Pan/Crop")) is not null,
                "Pan/Crop did not open.");
            return crop!;
        }

        static TextBox PresetInput(Window crop)
        {
            var input = Find(crop, "CropPresetCombo").FindFirstDescendant(c => c.ByControlType(ControlType.Edit));
            Assert.NotNull(input);
            return input.AsTextBox();
        }
    }
}
