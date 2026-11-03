using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenRend.Core;

namespace OpenRend.App;

public sealed class MarkerEditDialog : Window
{
    private readonly TextBox labelBox;
    private readonly ComboBox colorCombo;
    public string ResultLabel => labelBox.Text.Trim();
    public int ResultColorId => colorCombo.SelectedIndex;

    public MarkerEditDialog(TimelineMarker marker, Rational rate)
    {
        Style = (Style)FindResource(typeof(Window));
        Title = $"Edit Marker {marker.Index}";
        Width = 380;
        Height = 250;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panelBrush = (Brush)FindResource("PanelBrush");
        var textBrush = (Brush)FindResource("TextBrush");

        var root = new Grid { Background = panelBrush, Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Index & Timecode
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Label
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Color
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

        // Info row
        var timecode = FormatTimecode(marker.Frame, rate);
        var infoBlock = new TextBlock
        {
            Text = $"Marker {marker.Index} at frame {marker.Frame} ({timecode})",
            Foreground = textBrush,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(infoBlock, 0);
        root.Children.Add(infoBlock);

        // Label input row
        var labelGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        labelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        labelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock { Text = "Label:", Foreground = textBrush, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(lbl, 0);
        labelGrid.Children.Add(lbl);

        labelBox = new TextBox
        {
            Text = marker.Label ?? "",
            Background = (Brush)FindResource("HeaderBrush"),
            Foreground = textBrush,
            BorderBrush = (Brush)FindResource("BorderBrush"),
            Padding = new Thickness(6, 4, 6, 4)
        };
        Grid.SetColumn(labelBox, 1);
        labelGrid.Children.Add(labelBox);
        Grid.SetRow(labelGrid, 1);
        root.Children.Add(labelGrid);

        // Color input row
        var colorGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var colorLbl = new TextBlock { Text = "Color:", Foreground = textBrush, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(colorLbl, 0);
        colorGrid.Children.Add(colorLbl);

        colorCombo = new ComboBox
        {
            Background = (Brush)FindResource("HeaderBrush"),
            Foreground = textBrush,
            BorderBrush = (Brush)FindResource("BorderBrush"),
            Height = 28
        };
        for (var i = 0; i < TimelineMarker.MarkerColors.Length; i++)
        {
            var hex = TimelineMarker.MarkerColors[i];
            var item = new ComboBoxItem();
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var rect = new Rectangle
            {
                Width = 16,
                Height = 16,
                Fill = new BrushConverter().ConvertFromString(hex) as Brush,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var name = new TextBlock { Text = $"Color {i + 1} ({hex})", Foreground = textBrush, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(rect);
            sp.Children.Add(name);
            item.Content = sp;
            colorCombo.Items.Add(item);
        }
        colorCombo.SelectedIndex = Math.Clamp(marker.ColorId, 0, TimelineMarker.MarkerColors.Length - 1);
        Grid.SetColumn(colorCombo, 1);
        colorGrid.Children.Add(colorCombo);
        Grid.SetRow(colorGrid, 2);
        root.Children.Add(colorGrid);

        // Action buttons
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new Button
        {
            Content = "OK",
            Width = 75,
            Height = 26,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        okBtn.Click += (_, _) => { DialogResult = true; Close(); };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 75,
            Height = 26,
            IsCancel = true
        };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        Grid.SetRow(btnPanel, 4);
        root.Children.Add(btnPanel);

        Content = root;
        Loaded += (_, _) => labelBox.Focus();
    }

    private static string FormatTimecode(long frame, Rational rate)
    {
        if (rate.Numerator <= 0 || rate.Denominator <= 0) rate = new Rational(30, 1);
        var totalSeconds = (double)frame * rate.Denominator / rate.Numerator;
        var hours = (int)(totalSeconds / 3600);
        var minutes = (int)((totalSeconds % 3600) / 60);
        var seconds = (int)(totalSeconds % 60);
        var frames = (int)(frame % Math.Max(1, (long)Math.Round(rate.Value)));
        return $"{hours:D2}:{minutes:D2}:{seconds:D2}:{frames:D2}";
    }
}

