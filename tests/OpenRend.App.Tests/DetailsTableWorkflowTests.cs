using System.Drawing;
using FlaUI.Core.AutomationElements;
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
    public async Task DetailsColumnsResizeAndReorderWhileRowsKeepTheirActions()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "details-table.openrend");
        var asset = new MediaAsset
        {
            Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
            Width = 320, Height = 180, DurationSeconds = 3, FrameRate = 30, HasVideo = true
        };
        var clip = new TimelineClip { MediaId = asset.Id, Name = "Interview cut", StartFrame = 75, DurationFrames = 45 };
        await ProjectStore.SaveAsync(new()
        {
            Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1),
            Media = [asset], Tracks = [new() { Name = "Video", Kind = TrackKind.Video, Clips = [clip] }]
        }, path);

        try
        {
            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.KEY_4);
            Window? details = null;
            WaitFor(() => (details = FloatingPane(window, "Edit Details")) is not null, "Edit Details did not open.");
            Assert.NotNull(details);
            var events = Find(details, "DetailEvents");
            var track = Header(events, "Track");
            var eventColumn = Header(events, "Event");
            var original = track.BoundingRectangle;
            var resize = new Point(original.Right - 3, original.Top + original.Height / 2);
            Mouse.Drag(resize, new Point(resize.X + 48, resize.Y), MouseButton.Left);
            WaitFor(() => track.BoundingRectangle.Width >= original.Width + 30, "Dragging the column edge did not resize it.");

            var destination = new Point(track.BoundingRectangle.Left + 8, resize.Y);
            Mouse.Drag(eventColumn.GetClickablePoint(), destination, MouseButton.Left);
            WaitFor(() => Header(events, "Event").BoundingRectangle.Left < Header(events, "Track").BoundingRectangle.Left,
                "Dragging the column header did not reorder it.");

            var name = events.FindFirstDescendant(c => c.ByName("Interview cut"));
            Assert.NotNull(name);
            Mouse.DoubleClick(name.GetClickablePoint());
            WaitFor(() => Find(window, "Timecode").Name == "00:00:02.15", "Double-clicking an event row did not seek it.");
            details.Close();
            WaitFor(() => FloatingPane(window, "Edit Details") is null, "Edit Details stayed open.");

            Mouse.Click(Find(window, "MediaViewDetails").GetClickablePoint());
            var media = Find(window, "MediaDetailsList");
            Assert.True(Header(media, "Name").BoundingRectangle.Height > 0);
            Assert.NotNull(media.FindFirstDescendant(c => c.ByName("fixture.mp4")));
            running.CloseGracefully();
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }

        static AutomationElement Header(AutomationElement table, string name)
        {
            var header = table.FindFirstDescendant(c => c.ByControlType(ControlType.HeaderItem).And(c.ByName(name)));
            Assert.NotNull(header);
            return header;
        }
    }
}
