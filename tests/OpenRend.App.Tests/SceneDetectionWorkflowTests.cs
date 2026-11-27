using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.App.Tests;

public sealed partial class EditorWorkflowTests
{
    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task SceneDetectionReviewsRealCutsBeforeEditingTimeline()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var projectPath = Path.Combine(temporary, "scene-review.openrend");
        try
        {
            var source = Path.Combine(temporary, "three-scenes.mkv");
            await CreateSceneFixture(Path.Combine(Path.GetDirectoryName(executable)!, "ffmpeg.exe"), source);
            var asset = new MediaAsset
            {
                Path = source, HasVideo = true, Width = 160, Height = 90, DurationSeconds = 3, FrameRate = 30
            };
            var clip = new TimelineClip { MediaId = asset.Id, StartFrame = 12, DurationFrames = 90, Name = "Packed scenes" };
            await ProjectStore.SaveAsync(new()
            {
                Name = "UI integration", Width = 160, Height = 90, FrameRate = new(30, 1), Media = [asset],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
            }, projectPath);

            using var running = Launch(executable, projectPath, temporary);
            var window = running.Window;
            Find(window, $"Clip_{clip.Id:N}").Patterns.SelectionItem.Pattern.Select();
            var dialog = OpenSceneDetection(window);
            Assert.StartsWith("Packed scenes", Find(dialog, "SceneDetectionEvent").AsComboBox().SelectedItem?.Name, StringComparison.Ordinal);
            var minimum = Find(dialog, "MinimumSceneLength").AsTextBox();
            var analyze = Find(dialog, "AnalyzeScenes").AsButton();
            var apply = Find(dialog, "ApplySceneCuts").AsButton();
            Assert.False(apply.IsEnabled);
            minimum.Text = "0";
            analyze.Invoke();
            WaitFor(() => Find(dialog, "DialogValidationError").Name.Contains("50 to 60,000", StringComparison.Ordinal),
                "Invalid minimum duration did not show in the form.");
            Assert.DoesNotContain('*', window.Title);

            minimum.Text = "500";
            AnalyzeSceneCuts(dialog);
            var first = Find(dialog, "SceneCut_42");
            var second = Find(dialog, "SceneCut_72");
            first.Patterns.Toggle.Pattern.Toggle();
            second.Patterns.Toggle.Pattern.Toggle();
            WaitFor(() => !apply.IsEnabled, "Applying stayed enabled with no cuts checked.");
            first.Patterns.Toggle.Pattern.Toggle();
            Assert.DoesNotContain('*', window.Title);

            minimum.Text = "400";
            AnalyzeSceneCuts(dialog);
            Find(dialog, "SceneCut_72").Patterns.Toggle.Pattern.Toggle();
            var action = Find(dialog, "SceneDetectionAction").AsComboBox();
            action.Select(1);
            action.Patterns.ExpandCollapse.Pattern.Collapse();
            var captureDir = Path.Combine(root, ".local", "desktop-validation");
            Directory.CreateDirectory(captureDir);
            using (var screenshot = Capture.Element(dialog)) screenshot.ToFile(Path.Combine(captureDir, "scene-detection-review.png"));
            apply.Invoke();
            WaitFor(() => window.ModalWindows.Length == 0, "Applying the checked region cut did not close the dialog.");
            var saved = await SaveGrade(window, projectPath);
            Assert.Single(saved.Tracks[0].Clips);
            Assert.Equal(new[] { (12L, 42L), (42L, 102L) }, saved.Regions.Select(region => (region.StartFrame, region.EndFrame)));

            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            saved = await SaveGrade(window, projectPath);
            Assert.Empty(saved.Regions);
            dialog = OpenSceneDetection(window);
            AnalyzeSceneCuts(dialog);
            action = Find(dialog, "SceneDetectionAction").AsComboBox();
            action.Select(2);
            action.Patterns.ExpandCollapse.Pattern.Collapse();
            Find(dialog, "ApplySceneCuts").AsButton().Invoke();
            WaitFor(() => window.ModalWindows.Length == 0, "Creating scene subclips did not close the dialog.");
            saved = await SaveGrade(window, projectPath);
            Assert.Single(saved.Tracks[0].Clips);
            Assert.Equal(new[] { new MediaSourceRange(0, 1, 1), new(1, 1, 1), new(2, 1, 1) },
                saved.Media.Where(media => media.SourceRange.HasValue).Select(media => media.SourceRange!.Value));
            Assert.All(saved.Media, media => Assert.Equal(source, media.Path));
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            saved = await SaveGrade(window, projectPath);
            Assert.Single(saved.Media);

            dialog = OpenSceneDetection(window);
            AnalyzeSceneCuts(dialog);
            Find(dialog, "ApplySceneCuts").AsButton().Invoke();
            WaitFor(() => window.ModalWindows.Length == 0, "Applying the scene splits did not close the dialog.");
            saved = await SaveGrade(window, projectPath);
            Assert.Equal(new[] { (12L, 0L, 30L), (42L, 30L, 30L), (72L, 60L, 30L) },
                saved.Tracks[0].Clips.Select(piece => (piece.StartFrame, piece.SourceInFrame, piece.DurationFrames)));
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static Window OpenSceneDetection(Window window)
    {
        var tools = window.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName("Tools")));
        Assert.NotNull(tools);
        tools.Patterns.ExpandCollapse.Pattern.Expand();
        var scene = window.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName("Scene Detection…")));
        Assert.NotNull(scene);
        scene.AsMenuItem().Invoke();
        Window? dialog = null;
        WaitFor(() => (dialog = window.ModalWindows.SingleOrDefault(modal => modal.Title == "Scene Detection")) is not null,
            "Scene Detection did not open.");
        return dialog!;
    }

    private static void AnalyzeSceneCuts(Window dialog)
    {
        Find(dialog, "AnalyzeScenes").AsButton().Invoke();
        WaitFor(() => Find(dialog, "SceneDetectionStatus").Name == "Review 2 detected cuts.",
            "The scanner did not find the two cuts.", timeoutSeconds: 25);
    }

    private static async Task CreateSceneFixture(string executable, string path)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        string[] arguments = ["-v", "error", "-f", "lavfi", "-i", "color=red:s=160x90:r=30:d=1",
            "-f", "lavfi", "-i", "color=blue:s=160x90:r=30:d=1",
            "-f", "lavfi", "-i", "color=white:s=160x90:r=30:d=1",
            "-filter_complex", "[0:v][1:v][2:v]concat=n=3:v=1:a=0", "-c:v", "ffv1", path];
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await errors);
    }
}
