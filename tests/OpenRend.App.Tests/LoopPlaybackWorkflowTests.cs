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
    public async Task LoopPlaybackRepeatsTheDraggedRangeUntilDisabled()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var path = Path.Combine(temporary, "loop.openrend");
            var media = new MediaAsset
            {
                Path = Path.Combine(root, "tests", "native", "fixture.mp4"), Width = 320, Height = 180,
                FrameRate = 30, DurationSeconds = 3, HasVideo = true, HasAudio = true
            };
            var clip = new TimelineClip { MediaId = media.Id, DurationFrames = 90 };
            var rate = new Rational(30, 1);
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", FrameRate = rate, Width = 320, Height = 180, Media = [media],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] },
                    new() { Kind = TrackKind.Audio, Clips = [new() { MediaId = media.Id, DurationFrames = 90 }] }]
            }, path);

            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
            var clipBounds = Find(window, $"Clip_{clip.Id:N}").BoundingRectangle;
            var rulerY = clipBounds.Top - (int)(35 * clipBounds.Height / 67.0);
            Mouse.Drag(new System.Drawing.Point(clipBounds.Left + clipBounds.Width / 6, rulerY),
                new System.Drawing.Point(clipBounds.Left + clipBounds.Width / 2, rulerY), MouseButton.Left);
            var status = Find(window, "WorkspaceSelectionDuration");
            WaitFor(() => status.Name == $"Selection {TimecodeFormatter.Format(30, rate)}", "The loop range was not selected.");

            var playbackRate = Find(window, "PlaybackRate");
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.HOME);
            Chord(window, VirtualKeyShort.KEY_Q);
            Chord(window, VirtualKeyShort.SPACE);
            WaitFor(() => playbackRate.Name == "Rate: 1.00" && PresentedFrame(window) is >= 28 and < 45,
                "Playback did not enter and advance within the selection.");
            WaitFor(() => playbackRate.Name == "Rate: 1.00" && PresentedFrame(window) is >= 15 and < 25,
                "Playback did not wrap to the selected range start.");
            Chord(window, VirtualKeyShort.KEY_Q);
            WaitFor(() => playbackRate.Name == "Rate: 1.00" && PresentedFrame(window) >= 46,
                "Disabling the loop did not let playback pass the selected range.");
            Chord(window, VirtualKeyShort.SPACE);
            WaitFor(() => playbackRate.Name == "Rate: 0.00" && PresentedFrame(window) == 15,
                "Space did not return to the playback origin.");
            Assert.DoesNotContain('*', window.Title);
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
