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
    public async Task VideoFxPresetDropsRespectEventTrackAndOutputScopes()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var folder = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "video-fx-drop.openrend");
        try
        {
            var asset = new MediaAsset { Path = "openrend://color?hex=235860", HasVideo = true };
            var first = new TimelineClip { MediaId = asset.Id, Name = "First", DurationFrames = 24 };
            var second = new TimelineClip { MediaId = asset.Id, Name = "Second", StartFrame = 24, DurationFrames = 24 };
            var locked = new TimelineClip { MediaId = asset.Id, Name = "Locked", StartFrame = 48, DurationFrames = 24, IsLocked = true };
            var track = new TimelineTrack { Kind = TrackKind.Video, Clips = [first, second, locked],
                VideoEffects = [VideoEffect.Create(VideoEffectKind.Vignette)] };
            await ProjectStore.SaveAsync(new ProjectDocument { Name = "UI integration", Width = 320, Height = 180,
                Tracks = [track], Media = [asset] }, path);
            using var running = Launch(executable, path, folder);
            var window = running.Window;
            FitTimeline(window);
            Find(window, $"Clip_{first.Id:N}").Patterns.SelectionItem.Pattern.Select();
            OpenVideoFxBrowser(window);
            Find(window, "VideoFxSearch").AsTextBox().Text = "Sepia";
            var presets = Find(window, "VideoFxPresets");
            var subtle = FindPreview("Subtle Warmth");
            var strong = FindPreview("Strong Sepia");

            using (Keyboard.Pressing(VirtualKeyShort.SHIFT))
                Mouse.Drag(ThumbnailCenter(subtle), Find(window, $"Clip_{locked.Id:N}").GetClickablePoint(), MouseButton.Left);
            Wait.UntilInputIsProcessed();
            Assert.DoesNotContain('*', window.Title);

            using (Keyboard.Pressing(VirtualKeyShort.SHIFT))
                Mouse.Drag(ThumbnailCenter(subtle), Find(window, $"Clip_{second.Id:N}").GetClickablePoint(), MouseButton.Left);
            var afterEvent = await SaveGrade(window, path);
            Assert.Empty(afterEvent.Tracks[0].Clips[0].VideoEffects);
            CheckSepia(Assert.Single(afterEvent.Tracks[0].Clips[1].VideoEffects), .35);
            Assert.Empty(afterEvent.Tracks[0].Clips[2].VideoEffects);
            Assert.Equal(VideoEffectKind.Vignette, Assert.Single(afterEvent.Tracks[0].VideoEffects).Kind);
            Assert.Empty(afterEvent.VideoOutputEffects);
            Assert.Empty(window.ModalWindows);

            var header = Find(window, $"Track_{track.Id:N}").BoundingRectangle;
            Mouse.Drag(ThumbnailCenter(strong), new Point(header.Left + 35, header.Top + header.Height / 2), MouseButton.Left);
            Window? dialog = null;
            WaitFor(() => (dialog = window.ModalWindows.SingleOrDefault(item => item.Title == "Video track FX")) is not null,
                "Dropping onto a track header did not open the track effect.");
            Assert.Equal("2. Sepia", Find(dialog!, "VideoEffectChain").AsListBox().SelectedItem?.Name);
            Find(dialog!, "CloseVideoEffects").AsButton().Invoke();
            WaitFor(() => window.ModalWindows.Length == 0, "The effect window did not close.");
            var afterTrack = await SaveGrade(window, path);
            Assert.Equal(2, afterTrack.Tracks[0].VideoEffects.Count);
            CheckSepia(afterTrack.Tracks[0].VideoEffects[1], .85);
            CheckSepia(Assert.Single(afterTrack.Tracks[0].Clips[1].VideoEffects), .35);
            Assert.Empty(afterTrack.VideoOutputEffects);

            var preview = Assert.Single(window.FindAllDescendants(c => c.ByClassName("OpenRend.Preview")));
            WaitFor(() => PreviewRed(preview) > 70, "The first two effects did not reach the native preview.");
            var beforeOutput = PreviewRed(preview);
            using (Keyboard.Pressing(VirtualKeyShort.SHIFT))
                Mouse.Drag(ThumbnailCenter(subtle), ThumbnailCenter(preview), MouseButton.Left);
            var afterOutput = await SaveGrade(window, path);
            CheckSepia(Assert.Single(afterOutput.VideoOutputEffects), .35);
            WaitFor(() => PreviewRed(preview) >= beforeOutput + 4, "The output effect did not change the native preview.");
            Assert.Empty(window.ModalWindows);
            Assert.NotEqual(afterOutput.VideoOutputEffects[0].Id, afterOutput.Tracks[0].Clips[1].VideoEffects[0].Id);

            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            var afterUndo = await SaveGrade(window, path);
            Assert.Empty(afterUndo.VideoOutputEffects);
            CheckSepia(afterUndo.Tracks[0].VideoEffects[1], .85);
            CheckSepia(Assert.Single(afterUndo.Tracks[0].Clips[1].VideoEffects), .35);
            WaitFor(() => Math.Abs(PreviewRed(preview) - beforeOutput) <= 2, "Undo did not restore the preview.");
            running.CloseGracefully();

            AutomationElement FindPreview(string name)
            {
                AutomationElement? image = null;
                WaitFor(() => (image = VisibleNamedItem(presets, name)?.FindFirstDescendant(c => c.ByAutomationId("VideoFxPresetThumbnail"))) is not null,
                    $"Could not find the {name} preview.");
                return image!;
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static byte PreviewRed(AutomationElement preview)
    {
        using var capture = Capture.Element(preview);
        return capture.Bitmap.GetPixel(capture.Bitmap.Width / 2, capture.Bitmap.Height / 2).R;
    }

    private static void CheckSepia(VideoEffect effect, double amount)
    {
        Assert.Equal(VideoEffectKind.Sepia, effect.Kind);
        Assert.Equal(amount, effect.GetParameter("Amount"), 6);
    }
}
