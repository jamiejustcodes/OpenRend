using System.Globalization;
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
    public async Task ClipPropertiesValidateAndSaveLinkedEventTimingInOneUndoStep()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "event-properties.openrend");
        var asset = new MediaAsset
        {
            Path = Path.Combine(root, "tests", "native", "fixture.mp4"), Width = 320, Height = 180,
            DurationSeconds = 3, FrameRate = 30, HasVideo = true, HasAudio = true
        };
        var video = new TimelineClip
        {
            MediaId = asset.Id, GroupId = Guid.NewGuid(), Name = "Video target", StartFrame = 20, SourceInFrame = 5, DurationFrames = 30,
            FadeInFrames = 8, FadeOutFrames = 6, FadeOffsetFrames = 2, FadeDurationFrames = 36,
            Takes =
            [
                new() { MediaId = asset.Id, Name = "Video target", SourceInFrame = 5 },
                new() { MediaId = asset.Id, Name = "Alternate take", SourceInFrame = 3 }
            ]
        };
        var audio = new TimelineClip
        {
            MediaId = asset.Id, GroupId = video.GroupId, Name = "Linked audio", StartFrame = 20, SourceInFrame = 8, DurationFrames = 30
        };
        await ProjectStore.SaveAsync(new ProjectDocument
        {
            Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1), Media = [asset],
            Tracks = [new() { Kind = TrackKind.Audio, Clips = [audio] }, new() { Kind = TrackKind.Video, Clips = [video] }]
        }, path);

        try
        {
            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            Find(window, $"Clip_{video.Id:N}").Patterns.SelectionItem.Pattern.Select();
            SelectBrowserTab(window, "Clip Properties");
            var name = Find(window, "ClipName");
            var apply = Find(window, "ApplyClipPropertiesButton").AsButton();
            var duration = Find(window, "ClipDuration").AsTextBox();
            Assert.Equal(video.Name, name.Name);
            Find(window, $"Clip_{audio.Id:N}").Patterns.SelectionItem.Pattern.Select();
            WaitFor(() => name.Name == audio.Name && Find(window, "ClipGain").IsEnabled, "The form did not follow the linked audio event.");
            Find(window, $"Clip_{video.Id:N}").Patterns.SelectionItem.Pattern.Select();
            WaitFor(() => name.Name == video.Name, "The form did not return to the video event.");
            apply.Invoke();
            Assert.DoesNotContain('*', window.Title);

            duration.Text = "0";
            duration.Focus();
            Keyboard.Type(VirtualKeyShort.RETURN);
            WaitFor(() => Find(window, "ClipPropertyError").Name == "Enter a duration of at least one frame.",
                "Invalid input did not receive inline validation.");
            Assert.Empty(window.ModalWindows);
            Assert.DoesNotContain('*', window.Title);
            var artifacts = Path.Combine(root, ".local", "desktop-validation");
            Directory.CreateDirectory(artifacts);
            using (var screenshot = Capture.Element(window)) screenshot.ToFile(Path.Combine(artifacts, "clip-properties-validation.png"));

            duration.Text = "1000";
            apply.Invoke();
            WaitFor(() => Find(window, "ClipPropertyError").Name.Contains("beyond the source media", StringComparison.Ordinal),
                "The source-range validation message did not appear.");
            Assert.Equal("1000", duration.Text);
            Assert.DoesNotContain('*', window.Title);

            duration.Text = "30";
            Find(window, "ClipStart").AsTextBox().Text = "25";
            Find(window, "ClipIn").AsTextBox().Text = "10";
            Find(window, "ClipOpacity").AsTextBox().Text = .75.ToString(CultureInfo.CurrentCulture);
            apply.Invoke();
            WaitFor(() => window.Title.Contains('*'), "Applying valid properties did not edit the project.");
            SaveButton(window).Invoke();
            WaitFor(() => !window.Title.Contains('*'), "Saving the event properties did not finish.");
            var saved = await ProjectStore.LoadAsync(path);
            var savedVideo = Assert.Single(saved.Tracks[1].Clips);
            var savedAudio = Assert.Single(saved.Tracks[0].Clips);
            Assert.Equal((25L, 10L, 30L), (savedVideo.StartFrame, savedVideo.SourceInFrame, savedVideo.DurationFrames));
            Assert.Equal((25L, 13L, 30L), (savedAudio.StartFrame, savedAudio.SourceInFrame, savedAudio.DurationFrames));
            Assert.Equal((8L, 6L, 2L, 36L), (savedVideo.FadeInFrames, savedVideo.FadeOutFrames, savedVideo.FadeOffsetFrames, savedVideo.FadeDurationFrames!.Value));
            Assert.Equal(.75, savedVideo.Opacity);
            Assert.Equal(10, savedVideo.Takes![0].SourceInFrame);
            Assert.Equal(3, savedVideo.Takes[1].SourceInFrame);

            apply.Focus();
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            WaitFor(() => Find(window, "ClipStart").AsTextBox().Text == "20", "Undo did not restore the displayed timing.");
            SaveButton(window).Invoke();
            WaitFor(() => !window.Title.Contains('*'), "Saving the undo did not finish.");
            saved = await ProjectStore.LoadAsync(path);
            Assert.Equal(8, Assert.Single(saved.Tracks[0].Clips).SourceInFrame);
            Assert.Equal(5, Assert.Single(saved.Tracks[1].Clips).Takes![0].SourceInFrame);

            var lockEvent = Find(window, "LockEventButton");
            Mouse.Click(lockEvent.GetClickablePoint());
            WaitFor(() => lockEvent.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On && !apply.IsEnabled,
                "Lock did not set the toggle and disable property edits.");
            Mouse.Click(lockEvent.GetClickablePoint());
            WaitFor(() => lockEvent.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.Off && apply.IsEnabled,
                "Unlock did not restore property editing.");
            SaveButton(window).Invoke();
            WaitFor(() => !window.Title.Contains('*'), "The clean project was not saved.");
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
