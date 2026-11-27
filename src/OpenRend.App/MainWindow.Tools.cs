using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using OpenRend.Core;
using OpenRend.Project;

namespace OpenRend.App;

public partial class MainWindow
{
    private readonly Stack<string> explorerBackStack = new();
    private readonly Stack<string> explorerForwardStack = new();
    private string currentExplorerPath = string.Empty;

    private void InitializeToolsViews()
    {
        InitializeExplorerView();
        InitializeVideoFxView();
        InitializeMediaGeneratorView();
    }

    private void InitializeExplorerView()
    {
        var defaultFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (!Directory.Exists(defaultFolder))
            defaultFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        NavigateExplorer(defaultFolder, recordHistory: false);
    }

    public void NavigateExplorer(string folderPath, bool recordHistory = true)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;

        if (recordHistory && !string.IsNullOrEmpty(currentExplorerPath) && currentExplorerPath != folderPath)
        {
            explorerBackStack.Push(currentExplorerPath);
            explorerForwardStack.Clear();
        }

        currentExplorerPath = folderPath;
        if (ExplorerPathBox is not null) ExplorerPathBox.Text = folderPath;
        if (FolderLabel is not null) FolderLabel.Text = folderPath;

        try
        {
            var dir = new DirectoryInfo(folderPath);
            var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wav", ".mp3", ".flac", ".m4a", ".mxf", ".png", ".jpg", ".jpeg"
            };

            var files = dir.EnumerateFiles()
                .Where(f => supported.Contains(f.Extension))
                .OrderBy(f => f.Name)
                .ToList();

            if (ExplorerList is not null)
                ExplorerList.ItemsSource = files;
        }
        catch (Exception ex)
        {
            vm.Status = $"Cannot access folder: {ex.Message}";
        }
    }

    private void ExplorerBackClick(object sender, RoutedEventArgs e)
    {
        if (explorerBackStack.Count > 0)
        {
            var prev = explorerBackStack.Pop();
            explorerForwardStack.Push(currentExplorerPath);
            NavigateExplorer(prev, recordHistory: false);
        }
    }

    private void ExplorerForwardClick(object sender, RoutedEventArgs e)
    {
        if (explorerForwardStack.Count > 0)
        {
            var next = explorerForwardStack.Pop();
            explorerBackStack.Push(currentExplorerPath);
            NavigateExplorer(next, recordHistory: false);
        }
    }

    private void ExplorerUpClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(currentExplorerPath)) return;
        var parent = Directory.GetParent(currentExplorerPath);
        if (parent is not null && Directory.Exists(parent.FullName))
            NavigateExplorer(parent.FullName);
    }

    private void ExplorerRefreshClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(currentExplorerPath))
            NavigateExplorer(currentExplorerPath, recordHistory: false);
    }

    private void ExplorerPathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ExplorerPathBox is not null)
        {
            var path = ExplorerPathBox.Text.Trim();
            if (Directory.Exists(path))
                NavigateExplorer(path);
        }
    }

    private void ExplorerNavTreeChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag is string tag)
        {
            string targetPath = tag switch
            {
                "Desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "Documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                "Videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                _ => tag
            };

            if (Directory.Exists(targetPath))
                NavigateExplorer(targetPath);
        }
    }

    private async void ExplorerItemAddClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is FileInfo file)
        {
            await ImportPathsAsync([file.FullName]);
            ToolsTabs.SelectedIndex = 1;
        }
    }

    private void ExplorerItemPreviewClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FileInfo file }) StartExplorerPreview(file);
    }

    private HashSet<VideoEffectKind> favoriteVideoFx = [];

    private record VideoFxPreset(string Name, Dictionary<string, double>? Parameters = null);

    private static string GetFxCategory(VideoEffectDefinition def) => def switch
    {
        { Kind: VideoEffectKind.Glow or VideoEffectKind.FillLight } or { Category: "Glow" } => "Light",
        { Category: "AI" } => "AI/ML",
        { Category: "Color" } => "Color",
        { Category: "Blur" } => "Blur",
        { Category: "Stylize" or "Distort" } => "Creative",
        _ => "Utility"
    };

    private static IReadOnlyList<VideoFxPreset> GetPresetsForEffect(VideoEffectKind kind) => kind switch
    {
        VideoEffectKind.BlackAndWhite =>
        [
            new("Default"),
            new("50% Blend", new() { ["Amount"] = 0.5 }),
            new("75% Blend", new() { ["Amount"] = 0.75 })
        ],
        VideoEffectKind.GaussianBlur =>
        [
            new("Default"),
            new("Light Blur", new() { ["Amount"] = 0.15 }),
            new("Medium Blur", new() { ["Amount"] = 0.6 }),
            new("Heavy Blur", new() { ["Amount"] = 1.2 }),
            new("Maximum Blur", new() { ["Amount"] = 2.0 })
        ],
        VideoEffectKind.BrightnessContrast =>
        [
            new("Default"),
            new("Brighter (+20%)", new() { ["Amount"] = 0.2, ["SecondaryAmount"] = 0.0 }),
            new("Darker (-20%)", new() { ["Amount"] = -0.2, ["SecondaryAmount"] = 0.0 }),
            new("High Contrast", new() { ["Amount"] = 0.05, ["SecondaryAmount"] = 0.4 }),
            new("Low Contrast", new() { ["Amount"] = 0.0, ["SecondaryAmount"] = -0.3 }),
            new("Punchy Colors", new() { ["Amount"] = 0.1, ["SecondaryAmount"] = 0.25 })
        ],
        VideoEffectKind.Sepia =>
        [
            new("Default"),
            new("Subtle Warmth", new() { ["Amount"] = 0.35 }),
            new("Strong Sepia", new() { ["Amount"] = 0.85 })
        ],
        VideoEffectKind.Vignette =>
        [
            new("Default"),
            new("Subtle Falloff", new() { ["Amount"] = 0.35, ["Radius"] = 0.6, ["Softness"] = 0.4 }),
            new("Dramatic Spotlight", new() { ["Amount"] = 0.9, ["Radius"] = 0.25, ["Softness"] = 0.6 }),
            new("Heavy Lens", new() { ["Amount"] = 0.8, ["Radius"] = 0.35, ["Softness"] = 0.3 })
        ],
        VideoEffectKind.Glow =>
        [
            new("Default"),
            new("Soft Dreamy", new() { ["Amount"] = 0.8, ["Radius"] = 1.2, ["Threshold"] = 0.4 }),
            new("Subtle Warmth", new() { ["Amount"] = 0.3, ["Radius"] = 0.5, ["Threshold"] = 0.7 }),
            new("Intense Bloom", new() { ["Amount"] = 1.5, ["Radius"] = 1.5, ["Threshold"] = 0.3 })
        ],
        VideoEffectKind.FilmGrain =>
        [
            new("Default"),
            new("Fine 35mm Grain", new() { ["Amount"] = 0.08, ["Size"] = 1 }),
            new("Gritty 16mm Grain", new() { ["Amount"] = 0.25, ["Size"] = 2 }),
            new("Vintage Super8", new() { ["Amount"] = 0.4, ["Size"] = 3 })
        ],
        VideoEffectKind.Border =>
        [
            new("Default"),
            new("Thin White", new() { ["Amount"] = 0.015, ["Red"] = 1, ["Green"] = 1, ["Blue"] = 1 }),
            new("Thick Black", new() { ["Amount"] = 0.08, ["Red"] = 0, ["Green"] = 0, ["Blue"] = 0 }),
            new("Golden Frame", new() { ["Amount"] = 0.04, ["Red"] = 0.85, ["Green"] = 0.7, ["Blue"] = 0.2 })
        ],
        VideoEffectKind.Levels =>
        [
            new("Default"),
            new("Computer RGB to Studio RGB", new() { ["OutputBlack"] = 0.063, ["OutputWhite"] = 0.922 }),
            new("Studio RGB to Computer RGB", new() { ["InputBlack"] = 0.063, ["InputWhite"] = 0.922 }),
            new("Punch Midtones", new() { ["Gamma"] = 1.3 }),
            new("Crush Blacks", new() { ["InputBlack"] = 0.1, ["Gamma"] = 0.9 })
        ],
        VideoEffectKind.Invert =>
        [
            new("Default"),
            new("50% Blend", new() { ["Amount"] = 0.5 })
        ],
        VideoEffectKind.Sharpen =>
        [
            new("Default"),
            new("Subtle Sharpen", new() { ["Amount"] = 0.25 }),
            new("Medium Sharpen", new() { ["Amount"] = 0.8 }),
            new("Crisp Detail", new() { ["Amount"] = 1.5 })
        ],
        _ => [new("Default")]
    };

    private string VideoFxPreferencesPath => Path.Combine(Path.GetDirectoryName(recoveryPath)!, "video-effects.json");

    private void InitializeVideoFxView()
    {
        try { favoriteVideoFx.UnionWith(ToolBrowserFavoritesStore.Load<VideoEffectKind>(VideoFxPreferencesPath)); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            vm.Status = "Video effect favorites could not be loaded. " + error.Message;
        }

        VideoFxPlugIns.Items.Clear();
        foreach (var definition in VideoEffectCatalog.Definitions.OrderBy(effect => effect.Name))
        {
            var item = new ListBoxItem
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                DataContext = definition,
                Visibility = Visibility.Collapsed
            };
            AutomationProperties.SetName(item, definition.Name);

            var row = new DockPanel();
            var favorite = new ToggleButton
            {
                Style = (Style)FindResource("FavoritesToggleButton"),
                Width = 22,
                Height = 20,
                Margin = new Thickness(0, 0, 4, 0),
                IsChecked = favoriteVideoFx.Contains(definition.Kind),
                ToolTip = $"Favorite {definition.Name}",
                Tag = definition.Kind
            };
            AutomationProperties.SetName(favorite, $"Favorite {definition.Name}");
            favorite.Checked += VideoFxFavoriteChanged;
            favorite.Unchecked += VideoFxFavoriteChanged;

            row.Children.Add(favorite);
            row.Children.Add(new TextBlock
            {
                Text = definition.Name,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            item.Content = row;
            VideoFxPlugIns.Items.Add(item);
        }
        RefreshVideoFxBrowser();
    }

    private void VideoFxFavoriteChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: VideoEffectKind kind } favorite) return;
        var isFavorite = favorite.IsChecked == true;
        var wasFavorite = favoriteVideoFx.Contains(kind);
        if (isFavorite == wasFavorite) return;

        var updated = new HashSet<VideoEffectKind>(favoriteVideoFx);
        if (isFavorite) updated.Add(kind);
        else updated.Remove(kind);
        try { ToolBrowserFavoritesStore.Save(updated, VideoFxPreferencesPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            favorite.IsChecked = wasFavorite;
            vm.Status = "Video effect favorites could not be saved. " + error.Message;
            return;
        }

        favoriteVideoFx = updated;
        RefreshVideoFxBrowser();
    }

    private void VideoFxCategoryChanged(object sender, SelectionChangedEventArgs e) => RefreshVideoFxBrowser();

    private void VideoFxSearchChanged(object sender, TextChangedEventArgs e) => RefreshVideoFxBrowser();

    private void RefreshVideoFxBrowser()
    {
        if (VideoFxPlugIns is null || VideoFxSearch is null || EmptyVideoFx is null) return;
        var selectedTab = VideoFxCategories.SelectedItem as TabItem;
        var favorites = selectedTab == FavoriteFxCategory;
        var category = selectedTab?.Tag as string;
        var query = VideoFxSearch.Text.Trim();
        ListBoxItem? firstVisible = null;
        foreach (ListBoxItem item in VideoFxPlugIns.Items)
        {
            var definition = (VideoEffectDefinition)item.DataContext;
            var effectCategory = GetFxCategory(definition);
            var inCategory = favorites ? favoriteVideoFx.Contains(definition.Kind)
                : category is null || category == effectCategory;
            var matches = MatchesVideoEffect(definition, query)
                || GetPresetsForEffect(definition.Kind).Any(preset => preset.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
            item.Visibility = inCategory && matches ? Visibility.Visible : Visibility.Collapsed;
            if (item.Visibility == Visibility.Visible) firstVisible ??= item;
        }

        if (VideoFxPlugIns.SelectedItem is not ListBoxItem { Visibility: Visibility.Visible })
            VideoFxPlugIns.SelectedItem = firstVisible;
        else
            RefreshVideoFxPresets();

        EmptyVideoFx.Text = favorites && query.Length == 0
            ? "No favorite effects. Use the star beside an effect to add it."
            : "No matching video effects.";
        EmptyVideoFx.Visibility = firstVisible is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool MatchesVideoEffect(VideoEffectDefinition definition, string query) =>
        definition.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || GetFxCategory(definition).Contains(query, StringComparison.OrdinalIgnoreCase);

    private void VideoFxPluginSelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshVideoFxPresets();

    private void RefreshVideoFxPresets()
    {
        if (VideoFxPresets is null) return;
        var previous = VideoFxPresets.SelectedItem as ListBoxItem;
        VideoFxPresets.Items.Clear();
        if (VideoFxPlugIns.SelectedItem is not ListBoxItem { DataContext: VideoEffectDefinition definition })
        {
            VideoFxInfoText.Text = string.Empty;
            return;
        }

        VideoFxInfoText.Text = string.Join(", ", definition.Parameters.Select(parameter => parameter.Name));
        var query = VideoFxSearch.Text.Trim();
        var showAllPresets = MatchesVideoEffect(definition, query);
        foreach (var preset in GetPresetsForEffect(definition.Kind))
        {
            if (!showAllPresets && !preset.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            var item = new ListBoxItem
            {
                DataContext = definition,
                Tag = preset,
                Padding = new Thickness(8, 5, 8, 5),
                Content = preset.Name
            };
            VideoFxPresets.Items.Add(item);
            if (previous is { DataContext: VideoEffectDefinition oldDefinition, Tag: VideoFxPreset other }
                && oldDefinition.Kind == definition.Kind && other.Name == preset.Name)
                VideoFxPresets.SelectedItem = item;
        }
        if (VideoFxPresets.SelectedItem is null) VideoFxPresets.SelectedIndex = 0;
    }

    private void VideoFxPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ApplyVideoFxButton is not null)
            ApplyVideoFxButton.IsEnabled = VideoFxPresets.SelectedItem is not null;
    }

    private void VideoFxPresetDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(VideoFxPresets, source) is not ListBoxItem item) return;
        ApplyVideoFxPreset(item);
        e.Handled = true;
    }

    private void VideoFxPresetKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        if (!e.IsRepeat) ApplyVideoFxPreset(VideoFxPresets.SelectedItem);
        e.Handled = true;
    }

    private void ApplyVideoFxClick(object sender, RoutedEventArgs e) => ApplyVideoFxPreset(VideoFxPresets.SelectedItem);

    private void ApplyVideoFxPreset(object? item)
    {
        if (item is not ListBoxItem { DataContext: VideoEffectDefinition definition, Tag: VideoFxPreset preset }) return;

        var clips = vm.Project.Tracks.Where(track => track.Kind == TrackKind.Video)
            .SelectMany(track => track.Clips).Where(clip => vm.Selection.Contains(clip.Id)).ToArray();
        if (clips.Length == 0)
        {
            vm.Status = "Select a video event to apply the effect.";
            return;
        }

        Guard(() =>
        {
            vm.Editor.AddVideoEffectToEvents(clips.Select(clip => clip.Id), definition.Kind, preset.Parameters);
            vm.Status = clips.Length == 1 ? $"Added {definition.Name} to the selected event."
                : $"Added {definition.Name} to {clips.Length} selected events.";
            RequestPreview();
        });
    }
}
