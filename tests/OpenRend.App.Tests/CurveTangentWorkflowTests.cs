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
    public async Task RgbAndHueTangentsPreviewBeforeCommitAndUndoWithoutMovingPoints()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "curve-tangents.openrend");
        try
        {
            var asset = Solid("Orange plate", "#CD7437");
            var grade = VideoEffect.Create(VideoEffectKind.ColorGrading);
            grade.Curves = new();
            grade.Curves.Master.Points.Insert(1, new(.45, .45));
            var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 90, VideoEffects = [grade] };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 640, Height = 360, Media = [asset],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
            }, path);
            using var running = Launch(executable, path, directory);
            var main = running.Window;
            var preview = Assert.Single(main.FindAllDescendants(c => c.ByClassName("OpenRend.Preview")));
            Find(main, $"Clip_{clip.Id:N}").Patterns.SelectionItem.Pattern.Select();
            Chord(main, VirtualKeyShort.RIGHT);
            WaitFor(() => PresentedFrame(main) == 1 && Picture().R > 140, "The test picture was not visible.");
            var original = Picture();
            SelectBrowserTab(main, "Color Grading");
            Find(main, "CurvesItem").Patterns.SelectionItem.Pattern.Select();
            var rgb = Find(main, "CurvesItem_Control");
            Mouse.Click(GraphPoint(rgb, .45, .45));
            Drag(rgb, .575, .45, .62, .83, original, cancel: true);
            Drag(rgb, .575, .45, .62, .83, original, cancel: false);
            var linked = (await SavedGrade()).Curves!.Master.Points[1];
            Assert.Equal((.45, .45), (linked.X, linked.Y));
            Assert.InRange(linked.OutHandleY, .36, .4);
            Assert.Equal(-linked.OutHandleY, linked.InHandleY, 12);
            Assert.Equal(-linked.OutHandleX, linked.InHandleX, 12);
            Chord(main, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            WaitFor(() => Picture() == original, "Undo did not restore the picture after the RGB handle drag.");
            Assert.Equal(0, (await SavedGrade()).Curves!.Master.Points[1].OutHandleY);

            Mouse.Click(GraphPoint(rgb, .45, .45), MouseButton.Right);
            AutomationElement? link = null;
            WaitFor(() => (link = main.Automation.GetDesktop().FindFirstDescendant(c => c.ByAutomationId("Curve_LinkTangents")
                .And(c.ByProcessId(main.Properties.ProcessId.Value)))) is not null, "The tangent context menu did not open.");
            Mouse.Click(link!.GetClickablePoint());
            var unlinked = (await SavedGrade()).Curves!.Master.Points[1];
            Assert.True(unlinked.TangentsUnlinked);
            Mouse.Click(GraphPoint(rgb, .45, .45));
            using (Keyboard.Pressing(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT))
                Keyboard.Type(VirtualKeyShort.UP);
            WaitFor(() => main.Title.Contains('*'), "The keyboard tangent edit did not commit on release.");
            var nudged = (await SavedGrade()).Curves!.Master.Points[1];
            Assert.True(nudged.OutHandleY > 0);
            Assert.Equal(unlinked.InHandleY, nudged.InHandleY);
            Chord(main, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            await SavedGrade();

            ClickNamed(main, "HSL curves");
            ClickNamed(main, "Hue vs Lum");
            var hsl = Find(main, "HSLCurvesItem_Control");
            Mouse.Click(GraphPoint(hsl, 0, .5));
            Drag(hsl, 1 - 1.0 / 18, .5, .93, .87, original, cancel: false);
            var hue = (await SavedGrade()).HSLCurves!.HueVsLum.Points[0];
            Assert.Equal((0, .5), (hue.X, hue.Y));
            Assert.InRange(hue.InTangent!.Value.Y, .35, .39);
            Assert.Equal(-hue.InTangent.Value.X, hue.OutTangent!.Value.X, 12);
            Assert.Equal(-hue.InTangent.Value.Y, hue.OutTangent.Value.Y, 12);
            var captures = Path.Combine(root, ".local", "desktop-validation");
            Directory.CreateDirectory(captures);
            using (var capture = Capture.Element(main))
                capture.ToFile(Path.Combine(captures, "grading-hue-tangents.png"));
            Chord(main, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            WaitFor(() => Picture() == original, "One Undo did not restore the hue grade.");
            Assert.Null((await SavedGrade()).HSLCurves);
            running.CloseGracefully();

            async Task<VideoEffect> SavedGrade() =>
                Assert.Single((await SaveGrade(main, path)).Tracks[0].Clips[0].VideoEffects);

            Color Picture()
            {
                using var capture = Capture.Element(preview);
                return capture.Bitmap.GetPixel(capture.Bitmap.Width / 2, capture.Bitmap.Height / 2);
            }

            Point GraphPoint(AutomationElement graph, double x, double y)
            {
                var border = (int)Math.Round(14 * GetDpiForWindow(main.Properties.NativeWindowHandle.Value) / 96.0);
                var bounds = graph.BoundingRectangle;
                bounds.Inflate(-border, -border);
                return PointIn(bounds, x, 1 - y);
            }

            void Drag(AutomationElement graph, double startX, double startY, double endX, double endY, Color before, bool cancel)
            {
                Mouse.MoveTo(GraphPoint(graph, startX, startY));
                Mouse.Down(MouseButton.Left);
                try
                {
                    Mouse.MoveTo(GraphPoint(graph, endX, endY));
                    WaitFor(() => ColorDistance(Picture(), before) > 7, "Moving the tangent did not update the preview before release.");
                    Assert.DoesNotContain('*', main.Title);
                    if (!cancel) return;
                    Keyboard.Type(VirtualKeyShort.ESCAPE);
                    WaitFor(() => Picture() == before, "Escape did not restore the preview during the tangent drag.");
                    Assert.DoesNotContain('*', main.Title);
                }
                finally { Mouse.Up(MouseButton.Left); }
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
