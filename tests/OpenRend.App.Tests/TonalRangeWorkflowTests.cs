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
    public async Task TonalRangesPreviewThresholdDragsAndPersistOnlyCommittedChanges()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "tonal-ranges.openrend");
        try
        {
            var asset = Solid("Threshold plate", "#545454");
            var clip = new TimelineClip { MediaId = asset.Id, Name = asset.Name, DurationFrames = 120 };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 640, Height = 360, Media = [asset],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
            }, path);

            using var running = Launch(executable, path, temporary);
            var main = running.Window;
            var preview = Assert.Single(main.FindAllDescendants(condition => condition.ByClassName("OpenRend.Preview")));
            Find(main, $"Clip_{clip.Id:N}").Patterns.SelectionItem.Pattern.Select();
            Chord(main, VirtualKeyShort.RIGHT);
            WaitFor(() => Picture() is >= 82 and <= 86, "The threshold plate did not reach Video Preview.");
            var original = Picture();

            SelectBrowserTab(main, "Color Grading");
            Find(main, "TonalRangesItem").Patterns.SelectionItem.Pattern.Select();
            EditText(main, "RangeShadows_YBox", "0.18");
            WaitFor(() => Picture() > original + 6, "The Shadows wheel did not grade the picture.");
            var shadow = Picture();
            await SaveGrade(main, path);

            Chord(main, VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.KEY_2);
            var scopes = FindScopes(main);
            var area = main.BoundingRectangle;
            var scopeArea = scopes.BoundingRectangle;
            scopes.Patterns.Transform.Pattern.Move(area.Right - scopeArea.Width - 55, area.Bottom - scopeArea.Height - 60);
            var graph = Find(scopes, "VideoScopeGraph");
            var lowerField = Find(main, "RangeLower_Value").AsTextBox();
            EditText(main, "RangeLower_Value", "80");
            Assert.DoesNotContain('*', main.Title);
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            Assert.Equal("30", lowerField.Text);

            Find(main, "RangeThresholds_ShowOnScopes").Patterns.Toggle.Pattern.Toggle();
            WaitFor(() => GuideRow() >= 0, "The waveform did not show the lower threshold guide.");
            var originalGuide = GuideRow();
            Assert.DoesNotContain('*', main.Title);

            DragThreshold(cancel: true);
            DragThreshold(cancel: false);
            WaitFor(() => main.Title.Contains('*'), "Releasing the threshold slider did not commit.");
            var saved = await SaveGrade(main, path);
            var grade = Assert.Single(saved.Tracks[0].Clips[0].VideoEffects);
            Assert.InRange(grade.GetParameter(TonalRangeGrading.LowerKey), .53, .62);
            Assert.Equal(.7, grade.GetParameter(TonalRangeGrading.UpperKey));
            Assert.Equal(.18, grade.GetParameter(TonalRangeGrading.Shadows + "_Y"));

            Chord(main, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            WaitFor(() => Picture() == shadow && GuideRow() == originalGuide, "One Undo did not restore the picture and threshold guide.");
            EditText(main, "RangeLower_Value", "55");
            WaitFor(() => Picture() > shadow + 20, "Entering a valid lower threshold did not update the picture.");
            var captureDir = Path.Combine(root, ".local", "desktop-validation");
            Directory.CreateDirectory(captureDir);
            using (var image = Capture.Element(main)) image.ToFile(Path.Combine(captureDir, "grading-tonal-ranges.png"));
            SelectBrowserTab(main, "Project Media");
            WaitFor(() => GuideRow() < 0, "Leaving the docked grade left threshold guides behind.");

            OpenGradingScope(main, "VideoOutputColorGradingMenu");
            var floating = GradingDialog(main);
            Find(floating, "TonalRangesItem").Patterns.SelectionItem.Pattern.Select();
            EditText(floating, "RangeUpper_Value", "63");
            Find(floating, "ColorGrading_Close").AsButton().Invoke();
            WaitFor(() => main.ModalWindows.Length == 0, "Color Grading did not close.");
            saved = await SaveGrade(main, path);
            Assert.Equal(.55, Assert.Single(saved.Tracks[0].Clips[0].VideoEffects).GetParameter(TonalRangeGrading.LowerKey));
            Assert.Equal(.63, Assert.Single(saved.VideoOutputEffects).GetParameter(TonalRangeGrading.UpperKey));
            scopes.Close();
            running.CloseGracefully();

            void DragThreshold(bool cancel)
            {
                var bounds = Find(main, "RangeLower").BoundingRectangle;
                var start = bounds.Left + 7 + (int)Math.Round((bounds.Width - 14) * .3);
                var end = bounds.Left + 7 + (int)Math.Round((bounds.Width - 14) * .57);
                var y = bounds.Top + bounds.Height / 2;
                Mouse.MoveTo(new Point(start, y));
                Mouse.Down(MouseButton.Left);
                try
                {
                    Mouse.MoveTo(new Point(end, y));
                    WaitFor(() => GuideRow() < originalGuide - 14 && Picture() > shadow + 20,
                        "Dragging the lower threshold did not update the guide and picture while held.");
                    Assert.DoesNotContain('*', main.Title);
                    if (cancel)
                    {
                        Keyboard.Type(VirtualKeyShort.ESCAPE);
                        WaitFor(() => GuideRow() == originalGuide && Picture() == shadow, "Escape did not restore the grade and guide.");
                        Assert.DoesNotContain('*', main.Title);
                    }
                }
                finally { Mouse.Up(MouseButton.Left); }
            }

            int Picture()
            {
                using var image = Capture.Element(preview);
                return image.Bitmap.GetPixel(image.Bitmap.Width / 2, image.Bitmap.Height / 2).R;
            }

            int GuideRow()
            {
                using var image = Capture.Element(graph);
                for (var y = 0; y < image.Bitmap.Height; y++)
                for (var x = 1; x < 13; x++)
                    if (image.Bitmap.GetPixel(x, y).ToArgb() == Color.FromArgb(0x7B, 0xC7, 0xE9).ToArgb()) return y;
                return -1;
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static void EditText(Window window, string id, string value)
    {
        var text = Find(window, id).AsTextBox();
        text.Focus();
        text.Text = value;
        Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
    }
}
