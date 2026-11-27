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
    public async Task VideoFxSearchAndFavoritesApplyRealPresetsToSelectedVideoEvents()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "video-effects.openrend");
        var preferences = Path.Combine(temporary, "video-effects.json");
        var asset = new MediaAsset
        {
            Path = Path.Combine(root, "tests", "native", "fixture.mp4"),
            Width = 320, Height = 180, DurationSeconds = 3, FrameRate = 30, HasVideo = true, HasAudio = true
        };
        var grouped = new TimelineClip { MediaId = asset.Id, Name = "Linked video", GroupId = Guid.NewGuid(), DurationFrames = 30 };
        var audio = grouped.DeepClone();
        audio.Id = Guid.NewGuid();
        audio.Name = "Linked audio";
        await ProjectStore.SaveAsync(new ProjectDocument
        {
            Name = "UI integration", Width = 320, Height = 180, Media = [asset],
            Tracks =
            [
                new() { Kind = TrackKind.Video, Clips = [grouped, new() { MediaId = asset.Id, StartFrame = 45, DurationFrames = 30 }] },
                new() { Kind = TrackKind.Audio, Clips = [audio] }
            ]
        }, path);

        try
        {
            using (var running = Launch(executable, path, temporary))
            {
                var window = running.Window;
                Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                OpenVideoFxBrowser(window);
                var search = Find(window, "VideoFxSearch").AsTextBox();
                var effects = Find(window, "VideoFxPlugIns");
                var presets = Find(window, "VideoFxPresets");
                var apply = Find(window, "ApplyVideoFxButton").AsButton();
                SelectBrowserTab(Find(window, "VideoFxCategories"), "Blur");
                search.Text = "Sepia";
                WaitFor(() => !apply.IsEnabled && VisibleNamedItem(effects, "Sepia") is null,
                    "Search bypassed the selected Blur category.");

                Find(window, "AllVideoFxPlugins").Patterns.SelectionItem.Pattern.Select();
                WaitFor(() => VisibleNamedItem(effects, "Sepia") is not null && apply.IsEnabled,
                    "All effects did not restore the matching effect.");
                var favorite = VisibleNamedItem(effects, "Sepia")!.FindFirstDescendant(c => c.ByName("Favorite Sepia"));
                Assert.NotNull(favorite);
                favorite.Patterns.Toggle.Pattern.Toggle();
                WaitFor(() => VideoFxPreferencesStore.Load(preferences).Contains(VideoEffectKind.Sepia),
                    "The favorite was not written to preferences.");
                Find(window, "FavoriteVideoFx").Patterns.SelectionItem.Pattern.Select();
                search.Text = "Subtle Warmth";
                WaitFor(() => VisibleNamedItem(presets, "Subtle Warmth") is not null && VisibleNamedItem(presets, "Default") is null,
                    "Searching the Favorites category did not narrow the preset list.");
                search.Text = "missing preset";
                WaitFor(() => !apply.IsEnabled, "Applying remained enabled after all matches were filtered.");
                search.Text = "Subtle Warmth";
                WaitFor(() => apply.IsEnabled, "The matching preset could not be applied.");
                Assert.DoesNotContain('*', window.Title);

                apply.Invoke();
                WaitFor(() => window.Title.Contains('*'), "Applying the preset did not edit the project.");
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "Saving the selected events did not finish.");
                var saved = await ProjectStore.LoadAsync(path);
                var effectsByEvent = saved.Tracks[0].Clips.Select(clip => Assert.Single(clip.VideoEffects)).ToArray();
                Assert.All(effectsByEvent, effect =>
                {
                    Assert.Equal(VideoEffectKind.Sepia, effect.Kind);
                    Assert.Equal(0.35, effect.GetParameter("Amount"), 6);
                });
                Assert.NotEqual(effectsByEvent[0].Id, effectsByEvent[1].Id);
                Assert.Empty(saved.Tracks[1].Clips[0].VideoEffects);

                apply.Focus();
                Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
                WaitFor(() => window.Title.Contains('*'), "Undo did not restore the project before the preset.");
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "Saving the undo did not finish.");
                saved = await ProjectStore.LoadAsync(path);
                Assert.All(saved.Tracks.SelectMany(track => track.Clips), clip => Assert.Empty(clip.VideoEffects));

                VisibleNamedItem(presets, "Subtle Warmth")!.Focus();
                Keyboard.Type(VirtualKeyShort.RETURN);
                WaitFor(() => window.Title.Contains('*'), "Enter did not apply the preset from the list.");
                Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
                SaveButton(window).Invoke();
                WaitFor(() => !window.Title.Contains('*'), "Saving after keyboard apply and undo did not finish.");
                saved = await ProjectStore.LoadAsync(path);
                Assert.All(saved.Tracks.SelectMany(track => track.Clips), clip => Assert.Empty(clip.VideoEffects));
                running.CloseGracefully();
            }

            using (var running = Launch(executable, path, temporary))
            {
                var window = running.Window;
                OpenVideoFxBrowser(window);
                Find(window, "FavoriteVideoFx").Patterns.SelectionItem.Pattern.Select();
                var sepia = VisibleNamedItem(Find(window, "VideoFxPlugIns"), "Sepia");
                Assert.NotNull(sepia);
                var favorite = sepia.FindFirstDescendant(c => c.ByName("Favorite Sepia"));
                Assert.Equal(ToggleState.On, favorite?.Patterns.Toggle.Pattern.ToggleState.Value);
                favorite!.Patterns.Toggle.Pattern.Toggle();
                WaitFor(() => !VideoFxPreferencesStore.Load(preferences).Contains(VideoEffectKind.Sepia) &&
                    VisibleNamedItem(Find(window, "VideoFxPlugIns"), "Sepia") is null, "Removing the active favorite did not refresh the category.");
                Assert.DoesNotContain('*', window.Title);
                running.CloseGracefully();
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static void OpenVideoFxBrowser(Window window) => SelectBrowserTab(window, "Video FX");

    private static void SelectBrowserTab(AutomationElement parent, string name)
    {
        var tab = parent.FindFirstDescendant(c => c.ByControlType(ControlType.TabItem).And(c.ByName(name)));
        Assert.NotNull(tab);
        tab.Patterns.SelectionItem.Pattern.Select();
    }

    private static AutomationElement? VisibleNamedItem(AutomationElement list, string name)
    {
        var item = list.FindFirstDescendant(c => c.ByControlType(ControlType.ListItem).And(c.ByName(name)));
        return item is not null && !item.Properties.IsOffscreen.ValueOrDefault ? item : null;
    }
}
