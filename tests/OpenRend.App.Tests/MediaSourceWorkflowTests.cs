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
    public async Task StartupRelinkPreservesTheEventAndReadsTheReplacementChannels()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var path = Path.Combine(temporary, "relink.openrend");
            var asset = new MediaAsset { Path = Path.Combine(temporary, "missing.mp4"), HasVideo = true, HasAudio = true, DurationSeconds = 3 };
            var clip = new TimelineClip { MediaId = asset.Id, StartFrame = 14, SourceInFrame = 12, DurationFrames = 35 };
            await ProjectStore.SaveAsync(new ProjectDocument { Name = "UI integration", Media = [asset],
                Tracks = [new TimelineTrack { Kind = TrackKind.Video, Clips = [clip] }] }, path);

            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            Window? chooser = null;
            WaitFor(() => (chooser = window.ModalWindows.FirstOrDefault(candidate => candidate.Title.StartsWith("Locate missing media"))) is not null,
                "The startup relink dialog did not open.");
            var name = chooser!.FindFirstDescendant(c => c.ByControlType(ControlType.Edit).And(c.ByName("File name:").Or(c.ByAutomationId("1148"))));
            Assert.NotNull(name);
            var replacement = Path.Combine(root, "tests", "native", "fixture.mp4");
            name.AsTextBox().Text = replacement;
            var open = chooser.FindFirstDescendant(c => c.ByControlType(ControlType.Button).And(c.ByAutomationId("1")));
            Assert.NotNull(open);
            open.AsButton().Invoke();
            WaitFor(() => window.Title.Contains('*'), "Relinking did not update the project.");
            SaveButton(window).Invoke();
            WaitFor(() => !window.Title.Contains('*'), "The relinked project did not save.");
            var reopened = await ProjectStore.LoadAsync(path);
            var saved = Assert.Single(reopened.Media);
            var edit = Assert.Single(reopened.Tracks[0].Clips);
            Assert.Equal((asset.Id, replacement, 1, 48000), (saved.Id, saved.Path, saved.AudioChannels, saved.AudioSampleRate));
            Assert.Equal((clip.Id, clip.StartFrame, clip.SourceInFrame, clip.DurationFrames), (edit.Id, edit.StartFrame, edit.SourceInFrame, edit.DurationFrames));
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task ExplorerAutoPlayPreviewsAFileWithoutImportingIt()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var path = Path.Combine(temporary, "empty.openrend");
            var source = Path.Combine(temporary, "source.mp4");
            File.Copy(Path.Combine(root, "tests", "native", "fixture.mp4"), source);
            await ProjectStore.SaveAsync(new ProjectDocument { Name = "UI integration" }, path);
            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            Find(window, "ExplorerTab").AsTabItem().Select();
            var folder = Find(window, "ExplorerPathBox").AsTextBox();
            var bounds = folder.BoundingRectangle;
            Assert.Equal(window.Properties.ProcessId.Value, window.Automation.FromPoint(new System.Drawing.Point(bounds.Left + 10, bounds.Top + 8)).Properties.ProcessId.Value);
            folder.Text = temporary;
            folder.Focus();
            WaitFor(() => folder.Properties.HasKeyboardFocus.Value, "Explorer path did not receive keyboard focus.");
            Keyboard.Type(VirtualKeyShort.RETURN);
            var entries = Find(window, "ExplorerList").AsListBox();
            WaitFor(() => entries.Items.Length == 1, "Explorer did not show the source file.");
            var monitorMute = window.FindFirstDescendant(c => c.ByName("Mute monitor"));
            Assert.NotNull(monitorMute);
            monitorMute.AsButton().Invoke();
            var autoplay = Find(window, "ExplorerAutoPlay").AsToggleButton();
            WaitFor(() => PresentedFrame(window) == 0, "The initial empty timeline frame did not render.");
            var surface = Assert.Single(window.FindAllDescendants(c => c.ByClassName("OpenRend.Preview")));
            var emptyPixels = SamplePreview(surface);
            autoplay.Toggle();
            entries.Items[0].Select();
            var preview = Find(window, "PreviewInfo");
            WaitFor(() => preview.Name == "Source preview · source.mp4", "Explorer selection did not open the source preview.");
            WaitFor(() => PreviewDifference(emptyPixels, SamplePreview(surface)) > 20, "The source never drew video in the native preview.");
            Assert.DoesNotContain('*', window.Title);
            autoplay.Toggle();
            WaitFor(() => preview.Name.Contains("Frame:"), "Disabling autoplay did not restore the timeline preview.");
            WaitFor(() => PreviewDifference(emptyPixels, SamplePreview(surface)) < 1, "The empty timeline frame did not replace the source preview.");
            Assert.Empty((await ProjectStore.LoadAsync(path)).Media);
            running.CloseGracefully(waitForRender: false);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
