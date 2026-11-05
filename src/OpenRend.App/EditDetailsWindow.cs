using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;
using OpenRend.UI;

namespace OpenRend.App;

/// <summary>
/// Vegas Pro 1:1 Edit Details spreadsheet window (Ctrl+Alt+4 / Alt+4).
/// Provides tabular EDL inspection and editing across timeline events and markers,
/// supporting search filtering, track filtering, bidirectional timeline navigation,
/// and tab-separated clipboard export for spreadsheet workflows.
/// </summary>
public sealed class EditDetailsWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly Action onProjectChanged;

    private readonly ComboBox showCombo;
    private readonly CheckBox selectedTrackOnlyCheck;
    private readonly TextBox searchBox;
    private readonly Button copyButton;
    private readonly Button deleteButton;

    private readonly ListView eventsListView;
    private readonly ListView markersListView;
    private readonly ListView regionsListView;
    private readonly TextBlock summaryText;
    private readonly TextBlock statusText;

    private readonly List<EditDetailsEventRow> currentEventRows = [];
    private readonly List<EditDetailsMarkerRow> currentMarkerRows = [];
    private readonly List<EditDetailsRegionRow> currentRegionRows = [];

    public IReadOnlyList<EditDetailsEventRow> CurrentEventRows => currentEventRows;
    public IReadOnlyList<EditDetailsMarkerRow> CurrentMarkerRows => currentMarkerRows;
    public IReadOnlyList<EditDetailsRegionRow> CurrentRegionRows => currentRegionRows;
    public ComboBox ShowComboBox => showCombo;
    public CheckBox SelectedTrackOnlyCheckBox => selectedTrackOnlyCheck;
    public TextBox SearchTextBox => searchBox;
    public ListView EventsListView => eventsListView;
    public ListView MarkersListView => markersListView;
    public ListView RegionsListView => regionsListView;
    public TextBlock SummaryTextBlock => summaryText;
    public TextBlock StatusTextBlock => statusText;

    public EditDetailsWindow(EditorViewModel viewModel, Action projectChanged, Window? owner = null)
    {
        vm = viewModel;
        onProjectChanged = projectChanged;
        Owner = owner;

        Title = "Edit Details";
        Width = 860;
        Height = 480;
        MinWidth = 560;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var bgBrush = TryFindResource("PanelBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        var darkBrush = TryFindResource("HeaderBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));
        var textBrush = TryFindResource("TextBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC));
        var borderBrush = TryFindResource("BorderBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42));
        var activeBlueBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x62, 0x9A));

        Background = bgBrush;
        Foreground = textBrush;

        var root = new Grid { Margin = new Thickness(8) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Toolbar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Grid area
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status bar

        // 1. Top Toolbar (Show combo, track filter, search, copy, delete)
        var topBar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

        var rightStack = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(rightStack, Dock.Right);

        copyButton = new Button
        {
            Content = "Copy",
            Width = 64,
            Height = 24,
            Margin = new Thickness(4, 0, 2, 0),
            ToolTip = "Copy grid contents to clipboard as tab-separated values (Ctrl+C)"
        };
        copyButton.Click += (_, _) => CopyTableToClipboard();

        deleteButton = new Button
        {
            Content = "Delete",
            Width = 64,
            Height = 24,
            Margin = new Thickness(2, 0, 0, 0),
            ToolTip = "Delete selected events or markers from project"
        };
        deleteButton.Click += (_, _) => DeleteSelectedItems();

        rightStack.Children.Add(copyButton);
        rightStack.Children.Add(deleteButton);
        topBar.Children.Add(rightStack);

        var leftStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var showLabel = new TextBlock
        {
            Text = "Show:",
            Foreground = textBrush,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Margin = new Thickness(0, 0, 6, 0)
        };
        leftStack.Children.Add(showLabel);

        showCombo = new ComboBox
        {
            Width = 130,
            Height = 24,
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 11
        };
        showCombo.Items.Add(new ComboBoxItem { Content = "Events", IsSelected = true });
        showCombo.Items.Add(new ComboBoxItem { Content = "Markers" });
        showCombo.Items.Add(new ComboBoxItem { Content = "Regions" });
        showCombo.Items.Add(new ComboBoxItem { Content = "Selected Events" });
        showCombo.SelectionChanged += (_, _) => RefreshGrid();
        leftStack.Children.Add(showCombo);

        selectedTrackOnlyCheck = new CheckBox
        {
            Content = "Selected track only",
            Foreground = textBrush,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Margin = new Thickness(4, 0, 10, 0)
        };
        selectedTrackOnlyCheck.Checked += (_, _) => RefreshGrid();
        selectedTrackOnlyCheck.Unchecked += (_, _) => RefreshGrid();
        leftStack.Children.Add(selectedTrackOnlyCheck);

        var searchLabel = new TextBlock
        {
            Text = "Filter:",
            Foreground = textBrush,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Margin = new Thickness(4, 0, 4, 0)
        };
        leftStack.Children.Add(searchLabel);

        searchBox = new TextBox
        {
            Width = 140,
            Height = 22,
            FontSize = 11,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
            Foreground = textBrush,
            BorderBrush = borderBrush,
            ToolTip = "Filter rows by name, media path, or marker label"
        };
        searchBox.TextChanged += (_, _) => RefreshGrid();
        leftStack.Children.Add(searchBox);

        topBar.Children.Add(leftStack);
        Grid.SetRow(topBar, 0);
        root.Children.Add(topBar);

        // Grid Column Header Style
        var headerStyle = new Style(typeof(GridViewColumnHeader));
        headerStyle.Setters.Add(new Setter(GridViewColumnHeader.BackgroundProperty, darkBrush));
        headerStyle.Setters.Add(new Setter(GridViewColumnHeader.ForegroundProperty, textBrush));
        headerStyle.Setters.Add(new Setter(GridViewColumnHeader.BorderBrushProperty, borderBrush));
        headerStyle.Setters.Add(new Setter(GridViewColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        headerStyle.Setters.Add(new Setter(GridViewColumnHeader.PaddingProperty, new Thickness(6, 4, 6, 4)));
        headerStyle.Setters.Add(new Setter(GridViewColumnHeader.FontWeightProperty, FontWeights.SemiBold));
        headerStyle.Setters.Add(new Setter(GridViewColumnHeader.FontSizeProperty, 11.0));

        // ListView Item Style with selection and hover
        var itemStyle = new Style(typeof(ListViewItem));
        itemStyle.Setters.Add(new Setter(ListViewItem.ForegroundProperty, textBrush));
        itemStyle.Setters.Add(new Setter(ListViewItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(ListViewItem.PaddingProperty, new Thickness(4, 2, 4, 2)));

        var template = new ControlTemplate(typeof(ListViewItem));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "Bd";
        borderFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        borderFactory.SetValue(Border.PaddingProperty, new Thickness(2, 1, 2, 1));

        var presenterFactory = new FrameworkElementFactory(typeof(GridViewRowPresenter));
        borderFactory.AppendChild(presenterFactory);
        template.VisualTree = borderFactory;

        var mouseOverTrigger = new Trigger { Property = ListViewItem.IsMouseOverProperty, Value = true };
        mouseOverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)), "Bd"));
        template.Triggers.Add(mouseOverTrigger);

        var selectedTrigger = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, activeBlueBrush, "Bd"));
        selectedTrigger.Setters.Add(new Setter(ListViewItem.ForegroundProperty, Brushes.White));
        template.Triggers.Add(selectedTrigger);

        itemStyle.Setters.Add(new Setter(ListViewItem.TemplateProperty, template));

        // 2. Events ListView
        eventsListView = new ListView
        {
            Background = bgBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            SelectionMode = SelectionMode.Extended,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            ItemContainerStyle = itemStyle
        };

        var eventsGridView = new GridView { ColumnHeaderContainerStyle = headerStyle };
        eventsGridView.Columns.Add(new GridViewColumn { Header = "Track", Width = 90, DisplayMemberBinding = new Binding("TrackDisplay") });
        eventsGridView.Columns.Add(new GridViewColumn { Header = "Event", Width = 48, DisplayMemberBinding = new Binding("EventIndex") });
        eventsGridView.Columns.Add(new GridViewColumn { Header = "Start", Width = 90, DisplayMemberBinding = new Binding("StartTimecode") });
        eventsGridView.Columns.Add(new GridViewColumn { Header = "End", Width = 90, DisplayMemberBinding = new Binding("EndTimecode") });
        eventsGridView.Columns.Add(new GridViewColumn { Header = "Length", Width = 90, DisplayMemberBinding = new Binding("LengthTimecode") });
        eventsGridView.Columns.Add(new GridViewColumn { Header = "Name", Width = 140, DisplayMemberBinding = new Binding("Name") });
        eventsGridView.Columns.Add(new GridViewColumn { Header = "Active Take", Width = 100, DisplayMemberBinding = new Binding("ActiveTake") });
        eventsGridView.Columns.Add(new GridViewColumn { Header = "Source In", Width = 90, DisplayMemberBinding = new Binding("SourceInTimecode") });
        eventsGridView.Columns.Add(new GridViewColumn { Header = "Rate", Width = 55, DisplayMemberBinding = new Binding("PlaybackRateText") });
        eventsGridView.Columns.Add(new GridViewColumn { Header = "Mute", Width = 50, DisplayMemberBinding = new Binding("MuteText") });
        eventsGridView.Columns.Add(new GridViewColumn { Header = "Lock", Width = 50, DisplayMemberBinding = new Binding("LockText") });
        eventsGridView.Columns.Add(new GridViewColumn { Header = "Media File", Width = 220, DisplayMemberBinding = new Binding("MediaFile") });
        eventsGridView.Columns.Add(new GridViewColumn { Header = "Snap Offset", Width = 90, DisplayMemberBinding = new Binding("SnapOffsetTimecode") });
        eventsListView.View = eventsGridView;

        eventsListView.MouseDoubleClick += EventsListView_MouseDoubleClick;
        eventsListView.SelectionChanged += EventsListView_SelectionChanged;
        eventsListView.ContextMenu = CreateEventsContextMenu();

        // 3. Markers ListView
        markersListView = new ListView
        {
            Background = bgBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            SelectionMode = SelectionMode.Extended,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            ItemContainerStyle = itemStyle,
            Visibility = Visibility.Collapsed
        };

        var markersGridView = new GridView { ColumnHeaderContainerStyle = headerStyle };
        markersGridView.Columns.Add(new GridViewColumn { Header = "Marker", Width = 60, DisplayMemberBinding = new Binding("Index") });
        markersGridView.Columns.Add(new GridViewColumn { Header = "Position", Width = 100, DisplayMemberBinding = new Binding("Timecode") });
        markersGridView.Columns.Add(new GridViewColumn { Header = "Frame", Width = 80, DisplayMemberBinding = new Binding("Frame") });
        markersGridView.Columns.Add(new GridViewColumn { Header = "Name", Width = 280, DisplayMemberBinding = new Binding("Label") });
        markersGridView.Columns.Add(new GridViewColumn { Header = "Color", Width = 90, DisplayMemberBinding = new Binding("ColorName") });
        markersListView.View = markersGridView;

        markersListView.MouseDoubleClick += MarkersListView_MouseDoubleClick;
        markersListView.SelectionChanged += MarkersListView_SelectionChanged;
        markersListView.ContextMenu = CreateMarkersContextMenu();

        // 3b. Regions ListView
        regionsListView = new ListView
        {
            Background = bgBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            SelectionMode = SelectionMode.Extended,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            ItemContainerStyle = itemStyle,
            Visibility = Visibility.Collapsed
        };

        var regionsGridView = new GridView { ColumnHeaderContainerStyle = headerStyle };
        regionsGridView.Columns.Add(new GridViewColumn { Header = "Region", Width = 60, DisplayMemberBinding = new Binding("Index") });
        regionsGridView.Columns.Add(new GridViewColumn { Header = "Position", Width = 100, DisplayMemberBinding = new Binding("StartTimecode") });
        regionsGridView.Columns.Add(new GridViewColumn { Header = "End", Width = 100, DisplayMemberBinding = new Binding("EndTimecode") });
        regionsGridView.Columns.Add(new GridViewColumn { Header = "Length", Width = 100, DisplayMemberBinding = new Binding("LengthTimecode") });
        regionsGridView.Columns.Add(new GridViewColumn { Header = "Name", Width = 240, DisplayMemberBinding = new Binding("Label") });
        regionsGridView.Columns.Add(new GridViewColumn { Header = "Color", Width = 90, DisplayMemberBinding = new Binding("ColorName") });
        regionsListView.View = regionsGridView;

        regionsListView.MouseDoubleClick += RegionsListView_MouseDoubleClick;
        regionsListView.SelectionChanged += RegionsListView_SelectionChanged;
        regionsListView.ContextMenu = CreateRegionsContextMenu();

        var gridContainer = new Grid();
        gridContainer.Children.Add(eventsListView);
        gridContainer.Children.Add(markersListView);
        gridContainer.Children.Add(regionsListView);
        Grid.SetRow(gridContainer, 1);
        root.Children.Add(gridContainer);

        // 4. Status Bar
        var statusBar = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        summaryText = new TextBlock
        {
            Text = "0 items",
            Foreground = textBrush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(summaryText, Dock.Left);

        statusText = new TextBlock
        {
            Text = "Ready",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(statusText, Dock.Right);

        statusBar.Children.Add(summaryText);
        statusBar.Children.Add(statusText);
        Grid.SetRow(statusBar, 2);
        root.Children.Add(statusBar);

        Content = root;

        KeyDown += EditDetailsWindow_KeyDown;
        RefreshGrid();
    }

    private void EditDetailsWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            CopyTableToClipboard(selectedOnly: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteSelectedItems();
            e.Handled = true;
        }
    }

    private ContextMenu CreateEventsContextMenu()
    {
        var menu = new ContextMenu();
        var goTo = new MenuItem { Header = "Go to Event" };
        goTo.Click += (_, _) => NavigateSelectedEvent();
        menu.Items.Add(goTo);

        var selectTimeline = new MenuItem { Header = "Select on Timeline" };
        selectTimeline.Click += (_, _) => SelectSelectedEventOnTimeline();
        menu.Items.Add(selectTimeline);

        menu.Items.Add(new Separator());

        var copySelected = new MenuItem { Header = "Copy Selected Rows" };
        copySelected.Click += (_, _) => CopyTableToClipboard(selectedOnly: true);
        menu.Items.Add(copySelected);

        var copyAll = new MenuItem { Header = "Copy All Rows" };
        copyAll.Click += (_, _) => CopyTableToClipboard(selectedOnly: false);
        menu.Items.Add(copyAll);

        menu.Items.Add(new Separator());

        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) => DeleteSelectedItems();
        menu.Items.Add(deleteItem);

        return menu;
    }

    private ContextMenu CreateMarkersContextMenu()
    {
        var menu = new ContextMenu();
        var goTo = new MenuItem { Header = "Go to Marker" };
        goTo.Click += (_, _) => NavigateSelectedMarker();
        menu.Items.Add(goTo);

        menu.Items.Add(new Separator());

        var copySelected = new MenuItem { Header = "Copy Selected Rows" };
        copySelected.Click += (_, _) => CopyTableToClipboard(selectedOnly: true);
        menu.Items.Add(copySelected);

        var copyAll = new MenuItem { Header = "Copy All Rows" };
        copyAll.Click += (_, _) => CopyTableToClipboard(selectedOnly: false);
        menu.Items.Add(copyAll);

        menu.Items.Add(new Separator());

        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) => DeleteSelectedItems();
        menu.Items.Add(deleteItem);

        return menu;
    }

    private void EventsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        NavigateSelectedEvent();
    }

    private void MarkersListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        NavigateSelectedMarker();
    }

    private void EventsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (eventsListView.SelectedItem is EditDetailsEventRow row)
        {
            statusText.Text = $"Selected: {row.Name} (Start: {row.StartTimecode}, Length: {row.LengthTimecode})";
        }
    }

    private void MarkersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (markersListView.SelectedItem is EditDetailsMarkerRow row)
        {
            statusText.Text = $"Selected: Marker {row.Index} \"{row.Label}\" at {row.Timecode}";
        }
    }

    private void RegionsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        NavigateSelectedRegion();
    }

    private void RegionsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (regionsListView.SelectedItem is EditDetailsRegionRow row)
        {
            statusText.Text = $"Selected: Region {row.Index} \"{row.Label}\" ({row.StartTimecode} - {row.EndTimecode})";
        }
    }

    private void NavigateSelectedRegion()
    {
        if (regionsListView.SelectedItem is not EditDetailsRegionRow row) return;
        vm.Position = row.StartFrame;
        vm.LoopRegion = new FrameRange(row.StartFrame, row.EndFrame);
        onProjectChanged();
        statusText.Text = $"Position: {row.StartTimecode} (Region {row.Index})";
    }

    private ContextMenu CreateRegionsContextMenu()
    {
        var menu = new ContextMenu();
        var editItem = new MenuItem { Header = "Edit Region…" };
        editItem.Click += (_, _) =>
        {
            if (regionsListView.SelectedItem is EditDetailsRegionRow row)
            {
                var region = vm.Project.Regions.FirstOrDefault(r => r.Id == row.RegionId);
                if (region != null)
                {
                    var dlg = new RegionEditDialog(region, vm.Project.FrameRate) { Owner = this };
                    if (dlg.ShowDialog() == true)
                    {
                        vm.Editor.SetRegion(region.Id, dlg.ResultStartFrame, dlg.ResultEndFrame, dlg.ResultLabel, dlg.ResultColorId);
                        onProjectChanged();
                        RefreshGrid();
                    }
                }
            }
        };
        menu.Items.Add(editItem);

        var selectItem = new MenuItem { Header = "Select Region Time Range" };
        selectItem.Click += (_, _) => NavigateSelectedRegion();
        menu.Items.Add(selectItem);

        var delItem = new MenuItem { Header = "Delete Region" };
        delItem.Click += (_, _) => DeleteSelectedItems();
        menu.Items.Add(delItem);

        return menu;
    }

    private void NavigateSelectedEvent()
    {
        if (eventsListView.SelectedItem is not EditDetailsEventRow row) return;
        vm.Position = row.StartFrame;
        vm.Selection.Clear();
        vm.Selection.Add(row.ClipId);
        onProjectChanged();
        statusText.Text = $"Position: {row.StartTimecode}";
    }

    private void SelectSelectedEventOnTimeline()
    {
        if (eventsListView.SelectedItem is not EditDetailsEventRow row) return;
        vm.Selection.Clear();
        vm.Selection.Add(row.ClipId);
        onProjectChanged();
    }

    private void NavigateSelectedMarker()
    {
        if (markersListView.SelectedItem is not EditDetailsMarkerRow row) return;
        vm.Position = row.Frame;
        onProjectChanged();
        statusText.Text = $"Position: {row.Timecode}";
    }

    /// <summary>
    /// Refreshes row models from the current project state, applying view mode and filters.
    /// </summary>
    public void RefreshGrid()
    {
        var modeStr = (showCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Events";
        var filterText = searchBox?.Text?.Trim() ?? "";
        var selectedTrackOnly = selectedTrackOnlyCheck?.IsChecked == true;

        if (modeStr == "Markers")
        {
            if (eventsListView != null) eventsListView.Visibility = Visibility.Collapsed;
            if (regionsListView != null) regionsListView.Visibility = Visibility.Collapsed;
            if (markersListView != null) markersListView.Visibility = Visibility.Visible;

            currentMarkerRows.Clear();
            var markers = vm.Project.Markers.OrderBy(m => m.Frame).ToList();
            foreach (var m in markers)
            {
                if (!string.IsNullOrEmpty(filterText) &&
                    !m.Label.Contains(filterText, StringComparison.OrdinalIgnoreCase) &&
                    !m.Index.ToString().Contains(filterText))
                {
                    continue;
                }

                currentMarkerRows.Add(new EditDetailsMarkerRow
                {
                    MarkerId = m.Id,
                    Index = m.Index,
                    Frame = m.Frame,
                    Timecode = TimelineView.FormatTimecode(m.Frame, vm.Project.FrameRate),
                    Label = m.Label,
                    ColorId = m.ColorId
                });
            }

            if (markersListView != null)
            {
                markersListView.ItemsSource = null;
                markersListView.ItemsSource = currentMarkerRows;
            }
            if (summaryText != null) summaryText.Text = $"{currentMarkerRows.Count} markers listed";
        }
        else if (modeStr == "Regions")
        {
            if (eventsListView != null) eventsListView.Visibility = Visibility.Collapsed;
            if (markersListView != null) markersListView.Visibility = Visibility.Collapsed;
            if (regionsListView != null) regionsListView.Visibility = Visibility.Visible;

            currentRegionRows.Clear();
            var regions = vm.Project.Regions.OrderBy(r => r.StartFrame).ToList();
            foreach (var r in regions)
            {
                if (!string.IsNullOrEmpty(filterText) &&
                    !r.Label.Contains(filterText, StringComparison.OrdinalIgnoreCase) &&
                    !r.Index.ToString().Contains(filterText))
                {
                    continue;
                }

                currentRegionRows.Add(new EditDetailsRegionRow
                {
                    RegionId = r.Id,
                    Index = r.Index,
                    StartFrame = r.StartFrame,
                    StartTimecode = TimelineView.FormatTimecode(r.StartFrame, vm.Project.FrameRate),
                    EndFrame = r.EndFrame,
                    EndTimecode = TimelineView.FormatTimecode(r.EndFrame, vm.Project.FrameRate),
                    LengthFrames = r.DurationFrames,
                    LengthTimecode = TimelineView.FormatTimecode(r.DurationFrames, vm.Project.FrameRate),
                    Label = r.Label,
                    ColorId = r.ColorId
                });
            }

            if (regionsListView != null)
            {
                regionsListView.ItemsSource = null;
                regionsListView.ItemsSource = currentRegionRows;
            }
            if (summaryText != null) summaryText.Text = $"{currentRegionRows.Count} regions listed";
        }
        else
        {
            if (markersListView != null) markersListView.Visibility = Visibility.Collapsed;
            if (regionsListView != null) regionsListView.Visibility = Visibility.Collapsed;
            if (eventsListView != null) eventsListView.Visibility = Visibility.Visible;

            currentEventRows.Clear();
            var selectedOnly = modeStr == "Selected Events";

            var mediaMap = vm.Project.Media.ToDictionary(m => m.Id);

            for (var tIdx = 0; tIdx < vm.Project.Tracks.Count; tIdx++)
            {
                var track = vm.Project.Tracks[tIdx];

                // Check selected track filter
                if (selectedTrackOnly && tIdx != vm.ActiveTrackIndex)
                    continue;

                for (var cIdx = 0; cIdx < track.Clips.Count; cIdx++)
                {
                    var clip = track.Clips[cIdx];

                    if (selectedOnly && !vm.Selection.Contains(clip.Id))
                        continue;

                    mediaMap.TryGetValue(clip.MediaId, out var media);
                    var mediaPath = media?.Path ?? "";

                    if (!string.IsNullOrEmpty(filterText) &&
                        !clip.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase) &&
                        !mediaPath.Contains(filterText, StringComparison.OrdinalIgnoreCase) &&
                        !track.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var activeTakeName = clip.Takes != null && clip.ActiveTakeIndex >= 0 && clip.ActiveTakeIndex < clip.Takes.Count
                        ? clip.Takes[clip.ActiveTakeIndex].Name
                        : clip.Name;

                    currentEventRows.Add(new EditDetailsEventRow
                    {
                        ClipId = clip.Id,
                        TrackId = track.Id,
                        TrackIndex = tIdx + 1,
                        TrackName = track.Name,
                        EventIndex = cIdx,
                        StartFrame = clip.StartFrame,
                        StartTimecode = TimelineView.FormatTimecode(clip.StartFrame, vm.Project.FrameRate),
                        EndFrame = clip.EndFrame,
                        EndTimecode = TimelineView.FormatTimecode(clip.EndFrame, vm.Project.FrameRate),
                        LengthFrames = clip.DurationFrames,
                        LengthTimecode = TimelineView.FormatTimecode(clip.DurationFrames, vm.Project.FrameRate),
                        SnapOffsetFrames = clip.SnapOffsetFrames,
                        SnapOffsetTimecode = TimelineView.FormatTimecode(clip.SnapOffsetFrames, vm.Project.FrameRate),
                        Name = clip.Name,
                        ActiveTake = activeTakeName,
                        SourceInFrame = clip.SourceInFrame,
                        SourceInTimecode = TimelineView.FormatTimecode(clip.SourceInFrame, vm.Project.FrameRate),
                        PlaybackRate = clip.PlaybackRate,
                        IsMuted = clip.IsMuted,
                        IsLocked = clip.IsLocked,
                        MediaFile = Path.GetFileName(mediaPath)
                    });
                }
            }

            if (eventsListView != null)
            {
                eventsListView.ItemsSource = null;
                eventsListView.ItemsSource = currentEventRows;
            }
            if (summaryText != null) summaryText.Text = $"{currentEventRows.Count} events listed";
        }
    }

    /// <summary>
    /// Builds the Tab-Separated Values (TSV) string of the current grid view for clipboard export.
    /// </summary>
    public string GetTableText(bool selectedOnly = false)
    {
        var sb = new StringBuilder();
        var modeStr = (showCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Events";

        if (modeStr == "Markers")
        {
            sb.AppendLine("Marker\tPosition\tFrame\tName\tColor");
            var rows = selectedOnly && markersListView?.SelectedItems.Count > 0
                ? markersListView.SelectedItems.OfType<EditDetailsMarkerRow>().ToList()
                : currentMarkerRows;

            foreach (var m in rows)
            {
                sb.AppendLine($"{m.Index}\t{m.Timecode}\t{m.Frame}\t{m.Label}\t{m.ColorName}");
            }
        }
        else if (modeStr == "Regions")
        {
            sb.AppendLine("Region\tStart\tEnd\tLength\tName\tColor");
            var rows = selectedOnly && regionsListView?.SelectedItems.Count > 0
                ? regionsListView.SelectedItems.OfType<EditDetailsRegionRow>().ToList()
                : currentRegionRows;

            foreach (var r in rows)
            {
                sb.AppendLine($"{r.Index}\t{r.StartTimecode}\t{r.EndTimecode}\t{r.LengthTimecode}\t{r.Label}\t{r.ColorName}");
            }
        }
        else
        {
            sb.AppendLine("Track\tEvent\tStart\tEnd\tLength\tName\tActive Take\tSource In\tRate\tMute\tLock\tMedia File\tSnap Offset");
            var rows = selectedOnly && eventsListView?.SelectedItems.Count > 0
                ? eventsListView.SelectedItems.OfType<EditDetailsEventRow>().ToList()
                : currentEventRows;

            foreach (var r in rows)
            {
                sb.AppendLine($"{r.TrackDisplay}\t{r.EventIndex}\t{r.StartTimecode}\t{r.EndTimecode}\t{r.LengthTimecode}\t{r.Name}\t{r.ActiveTake}\t{r.SourceInTimecode}\t{r.PlaybackRateText}\t{r.MuteText}\t{r.LockText}\t{r.MediaFile}\t{r.SnapOffsetTimecode}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Copies table contents formatted as TSV to the system clipboard.
    /// </summary>
    public void CopyTableToClipboard(bool selectedOnly = false)
    {
        var text = GetTableText(selectedOnly);
        try
        {
            Clipboard.SetDataObject(text, true);
            statusText.Text = "Copied to clipboard.";
        }
        catch
        {
            // Fallback for restricted clipboard access environments
            statusText.Text = "Failed to access clipboard.";
        }
    }

    /// <summary>
    /// Deletes the selected items in the active grid from the project with atomic undo support.
    /// In Vegas Pro, locked events cannot be deleted and are safely preserved.
    /// </summary>
    public void DeleteSelectedItems()
    {
        var modeStr = (showCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Events";
        if (modeStr == "Markers")
        {
            var selectedMarkers = markersListView.SelectedItems.OfType<EditDetailsMarkerRow>().ToList();
            if (selectedMarkers.Count == 0) return;

            foreach (var sm in selectedMarkers)
            {
                vm.Editor.RemoveMarker(sm.MarkerId);
            }
            onProjectChanged();
            RefreshGrid();
            statusText.Text = $"Deleted {selectedMarkers.Count} marker(s).";
        }
        else if (modeStr == "Regions")
        {
            var selectedRegions = regionsListView.SelectedItems.OfType<EditDetailsRegionRow>().ToList();
            if (selectedRegions.Count == 0) return;

            foreach (var sr in selectedRegions)
            {
                vm.Editor.RemoveRegion(sr.RegionId);
            }
            onProjectChanged();
            RefreshGrid();
            statusText.Text = $"Deleted {selectedRegions.Count} region(s).";
        }
        else
        {
            var selectedEvents = eventsListView.SelectedItems.OfType<EditDetailsEventRow>().ToList();
            if (selectedEvents.Count == 0) return;

            var unlockedEvents = selectedEvents.Where(e => !e.IsLocked).ToList();
            if (unlockedEvents.Count == 0)
            {
                statusText.Text = "Cannot delete locked event(s).";
                return;
            }

            var idsToDelete = new HashSet<Guid>(unlockedEvents.Select(e => e.ClipId));
            vm.Editor.DeleteSelection(idsToDelete, vm.Ripple, vm.IgnoreGroups, vm.RippleMode);
            vm.Selection.ExceptWith(idsToDelete);

            onProjectChanged();
            RefreshGrid();
            statusText.Text = $"Deleted {unlockedEvents.Count} event(s).";
        }
    }
}

public sealed class EditDetailsEventRow
{
    public Guid ClipId { get; set; }
    public Guid TrackId { get; set; }
    public int TrackIndex { get; set; }
    public string TrackName { get; set; } = "";
    public string TrackDisplay => $"{TrackIndex} - {TrackName}";
    public int EventIndex { get; set; }
    public long StartFrame { get; set; }
    public string StartTimecode { get; set; } = "";
    public long EndFrame { get; set; }
    public string EndTimecode { get; set; } = "";
    public long LengthFrames { get; set; }
    public string LengthTimecode { get; set; } = "";
    public long SnapOffsetFrames { get; set; }
    public string SnapOffsetTimecode { get; set; } = "";
    public string Name { get; set; } = "";
    public string ActiveTake { get; set; } = "";
    public long SourceInFrame { get; set; }
    public string SourceInTimecode { get; set; } = "";
    public double PlaybackRate { get; set; } = 1.0;
    public string PlaybackRateText => PlaybackRate.ToString("0.000", CultureInfo.InvariantCulture);
    public bool IsMuted { get; set; }
    public string MuteText => IsMuted ? "Yes" : "No";
    public bool IsLocked { get; set; }
    public string LockText => IsLocked ? "Yes" : "No";
    public string MediaFile { get; set; } = "";
}

public sealed class EditDetailsMarkerRow
{
    public Guid MarkerId { get; set; }
    public int Index { get; set; }
    public long Frame { get; set; }
    public string Timecode { get; set; } = "";
    public string Label { get; set; } = "";
    public int ColorId { get; set; }
    public string ColorName => ColorId switch
    {
        0 => "Blue",
        1 => "Orange",
        2 => "Green",
        3 => "Red",
        4 => "Yellow",
        5 => "Cyan",
        6 => "Magenta",
        7 => "White",
        _ => $"Color {ColorId}"
    };
}

public sealed class EditDetailsRegionRow
{
    public Guid RegionId { get; set; }
    public int Index { get; set; }
    public long StartFrame { get; set; }
    public string StartTimecode { get; set; } = "";
    public long EndFrame { get; set; }
    public string EndTimecode { get; set; } = "";
    public long LengthFrames { get; set; }
    public string LengthTimecode { get; set; } = "";
    public string Label { get; set; } = "";
    public int ColorId { get; set; }
    public string ColorName => ColorId switch
    {
        0 => "Blue",
        1 => "Orange",
        2 => "Green",
        3 => "Red",
        4 => "Yellow",
        5 => "Cyan",
        6 => "Magenta",
        7 => "White",
        _ => $"Color {ColorId}"
    };
}
