using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.App.Tests;

public sealed partial class EditorWorkflowTests
{
    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task NewEventLoopPreferenceAppliesToNewLinkedMediaAndPersistsAfterRestart()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "new-event-loop.openrend");
        try
        {
            var asset = new MediaAsset { Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
                Width = 320, Height = 180, FrameRate = 30, DurationSeconds = 3, HasVideo = true, HasAudio = true };
            var existing = new TimelineClip { MediaId = asset.Id, DurationFrames = 30 };
            await ProjectStore.SaveAsync(new ProjectDocument
            {
                Name = "UI integration", Width = 320, Height = 180, Media = [asset],
                Tracks = [new() { Kind = TrackKind.Video, Clips = [existing] }]
            }, path);

            using (var running = Launch(executable, path, directory))
            {
                var main = running.Window;
                var preferences = OpenPreferences(main);
                var setting = Find(preferences, "LoopNewEvents").Patterns.Toggle.Pattern;
                Assert.Equal(ToggleState.Off, setting.ToggleState.Value);
                setting.Toggle();
                Find(preferences, "EditingPreferencesOk").AsButton().Invoke();
                WaitFor(() => main.ModalWindows.Length == 0, "The preferences dialog did not close after saving.");
                Assert.True(EditingPreferencesStore.Load(Path.Combine(directory, "editing.json")).LoopNewEvents);
                Assert.DoesNotContain('*', main.Title);
                await PlaceAndCheck(main, expectedGroups: 1);
                running.CloseGracefully(useCaptionButton: true);
            }

            using (var running = Launch(executable, path, directory))
            {
                var main = running.Window;
                var preferences = OpenPreferences(main);
                var setting = Find(preferences, "LoopNewEvents").Patterns.Toggle.Pattern;
                Assert.Equal(ToggleState.On, setting.ToggleState.Value);
                setting.Toggle();
                Find(preferences, "EditingPreferencesCancel").AsButton().Invoke();
                WaitFor(() => main.ModalWindows.Length == 0, "The preferences dialog did not close after Cancel.");
                await PlaceAndCheck(main, expectedGroups: 2);
                running.CloseGracefully(useCaptionButton: true);
            }

            async Task PlaceAndCheck(Window window, int expectedGroups)
            {
                SelectBrowserTab(window, "Project Media");
                var assetItem = Assert.Single(Find(window, "MediaList").AsListBox().Items);
                Mouse.DoubleClick(assetItem.GetClickablePoint());
                WaitFor(() => window.Title.Contains('*'), "Double-clicking the media item did not place it on the timeline.");
                var saved = await SaveGrade(window, path);
                var video = saved.Tracks.Single(track => track.Kind == TrackKind.Video);
                var audio = saved.Tracks.Single(track => track.Kind == TrackKind.Audio);
                Assert.Equal(VideoSourceMode.Bounded, video.Clips.Single(clip => clip.Id == existing.Id).VideoSourceMode);
                Assert.Equal(expectedGroups, audio.Clips.Count);
                foreach (var sound in audio.Clips)
                {
                    Assert.Equal(AudioSourceMode.Loop, sound.AudioSourceMode);
                    Assert.NotNull(sound.GroupId);
                    var picture = Assert.Single(video.Clips, clip => clip.GroupId == sound.GroupId);
                    Assert.Equal(VideoSourceMode.Loop, picture.VideoSourceMode);
                    Assert.Equal(AudioSourceMode.Bounded, picture.AudioSourceMode);
                }
            }

        }
        finally { Directory.Delete(directory, recursive: true); }

        static Window OpenPreferences(Window main)
        {
            var options = main.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName("Options")));
            Assert.NotNull(options);
            options.Patterns.ExpandCollapse.Pattern.Expand();
            Find(main, "EditingPreferencesMenu").AsMenuItem().Invoke();
            Window? preferences = null;
            WaitFor(() => (preferences = main.ModalWindows.SingleOrDefault(dialog => dialog.Title == "Preferences")) is not null,
                "Editing preferences did not open.");
            return preferences!;
        }
    }
}
