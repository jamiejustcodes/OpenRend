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
    public async Task NeutralToneSamplesTheRenderedPictureInDockedAndFloatingGrading()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "neutral-tone.openrend");
        try
        {
            var asset = new MediaAsset
            {
                Path = MediaGeneratorCatalog.CreateUri(MediaGeneratorKind.SolidColor, "Warm gray", new Dictionary<string, string> { ["hex"] = "#9A927A" }),
                IsGenerated = true, HasVideo = true, Width = 640, Height = 360, DurationSeconds = 3, FrameRate = 30,
                Generator = new() { Kind = MediaGeneratorKind.SolidColor, Title = "Solid Color", PresetName = "Warm gray", Parameters = new() { ["hex"] = "#9A927A" } }
            };
            var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 60, Name = "Warm gray" };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 640, Height = 360, Media = [asset],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
            }, path);

            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            Find(window, $"Clip_{clip.Id:N}").Patterns.SelectionItem.Pattern.Select();
            SelectBrowserTab(window, "Color Grading");
            var nativePreview = Assert.Single(window.FindAllDescendants(condition => condition.ByClassName("OpenRend.Preview")));
            WaitFor(() => Warm(ReadCenter(nativePreview)), "The cast source did not reach the native preview.");

            var dialog = OpenSampler(window);
            Assert.False(Find(dialog, "ApplyNeutralTone").IsEnabled);
            Chord(dialog, VirtualKeyShort.ESCAPE);
            WaitFor(() => window.ModalWindows.Length == 0, "Cancel did not close the picker.");
            Assert.DoesNotContain('*', window.Title);
            Assert.True(Warm(ReadCenter(nativePreview)));

            dialog = OpenSampler(window);
            var image = Find(dialog, "NeutralTonePreview");
            WaitFor(() => Warm(ReadCenter(image)), "The picker did not display the rendered frame.");
            Mouse.Click(PointIn(image.BoundingRectangle, .5, .5));
            var apply = Find(dialog, "ApplyNeutralTone").AsButton();
            WaitFor(() => apply.IsEnabled, "Selecting a point did not enable Apply.");
            apply.Invoke();
            WaitFor(() => window.ModalWindows.Length == 0 && window.Title.Contains('*'), "The docked picker did not apply the grade.");
            WaitFor(() => Neutral(ReadCenter(nativePreview)), "The sampled patch stayed tinted in the native preview.");
            var saved = await SaveGrade(window, path);
            CheckGrade(Assert.Single(saved.Tracks[0].Clips[0].VideoEffects));
            Assert.Empty(saved.VideoOutputEffects);

            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            WaitFor(() => Warm(ReadCenter(nativePreview)), "Undo did not restore the source color.");
            saved = await SaveGrade(window, path);
            Assert.Empty(saved.Tracks[0].Clips[0].VideoEffects);

            OpenGradingScope(window, "VideoOutputColorGradingMenu");
            var floating = GradingDialog(window);
            dialog = OpenSampler(floating);
            image = Find(dialog, "NeutralTonePreview");
            WaitFor(() => Warm(ReadCenter(image)), "The floating picker did not display the frame.");
            var bounds = image.BoundingRectangle;
            Mouse.Drag(PointIn(bounds, .25, .25), PointIn(bounds, .72, .74), MouseButton.Left);
            var capture = Path.Combine(root, ".local", "desktop-validation");
            Directory.CreateDirectory(capture);
            using (var screenshot = Capture.Element(dialog)) screenshot.ToFile(Path.Combine(capture, "neutral-tone-region.png"));
            Find(dialog, "ApplyNeutralTone").AsButton().Invoke();
            WaitFor(() => floating.ModalWindows.Length == 0 && window.Title.Contains('*'), "The floating picker did not apply the grade.");
            Find(floating, "ColorGrading_Close").AsButton().Invoke();
            WaitFor(() => window.ModalWindows.Length == 0, "Color Grading did not close.");
            saved = await SaveGrade(window, path);
            CheckGrade(Assert.Single(saved.VideoOutputEffects));
            Assert.Empty(saved.Tracks[0].Clips[0].VideoEffects);
            WaitFor(() => Neutral(ReadCenter(nativePreview)), "The output grade did not neutralize the preview.");
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static Window OpenSampler(Window owner)
    {
        Find(owner, "WbHslItem").Patterns.SelectionItem.Pattern.Select();
        Find(owner, "ColorGrading_SampleNeutralTone").AsButton().Invoke();
        Window? dialog = null;
        WaitFor(() => (dialog = owner.ModalWindows.SingleOrDefault(child => child.Title == "Sample Neutral Tone")) is not null,
            "The neutral-tone picker did not open.");
        return dialog!;
    }

    private static Color ReadCenter(AutomationElement element)
    {
        using var capture = Capture.Element(element);
        return capture.Bitmap.GetPixel(capture.Bitmap.Width / 2, capture.Bitmap.Height / 2);
    }

    private static bool Warm(Color color) => color.R > 100 && color.R - color.B > 20;

    private static bool Neutral(Color color) =>
        color.R > 100 && Math.Abs(color.R - color.B) <= 4 && Math.Abs(color.R - color.G) <= 4;

    private static void CheckGrade(VideoEffect effect)
    {
        Assert.InRange(effect.GetParameter("TemperatureAdjust"), -1500, -1000);
        Assert.InRange(effect.GetParameter("TintAdjust"), 5, 25);
    }
}
