using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;
using OpenRend.UI;

namespace OpenRend.App;

public partial class MainWindow
{
    private Point transitionDragStart;
    private bool transitionDragReady;

    private void TransitionSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (DissolvePluginItem is null || DefaultDissolvePreset is null) return;
        var query = TransitionSearch.Text.Trim();
        var visible = query.Length == 0 || "Dissolve".Contains(query, StringComparison.OrdinalIgnoreCase) ||
            "Default".Contains(query, StringComparison.OrdinalIgnoreCase);
        DissolvePluginItem.Visibility = DefaultDissolvePreset.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyDefaultDissolveClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Guard(() =>
        {
            if (vm.Selection.Count == 0) { vm.Status = "Select a video event to apply Dissolve."; return; }
            Pause();
            var count = vm.Editor.ApplyDissolveToSelection(vm.Selection);
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
        if (e.Key == Key.Enter && TransitionPresets.SelectedItem is not null)
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
