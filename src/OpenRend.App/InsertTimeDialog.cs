using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

/// <summary>
/// Vegas Pro 1:1 "Insert Time" modal dialog.
/// Prompts for the duration of time to insert at the current timeline edit cursor
/// and allows selecting the ripple scope (Affected Tracks, Affected Tracks & Markers, or All Tracks).
/// </summary>
public sealed class InsertTimeDialog : Window
{
    private readonly TextBox durationBox;
    private readonly RadioButton affectedTracksRadio;
    private readonly RadioButton affectedTracksMarkersRadio;
    private readonly RadioButton allTracksRadio;
    private readonly TextBlock errorText;

    private readonly Rational frameRate;
    private readonly RulerFormat rulerFormat;
    private readonly int sampleRate;
    private readonly double bpm;
    private readonly int beatsPerMeasure;
    private readonly long startTimeFrames;

    public long DurationFrames { get; private set; }
    public AutoRippleMode SelectedRippleMode { get; private set; }

    public InsertTimeDialog(
        long initialDurationFrames,
        AutoRippleMode currentRippleMode,
        Rational rate,
        RulerFormat format,
        int audioSampleRate = 48000,
        double beatsPerMinute = 120.0,
        int projectBeatsPerMeasure = 4,
        long projectStartTimeFrames = 0)
    {
        frameRate = rate;
        rulerFormat = format;
        sampleRate = audioSampleRate;
        bpm = beatsPerMinute;
        beatsPerMeasure = projectBeatsPerMeasure;
        startTimeFrames = projectStartTimeFrames;
        DurationFrames = Math.Max(1, initialDurationFrames);
        SelectedRippleMode = currentRippleMode;

        if (TryFindResource(typeof(Window)) is Style windowStyle) Style = windowStyle;
        Title = "Insert Time";
        Width = 420;
        Height = 270;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panelBrush = ResolveBrush("PanelBrush", new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)));
        var textBrush = ResolveBrush("TextBrush", Brushes.White);
        var headerBrush = ResolveBrush("HeaderBrush", new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)));
        var textMutedBrush = ResolveBrush("TextMutedBrush", new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)));
        var borderBrush = ResolveBrush("BorderBrush", new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)));

        var root = new Grid { Background = panelBrush, Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Format hint
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Duration input
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Ripple Mode GroupBox
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Error text
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

        var hintBlock = new TextBlock
        {
            Text = $"Format: {rulerFormat.ToDisplayName()}",
            Foreground = textMutedBrush,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(hintBlock, 0);
        root.Children.Add(hintBlock);

        // Input row
        var inputGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = "Amount of time to be inserted:",
            Foreground = textBrush,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(label, 0);
        inputGrid.Children.Add(label);

        var initialFormatted = TimecodeFormatter.Format(
            DurationFrames, frameRate, rulerFormat, sampleRate, bpm, beatsPerMeasure, startTimeFrames);

        durationBox = new TextBox
        {
            Text = initialFormatted,
            Background = headerBrush,
            Foreground = textBrush,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Padding = new Thickness(6, 4, 6, 4),
            BorderBrush = borderBrush
        };
        Grid.SetColumn(durationBox, 1);
        inputGrid.Children.Add(durationBox);
        Grid.SetRow(inputGrid, 1);
        root.Children.Add(inputGrid);

        // Auto-Ripple Mode Group
        var groupBox = new GroupBox
        {
            Header = "Auto-Ripple Options",
            Foreground = textBrush,
            BorderBrush = borderBrush,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(8, 6, 8, 6)
        };
        var radioStack = new StackPanel { Orientation = Orientation.Vertical };

        affectedTracksRadio = new RadioButton
        {
            Content = "Affected tracks",
            GroupName = "RippleModeGroup",
            Foreground = textBrush,
            Margin = new Thickness(0, 2, 0, 4),
            IsChecked = currentRippleMode == AutoRippleMode.AffectedTracks
        };
        affectedTracksMarkersRadio = new RadioButton
        {
            Content = "Affected tracks, bus tracks, markers",
            GroupName = "RippleModeGroup",
            Foreground = textBrush,
            Margin = new Thickness(0, 2, 0, 4),
            IsChecked = currentRippleMode == AutoRippleMode.AffectedTracksAndMarkers
        };
        allTracksRadio = new RadioButton
        {
            Content = "All tracks, markers, and regions",
            GroupName = "RippleModeGroup",
            Foreground = textBrush,
            Margin = new Thickness(0, 2, 0, 4),
            IsChecked = currentRippleMode == AutoRippleMode.AllTracksAndMarkers
        };

        // In Vegas Pro, "All tracks, markers, and regions" is the standard default for Insert Time
        if (affectedTracksRadio.IsChecked != true && affectedTracksMarkersRadio.IsChecked != true && allTracksRadio.IsChecked != true)
        {
            allTracksRadio.IsChecked = true;
        }

        radioStack.Children.Add(affectedTracksRadio);
        radioStack.Children.Add(affectedTracksMarkersRadio);
        radioStack.Children.Add(allTracksRadio);
        groupBox.Content = radioStack;
        Grid.SetRow(groupBox, 2);
        root.Children.Add(groupBox);

        // Error message
        errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55)),
            FontSize = 11,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(errorText, 3);
        root.Children.Add(errorText);

        // Buttons
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
            IsDefault = true
        };
        okButton.Click += (_, _) => OnOkClicked();

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 80,
            Height = 26,
            IsCancel = true
        };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        Grid.SetRow(buttonPanel, 5);
        root.Children.Add(buttonPanel);

        Content = root;
        Loaded += (_, _) =>
        {
            durationBox.Focus();
            durationBox.SelectAll();
        };
    }

    private Brush ResolveBrush(string resourceKey, Brush fallback)
    {
        try
        {
            return TryFindResource(resourceKey) as Brush ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private void OnOkClicked()
    {
        var rawText = durationBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            ShowError("Please enter a valid time or frame count.");
            return;
        }

        if (!TimecodeFormatter.TryParse(
            rawText,
            frameRate,
            rulerFormat,
            out var parsedFrame,
            sampleRate,
            bpm,
            beatsPerMeasure,
            startTimeFrames))
        {
            // Fallback: try direct integer frames or seconds
            if (long.TryParse(rawText, out var frames))
            {
                parsedFrame = frames;
            }
            else if (double.TryParse(rawText, out var seconds))
            {
                parsedFrame = (long)Math.Round(seconds * frameRate.Numerator / (double)frameRate.Denominator);
            }
            else
            {
                ShowError($"Cannot parse \"{rawText}\" in {rulerFormat.ToDisplayName()} format.");
                return;
            }
        }

        if (parsedFrame <= 0)
        {
            ShowError("Duration must be greater than zero.");
            return;
        }

        DurationFrames = parsedFrame;
        SelectedRippleMode = affectedTracksRadio.IsChecked == true
            ? AutoRippleMode.AffectedTracks
            : affectedTracksMarkersRadio.IsChecked == true
                ? AutoRippleMode.AffectedTracksAndMarkers
                : AutoRippleMode.AllTracksAndMarkers;

        DialogResult = true;
        Close();
    }

    private void ShowError(string msg)
    {
        errorText.Text = msg;
        errorText.Visibility = Visibility.Visible;
        durationBox.Focus();
        durationBox.SelectAll();
    }
}
