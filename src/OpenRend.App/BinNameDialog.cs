using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenRend.App;

public sealed class BinNameDialog : Window
{
    private readonly TextBox nameBox;

    public string BinName => nameBox.Text.Trim();

    public BinNameDialog(string title = "New Media Bin", string initialName = "New Bin")
    {
        Title = title;
        Width = 360;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var prompt = new TextBlock
        {
            Text = "Bin Name:",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(prompt, 0);
        root.Children.Add(prompt);

        nameBox = new TextBox
        {
            Text = initialName,
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4)
        };
        nameBox.SelectAll();
        Grid.SetRow(nameBox, 1);
        root.Children.Add(nameBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var okBtn = new Button
        {
            Content = "OK",
            Width = 75,
            Height = 24,
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38)),
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 8, 0)
        };
        okBtn.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(BinName))
            {
                MessageBox.Show("Please enter a valid bin name.", "Media Bin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        };
        buttonPanel.Children.Add(okBtn);

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 75,
            Height = 24,
            IsCancel = true,
            Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
        };
        buttonPanel.Children.Add(cancelBtn);

        Grid.SetRow(buttonPanel, 3);
        root.Children.Add(buttonPanel);

        Content = root;
        Loaded += (_, _) => nameBox.Focus();
    }
}
