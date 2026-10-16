using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.Capturing;
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
    public async Task EventButtonsEditAnOrderedEffectChainAndPersistIt()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        var projectPath = Path.Combine(temporary, "event-fx.openrend");
        var media = new MediaAsset { Path = Path.Combine(root, "tests", "native", "fixture.mp4"), Width = 320, Height = 180, DurationSeconds = 3, FrameRate = 30, HasVideo = true, HasAudio = true };
        var clip = new TimelineClip { MediaId = media.Id, Name = "fixture.mp4", DurationFrames = 90 };
        await ProjectStore.SaveAsync(new() { Name = "UI integration", Width = 320, Height = 180, Media = [media], Tracks = [new() { Kind = TrackKind.Video, Name = "Video 1", Clips = [clip] }] }, projectPath);
        try
        {
            using (var running = Launch(executable, projectPath, temporary))
            {
                var window = running.Window; VerifyLoadedWorkspace(window);
                Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
                WaitFor(() => window.FindFirstDescendant(c => c.ByAutomationId($"Clip_{clip.Id:N}_PanCrop")) is not null, "Fitting the timeline did not expose the event buttons.");
                var cropButton = Find(window, $"Clip_{clip.Id:N}_PanCrop").AsButton();
                cropButton.Invoke(); Window? crop = null;
                WaitFor(() => (crop = window.ModalWindows.SingleOrDefault(w => w.Title == "Video event Pan/Crop")) is not null, "The event Pan/Crop button did not open its editor.");
                Assert.NotNull(crop); crop.Close();
                Find(window, $"Clip_{clip.Id:N}_More").AsButton().Invoke();
                AutomationElement? options = null;
                WaitFor(() => (options = window.Automation.GetDesktop().FindFirstDescendant(c => c.ByControlType(ControlType.Menu).And(c.ByProcessId(window.Properties.ProcessId.Value)))) is not null, "Event options did not open.");
                Assert.NotNull(options);
                var lengths = options.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName("Show event lengths"))); Assert.NotNull(lengths); lengths.AsMenuItem().Invoke();
                var preview = Assert.Single(window.FindAllDescendants(c => c.ByClassName("OpenRend.Preview")));
                WaitFor(() => PresentedFrame(window) == 0, "The initial event frame did not render."); var original = SamplePreview(preview);
                var fxButton = Find(window, $"Clip_{clip.Id:N}_VideoEffects");
                Mouse.Click(fxButton.GetClickablePoint());
                Window? effects = null;
                WaitFor(() => (effects = window.ModalWindows.SingleOrDefault(w => w.Title == "Video event FX")) is not null, "Clicking the event FX button did not open its editor.");
                Assert.NotNull(effects);
                VerifyFxChooser(effects, root);
                Find(effects, "AddVideoEffect").AsButton().Invoke();
                var amount = Find(effects, "VideoEffectAmount").AsTextBox(); amount.Focus(); amount.Text = "0.25"; Chord(effects, VirtualKeyShort.RETURN);
                WaitFor(() => Find(effects, "VideoEffectAmount").AsTextBox().Text == "0.25", "Brightness was not applied.");
                var brightnessSlider = effects.FindFirstDescendant(c => c.ByControlType(ControlType.Slider).And(c.ByName("Brightness"))); Assert.NotNull(brightnessSlider);
                var sliderBounds = brightnessSlider.BoundingRectangle;
                Mouse.Drag(new System.Drawing.Point((int)(sliderBounds.Left + sliderBounds.Width * .625), (int)(sliderBounds.Top + sliderBounds.Height / 2)),
                    new System.Drawing.Point((int)(sliderBounds.Left + sliderBounds.Width * .8), (int)(sliderBounds.Top + sliderBounds.Height / 2)), MouseButton.Left);
                WaitFor(() => double.Parse(Find(effects, "VideoEffectAmount").AsTextBox().Text, System.Globalization.CultureInfo.InvariantCulture) > .3, "Dragging the brightness slider did not apply its value.");
                Find(effects, "UndoVideoEffect").AsButton().Invoke();
                WaitFor(() => Find(effects, "VideoEffectAmount").AsTextBox().Text == "0.25", "One undo did not restore the entire slider drag.");
                Mouse.MoveTo(new System.Drawing.Point((int)(sliderBounds.Left + sliderBounds.Width * .625), (int)(sliderBounds.Top + sliderBounds.Height / 2)));
                Mouse.Down(MouseButton.Left);
                try
                {
                    Mouse.MoveTo(new System.Drawing.Point((int)(sliderBounds.Left + sliderBounds.Width * .8), (int)(sliderBounds.Top + sliderBounds.Height / 2))); Wait.UntilInputIsProcessed();
                    Chord(effects, VirtualKeyShort.ESCAPE);
                    Mouse.MoveTo(new System.Drawing.Point((int)(sliderBounds.Left + sliderBounds.Width * .9), (int)(sliderBounds.Top + sliderBounds.Height / 2))); Wait.UntilInputIsProcessed();
                }
                finally { Mouse.Up(MouseButton.Left); }
                Assert.Equal("0.25", Find(effects, "VideoEffectAmount").AsTextBox().Text);
                brightnessSlider.Focus(); Chord(effects, VirtualKeyShort.RIGHT); Chord(effects, VirtualKeyShort.RIGHT);
                WaitFor(() => Find(effects, "VideoEffectAmount").AsTextBox().Text == "0.27", "Repeated keyboard slider steps lost focus.");
                Find(effects, "UndoVideoEffect").AsButton().Invoke(); Find(effects, "UndoVideoEffect").AsButton().Invoke();
                WaitFor(() => Find(effects, "VideoEffectAmount").AsTextBox().Text == "0.25", "Undo did not restore the keyboard edits.");
                var available = Find(effects, "AvailableVideoEffect").AsComboBox(); available.Select(2); available.Patterns.ExpandCollapse.Pattern.Collapse();
                Find(effects, "AddVideoEffect").AsButton().Invoke();
                WaitFor(() => Find(effects, "VideoEffectChain").AsListBox().Items.Length == 2, "Adding another effect did not extend the chain.");
                Find(effects, "MoveVideoEffectUp").AsButton().Invoke();
                WaitFor(() => Find(effects, "VideoEffectChain").AsListBox().Items[0].Text.Contains("Invert"), "Moving the effect did not change its order.");
                Find(effects, "VideoEffectEnabled").AsCheckBox().Toggle();
                WaitFor(() => Find(effects, "VideoEffectChain").AsListBox().Items[0].Text.Contains("bypassed"), "Effect bypass was not applied.");
                Find(effects, "VideoEffectEnabled").AsCheckBox().Toggle();
                amount = Find(effects, "VideoEffectAmount").AsTextBox(); amount.Focus(); amount.Text = "2"; Chord(effects, VirtualKeyShort.RETURN);
                Assert.Contains("from 0 to 1", string.Join(" ", effects.FindAllDescendants().Select(element => element.Name)));
                amount.Text = "0.5"; Chord(effects, VirtualKeyShort.RETURN);
                Find(effects, "UndoVideoEffect").AsButton().Invoke();
                WaitFor(() => Find(effects, "VideoEffectAmount").AsTextBox().Text == "1", "Undo did not restore the effect amount.");
                var artifact = Path.Combine(root, ".local", "desktop-validation"); Directory.CreateDirectory(artifact);
                using (var screenshot = Capture.Element(effects)) screenshot.ToFile(Path.Combine(artifact, "event-fx.png"));
                Find(effects, "CloseVideoEffects").AsButton().Invoke();
                WaitFor(() => PreviewDifference(original, SamplePreview(preview)) > 20, "The native timeline preview did not show the effect chain.");
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "Saving the effect chain did not clear the modified indicator.");
                var saved = (await ProjectStore.LoadAsync(projectPath)).Tracks[0].Clips[0].VideoEffects;
                Assert.Equal(new[] { VideoEffectKind.Invert, VideoEffectKind.BrightnessContrast }, saved.Select(effect => effect.Kind)); Assert.All(saved, effect => Assert.True(effect.IsEnabled)); Assert.Equal(.25, saved[1].Amount);
                using (var screenshot = Capture.Element(window)) screenshot.ToFile(Path.Combine(artifact, "event-toolbar.png"));
                RenderAndVerify(window, executable, Path.Combine(temporary, "effects.mp4"));
                running.CloseGracefully();
            }
            using (var reopened = Launch(executable, projectPath, temporary))
            {
                var window = reopened.Window; Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
                Find(window, $"Clip_{clip.Id:N}_VideoEffects").AsButton().Invoke(); Window? effects = null;
                WaitFor(() => (effects = window.ModalWindows.SingleOrDefault(w => w.Title == "Video event FX")) is not null, "Saved event effects did not reopen."); Assert.NotNull(effects);
                Assert.Equal(2, Find(effects, "VideoEffectChain").AsListBox().Items.Length);
                Find(effects, "RemoveVideoEffect").AsButton().Invoke();
                WaitFor(() => Find(effects, "VideoEffectChain").AsListBox().Items.Length == 1, "Removing an effect did not update the chain.");
                Find(effects, "UndoVideoEffect").AsButton().Invoke();
                var effectBounds = effects.BoundingRectangle;
                try { WaitFor(() => Find(effects, "VideoEffectChain").AsListBox().Items.Length == 2, "Undo did not restore the removed effect."); }
                catch
                {
                    using var diagnostic = Capture.Rectangle(effectBounds); diagnostic.ToFile(Path.Combine(root, ".local", "desktop-validation", "fx-undo-failure.png"));
                    throw;
                }
                Find(effects, "CloseVideoEffects").AsButton().Invoke(); SaveButton(window).Invoke(); WaitFor(() => !window.Title.Contains('*'), "Saving restored effects did not finish."); reopened.CloseGracefully();
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static void VerifyFxChooser(Window effects, string root)
    {
        Window Open()
        {
            Find(effects, "OpenFxChooser").AsButton().Invoke(); Window? chooser = null;
            WaitFor(() => (chooser = effects.ModalWindows.SingleOrDefault(window => window.Title == "Plug-In Chooser - Video Event FX")) is not null, "The FX chooser did not open.");
            return chooser!;
        }
        static void Select(Window chooser, string name)
        {
            Find(chooser, "FxChooserSearch").AsTextBox().Text = name;
            AutomationElement? item = null;
            WaitFor(() => (item = Find(chooser, "FxChooserCatalog").FindFirstDescendant(c => c.ByControlType(ControlType.ListItem).And(c.ByName(name)))) is not null, "The FX catalog did not find " + name + ".");
            item!.Patterns.SelectionItem.Pattern.Select();
        }
        var chooser = Open();
        Select(chooser, "Gaussian Blur"); Find(chooser, "FxChooserAdd").AsButton().Invoke();
        Assert.Single(Find(chooser, "FxChooserChain").AsListBox().Items);
        Find(chooser, "FxChooserCancel").AsButton().Invoke();
        WaitFor(() => effects.ModalWindows.Length == 0, "Cancel did not close the FX chooser.");
        Assert.Empty(Find(effects, "VideoEffectChain").AsListBox().Items);
        chooser = Open();
        var color = Find(chooser, "FxChooserCategories").FindFirstDescendant(c => c.ByControlType(ControlType.TreeItem).And(c.ByName("Color"))); Assert.NotNull(color);
        color.Patterns.SelectionItem.Pattern.Select();
        Find(chooser, "FxChooserSearch").AsTextBox().Text = "Gaussian";
        WaitFor(() => Find(chooser, "FxChooserCatalog").AsListBox().Items.Length == 0, "The category filter did not restrict search results.");
        var all = Find(chooser, "FxChooserCategories").FindFirstDescendant(c => c.ByControlType(ControlType.TreeItem).And(c.ByName("FX"))); Assert.NotNull(all); all.Patterns.SelectionItem.Pattern.Select();
        Select(chooser, "Black and White"); Find(chooser, "FxChooserAdd").AsButton().Invoke();
        Select(chooser, "Gaussian Blur"); Find(chooser, "FxChooserAdd").AsButton().Invoke();
        Assert.Equal(2, Find(chooser, "FxChooserChain").AsListBox().Items.Length);
        Find(chooser, "FxChooserChain").AsListBox().Items[0].Patterns.SelectionItem.Pattern.Select();
        Select(chooser, "Sepia"); Find(chooser, "FxChooserReplace").AsButton().Invoke();
        Assert.Equal("Sepia", Find(chooser, "FxChooserChain").AsListBox().Items[0].Text);
        Find(chooser, "FxChooserRemove").AsButton().Invoke();
        Assert.Single(Find(chooser, "FxChooserChain").AsListBox().Items);
        Find(chooser, "FxChooserSearch").AsTextBox().Text = "";
        WaitFor(() => Find(chooser, "FxChooserCatalog").AsListBox().Items.Length >= VideoEffectCatalog.Definitions.Count, "Clearing search did not restore the catalog.");
        var artifact = Path.Combine(root, ".local", "desktop-validation"); Directory.CreateDirectory(artifact);
        using (var screenshot = Capture.Element(chooser)) screenshot.ToFile(Path.Combine(artifact, "fx-chooser.png"));
        Find(chooser, "FxChooserAccept").AsButton().Invoke();
        WaitFor(() => Find(effects, "VideoEffectChain").AsListBox().Items.Length == 1, "Accept did not apply the staged chain.");
        Assert.Contains("Gaussian Blur", Find(effects, "VideoEffectChain").AsListBox().Items[0].Text);
        Find(effects, "UndoVideoEffect").AsButton().Invoke();
        WaitFor(() => Find(effects, "VideoEffectChain").AsListBox().Items.Length == 0, "One undo did not restore the complete chain from before the chooser.");
    }

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
                VerifyVideoPlayback(window);
                await VerifyTrimPointerAsync(window, projectPath);
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

    private void VerifyVideoPlayback(Window window)
    {
        var preview = Assert.Single(window.FindAllDescendants(c => c.ByClassName("OpenRend.Preview")));
        WaitFor(() => PresentedFrame(window) == 0, "The initial video frame was not presented.");
        var first = SamplePreview(preview);
        Find(window, "PlayButton").AsButton().Invoke();
        WaitFor(() => Find(window, "PlaybackRate").Name == "Rate: 1.00", "Playback did not start.");
        WaitFor(() => PresentedFrame(window) >= 6, "Video preview stayed on its first frame while the playback clock advanced.");
        var firstPlayingFrame = PresentedFrame(window);
        var duringPlayback = SamplePreview(preview);
        Assert.Equal("Rate: 1.00", Find(window, "PlaybackRate").Name);
        Assert.True(PreviewDifference(first, duringPlayback) > 1, "The native preview pixels did not change during playback.");
        WaitFor(() => PresentedFrame(window) >= firstPlayingFrame + 6, "Video did not continue advancing during playback.");
        var laterPlayingFrame = PresentedFrame(window);
        var later = SamplePreview(preview);
        Assert.Equal("Rate: 1.00", Find(window, "PlaybackRate").Name);
        Assert.True(PreviewDifference(duringPlayback, later) > 1, "The native preview stopped updating after one playback frame.");
        Chord(window, VirtualKeyShort.ESCAPE);
        WaitFor(() => Find(window, "PlaybackRate").Name == "Rate: 0.00" && PresentedFrame(window) == 0, "Stop did not present the exact cursor frame.");
        Assert.True(PreviewDifference(first, SamplePreview(preview)) < .5, "A late playback render replaced the stopped frame.");
        output.WriteLine($"Visible native preview changed through playback frames {firstPlayingFrame} and {laterPlayingFrame}; Stop restored frame 0.");
    }

    private static long PresentedFrame(Window window)
    {
        var text = Find(window, "PreviewInfo").Name;
        var marker = text.IndexOf("Frame: ", StringComparison.Ordinal);
        return marker >= 0 && long.TryParse(text[(marker + 7)..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], out var frame) ? frame : -1;
    }

    private static byte[] SamplePreview(AutomationElement preview)
    {
        using var image = Capture.Element(preview);
        var pixels = new byte[32 * 18 * 3];
        for (var y = 0; y < 18; y++)
        for (var x = 0; x < 32; x++)
        {
            var color = image.Bitmap.GetPixel((2 * x + 1) * image.Bitmap.Width / 64, (2 * y + 1) * image.Bitmap.Height / 36);
            var index = (y * 32 + x) * 3;
            pixels[index] = color.R; pixels[index + 1] = color.G; pixels[index + 2] = color.B;
        }
        return pixels;
    }

    private static double PreviewDifference(byte[] left, byte[] right) => left.Zip(right, (a, b) => Math.Abs(a - b)).Average();

    private static async Task VerifyTrimPointerAsync(Window window, string projectPath)
    {
        var timeline = Find(window, "Timeline");
        var clip = Assert.Single(timeline.FindAllDescendants(c => c.ByClassName("TimelineClip")));
        var bounds = clip.BoundingRectangle;
        var scale = GetDpiForWindow(window.Properties.NativeWindowHandle.Value) / 96.0;
        var outside = new System.Drawing.Point((int)Math.Round(bounds.Right + 8 * scale), bounds.Top + bounds.Height / 2);
        var shortened = new System.Drawing.Point(outside.X - (int)Math.Round(bounds.Width / 3.0), outside.Y);
        Mouse.MoveTo(outside); WaitForCursor(32644);
        Mouse.Down(MouseButton.Left);
        try { Mouse.MoveTo(shortened); Wait.UntilInputIsProcessed(); WaitForCursor(32644); }
        finally { Mouse.Up(MouseButton.Left); }
        SaveButton(window).Invoke();
        WaitFor(() => ProjectStore.LoadAsync(projectPath).GetAwaiter().GetResult().Tracks[0].Clips[0].DurationFrames < 90, "Dragging from outside the edge did not trim the clip.");
        var trimmed = (await ProjectStore.LoadAsync(projectPath)).Tracks[0].Clips[0];
        Assert.InRange(trimmed.DurationFrames, 55, 65); Assert.Equal(0, trimmed.StartFrame); Assert.Equal(0, trimmed.SourceInFrame);
        Find(window, "UndoButton").AsButton().Invoke(); SaveButton(window).Invoke();
        WaitFor(() => ProjectStore.LoadAsync(projectPath).GetAwaiter().GetResult().Tracks[0].Clips[0].DurationFrames == 90, "Undo did not restore the trimmed clip.");
        Mouse.MoveTo(outside); WaitForCursor(32644);
        Mouse.Down(MouseButton.Left);
        try { Mouse.MoveTo(shortened); Keyboard.Type(VirtualKeyShort.ESCAPE); Wait.UntilInputIsProcessed(); }
        finally { Mouse.Up(MouseButton.Left); }
        SaveButton(window).Invoke();
        Assert.Equal(90, (await ProjectStore.LoadAsync(projectPath)).Tracks[0].Clips[0].DurationFrames);
        Mouse.MoveTo(new System.Drawing.Point(bounds.Left + bounds.Width / 2, outside.Y)); WaitForCursor(32512);
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
        preset.Patterns.ExpandCollapse.Pattern.Collapse();
        var workspace = crop.FindFirstDescendant(c => c.ByName("Pan/Crop source workspace")); Assert.NotNull(workspace);
        var workspaceBounds = workspace.BoundingRectangle;
        var displayScale = GetDpiForWindow(crop.Properties.NativeWindowHandle.Value) / 96.0;
        var sourceScale = Math.Min((workspaceBounds.Width - 60 * displayScale) / 320, (workspaceBounds.Height - 60 * displayScale) / 180);
        var corner = new System.Drawing.Point((int)Math.Round(workspaceBounds.Left + workspaceBounds.Width / 2.0 - 80 * sourceScale), (int)Math.Round(workspaceBounds.Top + workspaceBounds.Height / 2.0 - 45 * sourceScale));
        Mouse.MoveTo(corner);
        try { WaitForCursor(32642); }
        catch
        {
            var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() }; GetCursorInfo(ref info);
            output.WriteLine($"Crop bounds={workspaceBounds}; source scale={sourceScale}; requested corner={corner}; pointer={info.X},{info.Y}; cursor={info.Cursor}; expected={LoadCursor(0, 32642)}; height={Find(crop, "CropHeight").AsTextBox().Text}; angle={Find(crop, "CropRotationDegrees").AsTextBox().Text}");
            var diagnostic = Path.Combine(Path.GetTempPath(), "OpenRend-crop-pointer-" + Guid.NewGuid().ToString("N") + ".png");
            using var screenshot = Capture.Element(crop); screenshot.ToFile(diagnostic); output.WriteLine("Crop pointer diagnostic: " + diagnostic);
            throw;
        }
        Mouse.Drag(corner, new System.Drawing.Point(corner.X - (int)Math.Round(10 * sourceScale), corner.Y), MouseButton.Left);
        WaitFor(() => double.Parse(width.Text, System.Globalization.CultureInfo.InvariantCulture) > 170, "Dragging the upper-left crop handle did not resize the crop.");
        var undoResize = crop.FindFirstDescendant(c => c.ByControlType(ControlType.Button).And(c.ByName("Undo crop edit"))); Assert.NotNull(undoResize); undoResize.AsButton().Invoke();
        WaitFor(() => width.Text == "160", "Undo did not restore the corner resize.");
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
        System.Drawing.Point KeyPoint(long at) => new((int)Math.Round(bounds.Left + 4 + (bounds.Width - 10) * at / 90.0), bounds.Top + 31);
        AutomationElement? curveMenu = null;
        AutomationElement? FindCurveMenu() => crop.Automation.GetDesktop().FindFirstDescendant(c => c.ByAutomationId("CropCurveMenu").And(c.ByProcessId(crop.Properties.ProcessId.Value)));
        Mouse.Click(KeyPoint(0), MouseButton.Right);
        WaitFor(() => (curveMenu = FindCurveMenu()) is not null, "The keyframe interpolation menu did not open.");
        Assert.NotNull(curveMenu);
        var modes = curveMenu.FindAllDescendants(c => c.ByControlType(ControlType.MenuItem));
        Assert.Equal(new[] { "Linear", "Fast", "Slow", "Smooth", "Sharp", "Hold" }, modes.Select(item => item.Name));
        Assert.True(modes[1].AsMenuItem().IsChecked);
        modes[4].AsMenuItem().Invoke();
        WaitFor(() => curve.AsComboBox().SelectedItem?.Text == "Sharp", "The menu did not apply its curve to the clicked key.");
        lane.Focus(); Chord(crop, VirtualKeyShort.SHIFT, VirtualKeyShort.F10);
        WaitFor(() => (curveMenu = FindCurveMenu()) is not null, "Keyboard access did not open the keyframe menu.");
        Assert.NotNull(curveMenu);
        modes = curveMenu.FindAllDescendants(c => c.ByControlType(ControlType.MenuItem));
        Assert.True(modes[4].AsMenuItem().IsChecked);
        modes[1].AsMenuItem().Invoke();
        WaitFor(() => curve.AsComboBox().SelectedItem?.Text == "Fast", "The keyboard-opened menu did not restore the curve.");
        frame.Focus(); frame.Text = "60"; Chord(crop, VirtualKeyShort.RETURN);
        Mouse.Drag(new System.Drawing.Point(KeyPoint(60).X, bounds.Top + 5), new System.Drawing.Point(KeyPoint(45).X, bounds.Top + 5), MouseButton.Left); Wait.UntilInputIsProcessed();
        Assert.Equal("60", Find(crop, "CropKeyframeFrame").AsTextBox().Text);
        Mouse.Drag(KeyPoint(60), KeyPoint(45), MouseButton.Left); Wait.UntilInputIsProcessed();
        WaitFor(() => frame.Text == "45", "Dragging the crop diamond did not move its keyframe.");
        var keyTime = Find(crop, "CropKeyframeFrame").AsTextBox(); keyTime.Focus(); keyTime.Text = "30"; frame.Focus();
        WaitFor(() => frame.Text == "30", "Exact keyframe timing did not move the key.");
        keyTime.Focus(); keyTime.Text = "0"; frame.Focus();
        WaitFor(() => frame.Text == "0" && width.Text == "80", "Retiming onto an occupied frame did not keep the moved key.");
        frame.Focus(); frame.Text = "1"; Chord(crop, VirtualKeyShort.RETURN);
        WaitFor(() => width.Text == "160", "Retiming did not preserve the destination key on the next frame.");
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
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo { public int Size; public uint Flags; public nint Cursor; public int X, Y; }
    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadCursor(nint instance, nint name);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
    private static void WaitForCursor(int id) => WaitFor(() =>
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        return GetCursorInfo(ref info) && info.Cursor == LoadCursor(0, id);
    }, "The pointer did not show the expected editing cursor (" + id + ").");
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
