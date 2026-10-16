using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Speech.Recognition;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using OpenRend.Core;
using OpenRend.Media;

namespace OpenRend.App;

/// <summary>
/// AI speech transcription and subtitle generation window matching Vegas Pro SubtitleEventControl.
/// Supports title templates, custom font styling, 9-point anchor alignment, automatic word
/// karaoke highlighting, line constraints, and multiple subtitle export formats.
/// </summary>
public sealed class SpeechToTextWindow : Window
{
    private readonly EditorViewModel vm;

    // Source and Recognition Models
    private readonly ComboBox trackComboBox = new();
    private readonly ComboBox languageComboBox = new();
    private readonly ComboBox modelComboBox = new();

    // Vegas Pro SubtitleEventControl styling properties
    private readonly ComboBox presetComboBox = new() { Width = 180 };
    private readonly ComboBox fontFamilyComboBox = new() { Width = 145 };
    private readonly TextBox fontSizeTextBox = new() { Width = 46, Text = "20", TextAlignment = TextAlignment.Center };
    private readonly ToggleButton boldButton = new() { Content = "B", Width = 30, Height = 24, FontWeight = FontWeights.Bold, ToolTip = "Font format: Bold." };
    private readonly ToggleButton italicButton = new() { Content = "I", Width = 30, Height = 24, FontStyle = FontStyles.Italic, ToolTip = "Font format: Italic." };
    private readonly ToggleButton uppercaseButton = new() { Content = "aA", Width = 30, Height = 24, ToolTip = "Font format: Uppercase." };

    // 9-Point Anchor Alignment Grid
    private readonly ComboBox verticalAlignComboBox = new() { Width = 85, ToolTip = "Align text vertically." };
    private readonly ComboBox horizontalAlignComboBox = new() { Width = 80, ToolTip = "Align text horizontally." };

    // Automatic Karaoke Highlighting
    private readonly ToggleButton automaticKaraokeButton = new() { Content = "Highlight Spoken Word", Width = 155, Height = 24, ToolTip = "Automatic mode: Highlight the word being spoken." };

    // Formatting & Line Constraints
    private readonly RadioButton singleLineRadio = new() { Content = "1 Line", GroupName = "LinesGroup", Margin = new Thickness(0, 0, 10, 0) };
    private readonly RadioButton twoLinesRadio = new() { Content = "2 Lines", GroupName = "LinesGroup", IsChecked = true, Margin = new Thickness(0, 0, 10, 0) };
    private readonly RadioButton wordGranularityRadio = new() { Content = "Word", GroupName = "GranularityGroup", Margin = new Thickness(0, 0, 10, 0) };
    private readonly RadioButton sentenceGranularityRadio = new() { Content = "Sentence", GroupName = "GranularityGroup", IsChecked = true, Margin = new Thickness(0, 0, 10, 0) };
    private readonly Slider maxCharsSlider = new() { Minimum = 10, Maximum = 80, Value = 37, Width = 120, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly TextBlock maxCharsLabel = new() { Text = "Max characters per line: 37", VerticalAlignment = VerticalAlignment.Center };

    // Color and Stroke Customization
    private readonly ComboBox textColorComboBox = new() { Width = 100 };
    private readonly ComboBox outlineColorComboBox = new() { Width = 90 };
    private readonly Slider outlineWidthSlider = new() { Minimum = 0, Maximum = 16, Value = 3, Width = 75, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly ComboBox backgroundColorComboBox = new() { Width = 115 };

    // Find and Replace
    private readonly TextBox findTextBox = new() { Width = 130 };
    private readonly TextBox replaceTextBox = new() { Width = 130 };

    // Status & Actions
    private readonly TextBlock statusText = new() { Text = "Ready" };
    private readonly Button transcribeButton = new() { Content = "Transcribe Audio", Width = 130, Height = 28 };
    private readonly Button addSubtitlesButton = new() { Content = "Generate Titles...", Width = 135, Height = 28, IsEnabled = false, ToolTip = "Add styled subtitle events to timeline" };
    private readonly Button exportSrtButton = new() { Content = "Export SRT...", Width = 95, Height = 28, IsEnabled = false };
    private readonly Button exportSubButton = new() { Content = "Export DVD Sub...", Width = 120, Height = 28, IsEnabled = false, ToolTip = "Export DVD Architect Subtitle Script (*.sub)" };
    private readonly Button exportTxtButton = new() { Content = "Export Text...", Width = 95, Height = 28, IsEnabled = false };

    private readonly ObservableCollection<SubtitleSegment> segments = [];
    private readonly ListBox segmentList = new();
    private bool suppressPresetUpdates;

    public sealed class SubtitleSegment
    {
        public int Index { get; set; }
        public double StartSeconds { get; set; }
        public double EndSeconds { get; set; }
        public string Text { get; set; } = "";
        public string TimeDisplay => $"{FormatTime(StartSeconds)}  -->  {FormatTime(EndSeconds)}";
        public override string ToString() => $"[{TimeDisplay}] {Text}";

        private static string FormatTime(double sec)
        {
            var ts = TimeSpan.FromSeconds(sec);
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
        }
    }

    public SpeechToTextWindow(EditorViewModel viewModel)
    {
        vm = viewModel;
        Style = (Style)FindResource(typeof(Window));
        Title = "AI Speech to Text & Subtitle Customizer";
        Width = 860;
        Height = 690;
        MinWidth = 720;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panelBrush = (Brush)FindResource("PanelBrush");
        var headerBrush = (Brush)FindResource("HeaderBrush");

        var root = new Grid { Background = panelBrush, Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Source, Language, Model
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Title Presets & Custom Styling Expander
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Find & Replace
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3: Transcript list
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4: Bottom actions & status

        // Row 0: Audio Source, Language, Model
        var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var trackPanel = new StackPanel();
        trackPanel.Children.Add(new TextBlock { Text = "Audio Source", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        trackComboBox.Items.Add("Entire Timeline Audio");
        foreach (var t in vm.Project.Tracks.Where(t => t.Kind == TrackKind.Audio))
            trackComboBox.Items.Add(t.Name);
        trackComboBox.SelectedIndex = 0;
        trackPanel.Children.Add(trackComboBox);
        Grid.SetColumn(trackPanel, 0); topGrid.Children.Add(trackPanel);

        var langPanel = new StackPanel();
        langPanel.Children.Add(new TextBlock { Text = "Language", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        string[] languages = ["Auto Detect", "English", "Spanish", "French", "German", "Japanese", "Chinese", "Italian", "Portuguese"];
        foreach (var l in languages) languageComboBox.Items.Add(l);
        languageComboBox.SelectedIndex = 0;
        langPanel.Children.Add(languageComboBox);
        Grid.SetColumn(langPanel, 2); topGrid.Children.Add(langPanel);

        var modelPanel = new StackPanel();
        modelPanel.Children.Add(new TextBlock { Text = "AI Model", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        string[] models = ["Whisper Base (Fast)", "Whisper Small (Balanced)", "Whisper Medium (High Accuracy)"];
        foreach (var m in models) modelComboBox.Items.Add(m);
        modelComboBox.SelectedIndex = 0;
        modelPanel.Children.Add(modelComboBox);
        Grid.SetColumn(modelPanel, 4); topGrid.Children.Add(modelPanel);

        Grid.SetRow(topGrid, 0); root.Children.Add(topGrid);

        // Row 1: Title Presets & Custom Styling Expander (Matching Vegas Pro SubtitleEventControl)
        var stylingRoot = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        var presetHeaderPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        presetHeaderPanel.Children.Add(new TextBlock { Text = "Title Preset:", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        presetComboBox.Items.Add("Subtitles (Vegas Default)");
        presetComboBox.Items.Add("YouTube Shorts (Yellow)");
        presetComboBox.Items.Add("YouTube Shorts (White)");
        presetComboBox.Items.Add("Neon Pop");
        presetComboBox.Items.Add("Classic White");
        presetComboBox.Items.Add("Banner Subtitle");
        presetComboBox.Items.Add("Custom");
        presetComboBox.SelectedIndex = 0;
        presetComboBox.SelectionChanged += PresetComboBox_SelectionChanged;
        presetHeaderPanel.Children.Add(presetComboBox);

        stylingRoot.Children.Add(presetHeaderPanel);

        var customExpander = new Expander
        {
            Header = "Subtitle Customization & Layout (Vegas Pro Properties)",
            IsExpanded = true,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var customGrid = new StackPanel { Margin = new Thickness(4, 6, 4, 4) };

        // Row A: Font, Size, Styles, Alignment, Karaoke
        var styleRow1 = new WrapPanel { Margin = new Thickness(0, 0, 0, 6), VerticalAlignment = VerticalAlignment.Center };

        styleRow1.Children.Add(new TextBlock { Text = "Font:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        PopulateFontFamilies();
        fontFamilyComboBox.SelectionChanged += (_, _) => MarkCustomPreset();
        styleRow1.Children.Add(fontFamilyComboBox);

        styleRow1.Children.Add(new TextBlock { Text = "Size:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) });
        fontSizeTextBox.TextChanged += (_, _) => MarkCustomPreset();
        styleRow1.Children.Add(fontSizeTextBox);

        boldButton.Margin = new Thickness(10, 0, 4, 0);
        boldButton.Click += (_, _) => MarkCustomPreset();
        styleRow1.Children.Add(boldButton);

        italicButton.Margin = new Thickness(0, 0, 4, 0);
        italicButton.Click += (_, _) => MarkCustomPreset();
        styleRow1.Children.Add(italicButton);

        uppercaseButton.Margin = new Thickness(0, 0, 10, 0);
        uppercaseButton.Click += (_, _) => MarkCustomPreset();
        styleRow1.Children.Add(uppercaseButton);

        styleRow1.Children.Add(new TextBlock { Text = "Align:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        verticalAlignComboBox.Items.Add("Top");
        verticalAlignComboBox.Items.Add("Middle");
        verticalAlignComboBox.Items.Add("Bottom");
        verticalAlignComboBox.SelectedIndex = 2; // Bottom
        verticalAlignComboBox.SelectionChanged += (_, _) => MarkCustomPreset();
        styleRow1.Children.Add(verticalAlignComboBox);

        horizontalAlignComboBox.Items.Add("Left");
        horizontalAlignComboBox.Items.Add("Center");
        horizontalAlignComboBox.Items.Add("Right");
        horizontalAlignComboBox.SelectedIndex = 1; // Center
        horizontalAlignComboBox.SelectionChanged += (_, _) => MarkCustomPreset();
        horizontalAlignComboBox.Margin = new Thickness(4, 0, 10, 0);
        styleRow1.Children.Add(horizontalAlignComboBox);

        automaticKaraokeButton.Click += (_, _) => MarkCustomPreset();
        styleRow1.Children.Add(automaticKaraokeButton);

        customGrid.Children.Add(styleRow1);

        // Row B: Lines, Granularity, Max Chars, Colors
        var styleRow2 = new WrapPanel { Margin = new Thickness(0, 0, 0, 4), VerticalAlignment = VerticalAlignment.Center };

        styleRow2.Children.Add(new TextBlock { Text = "Lines:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        singleLineRadio.Click += (_, _) => MarkCustomPreset();
        twoLinesRadio.Click += (_, _) => MarkCustomPreset();
        styleRow2.Children.Add(singleLineRadio);
        styleRow2.Children.Add(twoLinesRadio);

        styleRow2.Children.Add(new TextBlock { Text = "Granularity:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) });
        wordGranularityRadio.Click += (_, _) => MarkCustomPreset();
        sentenceGranularityRadio.Click += (_, _) => MarkCustomPreset();
        styleRow2.Children.Add(wordGranularityRadio);
        styleRow2.Children.Add(sentenceGranularityRadio);

        maxCharsSlider.ValueChanged += (_, _) =>
        {
            maxCharsLabel.Text = $"Max chars: {(int)maxCharsSlider.Value}";
            MarkCustomPreset();
        };
        styleRow2.Children.Add(maxCharsLabel);
        maxCharsSlider.Margin = new Thickness(6, 0, 10, 0);
        styleRow2.Children.Add(maxCharsSlider);

        styleRow2.Children.Add(new TextBlock { Text = "Text:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        textColorComboBox.Items.Add("White (#FFFFFF)");
        textColorComboBox.Items.Add("Yellow (#FFE600)");
        textColorComboBox.Items.Add("Neon Green (#39FF14)");
        textColorComboBox.Items.Add("Cyan (#00E5FF)");
        textColorComboBox.Items.Add("Coral Red (#FF5555)");
        textColorComboBox.SelectedIndex = 0;
        textColorComboBox.SelectionChanged += (_, _) => MarkCustomPreset();
        styleRow2.Children.Add(textColorComboBox);

        styleRow2.Children.Add(new TextBlock { Text = "Stroke:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
        outlineColorComboBox.Items.Add("Black");
        outlineColorComboBox.Items.Add("Dark Gray");
        outlineColorComboBox.Items.Add("White");
        outlineColorComboBox.Items.Add("None");
        outlineColorComboBox.SelectedIndex = 0;
        outlineColorComboBox.SelectionChanged += (_, _) => MarkCustomPreset();
        styleRow2.Children.Add(outlineColorComboBox);

        outlineWidthSlider.Margin = new Thickness(4, 0, 8, 0);
        outlineWidthSlider.ValueChanged += (_, _) => MarkCustomPreset();
        styleRow2.Children.Add(outlineWidthSlider);

        styleRow2.Children.Add(new TextBlock { Text = "Backing:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        backgroundColorComboBox.Items.Add("Transparent");
        backgroundColorComboBox.Items.Add("Dark Pill");
        backgroundColorComboBox.Items.Add("Full Black Bar");
        backgroundColorComboBox.SelectedIndex = 0;
        backgroundColorComboBox.SelectionChanged += (_, _) => MarkCustomPreset();
        styleRow2.Children.Add(backgroundColorComboBox);

        customGrid.Children.Add(styleRow2);
        customExpander.Content = customGrid;
        stylingRoot.Children.Add(customExpander);

        Grid.SetRow(stylingRoot, 1); root.Children.Add(stylingRoot);

        // Row 2: Find & Replace
        var findReplacePanel = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = false };
        findReplacePanel.Children.Add(new TextBlock { Text = "Find:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        findReplacePanel.Children.Add(findTextBox);
        findReplacePanel.Children.Add(new TextBlock { Text = "Replace:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) });
        findReplacePanel.Children.Add(replaceTextBox);

        var replaceNextButton = new Button { Content = "Replace Next", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        replaceNextButton.Click += ReplaceNextClick;
        findReplacePanel.Children.Add(replaceNextButton);

        var replaceAllButton = new Button { Content = "Replace All", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        replaceAllButton.Click += ReplaceAllClick;
        findReplacePanel.Children.Add(replaceAllButton);

        Grid.SetRow(findReplacePanel, 2); root.Children.Add(findReplacePanel);

        // Row 3: Transcript List with Double Click Inline Word/Line Editing (WordMouseDoubleClick)
        segmentList.ItemsSource = segments;
        segmentList.DisplayMemberPath = "ToString";
        segmentList.Background = headerBrush;
        segmentList.BorderBrush = Brushes.DimGray;
        segmentList.BorderThickness = new Thickness(1);
        segmentList.MouseDoubleClick += SegmentList_MouseDoubleClick;
        Grid.SetRow(segmentList, 3); root.Children.Add(segmentList);

        // Row 4: Action Buttons & Status (Matching Vegas Pro export capabilities)
        var bottomPanel = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        transcribeButton.Click += TranscribeClick;
        bottomPanel.Children.Add(transcribeButton);

        addSubtitlesButton.Margin = new Thickness(8, 0, 0, 0);
        addSubtitlesButton.Click += AddSubtitlesClick;
        bottomPanel.Children.Add(addSubtitlesButton);

        exportSrtButton.Margin = new Thickness(6, 0, 0, 0);
        exportSrtButton.Click += ExportSrtClick;
        bottomPanel.Children.Add(exportSrtButton);

        exportSubButton.Margin = new Thickness(6, 0, 0, 0);
        exportSubButton.Click += ExportSubClick;
        bottomPanel.Children.Add(exportSubButton);

        exportTxtButton.Margin = new Thickness(6, 0, 0, 0);
        exportTxtButton.Click += ExportTxtClick;
        bottomPanel.Children.Add(exportTxtButton);

        statusText.VerticalAlignment = VerticalAlignment.Center;
        statusText.Margin = new Thickness(10, 0, 0, 0);
        statusText.Foreground = Brushes.Silver;
        bottomPanel.Children.Add(statusText);

        var closeButton = new Button { Content = "Close", Width = 75, Height = 28, IsCancel = true };
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Right);
        bottomPanel.Children.Add(closeButton);

        Grid.SetRow(bottomPanel, 4); root.Children.Add(bottomPanel);

        Content = root;
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
            // System fonts query fallback
        }

        fontFamilyComboBox.SelectedItem = "Arial";
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressPresetUpdates) return;
        var preset = presetComboBox.SelectedItem?.ToString() ?? "";

        suppressPresetUpdates = true;
        try
        {
            if (preset.Contains("Yellow", StringComparison.OrdinalIgnoreCase))
            {
                fontFamilyComboBox.SelectedItem = "Arial Black";
                fontSizeTextBox.Text = "36";
                boldButton.IsChecked = true;
                italicButton.IsChecked = false;
                uppercaseButton.IsChecked = true;
                verticalAlignComboBox.SelectedItem = "Middle";
                horizontalAlignComboBox.SelectedItem = "Center";
                textColorComboBox.SelectedIndex = 1; // Yellow
                outlineColorComboBox.SelectedIndex = 0; // Black
                outlineWidthSlider.Value = 8;
                backgroundColorComboBox.SelectedIndex = 0; // Transparent
                singleLineRadio.IsChecked = true;
                wordGranularityRadio.IsChecked = true;
                automaticKaraokeButton.IsChecked = false;
            }
            else if (preset.Contains("Shorts (White)", StringComparison.OrdinalIgnoreCase))
            {
                fontFamilyComboBox.SelectedItem = "Arial Black";
                fontSizeTextBox.Text = "36";
                boldButton.IsChecked = true;
                italicButton.IsChecked = false;
                uppercaseButton.IsChecked = true;
                verticalAlignComboBox.SelectedItem = "Middle";
                horizontalAlignComboBox.SelectedItem = "Center";
                textColorComboBox.SelectedIndex = 0; // White
                outlineColorComboBox.SelectedIndex = 0; // Black
                outlineWidthSlider.Value = 8;
                backgroundColorComboBox.SelectedIndex = 0; // Transparent
                singleLineRadio.IsChecked = true;
                wordGranularityRadio.IsChecked = true;
                automaticKaraokeButton.IsChecked = false;
            }
            else if (preset.Contains("Neon", StringComparison.OrdinalIgnoreCase))
            {
                fontFamilyComboBox.SelectedItem = "Impact";
                fontSizeTextBox.Text = "38";
                boldButton.IsChecked = true;
                italicButton.IsChecked = false;
                uppercaseButton.IsChecked = true;
                verticalAlignComboBox.SelectedItem = "Middle";
                horizontalAlignComboBox.SelectedItem = "Center";
                textColorComboBox.SelectedIndex = 2; // Neon Green
                outlineColorComboBox.SelectedIndex = 0; // Black
                outlineWidthSlider.Value = 7;
                backgroundColorComboBox.SelectedIndex = 0; // Transparent
                singleLineRadio.IsChecked = true;
                wordGranularityRadio.IsChecked = true;
                automaticKaraokeButton.IsChecked = false;
            }
            else if (preset.Contains("Classic", StringComparison.OrdinalIgnoreCase))
            {
                fontFamilyComboBox.SelectedItem = "Segoe UI";
                fontSizeTextBox.Text = "24";
                boldButton.IsChecked = true;
                italicButton.IsChecked = false;
                uppercaseButton.IsChecked = false;
                verticalAlignComboBox.SelectedItem = "Bottom";
                horizontalAlignComboBox.SelectedItem = "Center";
                textColorComboBox.SelectedIndex = 0; // White
                outlineColorComboBox.SelectedIndex = 0; // Black
                outlineWidthSlider.Value = 3;
                backgroundColorComboBox.SelectedIndex = 1; // Dark Pill
                twoLinesRadio.IsChecked = true;
                sentenceGranularityRadio.IsChecked = true;
                automaticKaraokeButton.IsChecked = false;
            }
            else if (preset.Contains("Banner", StringComparison.OrdinalIgnoreCase))
            {
                fontFamilyComboBox.SelectedItem = "Arial";
                fontSizeTextBox.Text = "22";
                boldButton.IsChecked = true;
                italicButton.IsChecked = false;
                uppercaseButton.IsChecked = false;
                verticalAlignComboBox.SelectedItem = "Bottom";
                horizontalAlignComboBox.SelectedItem = "Center";
                textColorComboBox.SelectedIndex = 0; // White
                outlineColorComboBox.SelectedIndex = 0; // Black
                outlineWidthSlider.Value = 1;
                backgroundColorComboBox.SelectedIndex = 2; // Full Black Bar
                twoLinesRadio.IsChecked = true;
                sentenceGranularityRadio.IsChecked = true;
                automaticKaraokeButton.IsChecked = false;
            }
            else if (preset.Contains("Subtitles", StringComparison.OrdinalIgnoreCase) || preset.Contains("Default", StringComparison.OrdinalIgnoreCase))
            {
                // Vegas Pro default subtitle preset
                fontFamilyComboBox.SelectedItem = "Arial";
                fontSizeTextBox.Text = "20";
                boldButton.IsChecked = true;
                italicButton.IsChecked = false;
                uppercaseButton.IsChecked = false;
                verticalAlignComboBox.SelectedItem = "Bottom";
                horizontalAlignComboBox.SelectedItem = "Center";
                textColorComboBox.SelectedIndex = 0; // White
                outlineColorComboBox.SelectedIndex = 0; // Black
                outlineWidthSlider.Value = 2;
                backgroundColorComboBox.SelectedIndex = 0; // Transparent
                twoLinesRadio.IsChecked = true;
                sentenceGranularityRadio.IsChecked = true;
                automaticKaraokeButton.IsChecked = false;
            }
        }
        finally
        {
            suppressPresetUpdates = false;
        }
    }

    private void MarkCustomPreset()
    {
        if (suppressPresetUpdates) return;
        suppressPresetUpdates = true;
        try
        {
            presetComboBox.SelectedItem = "Custom";
        }
        finally
        {
            suppressPresetUpdates = false;
        }
    }

    private void SegmentList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (segmentList.SelectedItem is not SubtitleSegment seg) return;

        var inputWindow = new Window
        {
            Title = "Edit Subtitle Event (Vegas In-Place Editor)",
            Width = 480,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Style = (Style)FindResource(typeof(Window))
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = $"Time Range: {seg.TimeDisplay}", Foreground = Brushes.Silver, Margin = new Thickness(0, 0, 0, 8) });

        var editBox = new TextBox
        {
            Text = seg.Text,
            Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(editBox);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new Button { Content = "OK", Width = 80, Height = 26, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        okBtn.Click += (_, _) =>
        {
            seg.Text = editBox.Text.Trim();
            segmentList.Items.Refresh();
            inputWindow.DialogResult = true;
        };
        var cancelBtn = new Button { Content = "Cancel", Width = 80, Height = 26, IsCancel = true };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        inputWindow.Content = panel;
        inputWindow.ShowDialog();
    }

    private async void TranscribeClick(object sender, RoutedEventArgs e)
    {
        segments.Clear();
        transcribeButton.IsEnabled = false;
        addSubtitlesButton.IsEnabled = false;
        exportSrtButton.IsEnabled = false;
        exportSubButton.IsEnabled = false;
        exportTxtButton.IsEnabled = false;
        statusText.Text = "Analyzing audio tracks…";

        try
        {
            var hasAudioTracks = vm.Project.Tracks.Any(t => t.Kind == TrackKind.Audio && !t.IsMuted && t.Clips.Count > 0);
            var hasVideoWithAudio = vm.Project.Tracks.Any(t => t.Kind == TrackKind.Video && !t.IsMuted &&
                t.Clips.Any(c => vm.Project.Media.FirstOrDefault(m => m.Id == c.MediaId)?.HasAudio == true));

            if (!hasAudioTracks && !hasVideoWithAudio)
            {
                statusText.Text = "No audio events found on timeline.";
                MessageBox.Show(this, "The timeline has no audio events to transcribe. Add audio media first.", "AI Speech to Text", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            statusText.Text = "Extracting timeline audio…";

            var modelStr = modelComboBox.SelectedItem?.ToString() ?? "Whisper Base";
            var modelType = modelStr.Contains("Medium", StringComparison.OrdinalIgnoreCase) ? "medium" :
                            modelStr.Contains("Small", StringComparison.OrdinalIgnoreCase) ? "small" : "base";

            var langStr = languageComboBox.SelectedItem?.ToString() ?? "Auto Detect";
            var langCode = langStr switch
            {
                "English" => "en",
                "Spanish" => "es",
                "French" => "fr",
                "German" => "de",
                "Japanese" => "ja",
                "Chinese" => "zh",
                "Italian" => "it",
                "Portuguese" => "pt",
                _ => "auto"
            };

            var maxChars = (int)maxCharsSlider.Value;
            var isWordGranular = wordGranularityRadio.IsChecked == true;
            var allCaps = uppercaseButton.IsChecked == true;

            string? selectedTrack = null;
            if (trackComboBox.SelectedIndex > 0)
                selectedTrack = trackComboBox.SelectedItem?.ToString();

            statusText.Text = "Transcribing dialogue with AI speech model…";
            var results = await Task.Run(() => PerformTranscription(modelType, langCode, maxChars, selectedTrack, isWordGranular, allCaps));

            foreach (var seg in results)
                segments.Add(seg);

            var hasResults = segments.Count > 0;
            addSubtitlesButton.IsEnabled = hasResults;
            exportSrtButton.IsEnabled = hasResults;
            exportSubButton.IsEnabled = hasResults;
            exportTxtButton.IsEnabled = hasResults;
            statusText.Text = hasResults ? $"Transcribed {segments.Count} dialogue events." : "No dialogue recognized in audio.";

            if (!hasResults)
            {
                MessageBox.Show(this, "Speech recognition completed, but no spoken dialogue was recognized in the selected audio.", "AI Speech to Text", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            statusText.Text = $"Transcription failed: {ex.Message}";
            MessageBox.Show(this, $"Transcription error: {ex.Message}", "AI Speech to Text", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            transcribeButton.IsEnabled = true;
        }
    }

    private List<SubtitleSegment> PerformTranscription(string modelType, string langCode, int maxChars, string? selectedTrack, bool isWordGranular, bool allCaps)
    {
        var projectForAudio = vm.Project.DeepClone();
        var hasAudioTracks = projectForAudio.Tracks.Any(t => t.Kind == TrackKind.Audio && !t.IsMuted && t.Clips.Count > 0);
        if (!hasAudioTracks)
        {
            var tempAudioTrack = new TimelineTrack { Name = "TempAudio", Kind = TrackKind.Audio };
            foreach (var vTrack in projectForAudio.Tracks.Where(t => t.Kind == TrackKind.Video && !t.IsMuted))
            {
                foreach (var clip in vTrack.Clips)
                {
                    var media = projectForAudio.Media.FirstOrDefault(m => m.Id == clip.MediaId);
                    if (media is not null && media.HasAudio)
                    {
                        tempAudioTrack.Clips.Add(clip.DeepClone());
                    }
                }
            }
            projectForAudio.Tracks.Add(tempAudioTrack);
        }
        else if (selectedTrack is not null)
        {
            foreach (var t in projectForAudio.Tracks.Where(t => t.Kind == TrackKind.Audio))
                t.IsSolo = (t.Name == selectedTrack);
        }

        var totalFrames = projectForAudio.DurationFrames;
        if (totalFrames <= 0) return [];

        var totalDurationSec = projectForAudio.FrameRate.Seconds(totalFrames);
        var total48kFrames = checked((int)Math.Ceiling(totalDurationSec * MediaService.SampleRate));
        var total16kSamples = total48kFrames / 3;

        var tempDir = Path.Combine(Path.GetTempPath(), "OpenRend_STT");
        Directory.CreateDirectory(tempDir);
        var tempWav = Path.Combine(tempDir, $"stt_{Guid.NewGuid():N}.wav");

        try
        {
            using (var fs = new FileStream(tempWav, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write("RIFF"u8.ToArray());
                bw.Write(36 + total16kSamples * 2);
                bw.Write("WAVE"u8.ToArray());
                bw.Write("fmt "u8.ToArray());
                bw.Write(16);
                bw.Write((short)1); // PCM
                bw.Write((short)1); // Mono
                bw.Write(16000);   // Sample rate
                bw.Write(32000);   // Byte rate
                bw.Write((short)2); // Block align
                bw.Write((short)16);// Bits per sample
                bw.Write("data"u8.ToArray());
                bw.Write(total16kSamples * 2);

                const int blockFrames48k = 48000;
                var shortBuf = new short[blockFrames48k / 3];
                for (var f = 0; f < total48kFrames; f += blockFrames48k)
                {
                    var count = Math.Min(blockFrames48k, total48kFrames - f);
                    var samples = vm.Renderer.RenderAudio(projectForAudio, (double)f / MediaService.SampleRate, count);
                    var monoCount = count / 3;
                    for (var i = 0; i < monoCount; i++)
                    {
                        var src = i * 6;
                        var mono = (samples[src] + samples[src + 1]) * 0.5f;
                        shortBuf[i] = (short)Math.Clamp((int)Math.Round(mono * 32767f), short.MinValue, short.MaxValue);
                    }
                    for (var i = 0; i < monoCount; i++)
                        bw.Write(shortBuf[i]);
                }
            }

            // 1. Try AI Whisper via faster-whisper
            var list = TranscribeWithWhisper(tempWav, modelType, langCode, maxChars, isWordGranular, allCaps);
            if (list.Count > 0) return list;

            // 2. Fallback to Windows Speech Recognition (System.Speech)
            return TranscribeWithSystemSpeech(tempWav, maxChars, isWordGranular, allCaps);
        }
        finally
        {
            try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
        }
    }

    private static List<SubtitleSegment> TranscribeWithWhisper(string wavPath, string modelType, string langCode, int maxChars, bool isWordGranular, bool allCaps)
    {
        var results = new List<SubtitleSegment>();
        try
        {
            var scriptPath = EnsureWhisperScript();
            var psi = new ProcessStartInfo("python", $"\"{scriptPath}\" \"{wavPath}\" {modelType} {langCode}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(psi);
            if (proc == null) return results;

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(180_000);

            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                var jsonLine = stdout.Trim().Split('\n').LastOrDefault(l => l.Trim().StartsWith("[") || l.Trim().StartsWith("{"));
                if (!string.IsNullOrEmpty(jsonLine))
                {
                    using var doc = JsonDocument.Parse(jsonLine);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var index = 1;
                        foreach (var segElem in doc.RootElement.EnumerateArray())
                        {
                            var chunks = SplitIntoChunks(segElem, ref index, maxChars, isWordGranular, allCaps);
                            results.AddRange(chunks);
                        }
                    }
                }
            }
        }
        catch
        {
            // Fall back
        }
        return results;
    }

    private static List<SubtitleSegment> SplitIntoChunks(JsonElement segmentElement, ref int index, int maxChars, bool isWordGranular, bool allCaps)
    {
        var list = new List<SubtitleSegment>();
        var text = segmentElement.GetProperty("text").GetString()?.Trim() ?? "";
        var start = segmentElement.GetProperty("start").GetDouble();
        var end = segmentElement.GetProperty("end").GetDouble();

        var hasWords = segmentElement.TryGetProperty("words", out var wordsProp) && wordsProp.GetArrayLength() > 0;

        if (hasWords && isWordGranular)
        {
            var wordsList = new List<(string Word, double Start, double End)>();
            foreach (var w in wordsProp.EnumerateArray())
            {
                var wStr = w.GetProperty("word").GetString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(wStr)) continue;
                var wStart = w.GetProperty("start").GetDouble();
                var wEnd = w.GetProperty("end").GetDouble();
                wordsList.Add((wStr, wStart, wEnd));
            }

            if (wordsList.Count > 0)
            {
                var i = 0;
                while (i < wordsList.Count)
                {
                    var w1 = wordsList[i];
                    var single = false;

                    if (i == wordsList.Count - 1 || w1.Word.Length >= 8 || w1.Word.EndsWith('.') || w1.Word.EndsWith('!') || w1.Word.EndsWith('?') || w1.Word.EndsWith(',') || w1.Word.EndsWith(';'))
                    {
                        single = true;
                    }
                    else if (wordsList[i + 1].Start - w1.End > 0.22)
                    {
                        single = true;
                    }

                    if (single)
                    {
                        var chunkText = allCaps ? w1.Word.ToUpperInvariant() : w1.Word;
                        var cStart = Math.Round(w1.Start, 3);
                        var cEnd = Math.Round(Math.Max(cStart + 0.18, w1.End + 0.04), 3);
                        if (i + 1 < wordsList.Count && cEnd > wordsList[i + 1].Start)
                            cEnd = Math.Round(wordsList[i + 1].Start, 3);

                        list.Add(new SubtitleSegment
                        {
                            Index = index++,
                            StartSeconds = cStart,
                            EndSeconds = Math.Max(cStart + 0.12, cEnd),
                            Text = chunkText
                        });
                        i++;
                    }
                    else
                    {
                        var w2 = wordsList[i + 1];
                        var combined = $"{w1.Word} {w2.Word}";
                        var chunkText = allCaps ? combined.ToUpperInvariant() : combined;
                        var cStart = Math.Round(w1.Start, 3);
                        var cEnd = Math.Round(Math.Max(cStart + 0.25, w2.End + 0.04), 3);
                        if (i + 2 < wordsList.Count && cEnd > wordsList[i + 2].Start)
                            cEnd = Math.Round(wordsList[i + 2].Start, 3);

                        list.Add(new SubtitleSegment
                        {
                            Index = index++,
                            StartSeconds = cStart,
                            EndSeconds = Math.Max(cStart + 0.15, cEnd),
                            Text = chunkText
                        });
                        i += 2;
                    }
                }
                return list;
            }
        }

        // Standard multi-word phrase grouping honoring character limit
        if (hasWords && text.Length > maxChars)
        {
            var curWords = new List<string>();
            double chunkStart = -1;
            double chunkEnd = end;
            var curLen = 0;

            foreach (var w in wordsProp.EnumerateArray())
            {
                var word = w.GetProperty("word").GetString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(word)) continue;
                var wStart = w.GetProperty("start").GetDouble();
                var wEnd = w.GetProperty("end").GetDouble();

                if (chunkStart < 0) chunkStart = wStart;

                if (curLen + word.Length + 1 > maxChars && curWords.Count > 0)
                {
                    var phrase = string.Join(" ", curWords);
                    list.Add(new SubtitleSegment
                    {
                        Index = index++,
                        StartSeconds = Math.Round(chunkStart, 3),
                        EndSeconds = Math.Round(chunkEnd, 3),
                        Text = allCaps ? phrase.ToUpperInvariant() : phrase
                    });
                    curWords.Clear();
                    curLen = 0;
                    chunkStart = wStart;
                }

                curWords.Add(word);
                curLen += word.Length + 1;
                chunkEnd = wEnd;
            }

            if (curWords.Count > 0)
            {
                var phrase = string.Join(" ", curWords);
                list.Add(new SubtitleSegment
                {
                    Index = index++,
                    StartSeconds = Math.Round(chunkStart, 3),
                    EndSeconds = Math.Round(Math.Max(chunkStart + 0.5, chunkEnd), 3),
                    Text = allCaps ? phrase.ToUpperInvariant() : phrase
                });
            }
        }
        else
        {
            list.Add(new SubtitleSegment
            {
                Index = index++,
                StartSeconds = Math.Round(start, 3),
                EndSeconds = Math.Round(Math.Max(start + 0.5, end), 3),
                Text = allCaps ? text.ToUpperInvariant() : text
            });
        }

        return list;
    }

    private static List<SubtitleSegment> TranscribeWithSystemSpeech(string wavPath, int maxChars, bool isWordGranular, bool allCaps)
    {
        var results = new List<SubtitleSegment>();
        try
        {
            using var engine = new SpeechRecognitionEngine();
            var grammar = new DictationGrammar();
            engine.LoadGrammar(grammar);
            engine.SetInputToWaveFile(wavPath);

            var index = 1;
            while (true)
            {
                try
                {
                    var res = engine.Recognize();
                    if (res == null) break;
                    var text = res.Text?.Trim();
                    if (string.IsNullOrEmpty(text)) continue;

                    var start = res.Audio.AudioPosition.TotalSeconds;
                    var end = start + Math.Max(0.5, res.Audio.Duration.TotalSeconds);

                    if (isWordGranular)
                    {
                        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (words.Length > 0)
                        {
                            var dur = Math.Max(0.5, end - start);
                            var stepSec = dur / words.Length;
                            var i = 0;
                            while (i < words.Length)
                            {
                                var w1 = words[i];
                                var single = i == words.Length - 1 || w1.Length >= 8;
                                if (single)
                                {
                                    var chunkText = allCaps ? w1.ToUpperInvariant() : w1;
                                    var cStart = Math.Round(start + i * stepSec, 3);
                                    var cEnd = Math.Round(Math.Min(end, cStart + stepSec), 3);
                                    results.Add(new SubtitleSegment
                                    {
                                        Index = index++,
                                        StartSeconds = cStart,
                                        EndSeconds = Math.Max(cStart + 0.12, cEnd),
                                        Text = chunkText
                                    });
                                    i++;
                                }
                                else
                                {
                                    var w2 = words[i + 1];
                                    var combined = $"{w1} {w2}";
                                    var chunkText = allCaps ? combined.ToUpperInvariant() : combined;
                                    var cStart = Math.Round(start + i * stepSec, 3);
                                    var cEnd = Math.Round(Math.Min(end, cStart + stepSec * 2), 3);
                                    results.Add(new SubtitleSegment
                                    {
                                        Index = index++,
                                        StartSeconds = cStart,
                                        EndSeconds = Math.Max(cStart + 0.2, cEnd),
                                        Text = chunkText
                                    });
                                    i += 2;
                                }
                            }
                        }
                    }
                    else if (text.Length > maxChars)
                    {
                        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var curWords = new List<string>();
                        var curLen = 0;
                        var curStart = start;
                        var stepSec = (end - start) / Math.Max(1, words.Length);

                        for (var i = 0; i < words.Length; i++)
                        {
                            var w = words[i];
                            if (curLen + w.Length + 1 > maxChars && curWords.Count > 0)
                            {
                                var chunkEnd = start + i * stepSec;
                                var phrase = string.Join(" ", curWords);
                                results.Add(new SubtitleSegment
                                {
                                    Index = index++,
                                    StartSeconds = Math.Round(curStart, 3),
                                    EndSeconds = Math.Round(chunkEnd, 3),
                                    Text = allCaps ? phrase.ToUpperInvariant() : phrase
                                });
                                curWords.Clear();
                                curLen = 0;
                                curStart = chunkEnd;
                            }
                            curWords.Add(w);
                            curLen += w.Length + 1;
                        }

                        if (curWords.Count > 0)
                        {
                            var phrase = string.Join(" ", curWords);
                            results.Add(new SubtitleSegment
                            {
                                Index = index++,
                                StartSeconds = Math.Round(curStart, 3),
                                EndSeconds = Math.Round(end, 3),
                                Text = allCaps ? phrase.ToUpperInvariant() : phrase
                            });
                        }
                    }
                    else
                    {
                        results.Add(new SubtitleSegment
                        {
                            Index = index++,
                            StartSeconds = Math.Round(start, 3),
                            EndSeconds = Math.Round(end, 3),
                            Text = allCaps ? text.ToUpperInvariant() : text
                        });
                    }
                }
                catch
                {
                    break;
                }
            }
        }
        catch
        {
            // System speech unavailable
        }
        return results;
    }

    private static string EnsureWhisperScript()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenRend");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "whisper_transcribe.py");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, WhisperScriptContent, Encoding.UTF8);
        }
        return path;
    }

    private const string WhisperScriptContent = @"import sys
import json
import os

def transcribe(wav_path, model_size=""base"", language=""auto""):
    from faster_whisper import WhisperModel
    model = WhisperModel(model_size, device=""cpu"", compute_type=""int8"")
    lang = None if (not language or language.lower() in (""auto"", ""auto detect"")) else language
    segments, info = model.transcribe(wav_path, language=lang, word_timestamps=True)
    
    results = []
    for s in segments:
        text = s.text.strip()
        if not text:
            continue
        words = []
        if s.words:
            for w in s.words:
                words.append({
                    ""word"": w.word.strip(),
                    ""start"": round(w.start, 3),
                    ""end"": round(w.end, 3)
                })
        results.append({
            ""start"": round(s.start, 3),
            ""end"": round(s.end, 3),
            ""text"": text,
            ""words"": words
        })
    return results

if __name__ == ""__main__"":
    if len(sys.argv) < 2:
        sys.exit(1)
    wav_path = sys.argv[1]
    model_size = sys.argv[2] if len(sys.argv) > 2 else ""base""
    language = sys.argv[3] if len(sys.argv) > 3 else ""auto""
    try:
        res = transcribe(wav_path, model_size, language)
        print(json.dumps(res))
    except Exception as e:
        print(json.dumps({""error"": str(e)}))
        sys.exit(1)
";

    private void ReplaceNextClick(object sender, RoutedEventArgs e)
    {
        var find = findTextBox.Text;
        var replace = replaceTextBox.Text;
        if (string.IsNullOrEmpty(find)) return;

        foreach (var seg in segments)
        {
            if (seg.Text.Contains(find, StringComparison.OrdinalIgnoreCase))
            {
                var pos = seg.Text.IndexOf(find, StringComparison.OrdinalIgnoreCase);
                seg.Text = seg.Text.Remove(pos, find.Length).Insert(pos, replace);
                segmentList.Items.Refresh();
                statusText.Text = "Replaced 1 occurrence.";
                return;
            }
        }
        statusText.Text = "No more occurrences found.";
    }

    private void ReplaceAllClick(object sender, RoutedEventArgs e)
    {
        var find = findTextBox.Text;
        var replace = replaceTextBox.Text;
        if (string.IsNullOrEmpty(find)) return;

        var count = 0;
        foreach (var seg in segments)
        {
            if (seg.Text.Contains(find, StringComparison.OrdinalIgnoreCase))
            {
                seg.Text = seg.Text.Replace(find, replace, StringComparison.OrdinalIgnoreCase);
                count++;
            }
        }
        segmentList.Items.Refresh();
        statusText.Text = $"Replaced {count} occurrences.";
    }

    private void AddSubtitlesClick(object sender, RoutedEventArgs e)
    {
        if (segments.Count == 0) return;

        try
        {
            vm.Editor.Execute(p =>
            {
                var vAlign = verticalAlignComboBox.SelectedItem?.ToString() ?? "Bottom";
                var hAlign = horizontalAlignComboBox.SelectedItem?.ToString() ?? "Center";
                var anchor = $"{vAlign}_{hAlign}";

                var preset = presetComboBox.SelectedItem?.ToString() ?? "Subtitles";
                var isKaraoke = automaticKaraokeButton.IsChecked == true;

                var trackName = $"Subtitles from Speech ({preset}, {anchor}{(isKaraoke ? ", Karaoke" : "")})";
                var subTrack = p.Tracks.FirstOrDefault(t => t.Name.StartsWith("Subtitles from Speech", StringComparison.OrdinalIgnoreCase) && t.Kind == TrackKind.Video);
                if (subTrack is null)
                {
                    subTrack = new TimelineTrack
                    {
                        Name = trackName,
                        Kind = TrackKind.Video
                    };
                    p.Tracks.Insert(0, subTrack);
                }
                else
                {
                    subTrack.Name = trackName;
                }

                // Dedicated subtitle media asset
                var textMedia = p.Media.FirstOrDefault(m => m.Path == "openrend://subtitles");
                if (textMedia is null)
                {
                    textMedia = new MediaAsset
                    {
                        Id = Guid.NewGuid(),
                        Path = "openrend://subtitles",
                        DurationSeconds = Math.Max(86400.0, p.FrameRate.Seconds(p.DurationFrames) + 3600.0),
                        Width = p.Width,
                        Height = p.Height,
                        FrameRate = p.FrameRate.Value,
                        HasVideo = true,
                        HasAudio = false
                    };
                    p.Media.Add(textMedia);
                }

                subTrack.Clips.Clear();
                foreach (var seg in segments)
                {
                    var startFrame = p.FrameRate.Frames(seg.StartSeconds);
                    var durFrames = Math.Max(1, p.FrameRate.Frames(seg.EndSeconds - seg.StartSeconds));
                    var clip = new TimelineClip
                    {
                        MediaId = textMedia.Id,
                        Name = seg.Text,
                        StartFrame = startFrame,
                        DurationFrames = durFrames
                    };
                    subTrack.Clips.Add(clip);
                }
            });

            statusText.Text = $"Generated {segments.Count} titles on timeline.";
            MessageBox.Show(this, $"Generated {segments.Count} subtitle events on timeline track with full styling.", "AI Speech to Text", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Failed to add subtitles: {ex.Message}";
        }
    }

    private void ExportSrtClick(object sender, RoutedEventArgs e)
    {
        if (segments.Count == 0) return;

        var sfd = new SaveFileDialog
        {
            Title = "Export Subtitles (*.srt)",
            Filter = "SubRip Subtitles (*.srt)|*.srt",
            DefaultExt = ".srt",
            FileName = $"{vm.Project.Name}_subtitles.srt"
        };

        if (sfd.ShowDialog(this) == true)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                sb.AppendLine((i + 1).ToString());
                sb.AppendLine($"{FormatSrtTime(seg.StartSeconds)} --> {FormatSrtTime(seg.EndSeconds)}");
                sb.AppendLine(seg.Text);
                sb.AppendLine();
            }

            File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
            statusText.Text = $"Exported subtitles to {Path.GetFileName(sfd.FileName)}.";
            MessageBox.Show(this, $"Subtitles successfully exported to:\n{sfd.FileName}", "Export Subtitles", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExportSubClick(object sender, RoutedEventArgs e)
    {
        if (segments.Count == 0) return;

        var sfd = new SaveFileDialog
        {
            Title = "Export DVD Architect Subtitle Script (*.sub)",
            Filter = "DVD Architect Subtitle Script (*.sub)|*.sub",
            DefaultExt = ".sub",
            FileName = $"{vm.Project.Name}_dvd_subtitles.sub"
        };

        if (sfd.ShowDialog(this) == true)
        {
            // Vegas Pro DVD Architect format: <start> <end> <text>
            var sb = new StringBuilder();
            foreach (var seg in segments)
            {
                sb.AppendLine($"{FormatDvdTime(seg.StartSeconds)}\t{FormatDvdTime(seg.EndSeconds)}\t{seg.Text}");
            }

            File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
            statusText.Text = $"Exported DVD Architect script to {Path.GetFileName(sfd.FileName)}.";
            MessageBox.Show(this, $"DVD Architect subtitle script saved to:\n{sfd.FileName}", "Export Subtitles", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExportTxtClick(object sender, RoutedEventArgs e)
    {
        if (segments.Count == 0) return;

        var sfd = new SaveFileDialog
        {
            Title = "Export Transcript (*.txt)",
            Filter = "Text files (*.txt)|*.txt",
            DefaultExt = ".txt",
            FileName = $"{vm.Project.Name}_transcript.txt"
        };

        if (sfd.ShowDialog(this) == true)
        {
            var sb = new StringBuilder();
            foreach (var seg in segments)
            {
                sb.AppendLine(seg.Text);
            }

            File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
            statusText.Text = $"Exported transcript to {Path.GetFileName(sfd.FileName)}.";
            MessageBox.Show(this, $"Transcript saved to:\n{sfd.FileName}", "Export Subtitles", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static string FormatSrtTime(double sec)
    {
        var ts = TimeSpan.FromSeconds(sec);
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
    }

    private static string FormatDvdTime(double sec)
    {
        var ts = TimeSpan.FromSeconds(sec);
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }
}
