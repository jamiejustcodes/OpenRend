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
    public Task AutomaticTransitionAdjustsTakeEdgesWithoutRipple() => VerifyAutomaticTransition(false);

    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public Task AutomaticTransitionRipplesLaterEventsOnTheSameTrack() => VerifyAutomaticTransition(true);

    private async Task VerifyAutomaticTransition(bool ripple)
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "automatic.openrend");
        var asset = new MediaAsset { Path = Path.Combine(root, "tests", "native", "fixture.mp4"), Width = 320, Height = 180,
            DurationSeconds = 3, FrameRate = 30, HasVideo = true };
        var outgoing = new TimelineClip { MediaId = asset.Id, Name = "Outgoing", DurationFrames = 60 };
        var incoming = new TimelineClip { MediaId = asset.Id, Name = "Incoming", StartFrame = 60, SourceInFrame = 15, DurationFrames = 45 };
        var later = new TimelineClip { MediaId = asset.Id, Name = "Later", StartFrame = 120, DurationFrames = 30 };
        var other = new TimelineClip { MediaId = asset.Id, Name = "Other track", StartFrame = 120, DurationFrames = 30 };
        await ProjectStore.SaveAsync(new() { Name = "UI integration", Width = 320, Height = 180,
            FrameRate = new(30000, 1001), Media = [asset], Tracks = [new() { Kind = TrackKind.Video,
                Clips = [outgoing, incoming, later] }, new() { Kind = TrackKind.Video, Clips = [other] }] }, path);
        EditingPreferencesStore.Save(new() { CutOverlapSeconds = .5 }, Path.Combine(temporary, "editing.json"));
        try
        {
            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
            Find(window, $"Clip_{incoming.Id:N}").Patterns.SelectionItem.Pattern.Select();
            if (ripple) Mouse.Click(Find(window, "RippleToggle").GetClickablePoint());
            var tab = window.FindFirstDescendant(c => c.ByControlType(ControlType.TabItem).And(c.ByName("Transitions")));
            Assert.NotNull(tab); tab.Patterns.SelectionItem.Pattern.Select();
            Find(window, "AddDefaultDissolve").AsButton().Invoke();
            WaitFor(() => window.Title.Contains('*'), "The automatic transition did not change the project.");
            SaveButton(window).Invoke();
            WaitFor(() => !window.Title.Contains('*'), "The automatic transition was not saved.");
            var saved = await ProjectStore.LoadAsync(path);
            var clips = saved.Tracks[0].Clips;
            Assert.Equal((45L, 45L, ripple ? 15L : 0L), (clips[1].StartFrame, clips[1].DurationFrames, clips[1].SourceInFrame));
            Assert.Equal(15, clips[0].FadeOutFrames); Assert.Equal(15, clips[1].FadeInFrames);
            Assert.NotNull(clips[1].FadeInTransition);
            Assert.Equal(ripple ? 105 : 120, clips[2].StartFrame);
            Assert.Equal(120, saved.Tracks[1].Clips[0].StartFrame);
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
