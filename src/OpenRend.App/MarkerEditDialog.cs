using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OpenRend.Core;

namespace OpenRend.App;

public sealed class MarkerEditDialog : TimelineEditDialog
{
    private readonly TextBox labelBox;
    private readonly ComboBox colorPicker;

    public string ResultLabel => labelBox.Text.Trim();
    public int ResultColorId => colorPicker.SelectedIndex;

    public MarkerEditDialog(TimelineMarker marker, ProjectDocument project)
        : base($"Edit Marker {marker.Index}", 350)
    {
        var time = TimecodeFormatter.Format(marker.Frame, project.FrameRate, project.RulerFormat,
            project.AudioSampleRate, project.BeatsPerMinute, project.BeatsPerMeasure, project.RulerStartTimeFrames);
        var position = new TextBlock { Text = $"Position {time}", Margin = new Thickness(0, 0, 0, 10) };
        position.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        AutomationProperties.SetAutomationId(position, "MarkerPosition");
        Form.Children.Add(position);

        labelBox = new TextBox { Text = marker.Label };
        AutomationProperties.SetAutomationId(labelBox, "AnnotationLabel");
        Form.Children.Add(Field("Label", labelBox));

        colorPicker = TimelineAnnotationColors.CreatePicker(marker.ColorId);
        Form.Children.Add(Field("Color", colorPicker));
        FocusOnOpen(labelBox);
    }

    protected override bool TryAccept() => true;
}
