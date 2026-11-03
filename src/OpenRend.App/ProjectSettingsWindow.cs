using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

/// <summary>
/// 1:1 Vegas Pro Project Properties window replicating the authentic multi-tab layout:
/// Video, Audio, Ruler, Summary, and Advanced settings with preset templates.
/// </summary>
public sealed class ProjectSettingsWindow : Window
{
    private record TemplatePreset(string Name, int Width, int Height, Rational Rate, string FieldOrder, double AspectRatio);

    private static readonly TemplatePreset[] Presets =
    [
        new("HD 1080-30p (1920x1080, 29.970 fps)", 1920, 1080, new Rational(30000, 1001), "None (progressive scan)", 1.0),
        new("HD 1080-60p (1920x1080, 59.940 fps)", 1920, 1080, new Rational(60000, 1001), "None (progressive scan)", 1.0),
        new("HD 1080-24p (1920x1080, 23.976 fps)", 1920, 1080, new Rational(24000, 1001), "None (progressive scan)", 1.0),
        new("4K 2160-30p (3840x2160, 29.970 fps)", 3840, 2160, new Rational(30000, 1001), "None (progressive scan)", 1.0),
        new("4K 2160-60p (3840x2160, 59.940 fps)", 3840, 2160, new Rational(60000, 1001), "None (progressive scan)", 1.0),
        new("HD 720-30p (1280x720, 29.970 fps)", 1280, 720, new Rational(30000, 1001), "None (progressive scan)", 1.0),
        new("HD 720-60p (1280x720, 59.940 fps)", 1280, 720, new Rational(60000, 1001), "None (progressive scan)", 1.0),
        new("Portrait 9:16 (1080x1920, 30.000 fps)", 1080, 1920, new Rational(30, 1), "None (progressive scan)", 1.0),
        new("Square 1:1 (1080x1080, 30.000 fps)", 1080, 1080, new Rational(30, 1), "None (progressive scan)", 1.0),
        new("Custom", 1920, 1080, new Rational(30000, 1001), "None (progressive scan)", 1.0)
    ];

    private readonly ComboBox templateCombo;
    private readonly TextBox widthBox;
    private readonly TextBox heightBox;
    private readonly ComboBox fieldOrderCombo;
    private readonly ComboBox pixelAspectCombo;
    private readonly ComboBox frameRateCombo;
    private readonly ComboBox fullResQualityCombo;
    private readonly ComboBox motionBlurCombo;
    private readonly ComboBox deinterlaceCombo;
    private readonly ComboBox resampleCombo;
    private readonly TextBox prerenderFolderBox;
    private readonly TextBlock freeSpaceText;
    private readonly TextBox summaryTitleBox;
    private readonly TextBox summaryArtistBox;
    private readonly TextBox summaryCopyrightBox;
    private readonly TextBox summaryCommentsBox;
    private readonly ComboBox sampleRateCombo;
    private readonly ComboBox bitDepthCombo;
    private readonly ComboBox rulerTimeFormatCombo;

    public int ProjectWidth { get; private set; }
    public int ProjectHeight { get; private set; }
    public Rational Rate { get; private set; }
    public string ProjectName { get; private set; } = "";

    public ProjectSettingsWindow(ProjectDocument project)
    {
        Style = (Style)FindResource(typeof(Window));
        Title = "Project Properties";
        Width = 530;
        Height = 620;
        MinWidth = 500;
        MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("ColorBG");
        Foreground = (Brush)FindResource("TextColor");

        ProjectWidth = project.Width;
        ProjectHeight = project.Height;
        Rate = project.FrameRate;
        ProjectName = project.Name;

        var rootGrid = new Grid { Margin = new Thickness(8) };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var tabs = new TabControl
        {
            Background = (Brush)FindResource("ColorD"),
            BorderBrush = (Brush)FindResource("ColorM"),
            BorderThickness = new Thickness(1)
        };

        // --- Video Tab ---
        var videoTab = new TabItem { Header = "Video" };
        var videoScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var videoGrid = CreateFormGrid();

        int row = 0;
        // Template
        templateCombo = new ComboBox
        {
            ItemsSource = Presets.Select(p => p.Name).ToList(),
            Background = (Brush)FindResource("ColorBG"),
            Foreground = (Brush)FindResource("TextColor"),
            Margin = new Thickness(0, 2, 0, 2)
        };
        templateCombo.SelectionChanged += TemplateComboSelectionChanged;
        AddFormRow(videoGrid, row++, "Template:", templateCombo);

        // Width & Height
        widthBox = CreateTextBox(project.Width.ToString());
        widthBox.TextChanged += (_, _) => CheckCustomPreset();
        AddFormRow(videoGrid, row++, "Width:", widthBox);

        heightBox = CreateTextBox(project.Height.ToString());
        heightBox.TextChanged += (_, _) => CheckCustomPreset();
        AddFormRow(videoGrid, row++, "Height:", heightBox);

        // Field order
        fieldOrderCombo = CreateComboBox(["None (progressive scan)", "Upper field first", "Lower field first"], 0);
        AddFormRow(videoGrid, row++, "Field order:", fieldOrderCombo);

        // Pixel aspect ratio
        pixelAspectCombo = CreateComboBox(["1.0000 (Square)", "1.3333 (HDV)", "0.9091 (NTSC DV)"], 0);
        AddFormRow(videoGrid, row++, "Pixel aspect ratio:", pixelAspectCombo);

        // Frame rate
        frameRateCombo = CreateComboBox(["29.970 (NTSC)", "59.940", "23.976", "24.000", "25.000 (PAL)", "30.000", "50.000", "60.000"], 0);
        MatchFrameRateToCombo(project.FrameRate);
        frameRateCombo.SelectionChanged += (_, _) => CheckCustomPreset();
        AddFormRow(videoGrid, row++, "Frame rate:", frameRateCombo);

        // Full-resolution rendering quality
        fullResQualityCombo = CreateComboBox(["Good", "Best", "Preview", "Draft"], 0);
        AddFormRow(videoGrid, row++, "Full-resolution rendering quality:", fullResQualityCombo);

        // Motion blur type
        motionBlurCombo = CreateComboBox(["Gaussian", "Box", "Pyramid"], 0);
        AddFormRow(videoGrid, row++, "Motion blur type:", motionBlurCombo);

        // Deinterlace method
        deinterlaceCombo = CreateComboBox(["Blend fields", "Interpolate fields", "None"], 0);
        AddFormRow(videoGrid, row++, "Deinterlace method:", deinterlaceCombo);

        // Resample mode
        resampleCombo = CreateComboBox(["Disable resample", "Smart resample", "Force resample"], 0);
        AddFormRow(videoGrid, row++, "Resample mode:", resampleCombo);

        // Prerendered files folder
        var folderPanel = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var browseBtn = new Button
        {
            Content = "Browse…",
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(4, 0, 0, 0)
        };
        DockPanel.SetDock(browseBtn, Dock.Right);
        var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenRend", "Prerenders");
        prerenderFolderBox = CreateTextBox(defaultFolder);
        browseBtn.Click += (_, _) =>
        {
            var fbd = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = prerenderFolderBox.Text };
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                prerenderFolderBox.Text = fbd.SelectedPath;
                UpdateFreeSpace();
            }
        };
        folderPanel.Children.Add(browseBtn);
        folderPanel.Children.Add(prerenderFolderBox);
        AddFormRow(videoGrid, row++, "Prerendered files folder:", folderPanel);

        // Free disk space display
        freeSpaceText = new TextBlock
        {
            Foreground = (Brush)FindResource("TextColorGrey"),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 4)
        };
        UpdateFreeSpace();
        AddFormRow(videoGrid, row++, "Free storage space:", freeSpaceText);

        videoScroll.Content = videoGrid;
        videoTab.Content = videoScroll;
        tabs.Items.Add(videoTab);

        // --- Audio Tab ---
        var audioTab = new TabItem { Header = "Audio" };
        var audioGrid = CreateFormGrid();
        int aRow = 0;

        sampleRateCombo = CreateComboBox(["48,000 Hz", "44,100 Hz", "96,000 Hz", "192,000 Hz"], 0);
        AddFormRow(audioGrid, aRow++, "Sample rate (Hz):", sampleRateCombo);

        bitDepthCombo = CreateComboBox(["16-bit", "24-bit", "32-bit float"], 1);
        AddFormRow(audioGrid, aRow++, "Bit depth:", bitDepthCombo);

        var resampleAudioCombo = CreateComboBox(["Best", "Good"], 0);
        AddFormRow(audioGrid, aRow++, "Resample and stretch quality:", resampleAudioCombo);

        audioTab.Content = audioGrid;
        tabs.Items.Add(audioTab);

        // --- Ruler Tab ---
        var rulerTab = new TabItem { Header = "Ruler" };
        var rulerGrid = CreateFormGrid();
        int rRow = 0;

        rulerTimeFormatCombo = CreateComboBox(["Timecode (29.97 fps, Video)", "Time & Frames", "Seconds", "Samples"], 0);
        AddFormRow(rulerGrid, rRow++, "Ruler time format:", rulerTimeFormatCombo);

        var rulerStartBox = CreateTextBox("00:00:00:00");
        AddFormRow(rulerGrid, rRow++, "Ruler start time:", rulerStartBox);

        rulerTab.Content = rulerGrid;
        tabs.Items.Add(rulerTab);

        // --- Summary Tab ---
        var summaryTab = new TabItem { Header = "Summary" };
        var summaryGrid = CreateFormGrid();
        int sRow = 0;

        summaryTitleBox = CreateTextBox(project.Name);
        AddFormRow(summaryGrid, sRow++, "Title:", summaryTitleBox);

        summaryArtistBox = CreateTextBox("");
        AddFormRow(summaryGrid, sRow++, "Artist:", summaryArtistBox);

        summaryCopyrightBox = CreateTextBox("© OpenRend");
        AddFormRow(summaryGrid, sRow++, "Copyright:", summaryCopyrightBox);

        summaryCommentsBox = new TextBox
        {
            AcceptsReturn = true,
            Height = 80,
            Background = (Brush)FindResource("ColorBG"),
            Foreground = (Brush)FindResource("TextColor"),
            BorderBrush = (Brush)FindResource("ColorM"),
            Padding = new Thickness(4)
        };
        AddFormRow(summaryGrid, sRow++, "Comments:", summaryCommentsBox);

        summaryTab.Content = summaryGrid;
        tabs.Items.Add(summaryTab);

        Grid.SetRow(tabs, 0);
        rootGrid.Children.Add(tabs);

        // --- Bottom Buttons Bar ---
        var bottomBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 4)
        };

        var okBtn = new Button
        {
            Content = "OK",
            IsDefault = true,
            Width = 75,
            Height = 24,
            Margin = new Thickness(4, 0, 4, 0)
        };
        okBtn.Click += (_, _) => CommitAndClose(true);

        var cancelBtn = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            Width = 75,
            Height = 24,
            Margin = new Thickness(4, 0, 4, 0)
        };

        var applyBtn = new Button
        {
            Content = "Apply",
            Width = 75,
            Height = 24,
            Margin = new Thickness(4, 0, 0, 0)
        };
        applyBtn.Click += (_, _) => CommitSettings();

        bottomBar.Children.Add(okBtn);
        bottomBar.Children.Add(cancelBtn);
        bottomBar.Children.Add(applyBtn);

        Grid.SetRow(bottomBar, 1);
        rootGrid.Children.Add(bottomBar);

        Content = rootGrid;

        // Select initial matching template
        SelectMatchingPreset(project.Width, project.Height, project.FrameRate);
    }

    private void UpdateFreeSpace()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(prerenderFolderBox.Text) ?? "C:\\");
            if (drive.IsReady)
            {
                double gb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                freeSpaceText.Text = $"{gb:F1} Gigabytes";
                return;
            }
        }
        catch { }
        freeSpaceText.Text = "Unknown";
    }

    private void TemplateComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (templateCombo.SelectedIndex < 0 || templateCombo.SelectedIndex >= Presets.Length - 1) return;
        var p = Presets[templateCombo.SelectedIndex];
        widthBox.Text = p.Width.ToString();
        heightBox.Text = p.Height.ToString();
        MatchFrameRateToCombo(p.Rate);
    }

    private void CheckCustomPreset()
    {
        if (templateCombo.SelectedIndex == Presets.Length - 1) return; // Already Custom
        if (!int.TryParse(widthBox.Text, out var w) || !int.TryParse(heightBox.Text, out var h)) return;
        var r = ParseRateFromCombo();

        var match = Presets.Take(Presets.Length - 1).FirstOrDefault(p => p.Width == w && p.Height == h && p.Rate == r);
        if (match is null)
        {
            templateCombo.SelectedIndex = Presets.Length - 1; // Select Custom
        }
    }

    private void SelectMatchingPreset(int w, int h, Rational rate)
    {
        for (int i = 0; i < Presets.Length - 1; i++)
        {
            if (Presets[i].Width == w && Presets[i].Height == h && Presets[i].Rate == rate)
            {
                templateCombo.SelectedIndex = i;
                return;
            }
        }
        templateCombo.SelectedIndex = Presets.Length - 1; // Custom
    }

    private void MatchFrameRateToCombo(Rational rate)
    {
        double fps = rate.Value;
        for (int i = 0; i < frameRateCombo.Items.Count; i++)
        {
            var str = frameRateCombo.Items[i]?.ToString() ?? "";
            var numStr = str.Split(' ')[0];
            if (double.TryParse(numStr, out var val) && Math.Abs(val - fps) < 0.05)
            {
                frameRateCombo.SelectedIndex = i;
                return;
            }
        }
        frameRateCombo.SelectedIndex = 0;
    }

    private Rational ParseRateFromCombo()
    {
        var text = frameRateCombo.SelectedItem?.ToString() ?? "";
        if (text.StartsWith("29.970")) return new Rational(30000, 1001);
        if (text.StartsWith("59.940")) return new Rational(60000, 1001);
        if (text.StartsWith("23.976")) return new Rational(24000, 1001);
        if (text.StartsWith("24.000")) return new Rational(24, 1);
        if (text.StartsWith("25.000")) return new Rational(25, 1);
        if (text.StartsWith("30.000")) return new Rational(30, 1);
        if (text.StartsWith("50.000")) return new Rational(50, 1);
        if (text.StartsWith("60.000")) return new Rational(60, 1);
        return new Rational(30000, 1001);
    }

    private bool CommitSettings()
    {
        if (!int.TryParse(widthBox.Text, out var w) || !int.TryParse(heightBox.Text, out var h))
        {
            MessageBox.Show(this, "Please enter valid integer dimensions.", "Project Properties", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (w is < 16 or > 16384 || h is < 16 or > 16384 || w % 2 != 0 || h % 2 != 0)
        {
            MessageBox.Show(this, "Dimensions must be even numbers between 16 and 16384.", "Project Properties", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var name = summaryTitleBox.Text.Trim();
        if (name.Length == 0) name = "Untitled Project";

        ProjectWidth = w;
        ProjectHeight = h;
        Rate = ParseRateFromCombo();
        ProjectName = name;
        return true;
    }

    private void CommitAndClose(bool result)
    {
        if (CommitSettings())
        {
            DialogResult = result;
            Close();
        }
    }

    private static Grid CreateFormGrid()
    {
        var g = new Grid { Margin = new Thickness(12) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return g;
    }

    private static void AddFormRow(Grid grid, int row, string label, UIElement control)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        };
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
    }

    private TextBox CreateTextBox(string initial) => new()
    {
        Text = initial,
        Background = (Brush)FindResource("ColorBG"),
        Foreground = (Brush)FindResource("TextColor"),
        BorderBrush = (Brush)FindResource("ColorM"),
        Padding = new Thickness(4, 2, 4, 2),
        Margin = new Thickness(0, 2, 0, 2)
    };

    private ComboBox CreateComboBox(string[] items, int selectedIndex) => new()
    {
        ItemsSource = items,
        SelectedIndex = selectedIndex,
        Background = (Brush)FindResource("ColorBG"),
        Foreground = (Brush)FindResource("TextColor"),
        Margin = new Thickness(0, 2, 0, 2)
    };
}
