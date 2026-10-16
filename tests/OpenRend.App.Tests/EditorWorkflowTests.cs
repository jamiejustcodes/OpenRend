using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace OpenRend.App.Tests;

public sealed class WindowsDesktopFactAttribute : FactAttribute
{
    public WindowsDesktopFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            Skip = "Requires an interactive Windows desktop.";
        else if (Environment.GetEnvironmentVariable("OPENREND_RUN_UI_TESTS") != "1")
            Skip = "Set OPENREND_RUN_UI_TESTS=1 to run desktop input tests.";
    }
}

public sealed class EditorWorkflowTests(ITestOutputHelper output)
{
    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task SplitUndoRedoSaveAndReopenPreserveActualEditRanges()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        Assert.True(File.Exists(executable), $"Build the desktop application first: {executable}");
        var fixture = Path.Combine(root, "tests", "native", "fixture.mp4");
        Assert.True(File.Exists(fixture), "Generate the native test media fixture first.");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var projectPath = Path.Combine(temporary, "ui-workflow.openrend");
        var media = new MediaAsset { Path = fixture, DurationSeconds = 3, FrameRate = 30, HasVideo = true, HasAudio = true };
        var document = new ProjectDocument
        {
            Name = "UI integration",
            Width = 320,
            Height = 180,
            Media = [media],
            Tracks = [new TimelineTrack { Name = "Video 1", Kind = TrackKind.Video, Clips = [new TimelineClip { MediaId = media.Id, Name = "fixture.mp4", DurationFrames = 90 }] }]
        };
        await ProjectStore.SaveAsync(document, projectPath);
        try
        {
            using (var running = Launch(executable, projectPath, temporary))
            {
                var window = running.Window;
                VerifyLoadedWorkspace(window);
                try { await VerifyTimelineProvidersAsync(window, projectPath); }
                catch
                {
                    output.WriteLine("Project at failure: " + await File.ReadAllTextAsync(projectPath));
                    output.WriteLine("Editor title: " + window.Title);
                    foreach (var modal in window.ModalWindows)
                        output.WriteLine("Modal: " + string.Join(" | ", modal.FindAllDescendants().Select(e => e.Name)));
                    throw;
                }
                Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                Assert.True(Find(window, "CopyButton").IsEnabled);
                await VerifyPanCropAsync(window, projectPath);
                for (var i = 0; i < 30; i++) Chord(window, VirtualKeyShort.ALT, VirtualKeyShort.RIGHT);
                WaitFor(() => Find(window, "Timecode").Name == "00:00:01:00", "Frame stepping did not reach frame 30.");
                Chord(window, VirtualKeyShort.KEY_S);
                WaitFor(() => Find(window, "UndoButton").IsEnabled, "Split did not create an undo transaction.");
                Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
                try { await WaitForSavedClipsAsync(projectPath, 2); }
                catch
                {
                    output.WriteLine("Title after save shortcut: " + window.Title);
                    output.WriteLine(await File.ReadAllTextAsync(projectPath));
                    output.WriteLine(string.Join(Environment.NewLine, window.FindAllDescendants().Select(e => $"{e.ControlType} id={e.AutomationId} class={e.ClassName} name={e.Name}")));
                    throw;
                }
                AssertSplitRanges(await ProjectStore.LoadAsync(projectPath));

                Find(window, "UndoButton").AsButton().Invoke();
                WaitFor(() => Find(window, "RedoButton").IsEnabled, "Undo did not enable redo.");
                SaveButton(window).Invoke();
                var unsplit = await WaitForSavedClipsAsync(projectPath, 1);
                Assert.Equal(90, Assert.Single(unsplit.Tracks[0].Clips).DurationFrames);

                Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Y);
                SaveButton(window).Invoke();
                AssertSplitRanges(await WaitForSavedClipsAsync(projectPath, 2));
                WaitFor(() => !window.Title.Contains('*'), "Saving did not clear the modified indicator.");
                RenderAndVerify(window, executable, Path.Combine(temporary, "edited.mp4"));
                running.CloseGracefully();
            }

            using (var reopened = Launch(executable, projectPath, temporary))
            {
                VerifyLoadedWorkspace(reopened.Window);
                Assert.False(Find(reopened.Window, "UndoButton").IsEnabled);
                Assert.False(Find(reopened.Window, "RedoButton").IsEnabled);
                Chord(reopened.Window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                var propertiesTab = reopened.Window.FindFirstDescendant(c => c.ByControlType(ControlType.TabItem).And(c.ByName("Clip Properties")));
                Assert.NotNull(propertiesTab);
                propertiesTab.AsTabItem().Select();
                WaitFor(() => Find(reopened.Window, "ClipDuration").AsTextBox().Text == "30", "Reopened inspector does not show the saved split clip.");
                Assert.Equal("0", Find(reopened.Window, "ClipStart").AsTextBox().Text);
                Assert.Equal("0", Find(reopened.Window, "ClipIn").AsTextBox().Text);
                AssertSplitRanges(await ProjectStore.LoadAsync(projectPath));
                reopened.CloseGracefully();
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private async Task VerifyTimelineProvidersAsync(Window window, string projectPath)
    {
        var timeline = Find(window, "Timeline");
        Assert.Equal("OpenRendTimeline", timeline.ClassName);
        Assert.True(timeline.Patterns.Selection.IsSupported);
        var clip = Assert.Single(timeline.FindAllDescendants(c => c.ByClassName("TimelineClip")));
        Assert.True(clip.Patterns.SelectionItem.IsSupported);
        clip.Patterns.SelectionItem.Pattern.Select();
        WaitFor(() => clip.Patterns.SelectionItem.Pattern.IsSelected.Value, "Timeline clip selection provider did not select the clip.");
        Assert.Single(timeline.Patterns.Selection.Pattern.Selection.Value);
        foreach (var action in new[] { "Mute", "Solo", "Lock", "Visible" })
        {
            var button = Assert.Single(timeline.FindAllDescendants(c => c.ByClassName("TimelineTrackButton")), b => b.AutomationId.EndsWith("_" + action));
            Assert.True(button.Patterns.Invoke.IsSupported);
            Assert.True(button.Patterns.Toggle.IsSupported);
            var before = button.Patterns.Toggle.Pattern.ToggleState.Value;
            button.Patterns.Invoke.Pattern.Invoke();
            WaitFor(() => button.Patterns.Toggle.Pattern.ToggleState.Value != before, action + " did not update its accessible toggle state.");
            SaveButton(window).Invoke();
            await WaitForSavedStateAsync(projectPath, action, before != ToggleState.On);
            Find(window, "UndoButton").AsButton().Invoke();
            WaitFor(() => button.Patterns.Toggle.Pattern.ToggleState.Value == before, action + " did not restore its accessible state after undo.");
            SaveButton(window).Invoke();
            await WaitForSavedStateAsync(projectPath, action, before == ToggleState.On);
        }
        var opacity = Assert.Single(timeline.FindAllDescendants(c => c.ByClassName("TimelineTrackLevel")));
        Assert.True(opacity.Patterns.RangeValue.IsSupported);
        opacity.Patterns.RangeValue.Pattern.SetValue(.25);
        WaitFor(() => opacity.Patterns.RangeValue.Pattern.Value.Value == .25, "Track opacity did not change.");
        SaveButton(window).Invoke();
        WaitFor(() => ProjectStore.LoadAsync(projectPath).GetAwaiter().GetResult().Tracks[0].Opacity == .25, "Track opacity was not saved.");
        Find(window, "UndoButton").AsButton().Invoke();
        WaitFor(() => opacity.Patterns.RangeValue.Pattern.Value.Value == 1, "Track opacity undo did not restore the slider.");
        SaveButton(window).Invoke();
        WaitFor(() => ProjectStore.LoadAsync(projectPath).GetAwaiter().GetResult().Tracks[0].Opacity == 1, "Track opacity undo was not saved.");
        opacity.Focus();
        WaitFor(() => opacity.Properties.HasKeyboardFocus.Value, "The track slider did not report keyboard focus.");
        Chord(window, VirtualKeyShort.ESCAPE);
        Chord(window, VirtualKeyShort.LEFT);
        Assert.Equal(1, opacity.Patterns.RangeValue.Pattern.Value.Value);
    }

    private async Task VerifyPanCropAsync(Window window, string projectPath)
    {
        Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.END);
        var tools = window.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName("Tools")));
        Assert.NotNull(tools); tools.AsMenuItem().Expand();
        Find(window, "PanCropMenu").AsMenuItem().Invoke();
        Window? crop = null;
        WaitFor(() => (crop = window.ModalWindows.SingleOrDefault(w => w.Title == "Video event Pan/Crop")) is not null, "Pan/Crop did not open.");
        Assert.NotNull(crop);
        Assert.Equal("89", Find(crop, "CropFrame").AsTextBox().Text);
        var initialFrame = Find(crop, "CropFrame").AsTextBox(); initialFrame.Focus(); initialFrame.Text = "0"; Chord(crop, VirtualKeyShort.RETURN);
        var width = Find(crop, "CropWidth").AsTextBox();
        Assert.Equal("320", width.Text);
        var preset = crop.FindFirstDescendant(c => c.ByControlType(ControlType.ComboBox).And(c.ByName("Crop preset")));
        Assert.NotNull(preset); preset.AsComboBox().Select(1);
        WaitFor(() => width.Text == "160", "Half-size preset did not crop the source.");
        preset.AsComboBox().Select(0);
        WaitFor(() => width.Text == "320", "Full-frame preset did not restore the source.");
        width.Focus(); width.Text = "160"; Find(crop, "CropFrame").Focus();
        WaitFor(() => Find(crop, "CropHeight").AsTextBox().Text == "90", "Aspect lock did not resize crop height.");
        var curve = crop.FindFirstDescendant(c => c.ByControlType(ControlType.ComboBox).And(c.ByName("Keyframe interpolation")));
        Assert.NotNull(curve); Assert.Equal(6, curve.AsComboBox().Items.Length);
        curve.AsComboBox().Select(5); curve.AsComboBox().Select(3);
        var frame = Find(crop, "CropFrame").AsTextBox(); frame.Text = "60"; frame.Focus();
        Chord(crop, VirtualKeyShort.RETURN);
        width.Focus(); width.Text = "80"; frame.Focus();
        WaitFor(() => Find(crop, "CropHeight").AsTextBox().Text == "45", "Second crop key was not applied.");
        var angle = Find(crop, "CropRotationDegrees").AsTextBox(); angle.Focus(); angle.Text = "90"; frame.Focus();
        WaitFor(() => angle.Text == "90", "Crop angle was not committed.");
        var lane = crop.FindFirstDescendant(c => c.ByName("Crop keyframe timeline")); Assert.NotNull(lane);
        var bounds = lane.BoundingRectangle;
        System.Drawing.Point KeyPoint(long at) => new((int)Math.Round(bounds.Left + 4 + (bounds.Width - 10) * at / 89.0), bounds.Top + 31);
        Mouse.Drag(new System.Drawing.Point(KeyPoint(60).X, bounds.Top + 5), new System.Drawing.Point(KeyPoint(45).X, bounds.Top + 5), MouseButton.Left); Wait.UntilInputIsProcessed();
        Assert.Equal("60", Find(crop, "CropKeyframeFrame").AsTextBox().Text);
        Mouse.Drag(KeyPoint(60), KeyPoint(45), MouseButton.Left); Wait.UntilInputIsProcessed();
        WaitFor(() => frame.Text == "45", "Dragging the crop diamond did not move its keyframe.");
        var keyTime = Find(crop, "CropKeyframeFrame").AsTextBox(); keyTime.Focus(); keyTime.Text = "30"; frame.Focus();
        WaitFor(() => frame.Text == "30", "Exact keyframe timing did not move the key.");
        keyTime.Focus(); keyTime.Text = "0"; frame.Focus();
        WaitFor(() => frame.Text == "0" && width.Text == "80", "Retiming onto an occupied frame did not keep the moved key.");
        var undoCrop = crop.FindFirstDescendant(c => c.ByControlType(ControlType.Button).And(c.ByName("Undo crop edit")));
        Assert.NotNull(undoCrop); undoCrop.AsButton().Invoke();
        frame.Focus(); frame.Text = "30"; Chord(crop, VirtualKeyShort.RETURN);
        WaitFor(() => keyTime.Text == "30" && width.Text == "80", "Undo did not restore the moved keyframe.");
        crop.Close(); SaveButton(window).Invoke();
        WaitFor(() => ProjectStore.LoadAsync(projectPath).GetAwaiter().GetResult().Tracks[0].Clips[0].Crop?.Keyframes.Count == 2, "Crop keyframes were not saved.");
        var saved = (await ProjectStore.LoadAsync(projectPath)).Tracks[0].Clips[0].Crop!;
        Assert.Equal(new long[] { 0, 30 }, saved.Keyframes.Select(key => key.Frame));
        Assert.Equal(160, saved.Keyframes[0].State.Width); Assert.Equal(80, saved.Keyframes[1].State.Width);
        Assert.Equal(90, saved.Keyframes[1].State.RotationDegrees);
        Assert.All(saved.Keyframes, key => Assert.Equal(CropInterpolation.Fast, key.Interpolation));
        tools.AsMenuItem().Expand(); Find(window, "PanCropMenu").AsMenuItem().Invoke();
        WaitFor(() => (crop = window.ModalWindows.SingleOrDefault(w => w.Title == "Video event Pan/Crop")) is not null, "Saved Pan/Crop did not reopen.");
        Assert.NotNull(crop);
        Assert.Equal("80", Find(crop, "CropWidth").AsTextBox().Text);
        var reset = crop.FindFirstDescendant(c => c.ByControlType(ControlType.Button).And(c.ByName("Reset Pan/Crop")));
        Assert.NotNull(reset); reset.AsButton().Invoke();
        WaitFor(() => Find(crop, "CropWidth").AsTextBox().Text == "320", "Reset did not restore the source crop.");
        crop.Close(); Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.HOME); SaveButton(window).Invoke();
        WaitFor(() => ProjectStore.LoadAsync(projectPath).GetAwaiter().GetResult().Tracks[0].Clips[0].Crop is null, "Reset crop was not saved.");
    }

    private static async Task WaitForSavedStateAsync(string path, string action, bool expected)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(8))
        {
            var track = (await ProjectStore.LoadAsync(path)).Tracks[0];
            var value = action switch { "Mute" => track.IsMuted, "Solo" => track.IsSolo, "Lock" => track.IsLocked, _ => track.IsVisible };
            if (value == expected) return;
            await Task.Delay(80);
        }
        Assert.Fail(action + " track state was not persisted.");
    }

    private void RenderAndVerify(Window window, string executable, string outputPath)
    {
        Find(window, "RenderButton").AsButton().Invoke();
        Window? dialog = null;
        WaitFor(() => (dialog = window.ModalWindows.SingleOrDefault()) is not null, "Render did not open its output dialog.");
        Assert.NotNull(dialog);
        var controls = dialog.FindAllDescendants();
        output.WriteLine("Render output dialog: " + string.Join(" | ", controls.Select(e => $"{e.Properties.ControlType.ValueOrDefault}:{e.Properties.Name.ValueOrDefault}:{e.Properties.AutomationId.ValueOrDefault}")));
        var fileName = Assert.Single(controls, e => e.ControlType == ControlType.Edit && e.Name.Contains("File name", StringComparison.OrdinalIgnoreCase)).AsTextBox();
        fileName.Text = outputPath;
        var save = Assert.Single(controls, e => e.ControlType == ControlType.Button && e.Name.TrimStart('&') == "Save").AsButton();
        save.Invoke();
        WaitFor(() => File.Exists(outputPath), "The desktop render did not produce a finalized output file.", timeoutSeconds: 30);
        var probe = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(executable)!, "ffprobe.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-v", "error", "-show_entries", "stream=codec_type,width,height,nb_frames", "-of", "json", outputPath }) probe.ArgumentList.Add(argument);
        using var process = Process.Start(probe)!;
        var json = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10000), "Output verification timed out.");
        Assert.True(process.ExitCode == 0, error);
        using var result = JsonDocument.Parse(json);
        var video = result.RootElement.GetProperty("streams").EnumerateArray().Single(s => s.GetProperty("codec_type").GetString() == "video");
        Assert.Equal(320, video.GetProperty("width").GetInt32());
        Assert.Equal(180, video.GetProperty("height").GetInt32());
        Assert.Equal("90", video.GetProperty("nb_frames").GetString());
    }

    private RunningApplication Launch(string executable, string projectPath, string dataDirectory)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false };
        start.ArgumentList.Add(projectPath);
        start.Environment["OPENREND_DATA_DIR"] = dataDirectory;
        var app = Application.Launch(start);
        var automation = new UIA3Automation();
        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(12));
            Assert.NotNull(window);
            WaitFor(() => window.Title == "UI integration — OpenRend", "The command-line project was not opened.");
            return new RunningApplication(app, automation, window);
        }
        catch { app.Kill(); app.Dispose(); automation.Dispose(); throw; }
    }

    private void VerifyLoadedWorkspace(Window window)
    {
        try
        {
            Assert.Equal("UI integration — OpenRend", window.Title);
            var library = Find(window, "MediaList").AsListBox();
            WaitFor(() => library.Items.Length == 1, "The project media library is empty.");
            Assert.Contains("fixture.mp4", library.Items[0].FindAllDescendants().Select(e => e.Name));
            WaitFor(() => Find(window, "PreviewInfo").Properties.HelpText.ValueOrDefault?.Contains("Direct3D 11") == true, "The native preview did not initialize.");
            var nativeChildren = window.FindAllDescendants(c => c.ByClassName("OpenRend.Preview"));
            Assert.Contains(nativeChildren, e => e.Properties.NativeWindowHandle.ValueOrDefault != 0 && e.BoundingRectangle.Width > 100 && e.BoundingRectangle.Height > 50);
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            Assert.True(GetMonitorInfo(MonitorFromWindow(window.Properties.NativeWindowHandle.Value, 2), ref monitor));
            var bounds = window.BoundingRectangle;
            Assert.True(bounds.Top >= monitor.Work.Top - 1 && bounds.Left >= monitor.Work.Left - 1
                && bounds.Right <= monitor.Work.Right + 1 && bounds.Bottom <= monitor.Work.Bottom + 1, "The maximized workspace extends outside the monitor work area.");
            foreach (var (filter, expected) in new[] { ("Video", 1), ("Audio", 1), ("Missing Media", 0), ("All Media", 1) })
            {
                var item = Assert.Single(Find(window, "MediaTree").FindAllDescendants(c => c.ByControlType(ControlType.TreeItem).And(c.ByName(filter))));
                item.Patterns.SelectionItem.Pattern.Select();
                WaitFor(() => library.Items.Length == expected, "Media filter did not update: " + filter);
            }
            var split = window.FindFirstDescendant(c => c.ByControlType(ControlType.Button).And(c.ByName("Split")));
            Assert.NotNull(split);
            Assert.True(split.IsEnabled);
            Assert.True(Find(window, "PlayButton").IsEnabled);
        }
        catch
        {
            output.WriteLine(string.Join(Environment.NewLine, window.FindAllDescendants().Select(e => $"{e.ControlType} id={e.AutomationId} class={e.ClassName} name={e.Name}")));
            throw;
        }
    }

    private static AutomationElement Find(Window window, string automationId)
    {
        var element = window.FindFirstDescendant(c => c.ByAutomationId(automationId));
        Assert.NotNull(element);
        return element;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    private static Button SaveButton(Window window) => window.FindFirstDescendant(c => c.ByControlType(ControlType.Button).And(c.ByName("Save")))?.AsButton()
        ?? throw new Xunit.Sdk.XunitException("The Save button was not exposed.");

    private static void Chord(Window window, params VirtualKeyShort[] keys)
    {
        window.Focus();
        if (keys.Length > 1)
        {
            using (Keyboard.Pressing(keys[..^1]))
            {
                Wait.UntilInputIsProcessed();
                Keyboard.Type(keys[^1]);
                Wait.UntilInputIsProcessed();
            }
        }
        else Keyboard.Type(keys[0]);
        Wait.UntilInputIsProcessed();
    }

    private static void WaitFor(Func<bool> condition, string failure, int timeoutSeconds = 8)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
        {
            if (condition()) return;
            Thread.Sleep(80);
        }
        Assert.Fail(failure);
    }

    private static async Task<ProjectDocument> WaitForSavedClipsAsync(string path, int count)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(8))
        {
            var project = await ProjectStore.LoadAsync(path);
            if (project.Tracks.SelectMany(t => t.Clips).Count() == count) return project;
            await Task.Delay(80);
        }
        throw new Xunit.Sdk.XunitException($"Saved project did not contain {count} clips.");
    }

    private static void AssertSplitRanges(ProjectDocument project)
    {
        var clips = project.Tracks.SelectMany(t => t.Clips).OrderBy(c => c.StartFrame).ToArray();
        Assert.Equal(2, clips.Length);
        Assert.Equal((0L, 0L, 30L), (clips[0].StartFrame, clips[0].SourceInFrame, clips[0].DurationFrames));
        Assert.Equal((30L, 30L, 60L), (clips[1].StartFrame, clips[1].SourceInFrame, clips[1].DurationFrames));
        Assert.Equal(90, project.DurationFrames);
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "OpenRend.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the OpenRend repository.");
    }

    private sealed class RunningApplication(Application app, UIA3Automation automation, Window window) : IDisposable
    {
        public Window Window { get; } = window;
        public void CloseGracefully()
        {
            WaitFor(() => Find(Window, "RenderButton").IsEnabled, "Render did not finish before closing.");
            Window.Close();
            WaitFor(() => app.HasExited, "The clean project did not close gracefully.");
        }
        public void Dispose()
        {
            if (!app.HasExited) app.Kill();
            app.Dispose();
            automation.Dispose();
        }
    }
}
