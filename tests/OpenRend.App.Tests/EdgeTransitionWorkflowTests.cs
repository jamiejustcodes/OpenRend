using System.Drawing;
using FlaUI.Core.AutomationElements;
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
    public async Task TransitionPresetsDropOnBothClipEdgesAndKeepTheirOwnSettings()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var folder = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "clip-edge-transitions.openrend");
        var red = new MediaAsset { Path = "openrend://color?hex=FF0000", HasVideo = true };
        var blue = new MediaAsset { Path = "openrend://color?hex=0000FF", HasVideo = true };
        var clip = new TimelineClip { Name = "Foreground", MediaId = red.Id, StartFrame = 12, DurationFrames = 36, FadeInFrames = 8 };
        var rate = new Rational(30, 1);
        var tailLength = new EditingPreferences().OverlapFrames(rate);
        try
        {
            await ProjectStore.SaveAsync(new ProjectDocument { Name = "UI integration", Width = 320, Height = 180,
                FrameRate = rate, Media = [red, blue], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] },
                    new() { Kind = TrackKind.Video, Clips = [new() { MediaId = blue.Id, DurationFrames = 60 }] }] }, path);

            using (var running = Launch(executable, path, folder))
            {
                var window = running.Window;
                FitTimeline(window);
                OpenTransitions(window);
                Find(window, "TransitionSearch").AsTextBox().Text = "Linear Wipe";
                var presets = Find(window, "TransitionPresets");
                AutomationElement? wipe = null;
                WaitFor(() => (wipe = VisibleNamedItem(presets, "Left-Right, Hard Edge")?
                    .FindFirstDescendant(c => c.ByAutomationId("TransitionPresetThumbnail"))) is not null, "The wipe preview was not shown.");
                var bounds = Find(window, $"Clip_{clip.Id:N}").BoundingRectangle;
                using (Keyboard.Pressing(VirtualKeyShort.SHIFT))
                    Mouse.Drag(ThumbnailCenter(wipe!), new Point(bounds.Left + 3, bounds.Top + bounds.Height / 2), MouseButton.Left);
                var savedHead = (await SaveGrade(window, path)).Tracks[0].Clips[0];
                Assert.Equal(8, savedHead.FadeInFrames);
                Assert.Equal(VideoTransitionKind.LinearWipe, savedHead.FadeInTransition?.Kind);
                Assert.Empty(window.ModalWindows);
                Seek(window, 16);
                AssertSides(window, leftIsRed: true);

                Mouse.Drag(ThumbnailCenter(wipe!), new Point(bounds.Right - 3, bounds.Top + bounds.Height / 2), MouseButton.Left);
                var tail = TransitionWindow(window);
                Assert.Contains("Linear Wipe", Find(tail, "TransitionPluginName").Name);
                Find(tail, "CloseTransitionProperties").AsButton().Invoke();
                WaitFor(() => window.ModalWindows.Length == 0, "The new tail properties did not close.");
                Seek(window, clip.EndFrame - tailLength / 2);
                AssertSides(window, leftIsRed: false);

                Find(window, $"Clip_{clip.Id:N}_TransitionOut").AsButton().Invoke();
                tail = TransitionWindow(window);
                Find(tail, "TransitionParameter_Angle").Patterns.RangeValue.Pattern.SetValue(180);
                Find(tail, "CloseTransitionProperties").AsButton().Invoke();
                WaitFor(() => window.ModalWindows.Length == 0, "The tail properties did not close after editing.");
                AssertSides(window, leftIsRed: true);
                var saved = (await SaveGrade(window, path)).Tracks[0].Clips[0];
                Assert.Equal("180.0", saved.FadeOutTransition?.Parameters?["Angle"]);
                Assert.Equal(tailLength, saved.FadeOutFrames);
                Assert.Equal("0.0", TransitionCatalog.ResolveParameters(saved.FadeInTransition!)["Angle"]);
                Assert.Equal((clip.StartFrame, clip.DurationFrames), (saved.StartFrame, saved.DurationFrames));
                running.CloseGracefully();
            }

            using (var running = Launch(executable, path, folder))
            {
                var window = running.Window;
                FitTimeline(window);
                Seek(window, 16);
                AssertSides(window, leftIsRed: true);
                Seek(window, clip.EndFrame - tailLength / 2);
                AssertSides(window, leftIsRed: true);
                running.CloseGracefully();
            }
        }
        finally { Directory.Delete(folder, recursive: true); }

        void Seek(Window window, long frame)
        {
            var bounds = Find(window, $"Clip_{clip.Id:N}").BoundingRectangle;
            var x = bounds.Left + (int)Math.Round(bounds.Width * (frame - clip.StartFrame) / (double)clip.DurationFrames);
            Mouse.Click(new Point(x, bounds.Top - 10));
            WaitFor(() => Math.Abs(PresentedFrame(window) - frame) <= 1, "The preview did not reach the edge transition.");
        }
    }

    private static Window TransitionWindow(Window main)
    {
        Window? dialog = null;
        WaitFor(() => (dialog = main.ModalWindows.SingleOrDefault(window => window.Title == "Video event FX")) is not null,
            "The transition properties did not open.");
        return dialog!;
    }

    private static void AssertSides(Window main, bool leftIsRed)
    {
        var preview = Assert.Single(main.FindAllDescendants(c => c.ByClassName("OpenRend.Preview")));
        WaitFor(() =>
        {
            using var capture = Capture.Element(preview);
            var width = Math.Min(capture.Bitmap.Width, capture.Bitmap.Height * 16 / 9);
            var x = capture.Bitmap.Width / 2;
            var y = capture.Bitmap.Height / 2;
            var left = capture.Bitmap.GetPixel(x - 3 * width / 8, y);
            var right = capture.Bitmap.GetPixel(x + 3 * width / 8, y);
            return leftIsRed ? left.R > 230 && right.B > 230 : left.B > 230 && right.R > 230;
        }, "The edge wipe did not show the expected foreground and lower track.");
    }
}
