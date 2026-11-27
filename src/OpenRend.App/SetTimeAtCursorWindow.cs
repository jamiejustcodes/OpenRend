using System.Windows.Automation;
using System.Windows.Controls;
using OpenRend.Core;

namespace OpenRend.App;

public sealed class SetTimeAtCursorWindow : TimelineEditDialog
{
    private readonly TextBox timecodeBox;
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
        : base("Set Time at Cursor", 350)
    {
        frameRate = rate;
        rulerFormat = format;
        sampleRate = audioSampleRate;
        bpm = beatsPerMinute;
        beatsPerMeasure = projectBeatsPerMeasure;
        startTimeFrames = projectStartTimeFrames;
        maxFrames = Math.Max(0, maxProjectFrames);

        timecodeBox = TimeField(TimecodeFormatter.Format(
            currentFrame, frameRate, rulerFormat, sampleRate, bpm, beatsPerMeasure, startTimeFrames));
        AutomationProperties.SetAutomationId(timecodeBox, "CursorTimeField");
        Form.Children.Add(Field($"Cursor position ({rulerFormat.ToDisplayName()})", timecodeBox));
        FocusOnOpen(timecodeBox);
    }

    protected override bool TryAccept()
    {
        if (!TimecodeFormatter.TryParse(
            timecodeBox.Text.Trim(), frameRate, rulerFormat, out var parsedFrame,
            sampleRate, bpm, beatsPerMeasure, startTimeFrames))
        {
            return Reject(timecodeBox, "Enter a valid cursor position.");
        }

        ResultFrame = Math.Clamp(parsedFrame, 0, maxFrames);
        return true;
    }
}
