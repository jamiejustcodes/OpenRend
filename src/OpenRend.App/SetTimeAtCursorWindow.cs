using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

/// <summary>
/// Vegas Pro 1:1 "Set Time at Cursor" modal dialog (command id 32300).
/// Allows direct editing of the edit cursor position in the project ruler format.
/// </summary>
public sealed class SetTimeAtCursorWindow : Window
{
    private readonly TextBox timecodeBox;
    private readonly TextBlock errorText;
    private readonly Rational frameRate;
    private readonly RulerFormat rulerFormat;
    private readonly int sampleRate;
    private readonly double bpm;
    private readonly int beatsPerMeasure;
    private readonly long startTimeFrames;
    private readonly long maxFrames;

    public long? ResultFrame { get; private set; }

    public SetTimeAtCursorWindow(
        long currentFrame,
        Rational rate,
        RulerFormat format,
        int audioSampleRate = 48000,
        double beatsPerMinute = 120.0,
        int projectBeatsPerMeasure = 4,
        long projectStartTimeFrames = 0,
        long maxProjectFrames = 10_000_000)
    {
        frameRate = rate;
        rulerFormat = format;
        sampleRate = audioSampleRate;
        bpm = beatsPerMinute;
        beatsPerMeasure = projectBeatsPerMeasure;
        startTimeFrames = projectStartTimeFrames;
        maxFrames = Math.Max(0, maxProjectFrames);

        Style = (Style)FindResource(typeof(Window));
        Title = "Set Time at Cursor";
        Width = 380;
        Height = 220;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panelBrush = (Brush)FindResource("PanelBrush");
        var textBrush = (Brush)FindResource("TextBrush");
        var headerBrush = (Brush)FindResource("HeaderBrush");
        var accentBrush = (Brush)FindResource("AccentBrush");

        var root = new Grid { Background = panelBrush, Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header / Format hint
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Input row
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Error text
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Action Buttons

        var currentFormatted = TimecodeFormatter.Format(
            currentFrame, frameRate, rulerFormat, sampleRate, bpm, beatsPerMeasure, startTimeFrames);

        var hintBlock = new TextBlock
        {
            Text = $"Format: {rulerFormat.ToDisplayName()}",
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(hintBlock, 0);
        root.Children.Add(hintBlock);

        var inputGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = "Time at Cursor:",
            Foreground = textBrush,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(label, 0);
        inputGrid.Children.Add(label);

        timecodeBox = new TextBox
        {
            Text = currentFormatted,
            Background = headerBrush,
            Foreground = textBrush,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Padding = new Thickness(6, 4, 6, 4),
            BorderBrush = (Brush)FindResource("BorderBrush")
        };
        Grid.SetColumn(timecodeBox, 1);
        inputGrid.Children.Add(timecodeBox);

        Grid.SetRow(inputGrid, 1);
        root.Children.Add(inputGrid);

        errorText = new TextBlock
        {
            Foreground = Brushes.Tomato,
            FontSize = 11,
            Margin = new Thickness(110, 0, 0, 0),
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(errorText, 2);
        root.Children.Add(errorText);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 75,
            Height = 24,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Background = accentBrush,
            Foreground = textBrush
        };
        okButton.Click += OnOkClick;
        buttonPanel.Children.Add(okButton);

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 75,
            Height = 24,
            IsCancel = true,
            Background = headerBrush,
            Foreground = textBrush
        };
        buttonPanel.Children.Add(cancelButton);

        Grid.SetRow(buttonPanel, 4);
        root.Children.Add(buttonPanel);

        Content = root;
        Loaded += (_, _) =>
        {
            timecodeBox.Focus();
            timecodeBox.SelectAll();
        };
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        var input = timecodeBox.Text.Trim();
        if (TimecodeFormatter.TryParse(
            input,
            frameRate,
            rulerFormat,
            out var parsedFrame,
            sampleRate,
            bpm,
            beatsPerMeasure,
            startTimeFrames))
        {
            ResultFrame = Math.Clamp(parsedFrame, 0, maxFrames);
            DialogResult = true;
            Close();
        }
        else
        {
            errorText.Text = "Invalid timecode format.";
            errorText.Visibility = Visibility.Visible;
            timecodeBox.Focus();
            timecodeBox.SelectAll();
        }
    }
}
