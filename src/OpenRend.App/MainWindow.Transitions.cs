using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.UI;

namespace OpenRend.App;

public partial class MainWindow
{
    private Point transitionDragStart;
    private ListBoxItem? transitionDragItem;
    private bool transitionBrowserLoaded;
    private TransitionBrowserPreferences transitionPreferences = new();

    private string EditingPreferencesPath => Path.Combine(Path.GetDirectoryName(recoveryPath)!, "editing.json");
    private string GeneralPreferencesPath => Path.Combine(Path.GetDirectoryName(recoveryPath)!, "general.json");
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

        TransitionPlugIns.Items.Clear();
        DissolvePluginItem.DataContext = TransitionCatalog.FindPlugin(VideoTransitionKind.Dissolve);
        TransitionPlugIns.Items.Add(DissolvePluginItem);

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
                Style = (Style)FindResource("FavoritesToggleButton"),
                Width = 22,
                Height = 20,
                Margin = new Thickness(0, 0, 4, 0),
                IsChecked = transitionPreferences.IsFavorite(plugin.DisplayName),
                ToolTip = $"Favorite {plugin.DisplayName}",
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

    private void TransitionFavoriteChanged(object sender, RoutedEventArgs e) => SaveTransitionFavorite(FavoriteDissolve, "Dissolve");

    private void PluginFavoriteChanged(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string name } favorite) SaveTransitionFavorite(favorite, name);
    }

    private void SaveTransitionFavorite(ToggleButton favorite, string name)
    {
        if (!transitionBrowserLoaded) return;
        var before = transitionPreferences.IsFavorite(name);
        var isFavorite = favorite.IsChecked == true;
        if (before == isFavorite) return;
        var updated = transitionPreferences.WithFavorite(name, isFavorite);
        try { TransitionBrowserPreferencesStore.Save(updated, TransitionPreferencesPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            favorite.IsChecked = before;
            vm.Status = "Transition favorites could not be saved. " + error.Message;
            return;
        }
        transitionPreferences = updated;
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
        var editing = EditingPreferencesStore.Load(EditingPreferencesPath);
        var general = GeneralPreferencesStore.Load(GeneralPreferencesPath);
        var dialog = new EditingPreferencesWindow(editing, general) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        EditingPreferencesStore.Save(dialog.Result, EditingPreferencesPath);
        vm.LoopNewEvents = dialog.Result.LoopNewEvents;
        GeneralPreferencesStore.Save(dialog.GeneralResult, GeneralPreferencesPath);
        ApplyGeneralPreferences(dialog.GeneralResult);
        vm.Status = "Preferences saved.";
    });

    private void LoadNewEventLoopPreference()
    {
        try { vm.LoopNewEvents = EditingPreferencesStore.Load(EditingPreferencesPath).LoopNewEvents; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            vm.LoopNewEvents = false;
        }
    }

    private void TransitionSearchChanged(object sender, TextChangedEventArgs e)
        => RefreshTransitionBrowser();

    private void RefreshTransitionBrowser()
    {
        if (TransitionSearch is null || TransitionPlugIns is null || EmptyTransitions is null) return;
        var query = TransitionSearch.Text.Trim();
        var selectedTab = TransitionCategories.SelectedItem as TabItem;
        var category = selectedTab?.Tag as string;
        var favorites = selectedTab == FavoriteTransitionsCategory;

        ListBoxItem? firstVisible = null;
        foreach (var item in TransitionPlugIns.Items.OfType<ListBoxItem>())
        {
            if (item.DataContext is not TransitionPluginInfo plugin) continue;
            var inCategory = favorites ? transitionPreferences.IsFavorite(plugin.DisplayName)
                : category is null || category == plugin.Category;
            var matches = TransitionMatches(plugin, query)
                || plugin.Presets.Any(preset => preset.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
            item.Visibility = inCategory && matches ? Visibility.Visible : Visibility.Collapsed;
            if (item.Visibility == Visibility.Visible) firstVisible ??= item;
        }

        if (TransitionPlugIns.SelectedItem is not ListBoxItem { Visibility: Visibility.Visible })
            TransitionPlugIns.SelectedItem = firstVisible;
        else
            PopulatePresetsForSelectedPlugin();

        EmptyTransitions.Text = favorites && query.Length == 0
            ? "No favorite transitions. Use the star beside a transition to add it."
            : "No matching transitions.";
        EmptyTransitions.Visibility = firstVisible is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool TransitionMatches(TransitionPluginInfo plugin, string query) =>
        plugin.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || plugin.Category.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void PopulatePresetsForSelectedPlugin()
    {
        if (TransitionPresets is null || DefaultDissolvePreset is null) return;
        var previous = (TransitionPresets.SelectedItem as ListBoxItem)?.DataContext as TransitionPresetInfo;
        TransitionPresets.Items.Clear();
        if (TransitionPlugIns.SelectedItem is not ListBoxItem { Visibility: Visibility.Visible, DataContext: TransitionPluginInfo plugin })
            return;

        var query = TransitionSearch.Text.Trim();
        var showAllPresets = TransitionMatches(plugin, query);
        foreach (var preset in plugin.Presets)
        {
            if (!showAllPresets && !preset.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            var item = plugin.Kind == VideoTransitionKind.Dissolve && preset == plugin.Presets[0]
                ? DefaultDissolvePreset
                : new ListBoxItem
                {
                    Padding = new Thickness(8, 5, 8, 5),
                    Content = preset.Name,
                    ToolTip = "Drag to a video overlap or apply to the selected events"
                };
            item.DataContext = preset;
            TransitionPresets.Items.Add(item);
            if (preset == previous) item.IsSelected = true;
        }
        if (TransitionPresets.SelectedItem is null) TransitionPresets.SelectedIndex = 0;
    }

    private void TransitionPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ApplyTransitionButton is not null)
            ApplyTransitionButton.IsEnabled = TransitionPresets.SelectedItem is ListBoxItem { DataContext: TransitionPresetInfo };
    }

    private static VideoTransition MakeTransition(TransitionPresetInfo preset) => new()
    {
        Kind = preset.Kind,
        Preset = preset.Name,
        Parameters = preset.Parameters.ToDictionary(parameter => parameter.Key, parameter => parameter.Value)
    };

    private void ApplyTransitionClick(object sender, RoutedEventArgs e) => ApplyTransition(TransitionPresets.SelectedItem);

    private void ApplyTransition(object? item)
    {
        if (item is not ListBoxItem { DataContext: TransitionPresetInfo preset }) return;
        Guard(() =>
        {
            if (vm.Selection.Count == 0)
            {
                vm.Status = "Select a video event to apply the transition.";
                return;
            }
            Pause();
            var settings = EditingPreferencesStore.Load(EditingPreferencesPath);
            var transition = item == DefaultDissolvePreset ? null : MakeTransition(preset);
            var count = vm.Editor.ApplyDissolveToSelection(vm.Selection, settings.OverlapFrames(vm.Project.FrameRate), vm.Ripple, transition);
            vm.Status = count == 0 ? "No adjacent video overlap found for the selection."
                : count == 1 ? $"Applied {preset.Name} to one overlap." : $"Applied {preset.Name} to {count} overlaps.";
        });
    }

    private ListBoxItem? TransitionPresetItem(object source) => source is DependencyObject element
        ? ItemsControl.ContainerFromElement(TransitionPresets, element) as ListBoxItem
        : null;

    private void TransitionPresetDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || TransitionPresetItem(e.OriginalSource) is not { } item) return;
        ApplyTransition(item);
        e.Handled = true;
    }

    private void TransitionPresetKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        if (!e.IsRepeat) ApplyTransition(TransitionPresets.SelectedItem);
        e.Handled = true;
    }

    private void TransitionPresetMouseDown(object sender, MouseButtonEventArgs e)
    {
        transitionDragItem = TransitionPresetItem(e.OriginalSource);
        transitionDragStart = e.GetPosition(TransitionPresets);
    }

    private void TransitionPresetMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed && e.RightButton != MouseButtonState.Pressed)
        {
            transitionDragItem = null;
            return;
        }
        if (transitionDragItem is not { DataContext: TransitionPresetInfo preset } item) return;
        var offset = e.GetPosition(TransitionPresets) - transitionDragStart;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        transitionDragItem = null;
        var transition = item == DefaultDissolvePreset
            ? new VideoTransition { Kind = VideoTransitionKind.Dissolve, Preset = "Default" }
            : MakeTransition(preset);
        DragDrop.DoDragDrop(item, new DataObject(TimelineView.TransitionDataFormat, transition), DragDropEffects.Copy);
    }
}
