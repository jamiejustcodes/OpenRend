using System.Drawing;
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
            var alternate = new TimelineTrack { Kind = TrackKind.Video, Name = "Alternate video" };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1),
                Media = [media], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }, audio, alternate]
            }, path);

            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            FitTimeline(window);
            AutomationElement? clipElement = null;
            WaitFor(() => (clipElement = window.FindFirstDescendant(c => c.ByAutomationId($"Clip_{clip.Id:N}")))?.BoundingRectangle.Width > 100,
                "Fit Timeline did not expand the timeline event.");
            var clipBounds = clipElement!.BoundingRectangle;
            var menuPoint = new Point(clipBounds.Left + clipBounds.Width / 2, clipBounds.Top + clipBounds.Height / 2);
            Assert.Equal(window.Properties.ProcessId.Value, window.Automation.FromPoint(menuPoint).Properties.ProcessId.Value);
            Mouse.Click(menuPoint, MouseButton.Right);
            AutomationElement? command = null;
            WaitFor(() => (command = window.Automation.GetDesktop().FindFirstDescendant(c =>
                c.ByAutomationId("OpenEventInTrimmer").And(c.ByProcessId(window.Properties.ProcessId.Value)))) is not null,
                "The timeline did not expose Open in Trimmer.");
            Assert.True(command!.IsEnabled);
            var titleBefore = window.Title;
            var undoBefore = Find(window, "UndoButton").IsEnabled;
            command.AsMenuItem().Invoke();

            CheckReadouts("Cursor: 00:00:01:00", "In: 00:00:01:00", "Out: 00:00:02:15", "Length: 00:00:01:15");
            Assert.Equal(titleBefore, window.Title);
            Assert.Equal(undoBefore, Find(window, "UndoButton").IsEnabled);
            if (window.Title.Contains('*'))
            {
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "The disposable project did not save.");
            }
            var saved = Assert.Single((await ProjectStore.LoadAsync(path)).Tracks[0].Clips);
            Assert.Equal((clip.SourceInFrame, clip.DurationFrames), (saved.SourceInFrame, saved.DurationFrames));

            var eventBounds = Find(window, $"Clip_{clip.Id:N}").BoundingRectangle;
            var audioBounds = Find(window, $"Track_{audio.Id:N}").BoundingRectangle;
            var dropX = eventBounds.Left + eventBounds.Width / 3;
            DragStream("TrimmerStream_AudioOnly", new Point(dropX, audioBounds.Top + 20));
            var placement = await SavePlacement();
            Assert.Single(placement.Tracks[0].Clips);
            var sound = Assert.Single(placement.Tracks[1].Clips);
            Assert.Equal((15L, 30L, 45L), (sound.StartFrame, sound.SourceInFrame, sound.DurationFrames));
            Assert.Null(sound.GroupId);

            DragStream("TrimmerStream_VideoOnly", new Point(dropX, eventBounds.Top + eventBounds.Height / 2));
            placement = await SavePlacement();
            Assert.Single(placement.Tracks[1].Clips);
            var picture = Assert.Single(placement.Tracks[0].Clips, item => item.Id != clip.Id);
            Assert.Equal((15L, 30L, 45L), (picture.StartFrame, picture.SourceInFrame, picture.DurationFrames));
            Assert.Null(picture.GroupId);

            var videoOnly = Find(window, "TrimmerStream_VideoOnly");
            videoOnly.AsButton().Invoke();
            SeekOneSecondOnAlternateTrack(videoOnly);
            Type(VirtualKeyShort.RIGHT, VirtualKeyShort.KEY_I, VirtualKeyShort.END, VirtualKeyShort.LEFT, VirtualKeyShort.KEY_O);
            CheckReadouts("In: 00:00:01:01", "Out: 00:00:02:29", "Length: 00:00:01:28");
            Type(VirtualKeyShort.KEY_A);
            placement = await SavePlacement();
            var added = Assert.Single(placement.Tracks[2].Clips);
            Assert.Equal((30L, 31L, 58L), (added.StartFrame, added.SourceInFrame, added.DurationFrames));

            SeekOneSecondOnAlternateTrack(videoOnly);
            using (Keyboard.Pressing(VirtualKeyShort.SHIFT))
                Type(VirtualKeyShort.KEY_A);
            placement = await SavePlacement();
            var beforeCursor = Assert.Single(placement.Tracks[2].Clips, item => item.Id != added.Id);
            Assert.Equal((0L, 59L, 30L), (beforeCursor.StartFrame, beforeCursor.SourceInFrame, beforeCursor.DurationFrames));
            Assert.Equal(2, placement.Tracks[0].Clips.Count);
            Assert.Single(placement.Tracks[1].Clips);
            running.CloseGracefully();

            void CheckReadouts(params string[] values)
            {
                foreach (var value in values)
                    WaitFor(() => window.FindFirstDescendant(c => c.ByControlType(ControlType.Text).And(c.ByName(value))) is not null,
                        $"The Trimmer did not show {value}.");
            }

            void DragStream(string automationId, Point target)
            {
                var bounds = Find(window, automationId).BoundingRectangle;
                var source = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                foreach (var point in new[] { source, target })
                    Assert.Equal(window.Properties.ProcessId.Value, window.Automation.FromPoint(point).Properties.ProcessId.Value);
                Mouse.Drag(source, target, MouseButton.Left);
                WaitFor(() => window.Title.Contains('*'), $"Dragging {automationId} did not place a clip.");
            }

            async Task<ProjectDocument> SavePlacement()
            {
                WaitFor(() => window.Title.Contains('*'), "The Trimmer placement did not edit the project.");
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "The Trimmer placement did not save.");
                return await ProjectStore.LoadAsync(path);
            }

            void SeekOneSecondOnAlternateTrack(AutomationElement trimmerButton)
            {
                Find(window, $"Track_{alternate.Id:N}").Focus();
                Type(VirtualKeyShort.HOME);
                using (Keyboard.Pressing(VirtualKeyShort.SHIFT))
                    Type(VirtualKeyShort.RIGHT);
                trimmerButton.Focus();
            }

            static void Type(params VirtualKeyShort[] keys)
            {
                foreach (var key in keys)
                {
                    Keyboard.Type(key);
                    Wait.UntilInputIsProcessed();
                }
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
