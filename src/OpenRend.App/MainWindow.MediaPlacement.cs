using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenRend.App;

public partial class MainWindow
{
    private (ListBox List, Point Origin, object[]? Selected)? mediaDrag;

    private void MediaDragMouseDown(object sender, MouseButtonEventArgs e)
    {
        mediaDrag = null;
        if (sender is not ListBox list || e.OriginalSource is not DependencyObject origin ||
            ItemsControl.ContainerFromElement(list, origin) is not ListBoxItem item) return;

        // Preserve a batch when the user starts a drag on one of its selected rows.
        var selected = item.IsSelected && Keyboard.Modifiers == ModifierKeys.None && list.SelectedItems.Count > 1
            ? list.SelectedItems.Cast<object>().ToArray() : null;
        mediaDrag = (list, e.GetPosition(list), selected);
    }

    private void MediaMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox list || DragSelection(list, e) is not { } selected) return;
        var ids = selected.OfType<MediaItem>().Select(item => item.Asset.Id).ToArray();
        if (ids.Length > 0)
            DragDrop.DoDragDrop(list, new DataObject("OpenRend.Media", ids), DragDropEffects.Copy);
    }

    private void ExplorerMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox list || DragSelection(list, e) is not { } selected) return;
        var files = selected.OfType<FileInfo>().Select(file => file.FullName).ToArray();
        if (files.Length > 0)
            DragDrop.DoDragDrop(list, new DataObject(DataFormats.FileDrop, files), DragDropEffects.Copy);
    }

    private object[]? DragSelection(ListBox list, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { mediaDrag = null; return null; }
        if (mediaDrag is not { } drag || drag.List != list) return null;
        var travel = e.GetPosition(list) - drag.Origin;
        if (Math.Abs(travel.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(travel.Y) < SystemParameters.MinimumVerticalDragDistance) return null;

        mediaDrag = null;
        if (drag.Selected is { } selected && selected.Any(item => !list.SelectedItems.Contains(item)))
        {
            list.SelectedItems.Clear();
            foreach (var item in selected)
                if (list.Items.Contains(item)) list.SelectedItems.Add(item);
        }
        return list.Items.Cast<object>().Where(list.SelectedItems.Contains).ToArray();
    }
}
