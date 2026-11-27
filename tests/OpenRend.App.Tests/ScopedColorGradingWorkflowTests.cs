using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.App.Tests;

public sealed partial class EditorWorkflowTests
{
    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task ScopedGradingOpensCleanAndSavesLutsAndCurvesInTheChosenScope()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "scoped-grade.openrend");
        var asset = new MediaAsset
        {
            Path = Path.Combine(root, "tests", "native", "fixture.mp4"), Width = 320, Height = 180,
            DurationSeconds = 3, FrameRate = 30, HasVideo = true
        };
        var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 30, Name = "Interview grade" };
        await ProjectStore.SaveAsync(new ProjectDocument
        {
            Name = "UI integration", Width = 320, Height = 180, Media = [asset],
            Tracks = [new() { Kind = TrackKind.Video, Name = "Interview", Clips = [clip] }]
        }, path);

        try
        {
            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            Find(window, $"Clip_{clip.Id:N}").Patterns.SelectionItem.Pattern.Select();
            SelectBrowserTab(window, "Color Grading");
            Find(window, "ColorGrading_PopoutButton").AsButton().Invoke();
            var grading = GradingDialog(window);
            Assert.False(Find(grading, "ColorGrading_Undo").IsEnabled);
            Assert.DoesNotContain('*', window.Title);
            Find(grading, "ColorGrading_Close").AsButton().Invoke();
            WaitFor(() => window.ModalWindows.Length == 0, "The untouched grading panel did not close.");
            Assert.DoesNotContain('*', window.Title);

            var cases = new (string Command, string Target, Func<ProjectDocument, List<VideoEffect>> Read)[]
            {
                ("ColorGradingMenu", "Event FX", document => document.Tracks[0].Clips[0].VideoEffects),
                ("VideoTrackColorGradingMenu", "Track FX", document => document.Tracks[0].VideoEffects),
                ("MediaColorGradingMenu", "Media FX", document => document.Media[0].VideoEffects),
                ("VideoOutputColorGradingMenu", "Video Output FX", document => document.VideoOutputEffects)
            };
            foreach (var (command, target, read) in cases)
            {
                OpenGradingScope(window, command);
                grading = GradingDialog(window);
                Assert.StartsWith(target, Find(grading, "ColorGrading_TargetLabel").Name, StringComparison.Ordinal);
                Assert.DoesNotContain('*', window.Title);
                Find(grading, "LUTsItem").Patterns.SelectionItem.Pattern.Select();
                var look = Find(grading, "LookLut_Preset").AsComboBox();
                look.Select(2);
                WaitFor(() => Find(grading, "lookLut_Path").Name == BuiltinLutLibrary.LookLutNames[1],
                    "The selected Look LUT did not reach its scope.");
                Find(grading, "CurvesItem").Patterns.SelectionItem.Pattern.Select();
                var graph = Find(grading, "CurvesItem_Control").BoundingRectangle;
                Mouse.Drag(PointIn(graph, .4, .6), PointIn(graph, .62, .25), MouseButton.Left);
                var undo = Find(grading, "ColorGrading_Undo").AsButton();
                undo.Invoke();
                WaitFor(() => Find(grading, "ColorGrading_Redo").IsEnabled, "The curve gesture did not enable Redo.");
                Find(grading, "ColorGrading_Close").AsButton().Invoke();
                WaitFor(() => window.ModalWindows.Length == 0, "The grading panel did not close.");
                var saved = await SaveGrade(window, path);
                Assert.Null(Assert.Single(read(saved)).Curves);
                Assert.Equal(BuiltinLutLibrary.LookLutNames[1], Assert.Single(read(saved)).LookLut?.Title);

                OpenGradingScope(window, command);
                grading = GradingDialog(window);
                Find(grading, "ColorGrading_Redo").AsButton().Invoke();
                Find(grading, "CurvesItem").Patterns.SelectionItem.Pattern.Select();
                var hslButton = grading.FindFirstDescendant(c => c.ByControlType(ControlType.Button).And(c.ByName("HSL curves")));
                Assert.NotNull(hslButton);
                hslButton.AsButton().Invoke();
                var hsl = Find(grading, "HSLCurvesItem_Control").BoundingRectangle;
                Mouse.Click(PointIn(hsl, .43, .24));
                if (command == "ColorGradingMenu")
                {
                    var artifacts = Path.Combine(root, ".local", "desktop-validation");
                    Directory.CreateDirectory(artifacts);
                    using var screenshot = Capture.Element(grading);
                    screenshot.ToFile(Path.Combine(artifacts, "color-grading-floating.png"));
                }
                Find(grading, "ColorGrading_Close").AsButton().Invoke();
                WaitFor(() => window.ModalWindows.Length == 0, "The grading panel stayed open after editing.");
                saved = await SaveGrade(window, path);
                var grade = Assert.Single(read(saved));
                var point = Assert.Single(grade.Curves!.Master.Points, point => point.X is > 0 and < 1);
                Assert.InRange(point.X, .60, .70);
                Assert.InRange(point.Y, .72, .84);
                Assert.True(grade.HSLCurves?.HasAdjustments);
                Assert.Equal(BuiltinLutLibrary.LookLutNames[1], grade.LookLut?.Title);
            }
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static Point PointIn(Rectangle rectangle, double x, double y) =>
        new(rectangle.Left + (int)(rectangle.Width * x), rectangle.Top + (int)(rectangle.Height * y));

    private static Window GradingDialog(Window owner)
    {
        Window? dialog = null;
        WaitFor(() => (dialog = owner.ModalWindows.SingleOrDefault(window => window.Title == "Color Grading")) is not null,
            "The floating Color Grading panel did not open.");
        return dialog!;
    }

    private static void OpenGradingScope(Window window, string command)
    {
        var tools = window.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName("Tools")));
        Assert.NotNull(tools);
        tools.Patterns.ExpandCollapse.Pattern.Expand();
        Find(window, command).AsMenuItem().Invoke();
    }
}
