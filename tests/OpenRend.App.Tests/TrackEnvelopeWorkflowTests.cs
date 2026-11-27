using System.Drawing;
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
            FocusTrimmerWindow(window);
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

            void CheckOwner(System.Drawing.Point point) => WaitFor(
                () => window.Automation.FromPoint(point).Properties.ProcessId.Value == window.Properties.ProcessId.Value,
                "The editor does not own a track fader pointer target.");

            void CheckOwnerAndDrag(System.Drawing.Point from, System.Drawing.Point to)
            {
                CheckOwner(from);
                CheckOwner(to);
                Mouse.Drag(from, to, MouseButton.Left);
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task TrackEnvelopePointDragUndoesAndCancelsWithItsCurveIntact()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var path = Path.Combine(temporary, "track-envelope.openrend");
            var media = new MediaAsset
            {
                Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
                Width = 320, Height = 180, FrameRate = 30, DurationSeconds = 3, HasVideo = true
            };
            var clip = new TimelineClip { MediaId = media.Id, StartFrame = 12, DurationFrames = 70 };
            var track = new TimelineTrack
            {
                Kind = TrackKind.Video, Clips = [clip],
                Envelopes = [new()
                {
                    Type = EnvelopeType.Composite, IsEnabled = true,
                    Points = [new(20, .5, EnvelopeCurveType.Smooth), new(55, .5)]
                }]
            };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1),
                Media = [media], Tracks = [track]
            }, path);

            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            FitTimeline(window);
            DragTrimmer(PointAt(20), PointAt(35));
            await SaveAndVerify(35);
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            await SaveAndVerify(20);
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Y);
            await SaveAndVerify(35);

            var start = PointAt(35);
            var end = PointAt(44);
            Assert.True(SetCursorPos(start.X, start.Y));
            Mouse.Down(MouseButton.Left);
            try
            {
                Wait.UntilInputIsProcessed();
                Assert.True(SetCursorPos(end.X, end.Y));
                Wait.UntilInputIsProcessed();
                Keyboard.Type(VirtualKeyShort.ESCAPE);
                Wait.UntilInputIsProcessed();
            }
            finally { Mouse.Up(MouseButton.Left); }
            Wait.UntilInputIsProcessed();
            Assert.DoesNotContain('*', window.Title);
            Chord(window, VirtualKeyShort.KEY_M);
            await SaveAndVerify(35);
            running.CloseGracefully();

            Point PointAt(long frame)
            {
                var eventBounds = Find(window, $"Clip_{clip.Id:N}").BoundingRectangle;
                var row = Find(window, $"Track_{track.Id:N}").BoundingRectangle;
                var x = eventBounds.Left + (int)Math.Round((frame - clip.StartFrame) * eventBounds.Width / (double)clip.DurationFrames);
                var point = new Point(x, row.Top + row.Height / 2);
                Assert.Equal(window.Properties.ProcessId.Value, window.Automation.FromPoint(point).Properties.ProcessId.Value);
                return point;
            }

            async Task SaveAndVerify(long expectedFirstFrame)
            {
                WaitFor(() => window.Title.Contains('*'), "The envelope edit did not mark the project dirty.");
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "The envelope edit did not save.");
                var document = await ProjectStore.LoadAsync(path);
                var saved = Assert.Single(Assert.Single(document.Tracks).Envelopes!);
                Assert.Equal(new[] { expectedFirstFrame, 55L }, saved.Points.Select(point => point.Frame));
                Assert.Equal(EnvelopeCurveType.Smooth, saved.Points[0].Curve);
                Assert.InRange(saved.Points[0].Value, .48, .52);
                Assert.Equal(12, Assert.Single(document.Tracks[0].Clips).StartFrame);
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
