using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OpenRend.Core;

namespace OpenRend.App;

public sealed class InsertTimeDialog : TimelineEditDialog
{
    private readonly TextBox durationBox;
    private readonly RadioButton affectedTracksRadio;
    private readonly RadioButton affectedTracksMarkersRadio;
    private readonly Rational frameRate;
    private readonly RulerFormat rulerFormat;
    private readonly int sampleRate;
    private readonly double bpm;
    private readonly int beatsPerMeasure;

    public long DurationFrames { get; private set; }
    public AutoRippleMode SelectedRippleMode { get; private set; }

    public InsertTimeDialog(
        long initialDurationFrames,
        AutoRippleMode currentRippleMode,
        Rational rate,
        RulerFormat format,
        int audioSampleRate = 48000,
        double beatsPerMinute = 120.0,
        int projectBeatsPerMeasure = 4)
        : base("Insert Time", 380)
    {
        frameRate = rate;
        rulerFormat = format;
        sampleRate = audioSampleRate;
        bpm = beatsPerMinute;
        beatsPerMeasure = projectBeatsPerMeasure;
        DurationFrames = Math.Max(1, initialDurationFrames);
        SelectedRippleMode = currentRippleMode;

        durationBox = TimeField(TimecodeFormatter.Format(
            DurationFrames, frameRate, rulerFormat, sampleRate, bpm, beatsPerMeasure));
        AutomationProperties.SetAutomationId(durationBox, "InsertTimeDurationField");
        Form.Children.Add(Field($"Duration ({rulerFormat.ToDisplayName()})", durationBox));

        affectedTracksRadio = RippleOption("Affected tracks", AutoRippleMode.AffectedTracks);
        affectedTracksMarkersRadio = RippleOption("Affected tracks, markers and regions", AutoRippleMode.AffectedTracksAndMarkers);
        var allTracksRadio = RippleOption("All tracks, markers and regions", AutoRippleMode.AllTracksAndMarkers);
        var rippleOptions = new StackPanel();
        rippleOptions.Children.Add(affectedTracksRadio);
        rippleOptions.Children.Add(affectedTracksMarkersRadio);
        rippleOptions.Children.Add(allTracksRadio);
        Form.Children.Add(Options("Ripple scope", rippleOptions));
        FocusOnOpen(durationBox);
    }

    private RadioButton RippleOption(string name, AutoRippleMode mode) => new()
    {
        Content = name,
        IsChecked = SelectedRippleMode == mode,
        Margin = new Thickness(0, 0, 0, 6)
    };

    protected override bool TryAccept()
    {
        var text = durationBox.Text.Trim();
        if (!TimecodeFormatter.TryParse(text, frameRate, rulerFormat, out var frames, sampleRate, bpm, beatsPerMeasure))
        {
            return Reject(durationBox, "Enter a valid duration.");
        }

        if (frames <= 0)
            return Reject(durationBox, "Duration must be greater than zero.");

        DurationFrames = frames;
        SelectedRippleMode = affectedTracksRadio.IsChecked == true
            ? AutoRippleMode.AffectedTracks
            : affectedTracksMarkersRadio.IsChecked == true
                ? AutoRippleMode.AffectedTracksAndMarkers
                : AutoRippleMode.AllTracksAndMarkers;
        return true;
    }
}
