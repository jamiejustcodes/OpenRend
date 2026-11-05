using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace OpenRend.App;

public partial class MainWindow
{
    private string activeLayoutName = "Default Layout";
    private bool isAltDChordPending;
    private DispatcherTimer? layoutChordTimer;

    public string ActiveLayoutName
    {
        get => activeLayoutName;
        set => activeLayoutName = value;
    }

    private void InitializeLayoutManager()
    {
        layoutChordTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.0)
        };
        layoutChordTimer.Tick += (_, _) =>
        {
            isAltDChordPending = false;
            layoutChordTimer.Stop();
            vm.Status = "Ready";
        };

        UpdateLayoutSlotsMenu();
    }

    public void ApplyWorkspaceLayout(WorkspaceLayout layout)
    {
        // 1. Arrange timeline position (top vs bottom)
        if (layout.TimelineOnTop)
        {
            Grid.SetRow(Workspace, 4);
            Grid.SetRow(TimelineAreaGrid, 2);
            WorkspaceHeight.Height = new GridLength(1, GridUnitType.Star);
            TimelineRowHeight.Height = new GridLength(Math.Clamp(layout.WorkspaceHeight, 230, Math.Max(230, ActualHeight - 260)));
        }
        else
        {
            Grid.SetRow(Workspace, 2);
            Grid.SetRow(TimelineAreaGrid, 4);
            WorkspaceHeight.Height = new GridLength(Math.Clamp(layout.WorkspaceHeight, 230, Math.Max(230, ActualHeight - 260)));
            TimelineRowHeight.Height = new GridLength(1, GridUnitType.Star);
        }

        // 2. Panel split widths
        if (layout.MediaWidth > 50)
        {
            MediaWidth.Width = new GridLength(layout.MediaWidth);
        }
        if (layout.PreviewWidth > 50)
        {
            PreviewWidth.Width = new GridLength(Math.Clamp(layout.PreviewWidth, 320, Math.Max(320, ActualWidth - 420)));
        }

        // 3. Tab selections
        if (layout.ToolsTabIndex >= 0 && layout.ToolsTabIndex < ToolsTabs.Items.Count)
        {
            ToolsTabs.SelectedIndex = layout.ToolsTabIndex;
        }
        if (layout.PreviewDeckTabIndex >= 0 && layout.PreviewDeckTabIndex < PreviewDeckTabs.Items.Count)
        {
            PreviewDeckTabs.SelectedIndex = layout.PreviewDeckTabIndex;
        }

        // 4. Auxiliary floating/dock windows
        if (layout.MixingConsoleOpen)
        {
            MixingConsoleClick(this, new RoutedEventArgs());
        }
        else if (mixingConsoleWindow != null && mixingConsoleWindow.IsLoaded)
        {
            mixingConsoleWindow.Close();
        }

        if (layout.VideoScopesOpen)
        {
            VideoScopesClick(this, new RoutedEventArgs());
        }
        else if (videoScopesWindow != null && videoScopesWindow.IsLoaded)
        {
            videoScopesWindow.Close();
        }

        if (layout.EditDetailsOpen)
        {
            EditDetailsClick(this, new RoutedEventArgs());
        }
        else if (editDetailsWindow != null && editDetailsWindow.IsLoaded)
        {
            editDetailsWindow.Close();
        }

        activeLayoutName = layout.Name;
        vm.Status = $"Loaded window layout: {layout.Name}";
        UpdateLayoutSlotsMenu();
        Refresh();
        RequestPreview();
    }

    public WorkspaceLayout CaptureCurrentWorkspaceLayout(string name = "Custom Layout", int? shortcutSlot = null)
    {
        bool timelineOnTop = Grid.GetRow(TimelineAreaGrid) == 2;
        double wsHeight = timelineOnTop ? TimelineRowHeight.Height.Value : WorkspaceHeight.Height.Value;
        return new WorkspaceLayout
        {
            Name = name,
            WorkspaceHeight = wsHeight > 50 ? wsHeight : 520,
            MediaWidth = MediaWidth.Width.Value > 50 ? MediaWidth.Width.Value : 360,
            PreviewWidth = PreviewWidth.Width.Value > 50 ? PreviewWidth.Width.Value : 980,
            ToolsTabIndex = ToolsTabs.SelectedIndex,
            PreviewDeckTabIndex = PreviewDeckTabs.SelectedIndex,
            TimelineOnTop = timelineOnTop,
            MixingConsoleOpen = mixingConsoleWindow != null && mixingConsoleWindow.IsLoaded && mixingConsoleWindow.IsVisible,
            VideoScopesOpen = videoScopesWindow != null && videoScopesWindow.IsLoaded && videoScopesWindow.IsVisible,
            EditDetailsOpen = editDetailsWindow != null && editDetailsWindow.IsLoaded && editDetailsWindow.IsVisible,
            ShortcutSlot = shortcutSlot
        };
    }

    public void UpdateLayoutSlotsMenu()
    {
        var layouts = WorkspaceLayoutManager.LoadAllLayouts();
        var slotMap = new Dictionary<int, WorkspaceLayout>();
        foreach (var l in layouts)
        {
            if (l.ShortcutSlot.HasValue && l.ShortcutSlot.Value >= 0 && l.ShortcutSlot.Value <= 9)
            {
                slotMap[l.ShortcutSlot.Value] = l;
            }
        }

        MenuItem?[] slotMenus = [
            LayoutSlot0Menu, LayoutSlot1Menu, LayoutSlot2Menu, LayoutSlot3Menu, LayoutSlot4Menu,
            LayoutSlot5Menu, LayoutSlot6Menu, LayoutSlot7Menu, LayoutSlot8Menu, LayoutSlot9Menu
        ];

        for (int i = 0; i <= 9; i++)
        {
            if (slotMenus[i] is MenuItem menu)
            {
                if (slotMap.TryGetValue(i, out var assignedLayout))
                {
                    menu.Header = $"Layout &{i} {assignedLayout.Name}";
                    menu.ToolTip = $"Loads {assignedLayout.Name} (Alt+D, {i})";
                }
                else
                {
                    menu.Header = $"Layout &{i} <EMPTY>";
                    menu.ToolTip = $"Slot {i} is unassigned. Save a layout with shortcut slot {i} to populate.";
                }
            }
        }
    }

    public void ActivateLayoutSlot(int slotIndex)
    {
        var layout = WorkspaceLayoutManager.GetLayoutBySlot(slotIndex);
        if (layout != null)
        {
            ApplyWorkspaceLayout(layout);
        }
        else
        {
            vm.Status = $"Layout slot {slotIndex} is empty. Use 'Save Layout As...' to assign a layout to this slot.";
        }
    }

    private void OrganizeLayoutsClick(object sender, RoutedEventArgs e)
    {
        new OrganizeLayoutsWindow(
            layout => ApplyWorkspaceLayout(layout),
            () => UpdateLayoutSlotsMenu(),
            this
        ).ShowDialog();
    }

    private void SaveLayoutClick(object sender, RoutedEventArgs e)
    {
        var builtIns = WorkspaceLayoutManager.CreateBuiltInLayouts();
        if (!string.IsNullOrWhiteSpace(activeLayoutName) &&
            !builtIns.Any(b => string.Equals(b.Name, activeLayoutName, StringComparison.OrdinalIgnoreCase)))
        {
            var current = CaptureCurrentWorkspaceLayout(activeLayoutName);
            var existing = WorkspaceLayoutManager.LoadAllLayouts().FirstOrDefault(l => string.Equals(l.Name, activeLayoutName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) current.ShortcutSlot = existing.ShortcutSlot;
            WorkspaceLayoutManager.SaveLayout(current);
            vm.Status = $"Saved layout '{activeLayoutName}'.";
            UpdateLayoutSlotsMenu();
        }
        else
        {
            SaveLayoutAsClick(sender, e);
        }
    }

    private void SaveLayoutAsClick(object sender, RoutedEventArgs e)
    {
        var builtIns = WorkspaceLayoutManager.CreateBuiltInLayouts();
        var defaultName = string.IsNullOrWhiteSpace(activeLayoutName) ||
            builtIns.Any(b => string.Equals(b.Name, activeLayoutName, StringComparison.OrdinalIgnoreCase))
            ? "My Custom Layout" : activeLayoutName;

        var dialog = new SaveLayoutAsWindow(defaultName, null, WorkspaceLayoutManager.GetDefaultLayoutsDirectory(), this);
        if (dialog.ShowDialog() == true)
        {
            var layout = CaptureCurrentWorkspaceLayout(dialog.LayoutName, dialog.SelectedSlot);
            WorkspaceLayoutManager.SaveLayout(layout, dialog.LayoutFolder);
            activeLayoutName = layout.Name;
            vm.Status = $"Saved layout '{layout.Name}'.";
            UpdateLayoutSlotsMenu();
        }
    }

    private void ReloadLayoutClick(object sender, RoutedEventArgs e)
    {
        var all = WorkspaceLayoutManager.LoadAllLayouts();
        var target = all.FirstOrDefault(l => string.Equals(l.Name, activeLayoutName, StringComparison.OrdinalIgnoreCase))
            ?? WorkspaceLayoutManager.CreateBuiltInLayouts()[0];
        ApplyWorkspaceLayout(target);
        vm.Status = $"Reloaded layout '{target.Name}'.";
    }

    private void DefaultLayoutClick(object sender, RoutedEventArgs e)
    {
        ApplyWorkspaceLayout(WorkspaceLayoutManager.CreateBuiltInLayouts()[0]);
    }

    private void AudioMixingLayoutClick(object sender, RoutedEventArgs e)
    {
        ApplyWorkspaceLayout(WorkspaceLayoutManager.CreateBuiltInLayouts()[1]);
    }

    private void ColorCorrectionLayoutClick(object sender, RoutedEventArgs e)
    {
        ApplyWorkspaceLayout(WorkspaceLayoutManager.CreateBuiltInLayouts()[2]);
    }

    private void SourceWindowLayoutClick(object sender, RoutedEventArgs e)
    {
        ApplyWorkspaceLayout(WorkspaceLayoutManager.CreateBuiltInLayouts()[3]);
    }

    private void TimelineTopLayoutClick(object sender, RoutedEventArgs e)
    {
        ApplyWorkspaceLayout(WorkspaceLayoutManager.CreateBuiltInLayouts()[4]);
    }

    private void LayoutSlotClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && int.TryParse(item.Tag?.ToString(), out var slot))
        {
            ActivateLayoutSlot(slot);
        }
    }

    private bool HandleLayoutChordKey(KeyEventArgs e)
    {
        if (!isAltDChordPending) return false;

        isAltDChordPending = false;
        layoutChordTimer?.Stop();

        switch (e.Key)
        {
            case Key.D:
                DefaultLayoutClick(this, e);
                e.Handled = true;
                return true;
            case Key.A:
                AudioMixingLayoutClick(this, e);
                e.Handled = true;
                return true;
            case Key.C:
                ColorCorrectionLayoutClick(this, e);
                e.Handled = true;
                return true;
            case Key.S:
                SourceWindowLayoutClick(this, e);
                e.Handled = true;
                return true;
            case Key.T:
                TimelineTopLayoutClick(this, e);
                e.Handled = true;
                return true;
            case Key.R:
                ReloadLayoutClick(this, e);
                e.Handled = true;
                return true;
            case Key.W:
                SaveLayoutClick(this, e);
                e.Handled = true;
                return true;
            case Key.D0 or Key.NumPad0:
                ActivateLayoutSlot(0);
                e.Handled = true;
                return true;
            case Key.D1 or Key.NumPad1:
                ActivateLayoutSlot(1);
                e.Handled = true;
                return true;
            case Key.D2 or Key.NumPad2:
                ActivateLayoutSlot(2);
                e.Handled = true;
                return true;
            case Key.D3 or Key.NumPad3:
                ActivateLayoutSlot(3);
                e.Handled = true;
                return true;
            case Key.D4 or Key.NumPad4:
                ActivateLayoutSlot(4);
                e.Handled = true;
                return true;
            case Key.D5 or Key.NumPad5:
                ActivateLayoutSlot(5);
                e.Handled = true;
                return true;
            case Key.D6 or Key.NumPad6:
                ActivateLayoutSlot(6);
                e.Handled = true;
                return true;
            case Key.D7 or Key.NumPad7:
                ActivateLayoutSlot(7);
                e.Handled = true;
                return true;
            case Key.D8 or Key.NumPad8:
                ActivateLayoutSlot(8);
                e.Handled = true;
                return true;
            case Key.D9 or Key.NumPad9:
                ActivateLayoutSlot(9);
                e.Handled = true;
                return true;
            default:
                vm.Status = "Layout shortcut cancelled.";
                return false;
        }
    }
}

