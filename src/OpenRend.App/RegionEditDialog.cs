using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OpenRend.Core;

namespace OpenRend.App;

public sealed class RegionEditDialog : TimelineEditDialog
{
    private readonly TextBox labelBox;
    private readonly TextBox startBox;
    private readonly TextBox endBox;
    private readonly ComboBox colorPicker;
    private readonly TextBlock length;

    public string ResultLabel => labelBox.Text.Trim();
    public int ResultColorId => colorPicker.SelectedIndex;
    public long ResultStartFrame { get; private set; }
    public long ResultEndFrame { get; private set; }

    public RegionEditDialog(TimelineRegion region)
        : base($"Edit Region {region.Index}", 350)
    {
        ResultStartFrame = region.StartFrame;
        ResultEndFrame = region.EndFrame;

        labelBox = new TextBox { Text = region.Label };
        AutomationProperties.SetAutomationId(labelBox, "AnnotationLabel");
        Form.Children.Add(Field("Label", labelBox));

        startBox = TimeField(region.StartFrame.ToString(CultureInfo.CurrentCulture));
        endBox = TimeField(region.EndFrame.ToString(CultureInfo.CurrentCulture));
        AutomationProperties.SetAutomationId(startBox, "RegionStartFrame");
        AutomationProperties.SetAutomationId(endBox, "RegionEndFrame");

        var range = new Grid();
        range.ColumnDefinitions.Add(new ColumnDefinition());
        range.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        range.ColumnDefinitions.Add(new ColumnDefinition());
        var endField = Field("End frame", endBox);
        Grid.SetColumn(endField, 2);
        range.Children.Add(Field("Start frame", startBox));
        range.Children.Add(endField);
        Form.Children.Add(range);

        length = new TextBlock { Margin = new Thickness(0, 0, 0, 10) };
        length.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        AutomationProperties.SetAutomationId(length, "RegionLength");
        Form.Children.Add(length);
        startBox.TextChanged += (_, _) => UpdateLength();
        endBox.TextChanged += (_, _) => UpdateLength();
        UpdateLength();

        colorPicker = TimelineAnnotationColors.CreatePicker(region.ColorId);
        Form.Children.Add(Field("Color", colorPicker));
        FocusOnOpen(labelBox);
    }

    protected override bool TryAccept()
    {
        if (!TryReadFrame(startBox, out var start))
            return Reject(startBox, "Enter a nonnegative start frame.");
        if (!TryReadFrame(endBox, out var end) || end <= start)
            return Reject(endBox, "Enter an end frame after the start frame.");

        ResultStartFrame = start;
        ResultEndFrame = end;
        return true;
    }

    private void UpdateLength()
    {
        length.Text = TryReadFrame(startBox, out var start) && TryReadFrame(endBox, out var end) && end > start
            ? $"Length {end - start:N0} frames"
            : "Length —";
    }

    private static bool TryReadFrame(TextBox input, out long frame) =>
        long.TryParse(input.Text, out frame) && frame >= 0;
}
