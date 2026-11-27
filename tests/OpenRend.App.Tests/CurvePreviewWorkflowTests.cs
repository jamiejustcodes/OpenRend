using System.Drawing;
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
    public async Task CurveDragsPreviewBeforeReleaseAndCancelWithoutEditingTheProject()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "curve-preview.openrend");
        try
        {
            var asset = Solid("Warm plate", "#9A715D");
            var clip = new TimelineClip { MediaId = asset.Id, Name = asset.Name, DurationFrames = 90 };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 640, Height = 360, Media = [asset],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
            }, path);
            using var running = Launch(executable, path, temporary);
            var main = running.Window;
            var preview = Assert.Single(main.FindAllDescendants(condition => condition.ByClassName("OpenRend.Preview")));
            Find(main, $"Clip_{clip.Id:N}").Patterns.SelectionItem.Pattern.Select();
            Chord(main, VirtualKeyShort.RIGHT);
            WaitFor(() => PresentedFrame(main) == 1, "The preview did not reach frame 1.");
            try
            {
                WaitFor(() => ColorDistance(Picture(), Color.FromArgb(154, 113, 93)) < 6, "The test plate did not reach Video Preview.");
            }
            catch
            {
                var diagnostic = Path.Combine(root, ".local", "desktop-validation");
                Directory.CreateDirectory(diagnostic);
                using var screenshot = Capture.Element(preview);
                screenshot.ToFile(Path.Combine(diagnostic, "grading-curve-preview-diagnostic.png"));
                throw;
            }
            var original = Picture();
            SelectBrowserTab(main, "Color Grading");
            Find(main, "CurvesItem").Patterns.SelectionItem.Pattern.Select();
            var rgb = Find(main, "CurvesItem_Control");

            Drag(rgb, .45, .57, .55, .22, cancel: true);
            Assert.Equal(1, PresentedFrame(main));
            Assert.DoesNotContain('*', main.Title);
            Drag(rgb, .45, .57, .55, .22, cancel: false);
            WaitFor(() => main.Title.Contains('*') && ColorDistance(Picture(), original) > 12, "Releasing the RGB point did not commit its preview.");
            var saved = await SaveGrade(main, path);
            var eventGrade = Assert.Single(saved.Tracks[0].Clips[0].VideoEffects);
            Assert.Equal(3, eventGrade.Curves!.Master.Points.Count);
            Assert.Empty(saved.VideoOutputEffects);

            Chord(main, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            WaitFor(() => Picture() == original, "One Undo did not restore the RGB preview.");
            saved = await SaveGrade(main, path);
            Assert.Empty(saved.Tracks[0].Clips[0].VideoEffects);

            OpenGradingScope(main, "VideoOutputColorGradingMenu");
            var floating = GradingDialog(main);
            var area = main.BoundingRectangle;
            floating.Patterns.Transform.Pattern.Move(area.Left + 8, area.Bottom - floating.BoundingRectangle.Height - 50);
            Find(floating, "CurvesItem").Patterns.SelectionItem.Pattern.Select();
            ClickNamed(floating, "HSL curves");
            ClickNamed(floating, "Sat vs Sat");
            var hsl = Find(floating, "HSLCurvesItem_Control");
            Assert.Equal(original, Picture());
            // Target the control point at 25% saturation, where the test plate sits.
            var saturationPoint = .25 + 7.0 / hsl.BoundingRectangle.Width;
            Drag(hsl, saturationPoint, .50, saturationPoint, .22, cancel: true);
            Drag(hsl, saturationPoint, .50, saturationPoint, .22, cancel: false);
            WaitFor(() => main.Title.Contains('*') && ColorDistance(Picture(), original) > 12, "Releasing the HSL point did not commit its preview.");
            var captures = Path.Combine(root, ".local", "desktop-validation");
            Directory.CreateDirectory(captures);
            using (var capture = Capture.Element(floating)) capture.ToFile(Path.Combine(captures, "grading-hsl-live.png"));
            Find(floating, "ColorGrading_Close").AsButton().Invoke();
            WaitFor(() => main.ModalWindows.Length == 0, "Color Grading did not close.");
            saved = await SaveGrade(main, path);
            var points = Assert.Single(saved.VideoOutputEffects).HSLCurves!.SatVsSat.Points;
            Assert.Equal(5, points.Count);
            Assert.InRange(points[1].Y, .75, .85);
            Assert.Empty(saved.Tracks[0].Clips[0].VideoEffects);
            Chord(main, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            WaitFor(() => Picture() == original, "One Undo did not restore the HSL preview.");
            saved = await SaveGrade(main, path);
            Assert.Empty(saved.VideoOutputEffects);
            running.CloseGracefully();

            void Drag(AutomationElement graph, double startX, double startY, double endX, double endY, bool cancel)
            {
                var bounds = graph.BoundingRectangle;
                Mouse.MoveTo(PointIn(bounds, startX, startY));
                Mouse.Down(MouseButton.Left);
                try
                {
                    Mouse.MoveTo(PointIn(bounds, endX, endY));
                    WaitFor(() => ColorDistance(Picture(), original) > 12, "The curve preview did not update while the mouse button was held.");
                    Assert.DoesNotContain('*', main.Title);
                    if (!cancel) return;
                    Keyboard.Type(VirtualKeyShort.ESCAPE);
                    WaitFor(() => Picture() == original, "Escape did not restore the original grade during the drag.");
                    Assert.DoesNotContain('*', main.Title);
                }
                finally { Mouse.Up(MouseButton.Left); }
            }

            Color Picture()
            {
                using var image = Capture.Element(preview);
                var contentWidth = Math.Min(image.Bitmap.Width, image.Bitmap.Height * (16.0 / 9));
                var contentHeight = Math.Min(image.Bitmap.Height, image.Bitmap.Width * (9.0 / 16));
                return image.Bitmap.GetPixel((int)(image.Bitmap.Width / 2.0 + contentWidth * .23),
                    (int)(image.Bitmap.Height / 2.0 - contentHeight * .22));
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static void ClickNamed(Window window, string name)
    {
        var button = window.FindFirstDescendant(condition => condition.ByControlType(ControlType.Button).And(condition.ByName(name)));
        Assert.NotNull(button);
        button.AsButton().Invoke();
    }

    private static int ColorDistance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
}
