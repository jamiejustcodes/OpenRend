using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class TransitionCatalogTests
{
    [Fact]
    public void AllVegasTransitionKindsHaveCatalogEntriesAndPresets()
    {
        var allKinds = Enum.GetValues<VideoTransitionKind>();
        Assert.Equal(18, allKinds.Length);

        foreach (var kind in allKinds)
        {
            var plugin = TransitionCatalog.FindPlugin(kind);
            Assert.NotNull(plugin);
            Assert.NotEmpty(plugin.Presets);
            Assert.False(string.IsNullOrWhiteSpace(plugin.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(plugin.Category));
            Assert.Contains(plugin.Category, TransitionCatalog.Categories);

            foreach (var preset in plugin.Presets)
            {
                Assert.Equal(kind, preset.Kind);
                Assert.False(string.IsNullOrWhiteSpace(preset.Name));
                Assert.Equal(plugin.Category, preset.Category);
            }
        }
    }

    [Fact]
    public void CategoryFilteringReturnsAppropriatePlugins()
    {
        var fades = TransitionCatalog.GetPluginsForCategory("Fades");
        Assert.Contains(fades, p => p.Kind == VideoTransitionKind.Dissolve);
        Assert.Contains(fades, p => p.Kind == VideoTransitionKind.Flash);

        var wipes = TransitionCatalog.GetPluginsForCategory("Wipes");
        Assert.Contains(wipes, p => p.Kind == VideoTransitionKind.LinearWipe);
        Assert.Contains(wipes, p => p.Kind == VideoTransitionKind.ClockWipe);

        var reveals = TransitionCatalog.GetPluginsForCategory("Reveals");
        Assert.Contains(reveals, p => p.Kind == VideoTransitionKind.BarnDoor);
        Assert.Contains(reveals, p => p.Kind == VideoTransitionKind.Iris);

        var transforms = TransitionCatalog.GetPluginsForCategory("Transforms");
        Assert.Contains(transforms, p => p.Kind == VideoTransitionKind.Push);
        Assert.Contains(transforms, p => p.Kind == VideoTransitionKind.Slide);
        Assert.Contains(transforms, p => p.Kind == VideoTransitionKind.Zoom);

        var peels = TransitionCatalog.GetPluginsForCategory("3D and Peels");
        Assert.Contains(peels, p => p.Kind == VideoTransitionKind.PageLoop);
        Assert.Contains(peels, p => p.Kind == VideoTransitionKind.PagePeel);

        var favorites = TransitionCatalog.GetPluginsForCategory("Favorites", ["Linear Wipe", "Zoom"]);
        Assert.Equal(2, favorites.Count);
        Assert.Contains(favorites, p => p.Kind == VideoTransitionKind.LinearWipe);
        Assert.Contains(favorites, p => p.Kind == VideoTransitionKind.Zoom);
    }

    [Fact]
    public void VideoTransitionValidationAcceptsValidKindsAndRejectsInvalid()
    {
        var valid = new VideoTransition
        {
            Kind = VideoTransitionKind.LinearWipe,
            Preset = "Left-Right, Hard Edge",
            Parameters = new Dictionary<string, string> { ["Angle"] = "0.0" }
        };
        valid.Validate();

        var invalidKind = new VideoTransition { Kind = (VideoTransitionKind)999 };
        Assert.Throws<InvalidDataException>(() => invalidKind.Validate());

        var blankPreset = new VideoTransition { Preset = "" };
        Assert.Throws<InvalidDataException>(() => blankPreset.Validate());

        var nullPreset = new VideoTransition { Preset = null! };
        Assert.Throws<InvalidDataException>(() => nullPreset.Validate());
    }

    [Fact]
    public void VideoTransitionDeepCloneProducesIndependentParameters()
    {
        var original = new VideoTransition
        {
            Kind = VideoTransitionKind.BarnDoor,
            Preset = "Vertical, In, White Border",
            Parameters = new Dictionary<string, string>
            {
                ["Orientation"] = "1",
                ["BorderSize"] = "0.05"
            }
        };

        var clone = original.DeepClone();
        Assert.Equal(original.Kind, clone.Kind);
        Assert.Equal(original.Preset, clone.Preset);
        Assert.Equal(original.Parameters!["BorderSize"], clone.Parameters!["BorderSize"]);

        clone.Parameters["BorderSize"] = "0.10";
        Assert.Equal("0.05", original.Parameters["BorderSize"]);
        Assert.Equal("0.10", clone.Parameters["BorderSize"]);
    }

    [Fact]
    public async Task ProjectStoreRoundtripsVegasTransitionKindsAndParameters()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"openrend_test_trans_{Guid.NewGuid():N}.openrend");
        try
        {
            var mediaAsset = new MediaAsset { Path = "test.mp4", DurationSeconds = 10, Width = 1920, Height = 1080, HasVideo = true };
            var project = new ProjectDocument
            {
                Name = "Transition Test",
                Width = 1920,
                Height = 1080,
                Media = [mediaAsset],
                Tracks =
                [
                    new TimelineTrack
                    {
                        Kind = TrackKind.Video,
                        Clips =
                        [
                            new TimelineClip
                            {
                                MediaId = mediaAsset.Id,
                                StartFrame = 0,
                                DurationFrames = 60,
                                FadeOutFrames = 15
                            },
                            new TimelineClip
                            {
                                MediaId = mediaAsset.Id,
                                StartFrame = 45,
                                DurationFrames = 60,
                                FadeInFrames = 15,
                                FadeInTransition = new VideoTransition
                                {
                                    Kind = VideoTransitionKind.LinearWipe,
                                    Preset = "Top-Left, Diagonal, Soft Edge",
                                    Parameters = new Dictionary<string, string>
                                    {
                                        ["Angle"] = "45.0",
                                        ["Feather"] = "0.1"
                                    }
                                }
                            }
                        ]
                    }
                ]
            };

            await ProjectStore.SaveAsync(project, tempFile);
            var loaded = await ProjectStore.LoadAsync(tempFile);

            var loadedClip = loaded.Tracks[0].Clips[1];
            Assert.NotNull(loadedClip.FadeInTransition);
            Assert.Equal(VideoTransitionKind.LinearWipe, loadedClip.FadeInTransition.Kind);
            Assert.Equal("Top-Left, Diagonal, Soft Edge", loadedClip.FadeInTransition.Preset);
            Assert.NotNull(loadedClip.FadeInTransition.Parameters);
            Assert.Equal("45.0", loadedClip.FadeInTransition.Parameters["Angle"]);
            Assert.Equal("0.1", loadedClip.FadeInTransition.Parameters["Feather"]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void TransitionBrowserPreferencesHandlesMultipleFavoritesAndLegacyDissolve()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"trans_prefs_{Guid.NewGuid():N}.json");
        try
        {
            var prefs = new TransitionBrowserPreferences()
                .WithFavorite("Dissolve", true)
                .WithFavorite("Linear Wipe", true)
                .WithFavorite("Barn Door", true);

            Assert.True(prefs.FavoriteDissolve);
            Assert.True(prefs.IsFavorite("Dissolve"));
            Assert.True(prefs.IsFavorite("Linear Wipe"));
            Assert.True(prefs.IsFavorite("Barn Door"));
            Assert.False(prefs.IsFavorite("Push"));

            TransitionBrowserPreferencesStore.Save(prefs, tempFile);
            var loaded = TransitionBrowserPreferencesStore.Load(tempFile);

            Assert.True(loaded.FavoriteDissolve);
            Assert.True(loaded.IsFavorite("Dissolve"));
            Assert.True(loaded.IsFavorite("Linear Wipe"));
            Assert.True(loaded.IsFavorite("Barn Door"));
            Assert.False(loaded.IsFavorite("Push"));

            var unfavorited = loaded.WithFavorite("Dissolve", false);
            Assert.False(unfavorited.FavoriteDissolve);
            Assert.False(unfavorited.IsFavorite("Dissolve"));
            Assert.True(unfavorited.IsFavorite("Linear Wipe"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
