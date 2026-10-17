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
    public Task OverlapToCutShortcutUsesBeforeAlignment() => VerifyOverlapToCut(CutTransitionAlignment.BeforeCut, 15, 60);

    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public Task OverlapToCutShortcutUsesCenteredAlignment() => VerifyOverlapToCut(CutTransitionAlignment.CenteredOnCut, 16, 52);

    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public Task OverlapToCutShortcutUsesAfterAlignment() => VerifyOverlapToCut(CutTransitionAlignment.AfterCut, 15, 45);

    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public Task OverlapToCutReportsHalfFrameWithoutEditing() => VerifyOverlapToCut(CutTransitionAlignment.CenteredOnCut, 15, null);

    private async Task VerifyOverlapToCut(CutTransitionAlignment alignment, long overlap, long? cut)
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "overlap-cut.openrend");
        var asset = new MediaAsset { Path = Path.Combine(root, "tests", "native", "fixture.mp4"), Width = 320, Height = 180,
            DurationSeconds = 3, FrameRate = 30, HasVideo = true };
        var outgoing = new TimelineClip { MediaId = asset.Id, Name = "Outgoing", DurationFrames = 60, FadeOutFrames = overlap };
        var incoming = new TimelineClip { MediaId = asset.Id, Name = "Incoming", StartFrame = 60 - overlap, DurationFrames = 30 + overlap,
            SourceInFrame = 5, FadeInFrames = overlap, FadeInTransition = new() };
        var unrelated = new TimelineClip { MediaId = asset.Id, StartFrame = 120, DurationFrames = 30 };
        await ProjectStore.SaveAsync(new() { Name = "UI integration", Width = 320, Height = 180, FrameRate = new(30000, 1001), Media = [asset],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [outgoing, incoming, unrelated] }] }, path);
        var originalBytes = await File.ReadAllBytesAsync(path);
        EditingPreferencesStore.Save(new() { CutOverlapAlignment = alignment, CutOverlapSeconds = .1 }, Path.Combine(temporary, "editing.json"));
        try
        {
            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
            Find(window, $"Clip_{unrelated.Id:N}").Patterns.SelectionItem.Pattern.Select();
            var incomingBounds = Find(window, $"Clip_{incoming.Id:N}").BoundingRectangle;
            var scale = incomingBounds.Height / 67;
            var x = (int)(incomingBounds.Left + incomingBounds.Width * overlap / (2 * incoming.DurationFrames));
            Mouse.Click(new System.Drawing.Point(x, (int)(incomingBounds.Top - 10 * scale)));
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.DIVIDE);
            if (cut is null)
            {
                Window? error = null;
                WaitFor(() => (error = window.ModalWindows.SingleOrDefault()) is not null, "The half-frame limitation was not reported.");
                Assert.NotNull(error!.FindFirstDescendant(c => c.ByControlType(ControlType.Text).And(c.ByName(
                    "Centering this overlap would create a half-frame cut. OpenRend stores edits in whole project frames."))));
                Chord(error!, VirtualKeyShort.RETURN);
                WaitFor(() => window.ModalWindows.Length == 0, "The error did not close.");
                Assert.False(window.Title.Contains('*'));
                Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
            }
            else
            {
                WaitFor(() => window.Title.Contains('*'), "The overlap-to-cut shortcut did not edit the project.");
                Find(window, "UndoButton").AsButton().Invoke();
                WaitFor(() => !Find(window, "UndoButton").IsEnabled, "Conversion created more than one undo step.");
                Assert.NotNull(Find(window, $"Clip_{incoming.Id:N}_Transition"));
                Find(window, "RedoButton").AsButton().Invoke();
                WaitFor(() => Find(window, "UndoButton").IsEnabled, "Redo did not restore the cut.");
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "The cut was not saved.");
                var loaded = await ProjectStore.LoadAsync(path);
                var clips = loaded.Tracks[0].Clips;
                Assert.Equal(cut.Value, clips[0].EndFrame); Assert.Equal(cut.Value, clips[1].StartFrame);
                Assert.Equal(90, clips[1].EndFrame);
                Assert.Equal(5 + cut.Value - incoming.StartFrame, clips[1].SourceInFrame);
                Assert.Equal(0, clips[0].FadeOutFrames); Assert.Equal(0, clips[1].FadeInFrames);
                Assert.Null(clips[1].FadeInTransition); Assert.Equal(120, clips[2].StartFrame);
                Assert.True(Find(window, $"Clip_{unrelated.Id:N}").Patterns.SelectionItem.Pattern.IsSelected.Value);
            }
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
