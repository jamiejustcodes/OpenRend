using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
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
    public async Task TransitionPluginRemovalPreservesOverlapAndCanBeUndone()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "transition-removal.openrend");
        var asset = new MediaAsset { Path = Path.Combine(root, "tests", "native", "fixture.mp4"), Width = 320, Height = 180,
            DurationSeconds = 3, FrameRate = 30, HasVideo = true };
        var outgoing = new TimelineClip { MediaId = asset.Id, Name = "Outgoing", DurationFrames = 60, FadeOutFrames = 15 };
        var incoming = new TimelineClip { MediaId = asset.Id, Name = "Incoming", StartFrame = 45, DurationFrames = 45,
            FadeInFrames = 15, FadeInTransition = new() };
        await ProjectStore.SaveAsync(new() { Name = "UI integration", Width = 320, Height = 180, Media = [asset],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [outgoing, incoming] }] }, path);
        try
        {
            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
            Find(window, $"Clip_{incoming.Id:N}_More").AsButton().Invoke();
            AutomationElement? menu = null;
            WaitFor(() => (menu = window.Automation.GetDesktop().FindFirstDescendant(c =>
                c.ByControlType(ControlType.Menu).And(c.ByProcessId(window.Properties.ProcessId.Value)))) is not null,
                "The incoming event menu did not open.");
            var properties = menu!.FindFirstDescendant(c => c.ByAutomationId("TransitionProperties"));
            Assert.NotNull(properties); properties.AsMenuItem().Invoke();
            Window? dialog = null;
            WaitFor(() => (dialog = window.ModalWindows.SingleOrDefault(w => w.Title == "Video event FX")) is not null,
                "Transition Properties did not open.");
            var artifacts = Path.Combine(root, ".local", "desktop-validation"); Directory.CreateDirectory(artifacts);
            using (var screenshot = Capture.Element(dialog!)) screenshot.ToFile(Path.Combine(artifacts, "transition-properties.png"));
            Find(dialog!, "RemoveTransitionPlugin").AsButton().Invoke();
            WaitFor(() => !Find(dialog!, "RemoveTransitionPlugin").IsEnabled, "The transition plug-in was not removed.");
            Find(dialog!, "UndoTransitionEdit").AsButton().Invoke();
            WaitFor(() => Find(dialog!, "RemoveTransitionPlugin").IsEnabled, "Undo did not restore the plug-in.");
            Find(dialog!, "RedoTransitionEdit").AsButton().Invoke();
            WaitFor(() => !Find(dialog!, "RemoveTransitionPlugin").IsEnabled, "Redo did not remove the plug-in.");
            Find(dialog!, "CloseTransitionProperties").AsButton().Invoke();
            SaveButton(window).Invoke();
            WaitFor(() => !window.Title.Contains('*'), "The removal was not saved.");
            var loaded = await ProjectStore.LoadAsync(path);
            var clips = loaded.Tracks[0].Clips;
            Assert.Null(clips[1].FadeInTransition);
            Assert.Equal((0L, 60L, 15L), (clips[0].StartFrame, clips[0].DurationFrames, clips[0].FadeOutFrames));
            Assert.Equal((45L, 45L, 0L, 15L), (clips[1].StartFrame, clips[1].DurationFrames, clips[1].SourceInFrame, clips[1].FadeInFrames));
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
