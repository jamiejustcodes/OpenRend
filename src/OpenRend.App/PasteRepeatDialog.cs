namespace OpenRend.App;

using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;

/// <summary>
/// Vegas Pro 1:1 "Paste Repeat" modal dialog.
/// ID: 32087 / Shortcut: Ctrl+B.
/// Verbatim layout:
/// - "Number of times to paste:" (numeric spinner, 1..999)
/// - "Paste spacing" GroupBox:
///   - "End to end" RadioButton
///   - "Even spacing" RadioButton
///   - "Paste every:" with spinner / timecode formatting.
/// </summary>
public sealed class PasteRepeatDialog : Window
{
    private readonly TextBox countBox;
    private readonly RadioButton endToEndRadio;
    private readonly RadioButton evenSpacingRadio;
    private readonly TextBox spacingBox;
    private readonly Button spacingUpBtn;
    private readonly Button spacingDownBtn;

    private readonly Rational frameRate;
    private readonly RulerFormat rulerFormat;
    private readonly int sampleRate;

    public int RepeatCount { get; private set; } = 1;
    public bool IsEndToEnd => endToEndRadio.IsChecked == true;
    public long SpacingFrames { get; private set; }

    public PasteRepeatDialog(
        long defaultSpacingFrames,
        Rational rate,
        RulerFormat format = RulerFormat.TimeAndFrames,
        int audioSampleRate = 48000)
    {
        frameRate = rate;
        rulerFormat = format;
        sampleRate = audioSampleRate;
        SpacingFrames = Math.Max(1, defaultSpacingFrames);

        Title = "Paste Repeat";
        Width = 380;
        Height = 280;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panelBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
        var textBrush = Brushes.White;
        var textMutedBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        var borderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        var buttonBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        Background = panelBrush;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Count
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Spacing GroupBox
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

        // Row 0: Count row
        var countGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        countGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        countGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        countGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

        var countLabel = new TextBlock
        {
            Text = "Number of times to paste:",
            Foreground = textBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        countGrid.Children.Add(countLabel);

        countBox = new TextBox
        {
            Text = "1",
            Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)),
            Foreground = textBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
            HorizontalContentAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Height = 24
        };
        AutomationProperties.SetAutomationId(countBox, "PasteRepeatCountBox");
        Grid.SetColumn(countBox, 1);
        countGrid.Children.Add(countBox);

        var countSpinGrid = new Grid { Height = 24, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        countSpinGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        countSpinGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var countUp = new Button
        {
            Content = "▲",
            FontSize = 7,
            Foreground = textBrush,
            Background = buttonBrush,
            BorderBrush = borderBrush,
            Padding = new Thickness(0)
        };
        countUp.Click += (_, _) => AdjustCount(1);
        Grid.SetRow(countUp, 0);
        countSpinGrid.Children.Add(countUp);

        var countDown = new Button
        {
            Content = "▼",
            FontSize = 7,
            Foreground = textBrush,
            Background = buttonBrush,
            BorderBrush = borderBrush,
            Padding = new Thickness(0)
        };
        countDown.Click += (_, _) => AdjustCount(-1);
        Grid.SetRow(countDown, 1);
        countSpinGrid.Children.Add(countDown);

        Grid.SetColumn(countSpinGrid, 2);
        countGrid.Children.Add(countSpinGrid);

        Grid.SetRow(countGrid, 0);
        root.Children.Add(countGrid);

        // Row 1: Spacing GroupBox
        var spacingGroup = new GroupBox
        {
            Header = "Paste spacing",
            Foreground = textBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 14)
        };

        var spacingStack = new StackPanel();

        endToEndRadio = new RadioButton
        {
            Content = "End to end",
            Foreground = textBrush,
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 6)
        };
        AutomationProperties.SetAutomationId(endToEndRadio, "PasteRepeatEndToEndRadio");
        spacingStack.Children.Add(endToEndRadio);

        evenSpacingRadio = new RadioButton
        {
            Content = "Even spacing",
            Foreground = textBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };
        AutomationProperties.SetAutomationId(evenSpacingRadio, "PasteRepeatEvenSpacingRadio");
        spacingStack.Children.Add(evenSpacingRadio);

        var evenGrid = new Grid { Margin = new Thickness(20, 0, 0, 0) };
        evenGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        evenGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        evenGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

        var evenLabel = new TextBlock
        {
            Text = "Paste every:",
            Foreground = textBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        evenGrid.Children.Add(evenLabel);

        spacingBox = new TextBox
        {
            Text = TimecodeFormatter.Format(SpacingFrames, frameRate, rulerFormat, sampleRate),
            Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)),
            Foreground = textBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
            HorizontalContentAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Height = 24,
            IsEnabled = false
        };
        AutomationProperties.SetAutomationId(spacingBox, "PasteRepeatSpacingBox");
        Grid.SetColumn(spacingBox, 1);
        evenGrid.Children.Add(spacingBox);

        var spacingSpinGrid = new Grid { Height = 24, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        spacingSpinGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        spacingSpinGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        spacingUpBtn = new Button
        {
            Content = "▲",
            FontSize = 7,
            Foreground = textBrush,
            Background = buttonBrush,
            BorderBrush = borderBrush,
            Padding = new Thickness(0),
            IsEnabled = false
        };
        spacingUpBtn.Click += (_, _) => AdjustSpacing(1);
        Grid.SetRow(spacingUpBtn, 0);
        spacingSpinGrid.Children.Add(spacingUpBtn);

        spacingDownBtn = new Button
        {
            Content = "▼",
            FontSize = 7,
            Foreground = textBrush,
            Background = buttonBrush,
            BorderBrush = borderBrush,
            Padding = new Thickness(0),
            IsEnabled = false
        };
        spacingDownBtn.Click += (_, _) => AdjustSpacing(-1);
        Grid.SetRow(spacingDownBtn, 1);
        spacingSpinGrid.Children.Add(spacingDownBtn);

        Grid.SetColumn(spacingSpinGrid, 2);
        evenGrid.Children.Add(spacingSpinGrid);

        spacingStack.Children.Add(evenGrid);

        endToEndRadio.Checked += (_, _) => SetEvenSpacingEnabled(false);
        evenSpacingRadio.Checked += (_, _) => SetEvenSpacingEnabled(true);

        spacingGroup.Content = spacingStack;
        Grid.SetRow(spacingGroup, 1);
        root.Children.Add(spacingGroup);

        // Row 3: Buttons
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var okBtn = new Button
        {
            Content = "OK",
            Width = 75,
            Height = 24,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Background = buttonBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush
        };
        AutomationProperties.SetAutomationId(okBtn, "PasteRepeatOkButton");
        okBtn.Click += (_, _) => OnOk();
        btnPanel.Children.Add(okBtn);

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 75,
            Height = 24,
            IsCancel = true,
            Background = buttonBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush
        };
        AutomationProperties.SetAutomationId(cancelBtn, "PasteRepeatCancelButton");
        btnPanel.Children.Add(cancelBtn);

        Grid.SetRow(btnPanel, 3);
        root.Children.Add(btnPanel);

        Content = root;
    }

    private void SetEvenSpacingEnabled(bool enabled)
    {
        spacingBox.IsEnabled = enabled;
        spacingUpBtn.IsEnabled = enabled;
        spacingDownBtn.IsEnabled = enabled;
    }

    private void AdjustCount(int delta)
    {
        if (int.TryParse(countBox.Text.Trim(), out var cur))
        {
            cur = Math.Clamp(cur + delta, 1, 999);
            countBox.Text = cur.ToString();
        }
    }

    private void AdjustSpacing(int deltaFrames)
    {
        SpacingFrames = Math.Max(1, SpacingFrames + deltaFrames);
        spacingBox.Text = TimecodeFormatter.Format(SpacingFrames, frameRate, rulerFormat, sampleRate);
    }

    private void OnOk()
    {
        if (!int.TryParse(countBox.Text.Trim(), out var count) || count < 1)
        {
            MessageBox.Show(this, "Please enter a valid number of times to paste (1 - 999).", "Paste Repeat", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        RepeatCount = Math.Clamp(count, 1, 999);

        if (evenSpacingRadio.IsChecked == true)
        {
            if (TimecodeFormatter.TryParse(spacingBox.Text.Trim(), frameRate, rulerFormat, out var parsedFrames, sampleRate) && parsedFrames > 0)
            {
                SpacingFrames = parsedFrames;
            }
            else if (long.TryParse(spacingBox.Text.Trim(), out var directFrames) && directFrames > 0)
            {
                SpacingFrames = directFrames;
            }
        }

        DialogResult = true;
        Close();
    }
}

