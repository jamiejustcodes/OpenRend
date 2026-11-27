using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Project;

namespace OpenRend.App;

public partial class MainWindow
{
    private sealed record GeneratorEntry(MediaGeneratorKind Kind, string Name, string Category);

    private static readonly GeneratorEntry[] GeneratorEntries =
    [
        new(MediaGeneratorKind.Checkerboard, "Checkerboard", "Patterns"),
        new(MediaGeneratorKind.ColorGradient, "Color Gradient", "Backgrounds"),
        new(MediaGeneratorKind.NoiseTexture, "Noise Texture", "Backgrounds"),
        new(MediaGeneratorKind.SolidColor, "Solid Color", "Backgrounds"),
        new(MediaGeneratorKind.TestPattern, "Test Pattern", "Patterns"),
        new(MediaGeneratorKind.TitlesAndText, "Titles & Text", "Text")
    ];

    private HashSet<MediaGeneratorKind> favoriteGenerators = [];
    private Point generatorDragStart;
    private ListBoxItem? generatorDragItem;
    private string GeneratorFavoritesPath => Path.Combine(Path.GetDirectoryName(recoveryPath)!, "media-generators.json");

    private void InitializeMediaGeneratorView()
    {
        try { favoriteGenerators = ToolBrowserFavoritesStore.Load<MediaGeneratorKind>(GeneratorFavoritesPath); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            vm.Status = "Media generator favorites could not be loaded. " + error.Message;
        }

        MediaGenPlugIns.Items.Clear();
        foreach (var entry in GeneratorEntries)
        {
            var item = new ListBoxItem
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                DataContext = entry,
                Visibility = Visibility.Collapsed
            };
            AutomationProperties.SetName(item, entry.Name);
            var row = new DockPanel();
            var favorite = new ToggleButton
            {
                Style = (Style)FindResource("FavoritesToggleButton"),
                Width = 22,
                Height = 20,
                Margin = new Thickness(0, 0, 4, 0),
                IsChecked = favoriteGenerators.Contains(entry.Kind),
                ToolTip = $"Favorite {entry.Name}",
                Tag = entry.Kind
            };
            AutomationProperties.SetName(favorite, $"Favorite {entry.Name}");
            favorite.Checked += GeneratorFavoriteChanged;
            favorite.Unchecked += GeneratorFavoriteChanged;
            row.Children.Add(favorite);
            row.Children.Add(new TextBlock { Text = entry.Name, VerticalAlignment = VerticalAlignment.Center });
            item.Content = row;
            MediaGenPlugIns.Items.Add(item);
        }
        RefreshMediaGeneratorBrowser();
    }

    private void GeneratorFavoriteChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: MediaGeneratorKind kind } favorite) return;
        var isFavorite = favorite.IsChecked == true;
        var wasFavorite = favoriteGenerators.Contains(kind);
        if (isFavorite == wasFavorite) return;
        var updated = new HashSet<MediaGeneratorKind>(favoriteGenerators);
        if (isFavorite) updated.Add(kind);
        else updated.Remove(kind);
        try { ToolBrowserFavoritesStore.Save(updated, GeneratorFavoritesPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            favorite.IsChecked = wasFavorite;
            vm.Status = "Media generator favorites could not be saved. " + error.Message;
            return;
        }

        favoriteGenerators = updated;
        RefreshMediaGeneratorBrowser();
    }

    private void MediaGenFilterChanged(object sender, RoutedEventArgs e) => RefreshMediaGeneratorBrowser();

    private static bool GeneratorMatches(GeneratorEntry entry, string query) =>
        entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || entry.Category.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static bool PresetMatches(MediaGeneratorPreset preset, string query) =>
        preset.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || preset.Description.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void RefreshMediaGeneratorBrowser()
    {
        if (MediaGenPlugIns is null || MediaGenSearch is null || EmptyMediaGen is null) return;
        var selectedTab = MediaGenCategories.SelectedItem as TabItem;
        var favorites = selectedTab == FavoriteMediaGenCategory;
        var category = selectedTab?.Tag as string;
        var query = MediaGenSearch.Text.Trim();
        ListBoxItem? firstVisible = null;
        foreach (ListBoxItem item in MediaGenPlugIns.Items)
        {
            var entry = (GeneratorEntry)item.DataContext;
            var inCategory = favorites ? favoriteGenerators.Contains(entry.Kind)
                : category is null || entry.Category == category;
            var matches = GeneratorMatches(entry, query) || MediaGeneratorCatalog.Presets.Any(preset =>
                preset.Kind == entry.Kind && PresetMatches(preset, query));
            item.Visibility = inCategory && matches ? Visibility.Visible : Visibility.Collapsed;
            if (item.Visibility == Visibility.Visible) firstVisible ??= item;
        }

        if (MediaGenPlugIns.SelectedItem is not ListBoxItem { Visibility: Visibility.Visible })
            MediaGenPlugIns.SelectedItem = firstVisible;
        else
            RefreshGeneratorPresets();

        EmptyMediaGen.Text = favorites && query.Length == 0
            ? "No favorite generators. Use the star beside a generator to add it."
            : "No matching media generators.";
        EmptyMediaGen.Visibility = firstVisible is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MediaGenPluginSelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshGeneratorPresets();

    private void RefreshGeneratorPresets()
    {
        if (MediaGenPresets is null) return;
        var previous = (MediaGenPresets.SelectedItem as ListBoxItem)?.Tag as MediaGeneratorPreset;
        MediaGenPresets.Items.Clear();
        if (MediaGenPlugIns.SelectedItem is not ListBoxItem { DataContext: GeneratorEntry entry }) return;

        if (entry.Kind == MediaGeneratorKind.TitlesAndText)
        {
            MediaGenPresets.Items.Add(new ListBoxItem { DataContext = entry, Content = "Default Title", Padding = new Thickness(8) });
        }
        else
        {
            var query = MediaGenSearch.Text.Trim();
            var showAll = GeneratorMatches(entry, query);
            foreach (var preset in MediaGeneratorCatalog.Presets.Where(preset => preset.Kind == entry.Kind))
            {
                if (!showAll && !PresetMatches(preset, query)) continue;
                var card = new ListBoxItem
                {
                    Width = 132,
                    Padding = new Thickness(5),
                    Margin = new Thickness(2),
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new Image { Source = BuildGeneratorThumbnail(preset), Width = 114, Height = 64, Stretch = Stretch.Uniform },
                            new TextBlock
                            {
                                Text = preset.Name,
                                TextTrimming = TextTrimming.CharacterEllipsis,
                                TextAlignment = TextAlignment.Center,
                                Margin = new Thickness(0, 4, 0, 0)
                            }
                        }
                    },
                    DataContext = entry,
                    Tag = preset,
                    ToolTip = preset.Description
                };
                AutomationProperties.SetName(card, preset.Name);
                MediaGenPresets.Items.Add(card);
                if (preset == previous) card.IsSelected = true;
            }
        }

        if (MediaGenPresets.SelectedItem is null) MediaGenPresets.SelectedIndex = 0;
    }

    private BitmapSource BuildGeneratorThumbnail(MediaGeneratorPreset preset)
    {
        var media = vm.Editor.BuildGeneratedMedia(preset.Kind, preset.Name);
        var scale = Math.Min(1, Math.Min(256d / media.Width, 144d / media.Height));
        var width = Math.Max(1, (int)Math.Round(media.Width * scale));
        var height = Math.Max(1, (int)Math.Round(media.Height * scale));

        // Keep pixel-based pattern sizes proportional when the thumbnail is smaller than the project.
        var pixelParameter = preset.Kind switch
        {
            MediaGeneratorKind.Checkerboard => "size",
            MediaGeneratorKind.NoiseTexture => "scale",
            _ => null
        };
        if (pixelParameter is not null && media.Generator?.Parameters is { } parameters
            && int.TryParse(parameters.GetValueOrDefault(pixelParameter), out var size))
            parameters[pixelParameter] = Math.Max(1, (int)Math.Round(size * scale)).ToString(System.Globalization.CultureInfo.InvariantCulture);

        var pixels = MediaGeneratorSynthesizer.Render(media, width, height);
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    private void MediaGenPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InsertMediaGenButton is null || MediaGenInfoText is null) return;
        var item = MediaGenPresets.SelectedItem as ListBoxItem;
        var isTitle = item?.DataContext is GeneratorEntry { Kind: MediaGeneratorKind.TitlesAndText };
        InsertMediaGenButton.Content = isTitle ? "Create title…" : "Insert";
        InsertMediaGenButton.IsEnabled = item is not null;
        MediaGenInfoText.Text = (item?.Tag as MediaGeneratorPreset)?.Description ?? string.Empty;
    }

    private void InsertMediaGenClick(object sender, RoutedEventArgs e) => InsertGeneratorSelection(MediaGenPresets.SelectedItem);

    private void MediaGenPresetDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || GetGeneratorItem(e.OriginalSource) is not { } item) return;
        InsertGeneratorSelection(item);
        e.Handled = true;
    }

    private void MediaGenPresetKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        if (!e.IsRepeat) InsertGeneratorSelection(MediaGenPresets.SelectedItem);
        e.Handled = true;
    }

    private ListBoxItem? GetGeneratorItem(object source) => source is DependencyObject element
        ? ItemsControl.ContainerFromElement(MediaGenPresets, element) as ListBoxItem
        : null;

    private void MediaGenPresetMouseDown(object sender, MouseButtonEventArgs e)
    {
        generatorDragItem = GetGeneratorItem(e.OriginalSource);
        generatorDragStart = e.GetPosition(MediaGenPresets);
    }

    private void MediaGenPresetMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            generatorDragItem = null;
            return;
        }

        if (generatorDragItem is not { Tag: MediaGeneratorPreset preset } item) return;
        var offset = e.GetPosition(MediaGenPresets) - generatorDragStart;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        generatorDragItem = null;
        DragDrop.DoDragDrop(item, new DataObject(typeof(MediaGeneratorPreset), preset), DragDropEffects.Copy);
    }

    private void InsertGeneratorSelection(object? selection)
    {
        if (selection is ListBoxItem { Tag: MediaGeneratorPreset preset })
        {
            var focused = vm.Project.Tracks.ElementAtOrDefault(Timeline.ActiveTrackIndex);
            var target = focused is { Kind: TrackKind.Video, IsLocked: false, IsAdjustmentTrack: false } ? focused
                : vm.Project.Tracks.FirstOrDefault(track => track is { Kind: TrackKind.Video, IsLocked: false, IsAdjustmentTrack: false });
            PlaceGenerator(preset, target?.Id, vm.Position);
        }
        else if (selection is ListBoxItem { DataContext: GeneratorEntry { Kind: MediaGeneratorKind.TitlesAndText } })
        {
            Guard(() =>
            {
                Pause();
                new TitlesAndTextWindow(vm) { Owner = this }.ShowDialog();
                Refresh();
                RequestPreview();
            });
        }
    }

    private void PlaceGenerator(MediaGeneratorPreset preset, Guid? track, long frame) => Guard(() =>
    {
        Pause();
        var duration = vm.Project.FrameRate.Frames(MediaGeneratorCatalog.DefaultDurationSeconds);
        var clip = vm.Editor.AddGeneratedMediaClip(track, frame, preset.Kind, preset.Name, duration);
        vm.Status = $"Inserted {clip.Name}.";
        RequestPreview();
    });
}
