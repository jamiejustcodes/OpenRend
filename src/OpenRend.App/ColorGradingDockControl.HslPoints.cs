using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using OpenRend.Core;

namespace OpenRend.App;

public sealed partial class ColorGradingDockControl
{
    private static FrameworkElement CreateHslPointFields(HSLCurvesControl graph)
    {
        var panel = new StackPanel();
        var fields = new WrapPanel();
        panel.Children.Add(fields);
        var input = AddField("ColorGrading_HslPointInput");
        var adjustment = AddField("ColorGrading_HslPointAdjustment");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        error.SetResourceReference(TextBlock.ForegroundProperty, "ValidationTextBrush");
        AutomationProperties.SetAutomationId(error, "ColorGrading_HslPointError");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        panel.Children.Add(error);

        WireField(input.Box, isInput: true);
        WireField(adjustment.Box, isInput: false);
        graph.PointChanged += UpdateFields;
        UpdateFields();
        return panel;

        (TextBox Box, TextBlock Label) AddField(string id)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 4) };
            var label = new TextBlock { Width = 128, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var box = new TextBox { Width = 58, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAutomationId(box, id);
            AutomationProperties.SetLabeledBy(box, label);
            row.Children.Add(label);
            row.Children.Add(box);
            fields.Children.Add(row);
            return (box, label);
        }

        void WireField(TextBox box, bool isInput)
        {
            box.PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter) Commit(box, isInput, reportError: true);
                else if (args.Key == Key.Escape) UpdateFields();
                else return;
                args.Handled = true;
            };
            box.LostKeyboardFocus += (_, _) => Commit(box, isInput, reportError: false);
        }

        void UpdateFields()
        {
            input.Label.Text = graph.ActiveMode switch
            {
                HSLCurveMode.LumVsSat => "Luminance (%)",
                _ => graph.IsHueMode ? "Hue (°)" : "Saturation (%)"
            };
            adjustment.Label.Text = graph.ActiveMode switch
            {
                HSLCurveMode.HueVsHue => "Hue shift (°)",
                HSLCurveMode.HueVsLum or HSLCurveMode.SatVsLum => "Luminance change (%)",
                _ => "Saturation change (%)"
            };
            var point = graph.SelectedPoint;
            input.Box.IsEnabled = adjustment.Box.IsEnabled = point is not null;
            input.Box.IsReadOnly = !graph.CanMoveSelectedInput;
            input.Box.Text = point is { } p ? Format(p.X * InputScale()) : "";
            adjustment.Box.Text = point is { } q ? Format((q.Y - .5) * AdjustmentScale()) : "";
            error.Visibility = Visibility.Collapsed;
        }

        void Commit(TextBox box, bool isInput, bool reportError)
        {
            if (!box.IsEnabled || box.IsReadOnly || graph.SelectedPoint is not { } point) return;
            var scale = isInput ? InputScale() : AdjustmentScale();
            var value = isInput ? point.X * scale : (point.Y - .5) * scale;
            if (box.Text == Format(value)) return;
            var minimum = isInput ? 0 : -scale / 2;
            var maximum = isInput ? scale : scale / 2;
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) ||
                !double.IsFinite(number) || number < minimum || number > maximum)
            {
                ShowError($"Enter a value from {Format(minimum)} to {Format(maximum)}.");
                return;
            }
            if (graph.SetSelectedCoordinate(isInput, number / scale + (isInput ? 0 : .5))) UpdateFields();
            else ShowError(graph.IsHueMode ? "A point already uses that hue." : "Choose an input between adjacent points.");

            void ShowError(string message)
            {
                if (!reportError) { UpdateFields(); return; }
                error.Text = message;
                error.Visibility = Visibility.Visible;
            }
        }

        double InputScale() => graph.IsHueMode ? 360 : 100;
        double AdjustmentScale() => graph.ActiveMode switch
        {
            HSLCurveMode.HueVsHue => 360,
            HSLCurveMode.HueVsLum or HSLCurveMode.SatVsLum => 100,
            _ => 200
        };
        static string Format(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);
    }
}
