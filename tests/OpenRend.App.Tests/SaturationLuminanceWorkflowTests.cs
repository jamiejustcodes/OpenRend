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
    public async Task SaturationLuminanceCurveSupportsWholeImageAndTargetedBrightness()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "saturation-luminance.openrend");
        try
        {
            var parameters = new Dictionary<string, string> { ["color1"] = "#808080", ["color2"] = "#FF0000", ["size"] = "64" };
            var asset = new MediaAsset
            {
                IsGenerated = true, HasVideo = true, DurationSeconds = 3, Width = 640, Height = 360,
                Path = MediaGeneratorCatalog.CreateUri(MediaGeneratorKind.Checkerboard, "Saturation plate", parameters),
                Generator = new() { Kind = MediaGeneratorKind.Checkerboard, Title = "Checkerboard", PresetName = "Saturation plate", Parameters = parameters }
            };
            var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 60 };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 640, Height = 360, Media = [asset],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
            }, path);

            using var running = Launch(executable, path, directory);
            var main = running.Window;
            var preview = Assert.Single(main.FindAllDescendants(condition => condition.ByClassName("OpenRend.Preview")));
            Find(main, $"Clip_{clip.Id:N}").Patterns.SelectionItem.Pattern.Select();
            Chord(main, VirtualKeyShort.RIGHT);
            WaitFor(() => PresentedFrame(main) == 1 && Swatches() == (128, 0), "The two test colors did not reach the preview.");
            SelectBrowserTab(main, "Color Grading");
            Find(main, "CurvesItem").Patterns.SelectionItem.Pattern.Select();
            ClickNamed(main, "HSL curves");
            ClickNamed(main, "Sat vs Lum");
            var graph = Find(main, "HSLCurvesItem_Control");
            Assert.DoesNotContain('*', main.Title);

            Mouse.DoubleClick(AtGraph(.4, .5));
            Mouse.Drag(AtGraph(.4, .5), AtGraph(.4, .7), MouseButton.Left);
            WaitFor(() => Swatches() is (>= 175, >= 45), "A one-point adjustment did not brighten both swatches.");
            var one = Assert.Single((await SavedCurves()).SatVsLum.Points);
            Assert.InRange(one.X, .38, .42);
            Assert.InRange(one.Y, .68, .72);

            ClickNamed(main, "Reset Mode");
            WaitFor(() => Swatches() == (128, 0), "Reset did not restore both swatches.");
            Mouse.DoubleClick(AtGraph(.6, .5));
            Mouse.DoubleClick(AtGraph(.97, .5));
            var anchors = await SavedCurves();
            Assert.Equal(2, anchors.SatVsLum.Points.Count);
            Assert.True(anchors.SatVsLum.IsIdentity);

            Mouse.MoveTo(AtGraph(.97, .5));
            Mouse.Down(MouseButton.Left);
            try
            {
                Mouse.MoveTo(AtGraph(.97, .72));
                WaitFor(() => Swatches() is (128, >= 50), "The held drag did not brighten just the saturated swatch.");
                Assert.DoesNotContain('*', main.Title);
            }
            finally { Mouse.Up(MouseButton.Left); }

            var grade = await SavedCurves();
            Assert.InRange(grade.SatVsLum.Points[1].Y, .70, .74);
            Assert.True(grade.SatVsSat.IsIdentity);
            var captures = Path.Combine(root, ".local", "desktop-validation");
            Directory.CreateDirectory(captures);
            using (var capture = Capture.Element(main))
                capture.ToFile(Path.Combine(captures, "grading-saturation-luminance.png"));
            Chord(main, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            WaitFor(() => Swatches() == (128, 0), "One Undo did not restore the selectively graded image.");
            Assert.True(anchors.ContentEquals(await SavedCurves()));
            running.CloseGracefully();

            async Task<HSLCurvesData> SavedCurves() =>
                Assert.Single((await SaveGrade(main, path)).Tracks[0].Clips[0].VideoEffects).HSLCurves!;

            Point AtGraph(double x, double y)
            {
                var border = (int)Math.Round(14 * GetDpiForWindow(main.Properties.NativeWindowHandle.Value) / 96.0);
                var bounds = graph.BoundingRectangle;
                bounds.Inflate(-border, -border);
                return PointIn(bounds, x, 1 - y);
            }

            (int Gray, int RedGreen) Swatches()
            {
                using var image = Capture.Element(preview);
                var width = Math.Min(image.Bitmap.Width, image.Bitmap.Height * (16.0 / 9));
                var center = image.Bitmap.Width / 2.0;
                var colors = Enumerable.Range(1, 12).Select(i => image.Bitmap.GetPixel(
                    (int)(center + (i / 13.0 - .5) * width), image.Bitmap.Height / 2)).ToArray();
                return (colors.FirstOrDefault(color => color.R >= 100 && Math.Abs(color.R - color.G) <= 2).G,
                    colors.Where(color => color.R - color.G > 100).Select(color => (int)color.G).DefaultIfEmpty(-1).First());
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
