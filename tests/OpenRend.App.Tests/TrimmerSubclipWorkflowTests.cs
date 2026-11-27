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
    public async Task TrimmerCreatesNamedSubclipFromTheOpenedEventRange()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var projectPath = Path.Combine(temporary, "subclip.openrend");
        try
        {
            var source = Path.Combine(temporary, "three-scenes.mkv");
            await CreateSceneFixture(Path.Combine(Path.GetDirectoryName(executable)!, "ffmpeg.exe"), source);
            var asset = new MediaAsset
            {
                Path = source, HasVideo = true, Width = 160, Height = 90, DurationSeconds = 3, FrameRate = 30
            };
            var clip = new TimelineClip { MediaId = asset.Id, SourceInFrame = 30, DurationFrames = 30, Name = "Blue event" };
            await ProjectStore.SaveAsync(new()
            {
                Name = "UI integration", Width = 160, Height = 90, FrameRate = new(30, 1), Media = [asset],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
            }, projectPath);

            using var running = Launch(executable, projectPath, temporary);
            var window = running.Window;
            OpenInTrimmerCommand(window, clip.Id).AsMenuItem().Invoke();
            Find(window, "TrimmerCreateSubclip").AsButton().Invoke();
            WaitFor(() => window.ModalWindows.Any(modal => modal.Title == "Create Subclip"), "Create Subclip did not open.");
            var dialog = window.ModalWindows.Single(modal => modal.Title == "Create Subclip");
            var name = Find(dialog, "SubclipName").AsTextBox();
            var acceptElement = dialog.FindFirstDescendant(condition => condition.ByName("OK"));
            Assert.NotNull(acceptElement);
            var accept = acceptElement.AsButton();
            name.Text = " ";
            accept.Invoke();
            WaitFor(() => Find(dialog, "DialogValidationError").Name == "Enter a subclip name.", "A blank name was accepted.");
            Assert.DoesNotContain('*', window.Title);
            name.Text = "Blue selection";
            var captureDir = Path.Combine(root, ".local", "desktop-validation");
            Directory.CreateDirectory(captureDir);
            using (var screenshot = Capture.Element(dialog)) screenshot.ToFile(Path.Combine(captureDir, "create-subclip.png"));
            accept.Invoke();
            WaitFor(() => window.ModalWindows.Length == 0 && window.Title.Contains('*'), "The subclip was not committed.");
            var saved = await SaveGrade(window, projectPath);
            var subclip = Assert.Single(saved.Media, item => item.Id != asset.Id);
            Assert.Equal("Blue selection", subclip.Name);
            Assert.Equal(source, subclip.Path);
            Assert.Equal(new MediaSourceRange(1, 1, 1), subclip.SourceRange);
            Assert.Equal(clip.Id, Assert.Single(saved.Tracks[0].Clips).Id);

            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            saved = await SaveGrade(window, projectPath);
            Assert.Single(saved.Media);
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
