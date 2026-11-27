using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using OpenRend.Core;

namespace OpenRend.App;

public sealed partial class ColorGradingDockControl
{
    private readonly StackPanel rangePage;
    private readonly List<(string Prefix, ColorGradingWheelControl Wheel)> rangeWheels = [];
    private readonly Slider lowerThreshold = new();
    private readonly Slider upperThreshold = new();
    private readonly TextBox lowerThresholdValue = new();
    private readonly TextBox upperThresholdValue = new();
    private readonly TextBlock thresholdError = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly CheckBox showScopeLimits = new() { Content = "Show limits on scopes", VerticalAlignment = VerticalAlignment.Center };
    private Slider? thresholdDrag;
    private bool showingThresholdPreview;

    internal event Action? ScopeLimitsChanged;
    internal bool IsThresholdDragging => thresholdDrag is not null;
    internal bool IsThresholdSliderFocused => lowerThreshold.IsKeyboardFocused || upperThreshold.IsKeyboardFocused;

    internal LuminanceThresholds? ScopeLimits =>
        IsLoaded && IsVisible && rangePage.IsVisible && targetEffects is not null && showScopeLimits.IsChecked == true
            ? SelectedThresholds : null;

    private LuminanceThresholds SelectedThresholds => new(lowerThreshold.Value, upperThreshold.Value);

    private StackPanel CreateRangePage(out Grid wheels)
    {
        var page = new StackPanel();
        AutomationProperties.SetAutomationId(page, "ColorGrading_TonalRanges");
        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(3),
            Padding = new Thickness(8)
        };
        card.SetResourceReference(BorderBrushProperty, "BorderBrush");
        card.SetResourceReference(BackgroundProperty, "PanelBrush");
        var limits = new StackPanel();
        card.Child = limits;

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var reset = new Button { Content = "Reset limits" };
        AutomationProperties.SetAutomationId(reset, "RangeThresholds_Reset");
        reset.Click += (_, _) => CommitThresholds(new(TonalRangeGrading.DefaultLower, TonalRangeGrading.DefaultUpper));
        DockPanel.SetDock(reset, Dock.Right);
        header.Children.Add(reset);
        header.Children.Add(new TextBlock { Text = "Luminance thresholds", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        limits.Children.Add(header);
        limits.Children.Add(MakeThresholdRow("Lower threshold", TonalRangeGrading.LowerKey, lowerThreshold, lowerThresholdValue));
        limits.Children.Add(MakeThresholdRow("Upper threshold", TonalRangeGrading.UpperKey, upperThreshold, upperThresholdValue));
        limits.Children.Add(thresholdError);
        AutomationProperties.SetAutomationId(showScopeLimits, "RangeThresholds_ShowOnScopes");
        showScopeLimits.ToolTip = "Show the thresholds on the waveform, RGB parade, and histogram.";
        showScopeLimits.Checked += (_, _) => ScopeLimitsChanged?.Invoke();
        showScopeLimits.Unchecked += (_, _) => ScopeLimitsChanged?.Invoke();
        limits.Children.Add(showScopeLimits);
        page.Children.Add(card);

        wheels = new Grid();
        foreach (var (prefix, name) in new[]
        {
            (TonalRangeGrading.Shadows, "Shadows"), (TonalRangeGrading.Midtones, "Midtones"),
            (TonalRangeGrading.Highlights, "Highlights"), (TonalRangeGrading.Offset, "Offset")
        })
        {
            wheels.Children.Add(MakeWheelCard(name, prefix, -1, 1, 0, -1, 1, 0, -1, 1, 0, -1, 1, 0, .01, false,
                out var wheel, out _, out _, (r, g, b, y) => OnWheelGroupChanged(prefix, r, g, b, y),
                () => OnWheelGroupChanged(prefix, 0, 0, 0, 0)));
            rangeWheels.Add((prefix, wheel));
        }
        page.Children.Add(wheels);
        return page;
    }

    private FrameworkElement MakeThresholdRow(string name, string id, Slider slider, TextBox field)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        row.Children.Add(new TextBlock { Text = name, Width = 99, VerticalAlignment = VerticalAlignment.Center });
        var unit = new TextBlock { Text = "%", Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(unit, Dock.Right);
        row.Children.Add(unit);
        field.Width = 54;
        field.TextAlignment = TextAlignment.Right;
        AutomationProperties.SetAutomationId(field, id + "_Value");
        AutomationProperties.SetName(field, name + " (%)");
        DockPanel.SetDock(field, Dock.Right);
        row.Children.Add(field);

        slider.Maximum = 1;
        slider.SmallChange = .001;
        slider.LargeChange = .05;
        slider.Margin = new Thickness(6, 0, 6, 0);
        slider.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(slider, id);
        AutomationProperties.SetName(slider, name);
        row.Children.Add(slider);

        var isLower = id == TonalRangeGrading.LowerKey;
        slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) =>
        {
            slider.Focus();
            thresholdDrag = slider;
        }), true);
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, args) =>
        {
            if (thresholdDrag != slider) return;
            if (args.Canceled)
            {
                CancelThresholdDrag();
                return;
            }
            thresholdDrag = null;
            ClearThresholdPreview();
            CommitThresholds(SelectedThresholds);
        }), true);
        slider.ValueChanged += (_, _) =>
        {
            if (updating)
            {
                field.Text = FormatThreshold(slider.Value);
                return;
            }

            // Both rails represent the whole signal range. Clamp the adjustment at its neighbor.
            var minimum = isLower ? 0 : lowerThreshold.Value;
            var maximum = isLower ? upperThreshold.Value : 1;
            var constrained = Math.Clamp(slider.Value, minimum, maximum);
            if (slider.Value != constrained)
            {
                updating = true;
                try { slider.Value = constrained; }
                finally { updating = false; }
            }
            field.Text = FormatThreshold(slider.Value);
            ShowThresholdError(null);
            if (thresholdDrag == slider)
            {
                ScopeLimitsChanged?.Invoke();
                if (ActiveEffect is { IsEnabled: true } grade && TonalRangeGrading.HasAdjustments(grade))
                {
                    showingThresholdPreview = true;
                    GradingPreviewRequested?.Invoke(new(scope, targetId, grade.Id, Thresholds: SelectedThresholds));
                }
            }
            else
            {
                CommitThresholds(SelectedThresholds);
            }
        };

        slider.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape || thresholdDrag != slider) return;
            args.Handled = true;
            CancelThresholdDrag();
        };
        field.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter) CommitText(revertOnError: false);
            else if (args.Key == Key.Escape)
            {
                field.Text = FormatThreshold(slider.Value);
                ShowThresholdError(null);
            }
            else return;
            args.Handled = true;
        };
        field.LostKeyboardFocus += (_, _) => CommitText(revertOnError: true);
        return row;

        void CommitText(bool revertOnError)
        {
            if (updating || field.Text == FormatThreshold(slider.Value)) return;
            var minimum = isLower ? 0 : lowerThreshold.Value;
            var maximum = isLower ? upperThreshold.Value : 1;
            if (!double.TryParse(field.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var percent) ||
                !double.IsFinite(percent) || percent < minimum * 100 - 1e-9 || percent > maximum * 100 + 1e-9)
            {
                ShowThresholdError(isLower
                    ? $"Enter a lower threshold from 0 to {FormatThreshold(maximum)}%."
                    : $"Enter an upper threshold from {FormatThreshold(minimum)} to 100%.");
                if (revertOnError) field.Text = FormatThreshold(slider.Value);
                return;
            }

            slider.Value = Math.Round(percent / 100, 3);
            field.Text = FormatThreshold(slider.Value);
            ShowThresholdError(null);
        }
    }

    private void SyncRangeControls(VideoEffect effect)
    {
        foreach (var (prefix, wheel) in rangeWheels)
        {
            wheel.SetValues(effect.GetParameter(prefix + "_R"), effect.GetParameter(prefix + "_G"),
                effect.GetParameter(prefix + "_B"), effect.GetParameter(prefix + "_Y"));
        }
        SetThresholdControls(TonalRangeGrading.GetThresholds(effect));
        ShowThresholdError(null);
    }

    private void SetThresholdControls(LuminanceThresholds thresholds)
    {
        lowerThreshold.Value = thresholds.Lower;
        upperThreshold.Value = thresholds.Upper;
        lowerThresholdValue.Text = FormatThreshold(thresholds.Lower);
        upperThresholdValue.Text = FormatThreshold(thresholds.Upper);
    }

    private void CommitThresholds(LuminanceThresholds thresholds) => ResetParams(new()
    {
        [TonalRangeGrading.LowerKey] = thresholds.Lower,
        [TonalRangeGrading.UpperKey] = thresholds.Upper
    });

    private void CancelThresholdDrag()
    {
        if (thresholdDrag is not { } slider) return;
        thresholdDrag = null;
        if (Mouse.Captured is Thumb thumb && slider.IsAncestorOf(thumb)) thumb.CancelDrag();
        ClearThresholdPreview();
        var wasUpdating = updating;
        updating = true;
        try { SetThresholdControls(TonalRangeGrading.GetThresholds(ActiveEffect ?? NeutralGrade)); }
        finally { updating = wasUpdating; }
        ScopeLimitsChanged?.Invoke();
    }

    private void ClearThresholdPreview()
    {
        if (!showingThresholdPreview) return;
        showingThresholdPreview = false;
        GradingPreviewRequested?.Invoke(null);
    }

    private void ShowThresholdError(string? message)
    {
        thresholdError.Text = message ?? "";
        thresholdError.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string FormatThreshold(double value) => (value * 100).ToString("0.#", CultureInfo.CurrentCulture);
}
