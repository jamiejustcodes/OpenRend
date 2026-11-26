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
    public async Task TrackFadersAndEnvelopeShortcutsPersistTheirEdits()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var path = Path.Combine(temporary, "envelopes.openrend");
            var picture = new TimelineTrack { Kind = TrackKind.Video, Name = "Picture" };
            var sound = new TimelineTrack { Kind = TrackKind.Audio, Name = "Sound", Pan = -1 };
            await ProjectStore.SaveAsync(new ProjectDocument { Name = "UI integration", Tracks = [picture, sound] }, path);

            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            SetForegroundWindow(window.Properties.NativeWindowHandle.Value);
            window.Focus();
            var gain = Find(window, $"Track_{sound.Id:N}_Gain");
            var gainBounds = gain.BoundingRectangle;
            Assert.True(gainBounds.Width >= 30 && gainBounds.Height >= 14, $"The gain slider is clipped: {gainBounds}.");
            CheckOwnerAndDrag(new(gainBounds.Left + gainBounds.Width / 2, gainBounds.Top + gainBounds.Height / 2),
                new(gainBounds.Right - 1, gainBounds.Top + gainBounds.Height / 2));
            Wait.UntilInputIsProcessed();
            WaitFor(() => Math.Abs(20 * Math.Log10(gain.Patterns.RangeValue.Pattern.Value.Value) - 6) < 1e-6,
                "The audio fader did not reach +6 dB.");

            var pan = Find(window, $"Track_{sound.Id:N}_Pan");
            var panBounds = pan.BoundingRectangle;
            CheckOwnerAndDrag(new(panBounds.Left + 1, panBounds.Top + panBounds.Height / 2),
                new(panBounds.Left + panBounds.Width / 2 + 1, panBounds.Top + panBounds.Height / 2));
            WaitFor(() => pan.Patterns.RangeValue.Pattern.Value.Value == 0, "The pan fader did not settle at center.");
            Chord(window, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_P);
            Chord(window, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_V);

            var pictureBounds = Find(window, $"Track_{picture.Id:N}").BoundingRectangle;
            var picturePoint = new System.Drawing.Point(pictureBounds.Left + 80, pictureBounds.Top + 12);
            CheckOwner(picturePoint);
            Mouse.Click(picturePoint);
            Chord(window, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_V);
            SaveButton(window).Invoke();
            WaitFor(() => !window.Title.Contains('*'), "Track changes did not save.");
            var saved = await ProjectStore.LoadAsync(path);
            Assert.Equal(6, 20 * Math.Log10(saved.Tracks[1].Gain), 8);
            Assert.Equal(0, saved.Tracks[1].Pan);
            Assert.NotNull(saved.Tracks[0].Envelopes);
            Assert.NotNull(saved.Tracks[1].Envelopes);
            Assert.Contains(saved.Tracks[0].Envelopes!, envelope => envelope.Type == EnvelopeType.Composite);
            Assert.Contains(saved.Tracks[1].Envelopes!, envelope => envelope.Type == EnvelopeType.Volume);
            Assert.Contains(saved.Tracks[1].Envelopes!, envelope => envelope.Type == EnvelopeType.Pan);
            running.CloseGracefully(waitForRender: false);

            void CheckOwner(System.Drawing.Point point) => Assert.Equal(window.Properties.ProcessId.Value,
                window.Automation.FromPoint(point).Properties.ProcessId.Value);

            void CheckOwnerAndDrag(System.Drawing.Point from, System.Drawing.Point to)
            {
                CheckOwner(from);
                CheckOwner(to);
                Mouse.Drag(from, to, MouseButton.Left);
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
