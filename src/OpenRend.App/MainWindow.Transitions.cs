using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.UI;

namespace OpenRend.App;

public partial class MainWindow
{
    private Point transitionDragStart;
    private bool transitionDragReady;
    private bool transitionBrowserLoaded;
    private TransitionBrowserPreferences transitionPreferences = new();
    private VideoTransition? draggedTransition;
    private FrameworkElement? draggedElement;

    private string EditingPreferencesPath => Path.Combine(Path.GetDirectoryName(recoveryPath)!, "editing.json");
    private string TransitionPreferencesPath => Path.Combine(Path.GetDirectoryName(recoveryPath)!, "transitions.json");

    private void InitializeTransitionBrowser()
    {
        try { transitionPreferences = TransitionBrowserPreferencesStore.Load(TransitionPreferencesPath); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            vm.Status = "Transition favorites could not be loaded: " + error.Message;
            transitionPreferences = new();
        }

        FavoriteDissolve.IsChecked = transitionPreferences.FavoriteDissolve;

        // Build list of all 18 Vegas Pro transition plugins
        TransitionPlugIns.Items.Clear();
        // Always add Dissolve first with its existing XAML DissolvePluginItem
        DissolvePluginItem.DataContext = TransitionCatalog.FindPlugin(VideoTransitionKind.Dissolve);
        TransitionPlugIns.Items.Add(DissolvePluginItem);

        var starTemplate = (ControlTemplate)FavoriteDissolve.Template;

        foreach (var plugin in TransitionCatalog.Plugins)
        {
            if (plugin.Kind == VideoTransitionKind.Dissolve) continue;

            var item = new ListBoxItem
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                DataContext = plugin,
                Tag = plugin.Kind
            };
            AutomationProperties.SetName(item, plugin.DisplayName);

            var dock = new DockPanel();
            var star = new ToggleButton
            {
                Content = transitionPreferences.IsFavorite(plugin.DisplayName) ? "★" : "☆",
                Width = 22,
                Height = 20,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE3, 0xC4, 0x54)),
                FocusVisualStyle = (Style)FindResource("KeyboardFocus"),
                IsChecked = transitionPreferences.IsFavorite(plugin.DisplayName),
                ToolTip = $"Favorite {plugin.DisplayName}",
                Template = starTemplate,
                Tag = plugin.DisplayName
            };
            AutomationProperties.SetName(star, $"Favorite {plugin.DisplayName}");
            AutomationProperties.SetAutomationId(star, $"Favorite_{plugin.Kind}");
            star.Checked += PluginFavoriteChanged;
            star.Unchecked += PluginFavoriteChanged;

            var label = new TextBlock
            {
                Text = plugin.DisplayName,
                VerticalAlignment = VerticalAlignment.Center
            };

            dock.Children.Add(star);
            dock.Children.Add(label);
            item.Content = dock;
            TransitionPlugIns.Items.Add(item);
        }

        transitionBrowserLoaded = true;
        TransitionPlugIns.SelectedItem = DissolvePluginItem;
        RefreshTransitionBrowser();
    }

    private void TransitionFavoriteChanged(object sender, RoutedEventArgs e)
    {
        if (!transitionBrowserLoaded) return;
        var isFav = FavoriteDissolve.IsChecked == true;
        try
        {
            transitionPreferences = transitionPreferences.WithFavorite("Dissolve", isFav);
            TransitionBrowserPreferencesStore.Save(transitionPreferences, TransitionPreferencesPath);
        }
        catch (Exception error)
        {
            transitionBrowserLoaded = false;
            FavoriteDissolve.IsChecked = transitionPreferences.FavoriteDissolve;
            transitionBrowserLoaded = true;
            ShowError(error);
        }
        RefreshTransitionBrowser();
    }

    private void PluginFavoriteChanged(object sender, RoutedEventArgs e)
    {
        if (!transitionBrowserLoaded || sender is not ToggleButton star || star.Tag is not string pluginName) return;
        var isFav = star.IsChecked == true;
        star.Content = isFav ? "★" : "☆";
        try
        {
            transitionPreferences = transitionPreferences.WithFavorite(pluginName, isFav);
            TransitionBrowserPreferencesStore.Save(transitionPreferences, TransitionPreferencesPath);
        }
        catch (Exception error)
        {
            ShowError(error);
        }
        RefreshTransitionBrowser();
    }

    private void TransitionCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, TransitionCategories)) RefreshTransitionBrowser();
    }

    private void TransitionPluginSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PopulatePresetsForSelectedPlugin();
    }

    private void EditingPreferencesClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var dialog = new EditingPreferencesWindow(EditingPreferencesStore.Load(EditingPreferencesPath)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        EditingPreferencesStore.Save(dialog.Result, EditingPreferencesPath);
        vm.Status = "Editing preferences saved.";
    });

    private void TransitionSearchChanged(object sender, TextChangedEventArgs e)
        => RefreshTransitionBrowser();

    private void RefreshTransitionBrowser()
    {
        if (DissolvePluginItem is null || DefaultDissolvePreset is null || FavoriteDissolve is null || EmptyTransitions is null) return;
        var query = TransitionSearch.Text.Trim();
        var selectedCategoryTab = TransitionCategories.SelectedItem as TabItem;
        var categoryHeader = selectedCategoryTab?.Header?.ToString() ?? "All Plug-ins";
        var isFavoritesTab = selectedCategoryTab == FavoriteTransitionsCategory;

        FavoriteDissolve.Content = transitionPreferences.FavoriteDissolve ? "★" : "☆";
        FavoriteDissolve.IsChecked = transitionPreferences.FavoriteDissolve;

        var anyPluginVisible = false;
        foreach (var obj in TransitionPlugIns.Items)
        {
            if (obj is not ListBoxItem item || item.DataContext is not TransitionPluginInfo plugin) continue;

            var isFav = transitionPreferences.IsFavorite(plugin.DisplayName);
            var categoryMatch = isFavoritesTab
                ? isFav
                : (categoryHeader == "All Plug-ins" || string.Equals(plugin.Category, categoryHeader, StringComparison.OrdinalIgnoreCase));

            var queryMatch = query.Length == 0 ||
                plugin.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                plugin.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                plugin.Presets.Any(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

            var visible = categoryMatch && queryMatch;
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible) anyPluginVisible = true;
        }

        // Dissolve default preset visibility specifically for FlaUI test compatibility
        var dissolveVisible = DissolvePluginItem.Visibility == Visibility.Visible;
        DefaultDissolvePreset.Visibility = dissolveVisible ? Visibility.Visible : Visibility.Collapsed;

        PopulatePresetsForSelectedPlugin();

        EmptyTransitions.Text = isFavoritesTab && !transitionPreferences.Favorites.Any() && !transitionPreferences.FavoriteDissolve
            ? "No favorite transitions." : "No transitions match your search.";
        EmptyTransitions.Visibility = anyPluginVisible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PopulatePresetsForSelectedPlugin()
    {
        if (TransitionPresets is null || DefaultDissolvePreset is null) return;

        var selectedItem = TransitionPlugIns.SelectedItem as ListBoxItem;
        if (selectedItem?.DataContext is not TransitionPluginInfo plugin)
        {
            TransitionPresets.Items.Clear();
            return;
        }

        var query = TransitionSearch.Text.Trim();
        TransitionPresets.Items.Clear();

        if (plugin.Kind == VideoTransitionKind.Dissolve)
        {
            // First add the DefaultDissolvePreset to preserve exact test binding
            var defaultMatch = query.Length == 0 || "Dissolve Default Fades".Contains(query, StringComparison.OrdinalIgnoreCase);
            DefaultDissolvePreset.Visibility = defaultMatch ? Visibility.Visible : Visibility.Collapsed;
            DefaultDissolvePreset.DataContext = plugin.Presets.First();
            TransitionPresets.Items.Add(DefaultDissolvePreset);

            foreach (var preset in plugin.Presets.Skip(1))
            {
                if (query.Length > 0 && !preset.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                TransitionPresets.Items.Add(CreatePresetItem(plugin, preset));
            }
        }
        else
        {
            foreach (var preset in plugin.Presets)
            {
                if (query.Length > 0 && !preset.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                TransitionPresets.Items.Add(CreatePresetItem(plugin, preset));
            }
        }

        if (TransitionPresets.Items.Count > 0 && TransitionPresets.SelectedItem is null)
            TransitionPresets.SelectedIndex = 0;
    }

    private ListBoxItem CreatePresetItem(TransitionPluginInfo plugin, TransitionPresetInfo preset)
    {
        var item = new ListBoxItem
        {
            Width = 124,
            Height = 103,
            Padding = new Thickness(4),
            Margin = new Thickness(2),
            DataContext = preset,
            ToolTip = "Double-click to add to the selection, or drag to a video overlap."
        };
        AutomationProperties.SetName(item, preset.Name);

        var grid = new Grid();
        var stack = new StackPanel();

        var border = new Border
        {
            Width = 110,
            Height = 69,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
            BorderThickness = new Thickness(1),
            Background = GetThumbnailBrush(plugin.Category, plugin.Kind)
        };

        var label = new TextBlock
        {
            Text = preset.Name,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        stack.Children.Add(border);
        stack.Children.Add(label);

        var addButton = new Button
        {
            Content = "+",
            Width = 21,
            Height = 21,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "Add to selected events",
            Tag = preset
        };
        AutomationProperties.SetName(addButton, $"Add {preset.Name}");
        addButton.Click += (s, e) =>
        {
            e.Handled = true;
            ApplyPreset(preset);
        };

        grid.Children.Add(stack);
        grid.Children.Add(addButton);
        item.Content = grid;

        item.MouseDoubleClick += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Left) ApplyPreset(preset);
        };
        item.PreviewMouseLeftButtonDown += (s, e) =>
        {
            transitionDragReady = true;
            transitionDragStart = e.GetPosition(item);
            draggedTransition = new VideoTransition
            {
                Kind = preset.Kind,
                Preset = preset.Name,
                Parameters = preset.Parameters.ToDictionary(k => k.Key, v => v.Value)
            };
            draggedElement = item;
            item.IsSelected = true;
        };

        return item;
    }

    private Brush GetThumbnailBrush(string category, VideoTransitionKind kind)
    {
        return category switch
        {
            "Fades" => kind == VideoTransitionKind.Flash
                ? new LinearGradientBrush(Color.FromRgb(0xFF, 0xF9, 0xC4), Color.FromRgb(0xF5, 0x7F, 0x17), 45)
                : new LinearGradientBrush(Color.FromRgb(0x31, 0x6E, 0x9C), Color.FromRgb(0xB9, 0x73, 0x44), 0),
            "Wipes" => new LinearGradientBrush(Color.FromRgb(0x00, 0xA8, 0xFF), Color.FromRgb(0x9C, 0x27, 0xB0), 45),
            "Reveals" => new LinearGradientBrush(Color.FromRgb(0xFF, 0x98, 0x00), Color.FromRgb(0x37, 0x47, 0x4F), 90),
            "Transforms" => new LinearGradientBrush(Color.FromRgb(0x00, 0x96, 0x88), Color.FromRgb(0xE9, 0x1E, 0x63), 0),
            "3D and Peels" => new LinearGradientBrush(Color.FromRgb(0x3F, 0x51, 0xB5), Color.FromRgb(0xFF, 0xD7, 0x00), 45),
            _ => new LinearGradientBrush(Color.FromRgb(0x42, 0x42, 0x42), Color.FromRgb(0x61, 0x61, 0x61), 45)
        };
    }

    private void ApplyPreset(TransitionPresetInfo preset)
    {
        Guard(() =>
        {
            if (vm.Selection.Count == 0) { vm.Status = $"Select a video event to apply {preset.Kind}."; return; }
            Pause();
            var settings = EditingPreferencesStore.Load(EditingPreferencesPath);
            var trans = new VideoTransition
            {
                Kind = preset.Kind,
                Preset = preset.Name,
                Parameters = preset.Parameters.ToDictionary(k => k.Key, v => v.Value)
            };
            var count = vm.Editor.ApplyDissolveToSelection(vm.Selection, settings.OverlapFrames(vm.Project.FrameRate), vm.Ripple, trans);
            vm.Status = count == 0 ? "No adjacent video overlap found for the selection." :
                $"{preset.Kind} ({preset.Name}) applied to {count} overlap{(count == 1 ? "" : "s")}.";
        });
    }

    private void ApplyDefaultDissolveClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Guard(() =>
        {
            if (vm.Selection.Count == 0) { vm.Status = "Select a video event to apply Dissolve."; return; }
            Pause();
            var settings = EditingPreferencesStore.Load(EditingPreferencesPath);
            var count = vm.Editor.ApplyDissolveToSelection(vm.Selection, settings.OverlapFrames(vm.Project.FrameRate), vm.Ripple);
            vm.Status = count == 0 ? "No adjacent video overlap found for the selection." :
                $"Default Dissolve applied to {count} overlap{(count == 1 ? "" : "s")}.";
        });
    }

    private bool IsTransitionAddButton(object source)
    {
        for (var node = source as DependencyObject; node is not null;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node == AddDefaultDissolve) return true;
        return false;
    }

    private void TransitionPresetDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !IsTransitionAddButton(e.OriginalSource))
            ApplyDefaultDissolveClick(sender, e);
    }

    private void TransitionPresetKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (TransitionPresets.SelectedItem is ListBoxItem item)
            {
                if (item == DefaultDissolvePreset) ApplyDefaultDissolveClick(sender, e);
                else if (item.DataContext is TransitionPresetInfo preset) ApplyPreset(preset);
            }
        }
    }

    private void TransitionPresetMouseDown(object sender, MouseButtonEventArgs e)
    {
        transitionDragReady = !IsTransitionAddButton(e.OriginalSource);
        transitionDragStart = e.GetPosition(DefaultDissolvePreset);
        draggedTransition = new VideoTransition { Kind = VideoTransitionKind.Dissolve, Preset = "Default" };
        draggedElement = DefaultDissolvePreset;
        if (transitionDragReady) DefaultDissolvePreset.IsSelected = true;
    }

    private void TransitionPresetMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed && e.RightButton != MouseButtonState.Pressed)
        { transitionDragReady = false; return; }
        if (!transitionDragReady || draggedElement is null || draggedTransition is null) return;
        var offset = e.GetPosition(draggedElement) - transitionDragStart;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        transitionDragReady = false;
        DragDrop.DoDragDrop(draggedElement,
            new DataObject(TimelineView.TransitionDataFormat, draggedTransition.DeepClone()), DragDropEffects.Copy);
    }
}
