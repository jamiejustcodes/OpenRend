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

    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task SourceMenusCreateSubclipsAndOpenTheParentRange()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var projectPath = Path.Combine(temporary, "source-menus.openrend");
        try
        {
            var file = Path.Combine(temporary, "three-scenes.mkv");
            await CreateSceneFixture(Path.Combine(Path.GetDirectoryName(executable)!, "ffmpeg.exe"), file);
            var source = new MediaAsset { Path = file, HasVideo = true, Width = 160, Height = 90, DurationSeconds = 3, FrameRate = 30 };
            var subclip = source.DeepClone();
            subclip.Id = Guid.NewGuid();
            subclip.DisplayName = "Blue subclip";
            subclip.SourceRange = new(1, 1, 1);
            subclip.DurationSeconds = 1;
            var originalEvent = new TimelineClip { MediaId = source.Id, SourceInFrame = 30, DurationFrames = 30, Name = "Trimmed event" };
            var subclipEvent = new TimelineClip { MediaId = subclip.Id, StartFrame = 45, DurationFrames = 30, Name = subclip.Name };
            await ProjectStore.SaveAsync(new()
            {
                Name = "UI integration", Width = 160, Height = 90, FrameRate = new(30, 1), Media = [source, subclip],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [originalEvent, subclipEvent] }]
            }, projectPath);

            using var running = Launch(executable, projectPath, temporary);
            var window = running.Window;
            OpenInTrimmerCommand(window, originalEvent.Id, "CreateEventSubclip").AsMenuItem().Invoke();
            Window? dialog = null;
            WaitFor(() => (dialog = window.ModalWindows.SingleOrDefault(modal => modal.Title == "Create Subclip")) is not null,
                "The event command did not open Create Subclip.");
            const string createdName = "Named from event";
            Find(dialog!, "SubclipName").AsTextBox().Text = createdName;
            Find(dialog!, "SubclipReversePlayback").Patterns.Toggle.Pattern.Toggle();
            var accept = dialog!.FindFirstDescendant(condition => condition.ByControlType(ControlType.Button).And(condition.ByName("OK")));
            Assert.NotNull(accept);
            accept.AsButton().Invoke();
            WaitFor(() => window.ModalWindows.Length == 0 && window.Title.Contains('*'), "Creating a subclip did not edit Project Media.");
            var saved = await SaveGrade(window, projectPath);
            Assert.Equal(new MediaSourceRange(1, 1, 1, true), Assert.Single(saved.Media, asset => asset.Name == createdName).SourceRange);
            Assert.Equal(new[] { originalEvent.Id, subclipEvent.Id }, saved.Tracks[0].Clips.Select(clip => clip.Id));

            OpenInTrimmerCommand(window, subclipEvent.Id, "OpenEventParentInTrimmer").AsMenuItem().Invoke();
            CheckTrimmerRange("00:00:00:00", "00:00:03:00");
            OpenInTrimmerCommand(window, subclipEvent.Id).AsMenuItem().Invoke();
            CheckTrimmerRange("00:00:00:00", "00:00:01:00");
            RightClick(Find(window, "TrimmerCreateSubclip"));
            PopupCommand(window, "TrimmerSelectParentMedia").AsMenuItem().Invoke();
            CheckTrimmerRange("00:00:01:00", "00:00:02:00");

            MediaContextCommand(window, createdName, "OpenMediaParentInTrimmer").AsMenuItem().Invoke();
            CheckTrimmerRange("00:00:00:00", "00:00:03:00");
            Assert.DoesNotContain('*', window.Title);
            running.CloseGracefully();

            void CheckTrimmerRange(string first, string last)
            {
                foreach (var readout in new[] { $"Cursor: {first}", $"In: {first}", $"Out: {last}" })
                    WaitFor(() => window.FindFirstDescendant(condition => condition.ByName(readout).And(condition.ByControlType(ControlType.Text))) is not null,
                        $"The Trimmer did not show {readout}.");
            }

        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static AutomationElement MediaContextCommand(Window window, string name, string id)
    {
        var tab = window.FindFirstDescendant(condition => condition.ByControlType(ControlType.TabItem).And(condition.ByName("Project Media")));
        Assert.NotNull(tab);
        tab.Patterns.SelectionItem.Pattern.Select();
        var list = Find(window, "MediaList").AsListBox();
        ListBoxItem? entry = null;
        WaitFor(() => (entry = list.Items.SingleOrDefault(item => item.FindAllDescendants().Any(child => child.Name == name))) is not null,
            $"Project Media did not show {name}.");
        entry!.Patterns.SelectionItem.Pattern.Select();
        RightClick(entry);
        return PopupCommand(window, id);
    }

    private static AutomationElement PopupCommand(Window window, string id)
    {
        AutomationElement? command = null;
        WaitFor(() => (command = window.Automation.GetDesktop().FindFirstDescendant(condition =>
            condition.ByAutomationId(id).And(condition.ByProcessId(window.Properties.ProcessId.Value)))) is not null,
            $"The menu did not expose {id}.");
        Assert.True(command!.IsEnabled);
        return command;
    }

    private static void RightClick(AutomationElement element)
    {
        var bounds = element.BoundingRectangle;
        Mouse.Click(new System.Drawing.Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2), MouseButton.Right);
    }
}
