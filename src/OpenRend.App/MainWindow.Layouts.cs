using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace OpenRend.App;

public partial class MainWindow
{
    private string activeLayoutName = "Default Layout";
    private bool isAltDChordPending;
    private DispatcherTimer? layoutChordTimer;
    private double sourceDockWidth = 568;
    private double sourcePreviewWidth = 565;
    private GridLength normalMasterBusWidth = new(80);
    private double upperDockShare = .35;

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
        var sourceDock = layout.Trimmer?.SeparateDock == true;
        var defaultLayout = layout.IsBuiltIn && layout.Name == "Default Layout";
        ConfigureSourceWorkspace(sourceDock, layout.PreviewWidth);
        RestoreUpperDockShare(layout.UpperDockShare);
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
            TimelineRowHeight.Height = new GridLength(1, GridUnitType.Star);
            if (defaultLayout)
                SetWorkspaceShare(595.0 / 895);
            else
                WorkspaceHeight.Height = new GridLength(Math.Max(280, layout.WorkspaceHeight));
        }

        // 2. Panel split widths
        if (defaultLayout)
        {
            MediaWidth.Width = new GridLength(0.65, GridUnitType.Star);
            PreviewWidth.Width = new GridLength(1.85, GridUnitType.Star);
        }
        else if (!sourceDock && layout.MediaWidth > 50)
        {
            MediaWidth.Width = new GridLength(layout.MediaWidth);
        }
        if (!defaultLayout && !sourceDock && layout.PreviewWidth > 50)
        {
            PreviewWidth.Width = new GridLength(Math.Clamp(layout.PreviewWidth, 320, Math.Max(320, ActualWidth - 420)));
        }

        RestoreTrimmerPane(layout.Trimmer);
        RestoreWorkspacePanes(layout.Panes);
        ApplyLayoutConstraints();

        // 3. Tab selections
        SelectWorkspaceTab(ToolsTabs, layout.ToolsTabIndex);
        SelectWorkspaceTab(PreviewDeckTabs, layout.PreviewDeckTabIndex);
        SelectWorkspaceTab(UpperDockTabs, layout.UpperDockTabIndex);

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

    private static void SelectWorkspaceTab(TabControl dock, int? selectedIndex)
    {
        if (selectedIndex is int index && index >= 0 && index < dock.Items.Count &&
            dock.Items[index] is TabItem { Visibility: Visibility.Visible } tab)
            dock.SelectedItem = tab;
    }

    public WorkspaceLayout CaptureCurrentWorkspaceLayout(string name = "Custom Layout", int? shortcutSlot = null)
    {
        bool timelineOnTop = Grid.GetRow(TimelineAreaGrid) == 2;
        double wsHeight = timelineOnTop ? TimelineRowHeight.Height.Value :
            WorkspaceHeight.Height.IsStar ? WorkspaceHeight.ActualHeight : WorkspaceHeight.Height.Value;
        return new WorkspaceLayout
        {
            Name = name,
            WorkspaceHeight = wsHeight > 50 ? wsHeight : 520,
            MediaWidth = MediaWidth.Width.Value > 50 ? MediaWidth.Width.Value : 360,
            PreviewWidth = HasSeparateTrimmerDock ? sourcePreviewWidth : PreviewWidth.Width.Value > 50 ? PreviewWidth.Width.Value : 980,
            ToolsTabIndex = ToolsTabs.SelectedIndex,
            PreviewDeckTabIndex = PreviewDeckTabs.SelectedIndex,
            UpperDockTabIndex = UpperDockTabs.SelectedIndex,
            UpperDockShare = upperDockShare,
            TimelineOnTop = timelineOnTop,
            MixingConsoleOpen = mixingConsoleWindow != null && mixingConsoleWindow.IsLoaded && mixingConsoleWindow.IsVisible,
            VideoScopesOpen = videoScopesWindow != null && videoScopesWindow.IsLoaded && videoScopesWindow.IsVisible,
            EditDetailsOpen = editDetailsWindow != null && editDetailsWindow.IsLoaded && editDetailsWindow.IsVisible,
            Trimmer = CaptureTrimmerPane(),
            Panes = CaptureWorkspacePanes(),
            ShortcutSlot = shortcutSlot
        };
    }

    private void RestoreUpperDockShare(double? savedShare)
    {
        upperDockShare = savedShare is double value && double.IsFinite(value) ? Math.Clamp(value, .1, .9) : .35;
        UpdateUpperDockRows();
    }

    private void UpdateUpperDockRows()
    {
        var visible = UpperDockTabs.Items.OfType<TabItem>().Any(tab => tab.Visibility == Visibility.Visible);
        UpperDockTabs.Visibility = UpperDockSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        UpperDockRow.MinHeight = LowerDockRow.MinHeight = visible ? 100 : 0;
        UpperDockRow.Height = visible ? new GridLength(upperDockShare, GridUnitType.Star) : new GridLength(0);
        UpperDockSplitterRow.Height = new GridLength(visible ? UpperDockSplitter.Height : 0);
        LowerDockRow.Height = new GridLength(visible ? 1 - upperDockShare : 1, GridUnitType.Star);
    }

    private void UpperDockSplitterCompleted(object sender, DragCompletedEventArgs e)
    {
        var total = UpperDockRow.Height.Value + LowerDockRow.Height.Value;
        if (!e.Canceled && total > 0)
            RestoreUpperDockShare(UpperDockRow.Height.Value / total);
    }

    private void ConfigureSourceWorkspace(bool enabled, double previewWidth)
    {
        if (!enabled && !HasSeparateTrimmerDock)
            return;
        if (enabled && !HasSeparateTrimmerDock)
            normalMasterBusWidth = MasterBusWidth.Width;
        TimelineRowHeight.MinHeight = enabled ? 80 : 60;
        MediaWidth.MinWidth = enabled ? 0 : 220;
        MediaWidth.Width = enabled ? new GridLength(1, GridUnitType.Star) : new GridLength(0.65, GridUnitType.Star);
        MasterBusWidth.MaxWidth = enabled ? 120 : 110;
        MasterBusWidth.Width = enabled ? new GridLength(120) : normalMasterBusWidth;
        if (enabled)
            sourcePreviewWidth = double.IsFinite(previewWidth) && previewWidth >= 240 ? previewWidth : 565;
    }

    private void UpdateSourceDockColumns()
    {
        var visible = HasSeparateTrimmerDock && TrimmerTab.Visibility == Visibility.Visible;
        SourceDeckTabs.Visibility = SourceSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SourceSplitterWidth.Width = new GridLength(visible ? 5 : 0);
        SourceWidth.MinWidth = visible ? 240 : 0;
        ProgramWidth.MinWidth = visible ? 240 : 0;
        if (!HasSeparateTrimmerDock)
        {
            SourceWidth.Width = new GridLength(0);
            return;
        }

        // Keep the preview width first and give unused space to the tools deck.
        var available = Math.Max(0, ActualWidth - RootGrid.Margin.Left - RootGrid.Margin.Right - 8 -
            MasterBusWidth.Width.Value - (visible ? 15 : 10));
        var preview = Math.Min(sourcePreviewWidth, Math.Max(240, available - (visible ? 240 : 0)));
        var source = visible ? Math.Min(sourceDockWidth, Math.Max(240, available - preview)) : 0;
        PreviewWidth.Width = new GridLength(preview + source + SourceSplitterWidth.Width.Value);
        SourceWidth.Width = new GridLength(source);
    }

    private void SourceSplitterCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!HasSeparateTrimmerDock || e.Canceled)
            return;
        if (sender == SourceSplitter)
        {
            sourceDockWidth = SourceWidth.Width.Value;
            sourcePreviewWidth = PreviewDeckTabs.ActualWidth;
        }
        else if (SourceDeckTabs.Visibility == Visibility.Visible)
            sourceDockWidth = Math.Max(240, PreviewWidth.Width.Value - sourcePreviewWidth - 5);
        else
            sourcePreviewWidth = PreviewWidth.Width.Value;
        MediaWidth.Width = new GridLength(1, GridUnitType.Star);
        UpdateSourceDockColumns();
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
                    var layoutName = assignedLayout.Name.Replace("_", "__");
                    menu.Header = $"Layout _{i} · {layoutName}";
                    menu.ToolTip = $"Loads {assignedLayout.Name} (Alt+D, {i})";
                }
                else
                {
                    menu.Header = $"Layout _{i} (empty)";
                    menu.ToolTip = $"Save a layout to slot {i} to use this shortcut.";
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

