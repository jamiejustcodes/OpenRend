using FlaUI.Core.AutomationElements;
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
    public async Task OpenEventInTrimmerUsesTrimmedSourceRange()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var path = Path.Combine(temporary, "trimmed.openrend");
            var media = new MediaAsset
            {
                Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
                Width = 320, Height = 180, DurationSeconds = 3, FrameRate = 30, HasVideo = true
            };
            var clip = new TimelineClip { MediaId = media.Id, SourceInFrame = 30, DurationFrames = 45 };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1),
                Media = [media], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
            }, path);

            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
            Find(window, $"Clip_{clip.Id:N}_More").AsButton().Invoke();
            AutomationElement? command = null;
            WaitFor(() => (command = window.Automation.GetDesktop().FindFirstDescendant(c =>
                c.ByAutomationId("OpenEventInTrimmer").And(c.ByProcessId(window.Properties.ProcessId.Value)))) is not null,
                "The timeline did not expose Open in Trimmer.");
            Assert.True(command!.IsEnabled);
            var titleBefore = window.Title;
            var undoBefore = Find(window, "UndoButton").IsEnabled;
            command.AsMenuItem().Invoke();

            foreach (var readout in new[] { "Cursor: 00:00:01:00", "In: 00:00:01:00", "Out: 00:00:02:15", "Length: 00:00:01:15" })
                WaitFor(() => window.FindFirstDescendant(c => c.ByControlType(ControlType.Text).And(c.ByName(readout))) is not null,
                    $"The Trimmer did not show {readout}.");
            Assert.Equal(titleBefore, window.Title);
            Assert.Equal(undoBefore, Find(window, "UndoButton").IsEnabled);
            if (window.Title.Contains('*'))
            {
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "The disposable project did not save.");
            }
            var saved = Assert.Single((await ProjectStore.LoadAsync(path)).Tracks[0].Clips);
            Assert.Equal((clip.SourceInFrame, clip.DurationFrames), (saved.SourceInFrame, saved.DurationFrames));
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
