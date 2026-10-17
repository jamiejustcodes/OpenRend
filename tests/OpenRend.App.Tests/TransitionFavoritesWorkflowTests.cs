using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.App.Tests;

public sealed partial class EditorWorkflowTests
{
    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task TransitionFavoritesPersistAndFilterWithoutEditingTheProject()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "favorites.openrend");
        var preferences = Path.Combine(temporary, "transitions.json");
        const string corrupt = "{\"FavoriteDissolve\":\"invalid\"}";
        await File.WriteAllTextAsync(preferences, corrupt);
        var asset = new MediaAsset { Path = Path.Combine(root, "tests", "native", "fixture.mp4"), Width = 320, Height = 180,
            DurationSeconds = 3, FrameRate = 30, HasVideo = true };
        await ProjectStore.SaveAsync(new ProjectDocument { Name = "UI integration", Width = 320, Height = 180, Media = [asset],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [new() { MediaId = asset.Id, DurationFrames = 30 }] }] }, path);
        try
        {
            using (var running = Launch(executable, path, temporary))
            {
                var window = running.Window;
                OpenTransitions(window);
                Assert.Equal(corrupt, await File.ReadAllTextAsync(preferences));
                Find(window, "FavoriteTransitions").Patterns.SelectionItem.Pattern.Select();
                WaitFor(() => !PresetVisible(window), "An unfavorited transition appeared in Favorites.");
                Find(window, "AllTransitionPlugins").Patterns.SelectionItem.Pattern.Select();
                var favorite = Find(window, "FavoriteDissolve");
                Assert.True(favorite.Patterns.Toggle.IsSupported);
                favorite.Patterns.Toggle.Pattern.Toggle();
                WaitFor(() => TransitionBrowserPreferencesStore.Load(preferences).FavoriteDissolve, "The favorite was not saved.");
                Find(window, "FavoriteTransitions").Patterns.SelectionItem.Pattern.Select();
                WaitFor(() => PresetVisible(window), "The favorite was not listed.");
                var search = Find(window, "TransitionSearch").AsTextBox();
                search.Text = "missing";
                WaitFor(() => !PresetVisible(window), "Search did not filter the favorite.");
                search.Text = "Fades";
                WaitFor(() => PresetVisible(window), "The category name was not searchable.");
                search.Text = "";
                var artifacts = Path.Combine(root, ".local", "desktop-validation"); Directory.CreateDirectory(artifacts);
                using (var screenshot = Capture.Element(window)) screenshot.ToFile(Path.Combine(artifacts, "transition-favorites.png"));
                favorite.Patterns.Toggle.Pattern.Toggle();
                WaitFor(() => !TransitionBrowserPreferencesStore.Load(preferences).FavoriteDissolve && !PresetVisible(window),
                    "Removing the favorite did not refresh the active category.");
                Find(window, "AllTransitionPlugins").Patterns.SelectionItem.Pattern.Select();
                Find(window, "FavoriteDissolve").Patterns.Toggle.Pattern.Toggle();
                WaitFor(() => TransitionBrowserPreferencesStore.Load(preferences).FavoriteDissolve, "The favorite was not restored.");
                Assert.DoesNotContain('*', window.Title);
                running.CloseGracefully();
            }
            using (var running = Launch(executable, path, temporary))
            {
                OpenTransitions(running.Window);
                Assert.Equal(ToggleState.On, Find(running.Window, "FavoriteDissolve").Patterns.Toggle.Pattern.ToggleState.Value);
                Find(running.Window, "FavoriteTransitions").Patterns.SelectionItem.Pattern.Select();
                WaitFor(() => PresetVisible(running.Window), "The favorite did not survive relaunch.");
                Assert.DoesNotContain('*', running.Window.Title);
                running.CloseGracefully();
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static void OpenTransitions(Window window)
    {
        var tab = window.FindFirstDescendant(c => c.ByControlType(ControlType.TabItem).And(c.ByName("Transitions")));
        Assert.NotNull(tab); tab.Patterns.SelectionItem.Pattern.Select();
    }

    private static bool PresetVisible(Window window)
    {
        var preset = window.FindFirstDescendant(c => c.ByAutomationId("DefaultDissolvePreset"));
        return preset is not null && !preset.Properties.IsOffscreen.ValueOrDefault;
    }
}
