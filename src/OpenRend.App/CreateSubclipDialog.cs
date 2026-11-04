using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenRend.App;

/// <summary>
/// Vegas Pro 1:1 modal dialog for creating a named subclip from Trimmer In/Out selection.
/// </summary>
public sealed class CreateSubclipDialog : Window
{
    private readonly TextBox nameBox;

    public string SubclipName => nameBox.Text.Trim();

    public CreateSubclipDialog(string initialName, string mediaName, string inTc, string outTc, string lengthTc)
    {
        Title = "Create Subclip";
        Width = 400;
        Height = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Prompt
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // TextBox
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Info
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

        var prompt = new TextBlock
        {
            Text = "Subclip Name:",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            Margin = new Thickness(0, 0, 0, 6)
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

        var infoPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var srcText = new TextBlock
        {
            Text = $"Source: {mediaName}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var rangeText = new TextBlock
        {
            Text = $"Range: [{inTc} - {outTc}]   Length: {lengthTc}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
            Margin = new Thickness(0, 3, 0, 0)
        };
        infoPanel.Children.Add(srcText);
        infoPanel.Children.Add(rangeText);
        Grid.SetRow(infoPanel, 2);
        root.Children.Add(infoPanel);

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
            if (string.IsNullOrWhiteSpace(SubclipName))
            {
                nameBox.Focus();
                return;
            }
            DialogResult = true;
            Close();
        };

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 75,
            Height = 24,
            IsCancel = true,
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB))
        };
        cancelBtn.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };

        buttonPanel.Children.Add(okBtn);
        buttonPanel.Children.Add(cancelBtn);
        Grid.SetRow(buttonPanel, 4);
        root.Children.Add(buttonPanel);

        Loaded += (_, _) => nameBox.Focus();
    }
}

