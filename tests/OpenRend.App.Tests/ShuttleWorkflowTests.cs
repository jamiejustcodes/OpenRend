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
    public async Task TimelineShuttleChangesDirectionAndReleasesHeldSpeed()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "shuttle.openrend");
        try
        {
            var asset = new MediaAsset
            {
                Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
                Width = 320, Height = 180, DurationSeconds = 3, FrameRate = 30, HasVideo = true
            };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 320, Height = 180, Media = [asset],
                Tracks = [new() { Kind = TrackKind.Video, Clips =
                    [new() { MediaId = asset.Id, DurationFrames = 90 },
                     new() { MediaId = asset.Id, StartFrame = 810, DurationFrames = 90 }] }]
            }, path);

            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            var rate = Find(window, "PlaybackRate");
            void ExpectRate(string value) => WaitFor(() => rate.Name == $"Rate: {value}", $"Expected shuttle rate {value}.");

            foreach (var value in new[] { "1.00", "2.00", "4.00", "8.00", "16.00" })
            {
                Chord(window, VirtualKeyShort.KEY_L);
                ExpectRate(value);
            }
            Chord(window, VirtualKeyShort.KEY_K);
            ExpectRate("0.00");

            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.END);
            Chord(window, VirtualKeyShort.KEY_J);
            ExpectRate("-1.00");
            WaitFor(() => PresentedFrame(window) is >= 0 and < 895, "Reverse shuttle did not move the project cursor backward.");
            Chord(window, VirtualKeyShort.KEY_J);
            ExpectRate("-2.00");
            Chord(window, VirtualKeyShort.KEY_K);
            ExpectRate("0.00");

            foreach (var (direction, expected) in new[]
                     { (VirtualKeyShort.KEY_L, "0.25"), (VirtualKeyShort.KEY_J, "-0.25") })
            {
                using (Keyboard.Pressing(VirtualKeyShort.KEY_K, direction))
                    ExpectRate(expected);
                ExpectRate("0.00");
            }

            Assert.DoesNotContain('*', window.Title);
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
