using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenRend.App;

/// <summary>
/// 1:1 Vegas Pro "Save Layout As" dialog allowing users to name a window docking configuration,
/// assign a shortcut slot (0–9 / Alt+D, 0–9), and choose the destination layout folder.
/// </summary>
public sealed class SaveLayoutAsWindow : Window
{
    private readonly TextBox nameBox;
    private readonly ComboBox shortcutCombo;
    private readonly TextBox folderBox;

    public string LayoutName => nameBox.Text.Trim();
    public int? SelectedSlot => shortcutCombo.SelectedIndex <= 0 ? null : (shortcutCombo.SelectedIndex - 1);
    public string LayoutFolder => folderBox.Text.Trim();

    public SaveLayoutAsWindow(string initialName = "My Custom Layout", int? initialSlot = null, string? initialFolder = null, Window? owner = null)
    {
        Owner = owner;
        Title = "Save Layout As";
        Width = 480;
        Height = 250;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC));
        ResizeMode = ResizeMode.NoResize;

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Row 0: Name
        var nameGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameGrid.Children.Add(new TextBlock
        {
            Text = "Name:",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Foreground,
            FontSize = 11
        });
        nameBox = new TextBox
        {
            Text = initialName,
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            Foreground = Foreground,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 12
        };
        Grid.SetColumn(nameBox, 1);
        nameGrid.Children.Add(nameBox);
        Grid.SetRow(nameGrid, 0);
        root.Children.Add(nameGrid);

        // Row 1: Shortcut
        var shortcutGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        shortcutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
        shortcutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        shortcutGrid.Children.Add(new TextBlock
        {
            Text = "Shortcut:",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Foreground,
            FontSize = 11
        });

        shortcutCombo = new ComboBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            Foreground = Foreground,
            FontSize = 11,
            Height = 26
        };
        shortcutCombo.Items.Add("(None)");
        for (int i = 0; i <= 9; i++)
        {
            shortcutCombo.Items.Add($"Layout {i} (Alt+D, {i})");
        }
        shortcutCombo.SelectedIndex = initialSlot.HasValue && initialSlot.Value >= 0 && initialSlot.Value <= 9
            ? initialSlot.Value + 1
            : 0;
        Grid.SetColumn(shortcutCombo, 1);
        shortcutGrid.Children.Add(shortcutCombo);
        Grid.SetRow(shortcutGrid, 1);
        root.Children.Add(shortcutGrid);

        // Row 2: Folder
        var folderGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        folderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
        folderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        folderGrid.Children.Add(new TextBlock
        {
            Text = "Folder:",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Foreground,
            FontSize = 11
        });

        folderBox = new TextBox
        {
            Text = initialFolder ?? WorkspaceLayoutManager.GetDefaultLayoutsDirectory(),
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
            }
        };
        Grid.SetColumn(browseButton, 2);
        folderGrid.Children.Add(browseButton);
        Grid.SetRow(folderGrid, 2);
        root.Children.Add(folderGrid);

        // Row 4: Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 26,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            IsDefault = true
        };
        okButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(LayoutName))
            {
                MessageBox.Show(this, "Please enter a valid layout name.", "Save Layout As", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 80,
            Height = 26,
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Foreground,
            BorderThickness = new Thickness(0),
            IsCancel = true
        };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        Grid.SetRow(buttonPanel, 4);
        root.Children.Add(buttonPanel);

        Content = root;
        Loaded += (_, _) => nameBox.Focus();
    }
}

