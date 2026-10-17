using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.UI;

namespace OpenRend.App;

public partial class MainWindow
{
    private Point transitionDragStart;
    private bool transitionDragReady;
    private bool transitionBrowserLoaded;
    private bool transitionFavoriteValue;
    private string EditingPreferencesPath => Path.Combine(Path.GetDirectoryName(recoveryPath)!, "editing.json");
    private string TransitionPreferencesPath => Path.Combine(Path.GetDirectoryName(recoveryPath)!, "transitions.json");

    private void InitializeTransitionBrowser()
    {
        try { transitionFavoriteValue = TransitionBrowserPreferencesStore.Load(TransitionPreferencesPath).FavoriteDissolve; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { vm.Status = "Transition favorites could not be loaded: " + error.Message; }
        FavoriteDissolve.IsChecked = transitionFavoriteValue;
        transitionBrowserLoaded = true;
        RefreshTransitionBrowser();
    }

    private void TransitionFavoriteChanged(object sender, RoutedEventArgs e)
    {
        if (!transitionBrowserLoaded) return;
        try
        {
            TransitionBrowserPreferencesStore.Save(new() { FavoriteDissolve = FavoriteDissolve.IsChecked == true }, TransitionPreferencesPath);
            transitionFavoriteValue = FavoriteDissolve.IsChecked == true;
        }
        catch (Exception error)
        {
            transitionBrowserLoaded = false;
            FavoriteDissolve.IsChecked = transitionFavoriteValue;
            transitionBrowserLoaded = true;
            ShowError(error);
        }
        RefreshTransitionBrowser();
    }

    private void TransitionCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, TransitionCategories)) RefreshTransitionBrowser();
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
        var favorite = FavoriteDissolve.IsChecked == true;
        FavoriteDissolve.Content = favorite ? "★" : "☆";
        var visible = (FavoriteTransitionsCategory?.IsSelected != true || favorite) &&
            (query.Length == 0 || "Dissolve Default Fades".Contains(query, StringComparison.OrdinalIgnoreCase));
        DissolvePluginItem.Visibility = DefaultDissolvePreset.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        EmptyTransitions.Text = FavoriteTransitionsCategory?.IsSelected == true && !favorite
            ? "No favorite transitions." : "No transitions match your search.";
        EmptyTransitions.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
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
        if (e.Key == Key.Enter && DefaultDissolvePreset.Visibility == Visibility.Visible && TransitionPresets.SelectedItem is not null)
            ApplyDefaultDissolveClick(sender, e);
    }

    private void TransitionPresetMouseDown(object sender, MouseButtonEventArgs e)
    {
        transitionDragReady = !IsTransitionAddButton(e.OriginalSource);
        transitionDragStart = e.GetPosition(DefaultDissolvePreset);
        if (transitionDragReady) DefaultDissolvePreset.IsSelected = true;
    }

    private void TransitionPresetMouseMove(object sender, MouseEventArgs e)
    {
        // The list owns selection capture after the pointer leaves the thumbnail.
        if (e.LeftButton != MouseButtonState.Pressed && e.RightButton != MouseButtonState.Pressed)
        { transitionDragReady = false; return; }
        if (!transitionDragReady) return;
        var offset = e.GetPosition(DefaultDissolvePreset) - transitionDragStart;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        // A click selects the preset; only a real drag transfers an independent copy.
        transitionDragReady = false;
        DragDrop.DoDragDrop(DefaultDissolvePreset,
            new DataObject(TimelineView.TransitionDataFormat, new VideoTransition()), DragDropEffects.Copy);
    }
}
