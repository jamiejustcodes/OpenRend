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
                Width = 320, Height = 180, DurationSeconds = 3, FrameRate = 30, HasVideo = true, HasAudio = true
            };
            var clip = new TimelineClip { MediaId = media.Id, SourceInFrame = 30, DurationFrames = 45 };
            var audio = new TimelineTrack { Kind = TrackKind.Audio };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1),
                Media = [media], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }, audio]
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

            var handle = Find(window, "TrimmerStream_AudioOnly").BoundingRectangle;
            var eventBounds = Find(window, $"Clip_{clip.Id:N}").BoundingRectangle;
            var audioBounds = Find(window, $"Track_{audio.Id:N}").BoundingRectangle;
            var from = new System.Drawing.Point(handle.Left + handle.Width / 2, handle.Top + handle.Height / 2);
            var to = new System.Drawing.Point(eventBounds.Left + eventBounds.Width / 3, audioBounds.Top + 20);
            foreach (var point in new[] { from, to })
                Assert.Equal(window.Properties.ProcessId.Value, window.Automation.FromPoint(point).Properties.ProcessId.Value);
            Mouse.Drag(from, to, MouseButton.Left);
            WaitFor(() => window.Title.Contains('*'), "Dragging the audio handle did not place a clip.");
            SaveButton(window).Invoke();
            WaitFor(() => !window.Title.Contains('*'), "The stream-only Trimmer drop did not save.");
            var placement = await ProjectStore.LoadAsync(path);
            Assert.Single(placement.Tracks[0].Clips);
            var sound = Assert.Single(placement.Tracks[1].Clips);
            Assert.Equal((15L, 30L, 45L), (sound.StartFrame, sound.SourceInFrame, sound.DurationFrames));
            Assert.Null(sound.GroupId);
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
