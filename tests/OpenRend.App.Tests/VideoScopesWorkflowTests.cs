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
    public async Task SplitPreviewBypassesOutputGradeOnlyOnTheComparisonSide()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "output-comparison.openrend");
        try
        {
            var asset = Solid("Gray plate", "#808080");
            var grade = VideoEffect.Create(VideoEffectKind.ColorGrading);
            grade.SetParameter("BrightnessAdjust", .2);
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 640, Height = 360, Media = [asset], VideoOutputEffects = [grade],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [new() { MediaId = asset.Id, DurationFrames = 90 }] }]
            }, path);
            using var running = Launch(executable, path, temporary);
            var main = running.Window;
            var preview = Assert.Single(main.FindAllDescendants(condition => condition.ByClassName("OpenRend.Preview")));
            WaitFor(BothSidesGraded, "The output grade did not reach the preview.");
            var split = Find(main, "SplitScreenButton").AsButton();
            split.Invoke();
            WaitFor(() =>
            {
                var (left, right) = SampleSides();
                return Math.Min(left, right) is >= 126 and <= 130 && Math.Max(left, right) is >= 177 and <= 181;
            }, "The comparison should show the untreated source and the output grade.");
            split.Invoke();
            WaitFor(BothSidesGraded, "Closing the comparison did not restore the full output grade.");
            Assert.DoesNotContain('*', main.Title);
            running.CloseGracefully();

            bool BothSidesGraded()
            {
                var (left, right) = SampleSides();
                return left is >= 177 and <= 181 && right is >= 177 and <= 181;
            }

            (int Left, int Right) SampleSides()
            {
                using var image = Capture.Element(preview);
                var width = Math.Min(image.Bitmap.Width, image.Bitmap.Height * (16.0 / 9));
                var y = image.Bitmap.Height / 2;
                var center = image.Bitmap.Width / 2.0;
                return (image.Bitmap.GetPixel((int)(center - width / 4), y).R,
                    image.Bitmap.GetPixel((int)(center + width / 4), y).R);
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task VideoScopesMeasureUnguidedFramesAndCanHoldPlayback()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "scopes.openrend");
        try
        {
            var dim = Solid("Slate", "#2A4550");
            var bright = Solid("Gold", "#DDA85A");
            var exposure = VideoEffect.Create(VideoEffectKind.ColorGrading);
            exposure.SetParameter("ExposureLnAdjust", -.6);
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 640, Height = 360, FrameRate = new(30, 1),
                Media = [dim, bright],
                Tracks = [new() { Kind = TrackKind.Video, Clips =
                [
                    new() { Name = dim.Name, MediaId = dim.Id, DurationFrames = 30 },
                    new() { Name = bright.Name, MediaId = bright.Id, StartFrame = 30, DurationFrames = 180, VideoEffects = [exposure] }
                ] }]
            }, path);

            using var running = Launch(executable, path, temporary);
            var main = running.Window;
            var preview = Assert.Single(main.FindAllDescendants(condition => condition.ByClassName("OpenRend.Preview")));
            Chord(main, VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.KEY_2);
            var scopes = FindScopes(main);
            var mainArea = main.BoundingRectangle;
            var scopeArea = scopes.BoundingRectangle;
            scopes.Patterns.Transform.Pattern.Move(mainArea.Left + 8, mainArea.Bottom - scopeArea.Height - 64);
            var graph = Find(scopes, "VideoScopeGraph");
            var mode = Find(scopes, "VideoScopeMode").AsComboBox();
            WaitFor(() => graph.Name == "Waveform", "The waveform did not render.");
            WaitFor(() => ScopeRaster(graph).Any(pixel => ((pixel >> 8) & 255) > 160), "The waveform did not receive the first shot.");
            var first = ScopeRaster(graph);
            var originalPreview = SamplePreview(preview);

            var zoom = Find(main, "PreviewZoom").AsComboBox();
            zoom.Select(2);
            zoom.Patterns.ExpandCollapse.Pattern.Collapse();
            Find(main, "PreviewOverlayButton").AsButton().Invoke();
            WaitFor(() => !SamplePreview(preview).SequenceEqual(originalPreview), "Preview zoom and guides did not reach the native display.");
            Find(scopes, "VideoScopeRefresh").AsButton().Invoke();
            SelectMode(2, "RGB Parade");
            SelectMode(0, "Waveform");
            Assert.Equal(first, ScopeRaster(graph));

            CheckStudioCode(16, "0.0 IRE");
            CheckStudioCode(235, "100.0 IRE");
            CheckStudioCode(255, "109.1 IRE");

            var follow = Find(scopes, "VideoScopeFollowPlayback");
            follow.Patterns.Toggle.Pattern.Toggle();
            Assert.Equal(ToggleState.Off, follow.Patterns.Toggle.Pattern.ToggleState.Value);
            Chord(main, VirtualKeyShort.RETURN);
            WaitFor(() => PresentedFrame(main) >= 45, "Playback did not reach the second shot.");
            Assert.Equal(first, ScopeRaster(graph));
            Chord(main, VirtualKeyShort.RETURN);
            WaitFor(() => !ScopeRaster(graph).SequenceEqual(first), "Pausing did not refresh the held scope.");

            var second = ScopeRaster(graph);
            Find(main, "SplitScreenButton").AsButton().Invoke();
            WaitFor(() => !ScopeRaster(graph).SequenceEqual(second), "The scope did not receive the split comparison.");
            using (var wave = Capture.Element(graph))
            {
                int TraceRow(int x) => Enumerable.Range(0, wave.Bitmap.Height).First(y => wave.Bitmap.GetPixel(x, y).G > 160);
                Assert.InRange(TraceRow(wave.Bitmap.Width * 3 / 4) - TraceRow(wave.Bitmap.Width / 4), 15, 200);
            }
            Find(main, "SplitScreenButton").AsButton().Invoke();
            WaitFor(() => ScopeRaster(graph).SequenceEqual(second), "Closing the split view left one comparison trace behind.");

            mode.Select(1);
            mode.Select(2);
            mode.Select(3);
            mode.Patterns.ExpandCollapse.Pattern.Collapse();
            WaitFor(() => graph.Name == "Histogram", "The last scope selection was lost after rapid mode changes.");
            Assert.False(Find(scopes, "VideoScopeIntensity").IsEnabled);
            SelectMode(2, "RGB Parade");
            var captureDir = Path.Combine(root, ".local", "desktop-validation");
            Directory.CreateDirectory(captureDir);
            using (var image = Capture.Element(scopes)) image.ToFile(Path.Combine(captureDir, "video-scopes.png"));

            Chord(scopes, VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.KEY_2);
            WaitFor(() => ScopeWindow(main) is null, "Ctrl+Alt+2 did not close the focused scopes.");
            Chord(main, VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.KEY_2);
            scopes = FindScopes(main);
            WaitFor(() => Find(scopes, "VideoScopeGraph").Name == "RGB Parade", "The scope mode did not survive reopening.");
            Assert.Equal(ToggleState.Off, Find(scopes, "VideoScopeFollowPlayback").Patterns.Toggle.Pattern.ToggleState.Value);
            scopes.Close();
            Assert.DoesNotContain('*', main.Title);
            running.CloseGracefully();

            void SelectMode(int index, string label)
            {
                mode.Select(index);
                mode.Patterns.ExpandCollapse.Pattern.Collapse();
                WaitFor(() => graph.Name == label, $"{label} did not render.");
            }

            void CheckStudioCode(int code, string ire)
            {
                var bounds = graph.BoundingRectangle;
                var row = bounds.Height - 1 - code * (bounds.Height - 1) / 255;
                Mouse.MoveTo(new Point(bounds.Left + bounds.Width / 2, bounds.Top + row));
                WaitFor(() => Find(scopes, "VideoScopeReadout").Name == $"Code {code} · {ire}", $"The Studio readout did not match code {code}.");
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static Window FindScopes(Window main)
    {
        AutomationElement? scopes = null;
        WaitFor(() => (scopes = ScopeWindow(main)) is not null,
            "Video Scopes did not open.");
        return scopes!.AsWindow();
    }

    private static AutomationElement? ScopeWindow(Window main) =>
        main.FindFirstDescendant(condition => condition.ByAutomationId("VideoScopesWindow"))
        ?? main.Automation.GetDesktop().FindFirstChild(condition =>
            condition.ByAutomationId("VideoScopesWindow").And(condition.ByProcessId(main.Properties.ProcessId.Value)));

    private static int[] ScopeRaster(AutomationElement graph)
    {
        using var image = Capture.Element(graph);
        var pixels = new int[image.Bitmap.Width * image.Bitmap.Height];
        for (var y = 0; y < image.Bitmap.Height; y++)
        for (var x = 0; x < image.Bitmap.Width; x++)
            pixels[y * image.Bitmap.Width + x] = image.Bitmap.GetPixel(x, y).ToArgb();
        return pixels;
    }

    private static MediaAsset Solid(string name, string hex)
    {
        var parameters = new Dictionary<string, string> { ["hex"] = hex };
        return new()
        {
            Path = MediaGeneratorCatalog.CreateUri(MediaGeneratorKind.SolidColor, name, parameters),
            IsGenerated = true, HasVideo = true, Width = 640, Height = 360, DurationSeconds = 7, FrameRate = 30,
            Generator = new() { Kind = MediaGeneratorKind.SolidColor, Title = "Solid Color", PresetName = name, Parameters = parameters }
        };
    }
}
