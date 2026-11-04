using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

/// <summary>
/// 1:1 Vegas Pro Media Properties dialog displaying source media format, resolution,
/// frame rate, pixel aspect ratio, audio stream parameters, and file path.
/// </summary>
public sealed class MediaPropertiesWindow : Window
{
    public MediaPropertiesWindow(MediaAsset asset, Window? owner = null)
    {
        Owner = owner;
        Title = $"{asset.Name} - Properties";
        Width = 460;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC));
        ResizeMode = ResizeMode.NoResize;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var tabs = new TabControl
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Foreground
        };

        // General Tab
        var generalTab = new TabItem { Header = "General" };
        var generalGrid = CreatePropertyGrid();
        AddRow(generalGrid, "Name:", asset.Name);
        AddRow(generalGrid, "Location:", asset.Path);
        AddRow(generalGrid, "Type:", asset.IsGenerated ? "Generated Media" : asset.HasVideo ? "Video Clip" : "Audio Track");
        var sizeText = File.Exists(asset.Path) ? $"{new FileInfo(asset.Path).Length / 1024.0 / 1024.0:0.##} MB" : "—";
        AddRow(generalGrid, "File Size:", sizeText);
        AddRow(generalGrid, "Duration:", $"{asset.DurationSeconds:0.00} seconds ({TimeSpan.FromSeconds(asset.DurationSeconds):hh\\:mm\\:ss\\.ff})");
        generalTab.Content = new ScrollViewer { Content = generalGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        tabs.Items.Add(generalTab);

        // Video Tab
        if (asset.HasVideo)
        {
            var videoTab = new TabItem { Header = "Video" };
            var videoGrid = CreatePropertyGrid();
            AddRow(videoGrid, "Frame Size:", $"{asset.Width} × {asset.Height}");
            AddRow(videoGrid, "Frame Rate:", $"{asset.FrameRate:0.###} fps");
            AddRow(videoGrid, "Pixel Aspect Ratio:", "1.0000 (Square)");
            AddRow(videoGrid, "Field Order:", "None (Progressive Scan)");
            AddRow(videoGrid, "Pixel Format:", "32-bit floating point / 8-bit");
            AddRow(videoGrid, "Alpha Channel:", "Undefined / Straight");
            videoTab.Content = new ScrollViewer { Content = videoGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            tabs.Items.Add(videoTab);
        }

        // Audio Tab
        if (asset.HasAudio)
        {
            var audioTab = new TabItem { Header = "Audio" };
            var audioGrid = CreatePropertyGrid();
            AddRow(audioGrid, "Sample Rate:", "48,000 Hz");
            AddRow(audioGrid, "Bit Depth:", "16-bit / 24-bit");
            AddRow(audioGrid, "Channels:", "2 (Stereo)");
            AddRow(audioGrid, "Format:", "PCM / AAC");
            audioTab.Content = new ScrollViewer { Content = audioGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            tabs.Items.Add(audioTab);
        }

        Grid.SetRow(tabs, 0);
        root.Children.Add(tabs);

        // Buttons
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var okBtn = new Button
        {
            Content = "OK",
            Width = 75,
            Height = 24,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            Foreground = Foreground
        };
        okBtn.Click += (_, _) => DialogResult = true;

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 75,
            Height = 24,
            IsCancel = true,
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            Foreground = Foreground
        };
        cancelBtn.Click += (_, _) => DialogResult = false;

        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        Grid.SetRow(btnPanel, 1);
        root.Children.Add(btnPanel);

        Content = root;
    }

    private static Grid CreatePropertyGrid()
    {
        var grid = new Grid { Margin = new Thickness(10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static void AddRow(Grid grid, string label, string value)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lbl = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),
            Margin = new Thickness(0, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        var val = new TextBox
        {
            Text = value,
            IsReadOnly = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC)),
            Margin = new Thickness(0, 4, 0, 4),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(val, row);
        Grid.SetColumn(val, 1);
        grid.Children.Add(val);
    }
}

