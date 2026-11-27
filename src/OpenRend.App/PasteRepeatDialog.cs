using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using OpenRend.Core;

namespace OpenRend.App;

public sealed class PasteRepeatDialog : TimelineEditDialog
{
    private const int MaxRepeatCount = 999;
    private readonly TextBox countBox;
    private readonly RadioButton endToEndRadio;
    private readonly TextBox spacingBox;
    private readonly StackPanel spacingField;
    private readonly Rational frameRate;
    private readonly RulerFormat rulerFormat;
    private readonly int sampleRate;
    private readonly double bpm;
    private readonly int beatsPerMeasure;

    public int RepeatCount { get; private set; } = 1;
    public bool IsEndToEnd => endToEndRadio.IsChecked == true;
    public long SpacingFrames { get; private set; }

    public PasteRepeatDialog(
        long defaultSpacingFrames,
        Rational rate,
        RulerFormat format = RulerFormat.TimeAndFrames,
        int audioSampleRate = 48000,
        double beatsPerMinute = 120.0,
        int projectBeatsPerMeasure = 4)
        : base("Paste Repeat", 380)
    {
        frameRate = rate;
        rulerFormat = format;
        sampleRate = audioSampleRate;
        bpm = beatsPerMinute;
        beatsPerMeasure = projectBeatsPerMeasure;
        SpacingFrames = Math.Max(1, defaultSpacingFrames);

        countBox = TimeField("1");
        AutomationProperties.SetAutomationId(countBox, "PasteRepeatCountBox");
        Form.Children.Add(Field("Copies", countBox, WithStepButtons(countBox, "copy count", AdjustCount)));

        endToEndRadio = new RadioButton { Content = "End to end", IsChecked = true, Margin = new Thickness(0, 0, 0, 6) };
        var evenSpacingRadio = new RadioButton { Content = "Even spacing", Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(endToEndRadio, "PasteRepeatEndToEndRadio");
        AutomationProperties.SetAutomationId(evenSpacingRadio, "PasteRepeatEvenSpacingRadio");

        spacingBox = TimeField(FormatSpacing(SpacingFrames));
        AutomationProperties.SetAutomationId(spacingBox, "PasteRepeatSpacingBox");
        spacingField = Field($"Time between starts ({rulerFormat.ToDisplayName()})",
            spacingBox, WithStepButtons(spacingBox, "spacing", AdjustSpacing));
        spacingField.IsEnabled = false;
        endToEndRadio.Checked += (_, _) => spacingField.IsEnabled = false;
        evenSpacingRadio.Checked += (_, _) => spacingField.IsEnabled = true;

        var spacingOptions = new StackPanel();
        spacingOptions.Children.Add(endToEndRadio);
        spacingOptions.Children.Add(evenSpacingRadio);
        spacingOptions.Children.Add(spacingField);
        Form.Children.Add(Options("Spacing", spacingOptions));

        AutomationProperties.SetAutomationId(AcceptButton, "PasteRepeatOkButton");
        AutomationProperties.SetAutomationId(CancelButton, "PasteRepeatCancelButton");
        FocusOnOpen(countBox);
    }

    private static Grid WithStepButtons(TextBox input, string fieldName, Action<int> step)
    {
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition());
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var decrease = new Button { Content = "−", MinWidth = 28, Margin = new Thickness(4, 0, 0, 0) };
        var increase = new Button { Content = "+", MinWidth = 28, Margin = new Thickness(4, 0, 0, 0) };
        AutomationProperties.SetName(decrease, $"Decrease {fieldName}");
        AutomationProperties.SetName(increase, $"Increase {fieldName}");
        decrease.Click += (_, _) => step(-1);
        increase.Click += (_, _) => step(1);
        Grid.SetColumn(decrease, 1);
        Grid.SetColumn(increase, 2);
        panel.Children.Add(input);
        panel.Children.Add(decrease);
        panel.Children.Add(increase);

        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Up or Key.Down)) return;
            step(e.Key == Key.Up ? 1 : -1);
            e.Handled = true;
        };
        return panel;
    }

    private void AdjustCount(int delta)
    {
        var count = int.TryParse(countBox.Text, out var current) ? current : RepeatCount;
        countBox.Text = Math.Clamp((long)count + delta, 1, MaxRepeatCount).ToString(CultureInfo.CurrentCulture);
        countBox.SelectAll();
    }

    private void AdjustSpacing(int delta)
    {
        var current = TryParseSpacing(out var frames) ? frames : SpacingFrames;
        if (delta > 0 && current == long.MaxValue) return;
        spacingBox.Text = FormatSpacing(Math.Max(1, current + delta));
        spacingBox.SelectAll();
    }

    private string FormatSpacing(long frames) =>
        TimecodeFormatter.Format(frames, frameRate, rulerFormat, sampleRate, bpm, beatsPerMeasure);

    private bool TryParseSpacing(out long frames) =>
        TimecodeFormatter.TryParse(spacingBox.Text.Trim(), frameRate, rulerFormat, out frames, sampleRate, bpm, beatsPerMeasure)
        && frames > 0;

    protected override bool TryAccept()
    {
        if (!int.TryParse(countBox.Text.Trim(), out var count) || count is < 1 or > MaxRepeatCount)
            return Reject(countBox, $"Enter a copy count from 1 to {MaxRepeatCount}.");

        if (!IsEndToEnd)
        {
            if (!TryParseSpacing(out var frames))
                return Reject(spacingBox, "Enter spacing greater than zero.");
            SpacingFrames = frames;
        }

        RepeatCount = count;
        return true;
    }
}
