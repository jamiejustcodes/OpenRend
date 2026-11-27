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
    public async Task PinnedCurvesNavigateAndEditInVisibleGraphCoordinates()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "curve-navigation.openrend");
        try
        {
            var asset = Solid("Curve plate", "#808080");
            var grade = VideoEffect.Create(VideoEffectKind.ColorGrading);
            grade.Curves = new();
            grade.Curves.Master.Points.Insert(1, new CurvePoint(.4, .4));
            var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 60, VideoEffects = [grade] };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 640, Height = 360, Media = [asset],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
            }, path);

            using var running = Launch(executable, path, directory);
            var main = running.Window;
            Find(main, $"Clip_{clip.Id:N}").Patterns.SelectionItem.Pattern.Select();
            SelectBrowserTab(main, "Color Grading");
            Find(main, "CurvesItem").Patterns.SelectionItem.Pattern.Select();
            var rgb = Find(main, "CurvesItem_Control");
            Mouse.Click(AtGraph(rgb, .4, .6));
            Assert.Contains("In: 40.0%", PointReadout());
            Keyboard.Type(VirtualKeyShort.ADD);
            WaitFor(() => Zoom() == "200%", "Keypad plus did not zoom the focused curve.");
            var beforePan = SamplePreview(rgb);
            Keyboard.Type(VirtualKeyShort.NUMPAD6);
            WaitFor(() => !SamplePreview(rgb).SequenceEqual(beforePan), "Keypad 6 did not pan the curve.");
            Keyboard.Type(VirtualKeyShort.PRIOR);
            WaitFor(() => PointReadout().Contains("In: 100.0%", StringComparison.Ordinal), "Page Up did not select the next point.");
            Keyboard.Type(VirtualKeyShort.NEXT);
            WaitFor(() => PointReadout().Contains("In: 40.0%", StringComparison.Ordinal), "Page Down did not select the previous point.");
            Keyboard.Type(VirtualKeyShort.NUMPAD5);
            WaitFor(() => Zoom() == "100%", "Keypad 5 did not reset the curve.");
            Assert.DoesNotContain('*', main.Title);

            Find(main, "ColorGrading_PinCurves").Patterns.Toggle.Pattern.Toggle();
            Find(main, "UtilitiesItem").Patterns.SelectionItem.Pattern.Select();
            var splitter = Find(main, "ColorGrading_CurvesSplitter");
            var exposure = Find(main, "logExposure");
            Assert.True(rgb.BoundingRectangle.Top > splitter.BoundingRectangle.Bottom);
            var split = splitter.GetClickablePoint();
            Mouse.Drag(split, new System.Drawing.Point(split.X, split.Y - 28), MouseButton.Left);
            WaitFor(() => splitter.BoundingRectangle.Top < split.Y - 10, "The pinned graph could not be resized.");
            var pinnedTop = splitter.BoundingRectangle.Top;
            Find(main, "CurvesItem").Patterns.SelectionItem.Pattern.Select();
            Find(main, "UtilitiesItem").Patterns.SelectionItem.Pattern.Select();
            WaitFor(() => Math.Abs(splitter.BoundingRectangle.Top - pinnedTop) <= 2, "Switching pages lost the pinned graph height.");
            Assert.DoesNotContain('*', main.Title);

            exposure.Patterns.RangeValue.Pattern.SetValue(.2);
            Find(main, "ColorGrading_CurveZoomIn").AsButton().Invoke();
            WaitFor(() => Zoom() == "200%", "The pinned graph did not zoom.");
            Mouse.Drag(AtGraph(rgb, .30, .70), AtGraph(rgb, .60, .40), MouseButton.Left);
            var saved = await SaveGrade(main, path);
            var edited = Assert.Single(saved.Tracks[0].Clips[0].VideoEffects);
            Assert.Equal(.2, edited.GetParameter("ExposureLnAdjust"));
            var point = Assert.Single(edited.Curves!.Master.Points, point => point.X is > 0 and < 1);
            Assert.InRange(point.X, .53, .57);
            Assert.InRange(point.Y, .53, .57);

            Chord(main, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            saved = await SaveGrade(main, path);
            var restored = Assert.Single(saved.Tracks[0].Clips[0].VideoEffects);
            Assert.Equal(.2, restored.GetParameter("ExposureLnAdjust"));
            Assert.Equal(.4, restored.Curves!.Master.Points[1].X);
            Assert.Equal(.4, restored.Curves.Master.Points[1].Y);

            ClickNamed(main, "HSL curves");
            var hsl = Find(main, "HSLCurvesItem_Control");
            Assert.Equal("100%", Zoom());
            Mouse.MoveTo(PointIn(hsl.BoundingRectangle, .5, .35));
            Mouse.Scroll(1);
            WaitFor(() => Zoom() == "200%", "The HSL graph did not zoom under the pointer.");
            var screenshots = Path.Combine(root, ".local", "desktop-validation");
            Directory.CreateDirectory(screenshots);
            using (var screenshot = Capture.Element(main))
                screenshot.ToFile(Path.Combine(screenshots, "grading-pinned-curves.png"));
            Find(main, "ColorGrading_CurveFit").AsButton().Invoke();
            WaitFor(() => Zoom() == "100%", "Fit did not reset the pinned HSL graph.");
            Find(main, "ColorGrading_PinCurves").Patterns.Toggle.Pattern.Toggle();
            WaitFor(() => main.FindFirstDescendant(c => c.ByAutomationId("HSLCurvesItem_Control"))?.Properties.IsOffscreen.ValueOrDefault != false,
                "Unpinning did not hide the curve on the tone page.");
            Assert.DoesNotContain('*', main.Title);
            running.CloseGracefully();

            string Zoom() => Find(main, "ColorGrading_CurveZoom").Name;
            string PointReadout() => Find(main, "ColorGrading_RgbPoint").Name;
            System.Drawing.Point AtGraph(AutomationElement graph, double x, double y)
            {
                var border = (int)Math.Round(14 * GetDpiForWindow(main.Properties.NativeWindowHandle.Value) / 96.0);
                var bounds = graph.BoundingRectangle;
                bounds.Inflate(-border, -border);
                return PointIn(bounds, x, y);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
