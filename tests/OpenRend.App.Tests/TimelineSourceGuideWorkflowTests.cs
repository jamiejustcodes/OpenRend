using System.Drawing;
using FlaUI.Core.Capturing;
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
    public async Task SourceGuidesFollowLiveTrimAndDistinguishRepeatFromSourceEnd()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "source-guides.openrend");
        try
        {
            var video = new MediaAsset { Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
                Width = 320, Height = 180, DurationSeconds = 3, HasVideo = true };
            var audio = new MediaAsset { Path = Path.Combine(root, "tests", "native", "fixture.wav"),
                DurationSeconds = 2, HasAudio = true };
            var repeatingVideo = new TimelineClip { MediaId = video.Id, DurationFrames = 90, VideoSourceMode = VideoSourceMode.Loop };
            var heldVideo = new TimelineClip { MediaId = video.Id, StartFrame = 240, DurationFrames = 120,
                VideoSourceMode = VideoSourceMode.HoldLastFrame };
            var repeatingAudio = new TimelineClip { MediaId = audio.Id, DurationFrames = 180, AudioSourceMode = AudioSourceMode.Loop };
            var paddedAudio = new TimelineClip { MediaId = audio.Id, StartFrame = 240, DurationFrames = 120,
                AudioSourceMode = AudioSourceMode.PadSilence };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1), Media = [video, audio],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [repeatingVideo, heldVideo] },
                    new() { Kind = TrackKind.Audio, Clips = [repeatingAudio, paddedAudio] }]
            }, path);

            using var running = Launch(executable, path, directory);
            var main = running.Window;
            FitTimeline(main);
            var repeat = Color.FromArgb(0x63, 0xD6, 0xCB);
            var end = Color.FromArgb(0xBE, 0xC7, 0xD0);
            var picture = Find(main, $"Clip_{repeatingVideo.Id:N}").BoundingRectangle;
            var sound = Find(main, $"Clip_{repeatingAudio.Id:N}").BoundingRectangle;
            var held = Find(main, $"Clip_{heldVideo.Id:N}").BoundingRectangle;
            var padded = Find(main, $"Clip_{paddedAudio.Id:N}").BoundingRectangle;

            WaitFor(() => GuideAt(sound, 1.0 / 3, repeat) && GuideAt(sound, 2.0 / 3, repeat),
                "The audio event did not show both repeat boundaries.");
            Assert.True(GuideAt(held, .75, end), "The hold boundary did not use the source-end guide.");
            Assert.True(GuideAt(padded, .5, end), "The silence boundary did not use the source-end guide.");
            Assert.False(GuideAt(picture, 1, repeat));

            var y = picture.Top + picture.Height / 2;
            Mouse.MoveTo(picture.Right - 1, y);
            WaitForCursor(32644);
            Mouse.Down(MouseButton.Left);
            try
            {
                Mouse.MoveTo(picture.Right + picture.Width / 4, y);
                WaitFor(() => GuideAt(picture, 1, repeat), "Extending the event did not show its first repeat while held.");
                Assert.DoesNotContain('*', main.Title);
                var captures = Path.Combine(root, ".local", "desktop-validation");
                Directory.CreateDirectory(captures);
                using (var image = Capture.Element(main)) image.ToFile(Path.Combine(captures, "timeline-source-guides.png"));
                Keyboard.Type(VirtualKeyShort.ESCAPE);
                WaitFor(() => !GuideAt(picture, 1, repeat), "Escape did not clear the draft repeat guide.");
            }
            finally { Mouse.Up(MouseButton.Left); }

            y = sound.Top + sound.Height / 2;
            var middle = sound.Left + sound.Width / 2;
            Mouse.MoveTo(middle, y);
            try
            {
                using (Keyboard.Pressing(VirtualKeyShort.ALT))
                {
                    Mouse.Down(MouseButton.Left);
                    Mouse.MoveTo(middle + sound.Width / 12, y);
                    WaitFor(() => GuideAt(sound, .25, repeat) && !GuideAt(sound, 1.0 / 3, repeat),
                        "Slipping the audio event did not move the repeat boundary with its source phase.");
                }
                Keyboard.Type(VirtualKeyShort.ESCAPE);
                WaitFor(() => GuideAt(sound, 1.0 / 3, repeat), "Escape did not restore the original audio phase guide.");
            }
            finally { Mouse.Up(MouseButton.Left); }

            Assert.Equal(90, (await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0].DurationFrames);
            Assert.DoesNotContain('*', main.Title);
            running.CloseGracefully(useCaptionButton: true);

            bool GuideAt(Rectangle clip, double fraction, Color expected)
            {
                var area = main.BoundingRectangle;
                using var capture = Capture.Element(main);
                var x = clip.Left - area.Left + (int)Math.Round(clip.Width * fraction);
                var y = clip.Top - area.Top;
                for (var row = y + 2; row <= y + 8; row++)
                for (var column = x - 3; column <= x + 3; column++)
                {
                    if (column < 0 || column >= capture.Bitmap.Width || row < 0 || row >= capture.Bitmap.Height) continue;
                    var actual = capture.Bitmap.GetPixel(column, row);
                    if (Math.Abs(actual.R - expected.R) + Math.Abs(actual.G - expected.G) + Math.Abs(actual.B - expected.B) <= 18)
                        return true;
                }
                return false;
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
