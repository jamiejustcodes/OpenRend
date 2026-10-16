using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using OpenRend.Core;
using OpenRend.Media;

namespace OpenRend.App;

/// <summary>
/// VEGAS Titles &amp; Text generator editor matching the Vegas Pro OFX Titles &amp; Text dialog.
/// Supports live timeline preview updates, multi-line typography, 9-point anchor alignment,
/// custom strokes, drop shadows, and background box/banner layout.
/// </summary>
public sealed class TitlesAndTextWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly TimelineClip targetClip;
    private readonly MediaAsset targetMedia;
    private readonly bool isNewClip;

    private readonly ComboBox presetComboBox = new() { Width = 180 };
    private readonly TextBox textEditor = new() { Height = 90, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox fontFamilyComboBox = new() { Width = 150 };
    private readonly TextBox fontSizeTextBox = new() { Width = 46, Text = "48", TextAlignment = TextAlignment.Center };
    private readonly ToggleButton boldButton = new() { Content = "B", Width = 30, Height = 24, FontWeight = FontWeights.Bold, ToolTip = "Font format: Bold." };
    private readonly ToggleButton italicButton = new() { Content = "I", Width = 30, Height = 24, FontStyle = FontStyles.Italic, ToolTip = "Font format: Italic." };
    private readonly ToggleButton uppercaseButton = new() { Content = "aA", Width = 30, Height = 24, ToolTip = "Font format: Uppercase." };

    private readonly ComboBox anchorComboBox = new() { Width = 130 };
    private readonly ComboBox textColorComboBox = new() { Width = 110 };
    private readonly ComboBox outlineColorComboBox = new() { Width = 95 };
    private readonly Slider outlineWidthSlider = new() { Minimum = 0, Maximum = 20, Value = 0, Width = 80, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly CheckBox shadowCheckBox = new() { Content = "Shadow", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox backgroundColorComboBox = new() { Width = 115 };
    private readonly CheckBox cropBackgroundCheckBox = new() { Content = "Crop background to text", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };

    private bool suppressUpdates;

    public TitlesAndTextWindow(EditorViewModel viewModel, TimelineClip? clip = null, MediaAsset? media = null)
    {
        vm = viewModel;
        Style = (Style)FindResource(typeof(Window));
        Title = "VEGAS Titles & Text Generator";
        Width = 620;
        Height = 560;
        MinWidth = 520;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panelBrush = (Brush)FindResource("PanelBrush");

        if (clip is not null && media is not null)
        {
            targetClip = clip;
            targetMedia = media;
            isNewClip = false;
        }
        else
        {
            isNewClip = true;
            // Create dedicated text media generator asset
            targetMedia = new MediaAsset
            {
                Id = Guid.NewGuid(),
                Path = "openrend://text?preset=Default",
                DurationSeconds = 86400.0,
                Width = vm.Project.Width,
                Height = vm.Project.Height,
                FrameRate = vm.Project.FrameRate.Value,
                HasVideo = true,
                HasAudio = false
            };

            var cursorFrame = vm.Position;
            var durFrames = vm.Project.FrameRate.Frames(5.0); // 5 seconds default in Vegas
            targetClip = new TimelineClip
            {
                MediaId = targetMedia.Id,
                Name = "Sample Text",
                StartFrame = cursorFrame,
                DurationFrames = durFrames
            };

            // Insert into active video track or create new top video track
            vm.Editor.Execute(p =>
            {
                p.Media.Add(targetMedia);
                var vTrack = p.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Video);
                if (vTrack is null)
                {
                    vTrack = new TimelineTrack { Name = "Titles & Text", Kind = TrackKind.Video };
                    p.Tracks.Insert(0, vTrack);
                }
                vTrack.Clips.Add(targetClip);
            });
        }

        var root = new Grid { Background = panelBrush, Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Preset
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Text
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Font & Alignment
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Colors & Styling
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacing
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Bottom Buttons

        // Row 0: Presets
        var presetRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        presetRow.Children.Add(new TextBlock { Text = "Title Preset:", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        presetComboBox.Items.Add("Default");
        presetComboBox.Items.Add("Title 01");
        presetComboBox.Items.Add("Subtitle");
        presetComboBox.Items.Add("Lower Third");
        presetComboBox.Items.Add("Banner Title");
        presetComboBox.Items.Add("Pop In");
        presetComboBox.Items.Add("Custom");
        presetComboBox.SelectedIndex = 0;
        presetComboBox.SelectionChanged += PresetComboBox_SelectionChanged;
        presetRow.Children.Add(presetComboBox);
        Grid.SetRow(presetRow, 0); root.Children.Add(presetRow);

        // Row 1: Text Content Editor
        var textPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        textPanel.Children.Add(new TextBlock { Text = "Text Content:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        textEditor.Text = targetClip.Name;
        textEditor.TextChanged += (_, _) => UpdateClipLive();
        textPanel.Children.Add(textEditor);
        Grid.SetRow(textPanel, 1); root.Children.Add(textPanel);

        // Row 2: Typography & Alignment
        var fontGroup = new GroupBox { Header = "Typography & Alignment", Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(8) };
        var fontStack = new StackPanel();

        var fontRow1 = new WrapPanel { Margin = new Thickness(0, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center };
        fontRow1.Children.Add(new TextBlock { Text = "Font:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        PopulateFontFamilies();
        fontFamilyComboBox.SelectionChanged += (_, _) => { MarkCustom(); UpdateClipLive(); };
        fontRow1.Children.Add(fontFamilyComboBox);

        fontRow1.Children.Add(new TextBlock { Text = "Size:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) });
        fontSizeTextBox.TextChanged += (_, _) => { MarkCustom(); UpdateClipLive(); };
        fontRow1.Children.Add(fontSizeTextBox);

        boldButton.Margin = new Thickness(10, 0, 4, 0);
        boldButton.Click += (_, _) => { MarkCustom(); UpdateClipLive(); };
        fontRow1.Children.Add(boldButton);

        italicButton.Margin = new Thickness(0, 0, 4, 0);
        italicButton.Click += (_, _) => { MarkCustom(); UpdateClipLive(); };
        fontRow1.Children.Add(italicButton);

        uppercaseButton.Margin = new Thickness(0, 0, 0, 0);
        uppercaseButton.Click += (_, _) => { MarkCustom(); UpdateClipLive(); };
        fontRow1.Children.Add(uppercaseButton);
        fontStack.Children.Add(fontRow1);

        var fontRow2 = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        fontRow2.Children.Add(new TextBlock { Text = "Anchor Point:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        string[] anchors = ["Top Left", "Top Center", "Top Right", "Middle Left", "Middle Center", "Middle Right", "Bottom Left", "Bottom Center", "Bottom Right"];
        foreach (var a in anchors) anchorComboBox.Items.Add(a);
        anchorComboBox.SelectedItem = "Middle Center";
        anchorComboBox.SelectionChanged += (_, _) => { MarkCustom(); UpdateClipLive(); };
        fontRow2.Children.Add(anchorComboBox);
        fontStack.Children.Add(fontRow2);

        fontGroup.Content = fontStack;
        Grid.SetRow(fontGroup, 2); root.Children.Add(fontGroup);

        // Row 3: Colors, Outline, Shadow, Background
        var colorGroup = new GroupBox { Header = "Colors, Outline & Shadow", Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(8) };
        var colorStack = new StackPanel();

        var colorRow1 = new WrapPanel { Margin = new Thickness(0, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center };
        colorRow1.Children.Add(new TextBlock { Text = "Text Fill:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        textColorComboBox.Items.Add("White (#FFFFFF)");
        textColorComboBox.Items.Add("Yellow (#FFE600)");
        textColorComboBox.Items.Add("Neon Green (#39FF14)");
        textColorComboBox.Items.Add("Cyan (#00E5FF)");
        textColorComboBox.Items.Add("Coral Red (#FF5555)");
        textColorComboBox.SelectedIndex = 0;
        textColorComboBox.SelectionChanged += (_, _) => { MarkCustom(); UpdateClipLive(); };
        colorRow1.Children.Add(textColorComboBox);

        colorRow1.Children.Add(new TextBlock { Text = "Outline:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) });
        outlineColorComboBox.Items.Add("Black (#000000)");
        outlineColorComboBox.Items.Add("Dark Gray (#333333)");
        outlineColorComboBox.Items.Add("White (#FFFFFF)");
        outlineColorComboBox.Items.Add("None");
        outlineColorComboBox.SelectedIndex = 0;
        outlineColorComboBox.SelectionChanged += (_, _) => { MarkCustom(); UpdateClipLive(); };
        colorRow1.Children.Add(outlineColorComboBox);

        outlineWidthSlider.Margin = new Thickness(4, 0, 10, 0);
        outlineWidthSlider.ValueChanged += (_, _) => { MarkCustom(); UpdateClipLive(); };
        colorRow1.Children.Add(outlineWidthSlider);

        shadowCheckBox.Click += (_, _) => { MarkCustom(); UpdateClipLive(); };
        colorRow1.Children.Add(shadowCheckBox);
        colorStack.Children.Add(colorRow1);

        var colorRow2 = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        colorRow2.Children.Add(new TextBlock { Text = "Background:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        backgroundColorComboBox.Items.Add("Transparent (#00000000)");
        backgroundColorComboBox.Items.Add("Dark Translucent (#90000000)");
        backgroundColorComboBox.Items.Add("Solid Black (#FF000000)");
        backgroundColorComboBox.Items.Add("Dark Navy (#B0101828)");
        backgroundColorComboBox.SelectedIndex = 0;
        backgroundColorComboBox.SelectionChanged += (_, _) => { MarkCustom(); UpdateClipLive(); };
        colorRow2.Children.Add(backgroundColorComboBox);

        cropBackgroundCheckBox.Margin = new Thickness(10, 0, 0, 0);
        cropBackgroundCheckBox.Click += (_, _) => { MarkCustom(); UpdateClipLive(); };
        colorRow2.Children.Add(cropBackgroundCheckBox);
        colorStack.Children.Add(colorRow2);

        colorGroup.Content = colorStack;
        Grid.SetRow(colorGroup, 3); root.Children.Add(colorGroup);

        // Row 5: Action Buttons
        var bottomPanel = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        var okButton = new Button { Content = "OK", Width = 80, Height = 28, IsDefault = true };
        okButton.Click += (_, _) => { UpdateClipLive(); DialogResult = true; Close(); };
        bottomPanel.Children.Add(okButton);

        var closeButton = new Button { Content = "Close", Width = 80, Height = 28, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        closeButton.Click += (_, _) => Close();
        bottomPanel.Children.Add(closeButton);

        Grid.SetRow(bottomPanel, 5); root.Children.Add(bottomPanel);

        Content = root;

        // Load existing style if editing
        if (!isNewClip && targetMedia.Path.StartsWith("openrend://text?", StringComparison.OrdinalIgnoreCase))
        {
            LoadFromStyle(SubtitleStyle.FromQuery(targetMedia.Path.Substring("openrend://text?".Length)));
        }
    }

    private void PopulateFontFamilies()
    {
        var popularFonts = new[] { "Arial", "Segoe UI", "Arial Black", "Impact", "Montserrat", "Verdana", "Tahoma", "Times New Roman" };
        foreach (var font in popularFonts)
        {
            if (!fontFamilyComboBox.Items.Contains(font))
                fontFamilyComboBox.Items.Add(font);
        }

        try
        {
            foreach (var family in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
            {
                var name = family.Source;
                if (!fontFamilyComboBox.Items.Contains(name))
                    fontFamilyComboBox.Items.Add(name);
            }
        }
        catch
        {
            // System font fallback
        }

        fontFamilyComboBox.SelectedItem = "Arial";
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressUpdates) return;
        var preset = presetComboBox.SelectedItem?.ToString() ?? "Default";

        suppressUpdates = true;
        try
        {
            switch (preset)
            {
                case "Default":
                    fontFamilyComboBox.SelectedItem = "Arial";
                    fontSizeTextBox.Text = "48";
                    boldButton.IsChecked = false;
                    italicButton.IsChecked = false;
                    uppercaseButton.IsChecked = false;
                    anchorComboBox.SelectedItem = "Middle Center";
                    textColorComboBox.SelectedIndex = 0; // White
                    outlineColorComboBox.SelectedIndex = 3; // None
                    outlineWidthSlider.Value = 0;
                    shadowCheckBox.IsChecked = true;
                    backgroundColorComboBox.SelectedIndex = 0; // Transparent
                    cropBackgroundCheckBox.IsChecked = true;
                    break;
                case "Title 01":
                    fontFamilyComboBox.SelectedItem = "Arial";
                    fontSizeTextBox.Text = "64";
                    boldButton.IsChecked = true;
                    italicButton.IsChecked = false;
                    uppercaseButton.IsChecked = true;
                    anchorComboBox.SelectedItem = "Middle Center";
                    textColorComboBox.SelectedIndex = 0; // White
                    outlineColorComboBox.SelectedIndex = 0; // Black
                    outlineWidthSlider.Value = 4;
                    shadowCheckBox.IsChecked = true;
                    backgroundColorComboBox.SelectedIndex = 0;
                    cropBackgroundCheckBox.IsChecked = true;
                    break;
                case "Subtitle":
                    fontFamilyComboBox.SelectedItem = "Arial";
                    fontSizeTextBox.Text = "24";
                    boldButton.IsChecked = true;
                    italicButton.IsChecked = false;
                    uppercaseButton.IsChecked = false;
                    anchorComboBox.SelectedItem = "Bottom Center";
                    textColorComboBox.SelectedIndex = 0; // White
                    outlineColorComboBox.SelectedIndex = 0; // Black
                    outlineWidthSlider.Value = 3;
                    shadowCheckBox.IsChecked = true;
                    backgroundColorComboBox.SelectedIndex = 0;
                    cropBackgroundCheckBox.IsChecked = true;
                    break;
                case "Lower Third":
                    fontFamilyComboBox.SelectedItem = "Segoe UI";
                    fontSizeTextBox.Text = "28";
                    boldButton.IsChecked = true;
                    italicButton.IsChecked = false;
                    uppercaseButton.IsChecked = false;
                    anchorComboBox.SelectedItem = "Bottom Left";
                    textColorComboBox.SelectedIndex = 3; // Cyan
                    outlineColorComboBox.SelectedIndex = 3; // None
                    outlineWidthSlider.Value = 0;
                    shadowCheckBox.IsChecked = false;
                    backgroundColorComboBox.SelectedIndex = 1; // Dark Translucent
                    cropBackgroundCheckBox.IsChecked = true;
                    break;
                case "Banner Title":
                    fontFamilyComboBox.SelectedItem = "Arial Black";
                    fontSizeTextBox.Text = "36";
                    boldButton.IsChecked = true;
                    italicButton.IsChecked = false;
                    uppercaseButton.IsChecked = true;
                    anchorComboBox.SelectedItem = "Middle Center";
                    textColorComboBox.SelectedIndex = 1; // Yellow
                    outlineColorComboBox.SelectedIndex = 0; // Black
                    outlineWidthSlider.Value = 2;
                    shadowCheckBox.IsChecked = false;
                    backgroundColorComboBox.SelectedIndex = 2; // Solid Black
                    cropBackgroundCheckBox.IsChecked = false; // Spans full banner width
                    break;
                case "Pop In":
                    fontFamilyComboBox.SelectedItem = "Impact";
                    fontSizeTextBox.Text = "52";
                    boldButton.IsChecked = true;
                    italicButton.IsChecked = false;
                    uppercaseButton.IsChecked = true;
                    anchorComboBox.SelectedItem = "Middle Center";
                    textColorComboBox.SelectedIndex = 2; // Neon Green
                    outlineColorComboBox.SelectedIndex = 0; // Black
                    outlineWidthSlider.Value = 8;
                    shadowCheckBox.IsChecked = true;
                    backgroundColorComboBox.SelectedIndex = 0;
                    cropBackgroundCheckBox.IsChecked = true;
                    break;
            }
        }
        finally
        {
            suppressUpdates = false;
        }

        UpdateClipLive();
    }

    private void MarkCustom()
    {
        if (suppressUpdates) return;
        suppressUpdates = true;
        try
        {
            presetComboBox.SelectedItem = "Custom";
        }
        finally
        {
            suppressUpdates = false;
        }
    }

    private void LoadFromStyle(SubtitleStyle style)
    {
        suppressUpdates = true;
        try
        {
            if (fontFamilyComboBox.Items.Contains(style.FontFamily))
                fontFamilyComboBox.SelectedItem = style.FontFamily;
            fontSizeTextBox.Text = style.FontSize > 0 ? style.FontSize.ToString("0") : "48";
            boldButton.IsChecked = style.Bold;
            italicButton.IsChecked = style.Italic;
            uppercaseButton.IsChecked = style.Uppercase;

            var matchAnchor = $"{style.VerticalAlignment} {style.HorizontalAlignment}";
            if (anchorComboBox.Items.Contains(matchAnchor))
                anchorComboBox.SelectedItem = matchAnchor;

            outlineWidthSlider.Value = style.OutlineWidth;
            shadowCheckBox.IsChecked = style.ShadowEnabled;
            cropBackgroundCheckBox.IsChecked = style.FitBackgroundColor;
        }
        finally
        {
            suppressUpdates = false;
        }
    }

    private void UpdateClipLive()
    {
        if (suppressUpdates) return;

        var font = fontFamilyComboBox.SelectedItem?.ToString() ?? "Arial";
        float.TryParse(fontSizeTextBox.Text, out var size);
        if (size <= 0) size = 48f;

        var bold = boldButton.IsChecked == true;
        var italic = italicButton.IsChecked == true;
        var uppercase = uppercaseButton.IsChecked == true;

        var anchor = anchorComboBox.SelectedItem?.ToString()?.Replace(" ", "_") ?? "Middle_Center";

        var textCol = textColorComboBox.SelectedIndex switch
        {
            1 => "#FFE600",
            2 => "#39FF14",
            3 => "#00E5FF",
            4 => "#FF5555",
            _ => "#FFFFFF"
        };

        var outlineCol = outlineColorComboBox.SelectedIndex switch
        {
            1 => "#333333",
            2 => "#FFFFFF",
            3 => "#00000000",
            _ => "#000000"
        };
        var outlineWidth = (float)outlineWidthSlider.Value;
        if (outlineColorComboBox.SelectedIndex == 3) outlineWidth = 0f;

        var shadow = shadowCheckBox.IsChecked == true;
        var bgCol = backgroundColorComboBox.SelectedIndex switch
        {
            1 => "#90000000",
            2 => "#FF000000",
            3 => "#B0101828",
            _ => "#00000000"
        };
        var fitBg = cropBackgroundCheckBox.IsChecked == true;

        var style = new SubtitleStyle
        {
            PresetName = presetComboBox.SelectedItem?.ToString() ?? "Custom",
            FontFamily = font,
            FontSize = size,
            Bold = bold,
            Italic = italic,
            Uppercase = uppercase,
            AnchorAlignment = anchor,
            TextColor = textCol,
            OutlineColor = outlineCol,
            OutlineWidth = outlineWidth,
            ShadowEnabled = shadow,
            BackgroundColor = bgCol,
            FitBackgroundColor = fitBg
        };

        // Update target media and clip immediately
        vm.Editor.Execute(p =>
        {
            var media = p.Media.FirstOrDefault(m => m.Id == targetMedia.Id);
            if (media is not null)
            {
                media.Path = $"openrend://text?{style.ToQuery()}";
            }
            foreach (var track in p.Tracks)
            {
                var clip = track.Clips.FirstOrDefault(c => c.Id == targetClip.Id);
                if (clip is not null)
                {
                    clip.Name = textEditor.Text;
                    break;
                }
            }
        });
    }
}
