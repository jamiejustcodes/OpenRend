using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenRend.App;

public sealed class MediaTagsDialog : Window
{
    private readonly TextBox tagsBox;

    public List<string> Tags => tagsBox.Text
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public MediaTagsDialog(string mediaName, IEnumerable<string> currentTags)
    {
        Title = "Media Tags";
        Width = 400;
        Height = 200;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = $"Tags for: {mediaName}",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var prompt = new TextBlock
        {
            Text = "Enter comma-separated tags (e.g. interview, b-roll, 4k):",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(prompt, 1);
        root.Children.Add(prompt);

        tagsBox = new TextBox
        {
            Text = string.Join(", ", currentTags),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4)
        };
        tagsBox.SelectAll();
        Grid.SetRow(tagsBox, 2);
        root.Children.Add(tagsBox);

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
        okBtn.Click += (_, _) => DialogResult = true;
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

        Grid.SetRow(buttonPanel, 4);
        root.Children.Add(buttonPanel);

        Content = root;
        Loaded += (_, _) => tagsBox.Focus();
    }
}
