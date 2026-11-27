using System.Drawing;
using System.Globalization;
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
    public async Task TransitionSliderPreviewsWhileHeldAndCommitsOneEdit()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "transition-slider.openrend");
        try
        {
            var red = new MediaAsset { Path = "openrend://color?hex=#C80000", HasVideo = true };
            var green = new MediaAsset { Path = "openrend://color?hex=#00C800", HasVideo = true };
            var outgoing = new TimelineClip { MediaId = red.Id, Name = "Outgoing", DurationFrames = 36, FadeOutFrames = 24 };
            var incoming = new TimelineClip { MediaId = green.Id, Name = "Incoming", StartFrame = 12, DurationFrames = 36, FadeInFrames = 24,
                FadeInTransition = new VideoTransition { Kind = VideoTransitionKind.LinearWipe, Preset = "Left-Right, Hard Edge",
                    Parameters = new() { ["Angle"] = "0.0", ["Feather"] = "0.00" } } };
            await ProjectStore.SaveAsync(new ProjectDocument { Name = "UI integration", Width = 320, Height = 180,
                FrameRate = new(30, 1), Media = [red, green], Tracks = [new() { Kind = TrackKind.Video, Clips = [outgoing, incoming] }] }, path);

            using var running = Launch(executable, path, directory);
            var main = running.Window;
            Chord(main, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
            var range = Find(main, $"Clip_{outgoing.Id:N}").BoundingRectangle;
            Mouse.Click(new Point(range.Left + (int)Math.Round(range.Width * 2 / 3d), range.Top - 10));
            WaitFor(() => PresentedFrame(main) == 24, "The timeline did not seek to the middle of the wipe.");
            var preview = Assert.Single(main.FindAllDescendants(c => c.ByClassName("OpenRend.Preview")));
            WaitFor(() => IsWipeSample(green: false), "The starting horizontal wipe pixel was not red.");
            Find(main, $"Clip_{incoming.Id:N}_Transition").AsButton().Invoke();
            Window? propertyWindow = null;
            WaitFor(() => (propertyWindow = main.ModalWindows.SingleOrDefault(dialog => dialog.Title == "Video event FX")) is not null,
                "Transition properties did not open.");
            var properties = propertyWindow!;
            var area = main.BoundingRectangle;
            properties.Patterns.Transform.Pattern.Move(area.Left + 12, area.Bottom - properties.BoundingRectangle.Height - 32);
            var undo = Find(properties, "UndoTransitionEdit").AsButton();
            Assert.False(undo.IsEnabled);

            DragAngle(cancel: true);
            Assert.False(undo.IsEnabled);
            DragAngle(cancel: false);
            WaitFor(() => undo.IsEnabled && main.Title.Contains('*'), "Releasing the slider did not commit the wipe angle.");
            undo.Invoke();
            WaitFor(() => !undo.IsEnabled && IsWipeSample(green: false), "One Undo did not restore the original angle and preview.");
            Find(properties, "RedoTransitionEdit").AsButton().Invoke();
            WaitFor(() => IsWipeSample(green: true), "Redo did not restore the adjusted angle.");
            var angleControl = Find(properties, "TransitionParameter_Angle");
            var rangeValue = angleControl.Patterns.RangeValue.Pattern.Value;
            var beforeKeys = rangeValue.Value;
            angleControl.Focus();
            Keyboard.Type(VirtualKeyShort.RIGHT);
            Keyboard.Type(VirtualKeyShort.RIGHT);
            WaitFor(() => rangeValue.Value > beforeKeys + .15, "The angle slider lost focus before the second key step.");
            Find(properties, "CloseTransitionProperties").AsButton().Invoke();
            WaitFor(() => main.ModalWindows.Length == 0, "Transition properties did not close.");
            var saved = await SaveGrade(main, path);
            var angle = saved.Tracks[0].Clips[1].FadeInTransition!.Parameters!["Angle"];
            Assert.InRange(double.Parse(angle, CultureInfo.InvariantCulture), 78, 102);
            running.CloseGracefully();

            bool IsWipeSample(bool green)
            {
                using var image = Capture.Element(preview);
                var width = Math.Min(image.Bitmap.Width, image.Bitmap.Height * 16 / 9);
                var color = image.Bitmap.GetPixel(image.Bitmap.Width / 2 + width / 4, image.Bitmap.Height / 4);
                return green ? color.G > 190 && color.R < 10 : color.R > 190 && color.G < 10;
            }

            void DragAngle(bool cancel)
            {
                var slider = Find(properties, "TransitionParameter_Angle");
                var value = slider.Patterns.RangeValue.Pattern.Value;
                Assert.Equal(0, value.Value);
                var bounds = slider.BoundingRectangle;
                var y = bounds.Top + bounds.Height / 2;
                var start = bounds.Left + 7;
                var width = bounds.Width - 14;
                Mouse.MoveTo(start, y);
                Mouse.Down(MouseButton.Left);
                try
                {
                    Mouse.MoveTo(start + width / 8, y);
                    Wait.UntilInputIsProcessed();
                    Mouse.MoveTo(start + width / 4, y);
                    WaitFor(() => value.Value > 75 && IsWipeSample(green: true), "The wipe preview did not follow the held slider.");
                    Assert.False(undo.IsEnabled);
                    Assert.DoesNotContain('*', main.Title);
                    if (cancel)
                    {
                        Keyboard.Type(VirtualKeyShort.ESCAPE);
                        WaitFor(() => value.Value == 0 && IsWipeSample(green: false), "Escape did not restore the slider and picture.");
                    }
                }
                catch
                {
                    var captures = Path.Combine(root, ".local", "desktop-validation");
                    Directory.CreateDirectory(captures);
                    using var image = Capture.Element(main);
                    image.ToFile(Path.Combine(captures, "transition-slider-failure.png"));
                    throw;
                }
                finally { Mouse.Up(MouseButton.Left); }
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
