using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenRend.App;

/// <summary>
/// 1:1 Vegas Pro "Organize Layouts" dialog allowing users to browse, reorder,
/// rename, delete, and apply window layout presets.
/// </summary>
public sealed class OrganizeLayoutsWindow : Window
{
    private readonly Action<WorkspaceLayout> applyLayoutCallback;
    private readonly Action refreshMainMenuCallback;
    private readonly TextBox folderBox;
    private readonly ListView layoutListView;
    private readonly ObservableCollection<LayoutItemViewModel> items = [];

    public sealed class LayoutItemViewModel
    {
        public WorkspaceLayout Layout { get; }
        public string Name => Layout.Name;
        public string Shortcut => Layout.ShortcutSlot.HasValue ? $"Alt+D, {Layout.ShortcutSlot.Value}" : "—";
        public string Type => Layout.IsBuiltIn ? "Standard" : "User";

        public LayoutItemViewModel(WorkspaceLayout layout)
        {
            Layout = layout;
        }
    }

    public OrganizeLayoutsWindow(Action<WorkspaceLayout> applyLayoutCallback, Action refreshMainMenuCallback, Window? owner = null)
    {
        Owner = owner;
        this.applyLayoutCallback = applyLayoutCallback;
        this.refreshMainMenuCallback = refreshMainMenuCallback;

        Title = "Organize Layouts";
        Width = 580;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC));
        ResizeMode = ResizeMode.NoResize;

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Row 0: Folder
        var folderGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        folderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
        folderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        folderGrid.Children.Add(new TextBlock
        {
            Text = "Current layout folder:",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Foreground,
            FontSize = 11
        });

        folderBox = new TextBox
        {
            Text = WorkspaceLayoutManager.GetDefaultLayoutsDirectory(),
            IsReadOnly = true,
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 11
        };
        Grid.SetColumn(folderBox, 1);
        folderGrid.Children.Add(folderBox);

        var browseButton = new Button
        {
            Content = "Browse…",
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Foreground,
            BorderThickness = new Thickness(0)
        };
        browseButton.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                InitialDirectory = folderBox.Text
            };
            if (dialog.ShowDialog() == true)
            {
                folderBox.Text = dialog.FolderName;
                ReloadLayouts();
            }
        };
        Grid.SetColumn(browseButton, 2);
        folderGrid.Children.Add(browseButton);
        Grid.SetRow(folderGrid, 0);
        root.Children.Add(folderGrid);

        // Row 1: Label
        var listLabel = new TextBlock
        {
            Text = "Available layouts in current folder:",
            Foreground = Foreground,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(listLabel, 1);
        root.Children.Add(listLabel);

        // Row 2: List + Action Buttons
        var centerGrid = new Grid();
        centerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        centerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

        layoutListView = new ListView
        {
            ItemsSource = items,
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            Foreground = Foreground,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(1),
            SelectionMode = SelectionMode.Single
        };

        var gridView = new GridView();
        gridView.Columns.Add(new GridViewColumn
        {
            Header = "Name",
            Width = 240,
            DisplayMemberBinding = new System.Windows.Data.Binding("Name")
        });
        gridView.Columns.Add(new GridViewColumn
        {
            Header = "Shortcut",
            Width = 100,
            DisplayMemberBinding = new System.Windows.Data.Binding("Shortcut")
        });
        gridView.Columns.Add(new GridViewColumn
        {
            Header = "Type",
            Width = 80,
            DisplayMemberBinding = new System.Windows.Data.Binding("Type")
        });
        layoutListView.View = gridView;
        Grid.SetColumn(layoutListView, 0);
        centerGrid.Children.Add(layoutListView);

        var actionsPanel = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };

        var applyBtn = CreateActionButton("Apply", OnApply);
        var moveUpBtn = CreateActionButton("Move Up", OnMoveUp);
        var moveDownBtn = CreateActionButton("Move Down", OnMoveDown);
        var renameBtn = CreateActionButton("Rename", OnRename);
        var deleteBtn = CreateActionButton("Delete", OnDelete);

        actionsPanel.Children.Add(applyBtn);
        actionsPanel.Children.Add(moveUpBtn);
        actionsPanel.Children.Add(moveDownBtn);
        actionsPanel.Children.Add(renameBtn);
        actionsPanel.Children.Add(deleteBtn);

        Grid.SetColumn(actionsPanel, 1);
        centerGrid.Children.Add(actionsPanel);
        Grid.SetRow(centerGrid, 2);
        root.Children.Add(centerGrid);

        // Row 3: Close
        var closeButton = new Button
        {
            Content = "Close",
            Width = 80,
            Height = 26,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Foreground,
            BorderThickness = new Thickness(0),
            IsCancel = true
        };
        closeButton.Click += (_, _) => Close();
        Grid.SetRow(closeButton, 3);
        root.Children.Add(closeButton);

        Content = root;
        ReloadLayouts();
    }

    private Button CreateActionButton(string text, RoutedEventHandler handler)
    {
        var btn = new Button
        {
            Content = text,
            Height = 26,
            Margin = new Thickness(0, 0, 0, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            Foreground = Foreground,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(1)
        };
        btn.Click += handler;
        return btn;
    }

    private void ReloadLayouts()
    {
        items.Clear();
        var all = WorkspaceLayoutManager.LoadAllLayouts(folderBox.Text);
        foreach (var l in all)
        {
            items.Add(new LayoutItemViewModel(l));
        }
        if (items.Count > 0)
        {
            layoutListView.SelectedIndex = 0;
        }
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (layoutListView.SelectedItem is LayoutItemViewModel vm)
        {
            applyLayoutCallback(vm.Layout);
        }
    }

    private void OnMoveUp(object sender, RoutedEventArgs e)
    {
        var idx = layoutListView.SelectedIndex;
        if (idx <= 0) return;

        var current = items[idx];
        var above = items[idx - 1];

        // Swap shortcut slots if user layouts
        if (!current.Layout.IsBuiltIn && !above.Layout.IsBuiltIn)
        {
            var tempSlot = current.Layout.ShortcutSlot;
            current.Layout.ShortcutSlot = above.Layout.ShortcutSlot;
            above.Layout.ShortcutSlot = tempSlot;

            WorkspaceLayoutManager.SaveLayout(current.Layout, folderBox.Text);
            WorkspaceLayoutManager.SaveLayout(above.Layout, folderBox.Text);
        }

        items.RemoveAt(idx);
        items.Insert(idx - 1, current);
        layoutListView.SelectedIndex = idx - 1;
        refreshMainMenuCallback();
    }

    private void OnMoveDown(object sender, RoutedEventArgs e)
    {
        var idx = layoutListView.SelectedIndex;
        if (idx < 0 || idx >= items.Count - 1) return;

        var current = items[idx];
        var below = items[idx + 1];

        if (!current.Layout.IsBuiltIn && !below.Layout.IsBuiltIn)
        {
            var tempSlot = current.Layout.ShortcutSlot;
            current.Layout.ShortcutSlot = below.Layout.ShortcutSlot;
            below.Layout.ShortcutSlot = tempSlot;

            WorkspaceLayoutManager.SaveLayout(current.Layout, folderBox.Text);
            WorkspaceLayoutManager.SaveLayout(below.Layout, folderBox.Text);
        }

        items.RemoveAt(idx);
        items.Insert(idx + 1, current);
        layoutListView.SelectedIndex = idx + 1;
        refreshMainMenuCallback();
    }

    private void OnRename(object sender, RoutedEventArgs e)
    {
        if (layoutListView.SelectedItem is not LayoutItemViewModel vm) return;
        if (vm.Layout.IsBuiltIn)
        {
            MessageBox.Show(this, "Standard built-in layouts cannot be renamed.", "Organize Layouts", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveLayoutAsWindow(vm.Layout.Name, vm.Layout.ShortcutSlot, folderBox.Text, this)
        {
            Title = "Rename Layout"
        };
        if (dialog.ShowDialog() == true)
        {
            WorkspaceLayoutManager.RenameLayout(vm.Layout.Name, dialog.LayoutName, folderBox.Text);
            if (dialog.SelectedSlot != vm.Layout.ShortcutSlot)
            {
                WorkspaceLayoutManager.AssignShortcutSlot(dialog.LayoutName, dialog.SelectedSlot, folderBox.Text);
            }
            ReloadLayouts();
            refreshMainMenuCallback();
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (layoutListView.SelectedItem is not LayoutItemViewModel vm) return;
        if (vm.Layout.IsBuiltIn)
        {
            MessageBox.Show(this, "Standard built-in layouts cannot be deleted.", "Organize Layouts", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(this, $"Are you sure you want to delete layout '{vm.Layout.Name}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            WorkspaceLayoutManager.DeleteLayout(vm.Layout.Name, folderBox.Text);
            ReloadLayouts();
            refreshMainMenuCallback();
        }
    }
}

