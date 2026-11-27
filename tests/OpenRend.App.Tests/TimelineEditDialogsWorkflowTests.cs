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
    public async Task TimelineEditDialogsValidateAndKeepPositionsSeparateFromDurations()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "timeline-dialogs.openrend");
        var asset = new MediaAsset
        {
            Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
            Width = 320, Height = 180, DurationSeconds = 3, FrameRate = 30, HasVideo = true
        };
        var clip = new TimelineClip { MediaId = asset.Id, Name = "Source", DurationFrames = 60 };
        await ProjectStore.SaveAsync(new()
        {
            Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30, 1),
            RulerFormat = RulerFormat.AbsoluteFrames, RulerStartTimeFrames = 100,
            Media = [asset], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
        }, path);

        try
        {
            using var running = Launch(executable, path, temporary);
            var window = running.Window;

            var insert = window.FindFirstDescendant(c => c.ByName("Insert"));
            Assert.NotNull(insert);
            insert.AsMenuItem().Expand();
            Find(window, "InsertTimeMenu").AsMenuItem().Invoke();
            var insertTime = OpenDialog(window, "Insert Time");
            var duration = Find(insertTime, "InsertTimeDurationField").AsTextBox();
            Assert.Contains("Duration", duration.Name);
            Assert.Equal("30", duration.Text);
            duration.Text = "0";
            Chord(insertTime, VirtualKeyShort.RETURN);
            WaitFor(() => Find(insertTime, "DialogValidationError").Name.Contains("greater than zero"),
                "Insert Time did not report an invalid duration.");
            duration.Text = "12";
            Chord(insertTime, VirtualKeyShort.RETURN);
            WaitFor(() => window.ModalWindows.Length == 0, "Insert Time did not accept a valid duration.");

            Mouse.DoubleClick(Find(window, "Timecode").GetClickablePoint());
            var cursorTime = OpenDialog(window, "Set Time at Cursor");
            var position = Find(cursorTime, "CursorTimeField").AsTextBox();
            Assert.Equal("100", position.Text);
            position.Text = "bad position";
            Chord(cursorTime, VirtualKeyShort.RETURN);
            WaitFor(() => Find(cursorTime, "DialogValidationError").Name.Contains("valid cursor position"),
                "Set Time at Cursor did not report invalid input.");
            position.Text = "172";
            Chord(cursorTime, VirtualKeyShort.RETURN);
            WaitFor(() => window.ModalWindows.Length == 0 && Find(window, "Timecode").Name == "172",
                "The cursor did not move using the ruler's start offset.");

            Find(window, $"Clip_{clip.Id:N}").Patterns.SelectionItem.Pattern.Select();
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_C);
            Chord(window, VirtualKeyShort.ALT, VirtualKeyShort.KEY_E);
            WaitFor(() => window.FindFirstDescendant(c => c.ByAutomationId("PasteRepeatMenu"))?.Name == "Paste Repeat…",
                "The Edit menu did not show the Paste Repeat label.");
            Keyboard.Type(VirtualKeyShort.KEY_A);
            var paste = OpenDialog(window, "Paste Repeat");
            var copies = Find(paste, "PasteRepeatCountBox").AsTextBox();
            Assert.True(copies.Properties.HasKeyboardFocus.Value);
            var spacing = Find(paste, "PasteRepeatSpacingBox").AsTextBox();
            Assert.False(spacing.IsEnabled);
            Mouse.Click(Find(paste, "PasteRepeatEvenSpacingRadio").GetClickablePoint());
            WaitFor(() => spacing.IsEnabled, "Even spacing did not enable its input.");
            spacing.Text = "invalid";
            Chord(paste, VirtualKeyShort.RETURN);
            WaitFor(() => Find(paste, "DialogValidationError").Name.Contains("spacing greater than zero"),
                "Paste Repeat accepted an invalid spacing value.");
            Chord(paste, VirtualKeyShort.ESCAPE);
            WaitFor(() => window.ModalWindows.Length == 0, "Escape did not cancel Paste Repeat.");
            SaveButton(window).Invoke();
            WaitFor(() => !window.Title.Contains('*'), "Insert Time did not save.");
            Assert.Equal(12, Assert.Single((await ProjectStore.LoadAsync(path)).Tracks[0].Clips).StartFrame);

            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_B);
            paste = OpenDialog(window, "Paste Repeat");
            copies = Find(paste, "PasteRepeatCountBox").AsTextBox();
            copies.Text = "2";
            Chord(paste, VirtualKeyShort.UP);
            WaitFor(() => copies.Text == "3", "The Up key did not increment the count.");
            Mouse.Click(Find(paste, "PasteRepeatEvenSpacingRadio").GetClickablePoint());
            spacing = Find(paste, "PasteRepeatSpacingBox").AsTextBox();
            spacing.Text = "16";
            var increase = paste.FindFirstDescendant(c => c.ByName("Increase spacing"));
            Assert.NotNull(increase);
            Mouse.Click(increase.GetClickablePoint());
            WaitFor(() => spacing.Text == "17", "The spacing step button did not use the typed value.");
            spacing.Focus();
            Chord(paste, VirtualKeyShort.RETURN);
            WaitFor(() => window.ModalWindows.Length == 0, "Paste Repeat did not accept valid inputs.");
            SaveButton(window).Invoke();
            WaitFor(() => !window.Title.Contains('*'), "Paste Repeat did not save.");
            var saved = await ProjectStore.LoadAsync(path);
            Assert.Equal(new long[] { 12, 72, 89, 106 }, saved.Tracks[0].Clips.Select(item => item.StartFrame).Order());

            Chord(window, VirtualKeyShort.ALT, VirtualKeyShort.KEY_O);
            WaitFor(() => window.FindFirstDescendant(c => c.ByAutomationId("RulerFormatMenu"))?.Name == "Ruler Format",
                "The Options menu did not show the Ruler Format label.");
            Keyboard.Type(VirtualKeyShort.KEY_F);
            WaitFor(() => Find(window, "RulerFormatMenu").Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value == ExpandCollapseState.Expanded,
                "F did not open Ruler Format from the Options menu.");
            Assert.Equal("Time & Frames", Find(window, "RulerFormatTimeAndFrames").Name);
            Keyboard.Type(VirtualKeyShort.KEY_M);
            WaitFor(() => window.Title.Contains('*') && Find(window, "Timecode").Name.Contains('.'),
                "M did not select Time & Frames and mark the project dirty.");
            SaveButton(window).Invoke();
            WaitFor(() => !window.Title.Contains('*'), "The ruler format did not save.");
            saved = await ProjectStore.LoadAsync(path);
            Assert.Equal(RulerFormat.TimeAndFrames, saved.RulerFormat);
            Assert.Empty(saved.Markers);
            running.CloseGracefully();
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }

        static Window OpenDialog(Window owner, string title)
        {
            Window? dialog = null;
            WaitFor(() => (dialog = owner.ModalWindows.SingleOrDefault(item => item.Title == title)) is not null,
                $"{title} did not open.");
            return dialog!;
        }
    }
}
