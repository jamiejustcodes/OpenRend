using System.Text.Json;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class RulerFormatEditorTests
{
    private static (EditorViewModel Vm, ProjectDocument Doc) CreateVm(Rational? rate = null)
    {
        var r = rate ?? new Rational(30000, 1001);
        var doc = new ProjectDocument
        {
            Name = "Ruler Test Project",
            Width = 1920,
            Height = 1080,
            FrameRate = r,
            AudioSampleRate = 48000,
            BeatsPerMinute = 120.0,
            BeatsPerMeasure = 4,
            RulerFormat = RulerFormat.TimeAndFrames
        };

        var mediaService = new MediaService();
        var renderer = new TimelineRenderService(mediaService);
        var exportService = new ExportService(renderer);
        var vm = new EditorViewModel(mediaService, renderer, exportService);
        vm.Replace(doc, null);
        return (vm, doc);
    }

    [Fact]
    public void Format_Time_ProducesHoursMinutesSecondsMilliseconds()
    {
        var rate = new Rational(30, 1);
        // 0 frames = 0s
        Assert.Equal("00:00:00.000", TimecodeFormatter.Format(0, rate, RulerFormat.Time));
        // 45 frames = 1.5s
        Assert.Equal("00:00:01.500", TimecodeFormatter.Format(45, rate, RulerFormat.Time));
        // 3600 frames = 120s = 2 minutes
        Assert.Equal("00:02:00.000", TimecodeFormatter.Format(3600, rate, RulerFormat.Time));
    }

    [Fact]
    public void Format_Seconds_ProducesDecimalSecondsAndRulerTicks()
    {
        var rate = new Rational(25, 1);
        // 50 frames = 2.0s
        Assert.Equal("2.000", TimecodeFormatter.Format(50, rate, RulerFormat.Seconds));
        Assert.Equal("2.000 s", TimecodeFormatter.Format(50, rate, RulerFormat.Seconds, rulerTick: true));
        // 12.5s = 312.5 frames (312 frames ~ 12.48s)
        Assert.Equal("1.000 s", TimecodeFormatter.Format(25, rate, RulerFormat.Seconds, rulerTick: true));
    }

    [Fact]
    public void Format_TimeAndFrames_UsesDotFrameDelimiter()
    {
        var rate = new Rational(30, 1);
        Assert.Equal("00:00:00.00", TimecodeFormatter.Format(0, rate, RulerFormat.TimeAndFrames));
        Assert.Equal("00:00:01.15", TimecodeFormatter.Format(45, rate, RulerFormat.TimeAndFrames));
        Assert.Equal("00:01:05.10", TimecodeFormatter.Format(1960, rate, RulerFormat.TimeAndFrames));
    }

    [Fact]
    public void Format_AbsoluteFrames_FormatsWithThousandsSeparators()
    {
        var rate = new Rational(30, 1);
        Assert.Equal("0", TimecodeFormatter.Format(0, rate, RulerFormat.AbsoluteFrames));
        Assert.Equal("1,250", TimecodeFormatter.Format(1250, rate, RulerFormat.AbsoluteFrames));
        Assert.Equal("125,000", TimecodeFormatter.Format(125000, rate, RulerFormat.AbsoluteFrames));
    }

    [Fact]
    public void Format_MeasuresAndBeats_CalculatesFromBpmAndBeatsPerMeasure()
    {
        var rate = new Rational(30, 1);
        // At 120 BPM in 4/4, 1 beat = 0.5s = 15 frames. 1 measure = 4 beats = 2.0s = 60 frames.
        // Frame 0: Measure 1, Beat 1, Tick 000
        Assert.Equal("1.1.000", TimecodeFormatter.Format(0, rate, RulerFormat.MeasuresAndBeats, bpm: 120, beatsPerMeasure: 4));
        // Frame 15: Measure 1, Beat 2, Tick 000
        Assert.Equal("1.2.000", TimecodeFormatter.Format(15, rate, RulerFormat.MeasuresAndBeats, bpm: 120, beatsPerMeasure: 4));
        // Frame 60: Measure 2, Beat 1, Tick 000
        Assert.Equal("2.1.000", TimecodeFormatter.Format(60, rate, RulerFormat.MeasuresAndBeats, bpm: 120, beatsPerMeasure: 4));
        // Frame 75: Measure 2, Beat 2, Tick 000
        Assert.Equal("2.2.000", TimecodeFormatter.Format(75, rate, RulerFormat.MeasuresAndBeats, bpm: 120, beatsPerMeasure: 4));
    }

    [Fact]
    public void Format_FilmStandards_16mmAnd35mm()
    {
        var rate = new Rational(24, 1);
        // 16mm: 40 frames per foot
        Assert.Equal("0+00", TimecodeFormatter.Format(0, rate, RulerFormat.FeetAndFrames16mm));
        Assert.Equal("1+00", TimecodeFormatter.Format(40, rate, RulerFormat.FeetAndFrames16mm));
        Assert.Equal("2+15", TimecodeFormatter.Format(95, rate, RulerFormat.FeetAndFrames16mm));

        // 35mm: 16 frames per foot
        Assert.Equal("0+00", TimecodeFormatter.Format(0, rate, RulerFormat.FeetAndFrames35mm));
        Assert.Equal("1+00", TimecodeFormatter.Format(16, rate, RulerFormat.FeetAndFrames35mm));
        Assert.Equal("5+14", TimecodeFormatter.Format(94, rate, RulerFormat.FeetAndFrames35mm));
    }

    [Fact]
    public void Format_SmpteVariants_DropNonDropAndBroadcastRates()
    {
        var ntsc = new Rational(30000, 1001);

        // SMPTE Non-Drop (nominal 30 fps): colons
        Assert.Equal("00:00:01:00", TimecodeFormatter.Format(30, ntsc, RulerFormat.SmpteNonDrop));

        // SMPTE Drop (nominal 30 fps): semicolons
        Assert.Equal("00:00:01;00", TimecodeFormatter.Format(30, ntsc, RulerFormat.SmpteDrop));

        // SMPTE EBU: 25 fps
        var pal = new Rational(25, 1);
        Assert.Equal("00:00:01:00", TimecodeFormatter.Format(25, pal, RulerFormat.SmpteEBU));

        // SMPTE Film Sync: 24 fps
        var film = new Rational(24, 1);
        Assert.Equal("00:00:01:00", TimecodeFormatter.Format(24, film, RulerFormat.SmpteFilmSync));

        // SMPTE 30: 30 fps
        var fps30 = new Rational(30, 1);
        Assert.Equal("00:00:01:00", TimecodeFormatter.Format(30, fps30, RulerFormat.Smpte30));
    }

    [Fact]
    public void Format_AudioCDTime_Uses75FpsRedBookStandard()
    {
        var rate = new Rational(30, 1);
        // 1 second (30 frames at 30fps) = 75 CD sectors
        Assert.Equal("00:01:00", TimecodeFormatter.Format(30, rate, RulerFormat.AudioCDTime));
        // 2 seconds = 150 CD sectors = 00:02:00
        Assert.Equal("00:02:00", TimecodeFormatter.Format(60, rate, RulerFormat.AudioCDTime));
    }

    [Fact]
    public void Format_Samples_CalculatesFromAudioSampleRate()
    {
        var rate = new Rational(30, 1);
        // 1 second (30 frames) at 48000 Hz = 48,000 samples
        Assert.Equal("48,000", TimecodeFormatter.Format(30, rate, RulerFormat.Samples, sampleRate: 48000));
        // 2 seconds at 96000 Hz = 192,000 samples
        Assert.Equal("192,000", TimecodeFormatter.Format(60, rate, RulerFormat.Samples, sampleRate: 96000));
    }

    [Fact]
    public void TryParse_RoundTrips_MultipleFormats()
    {
        var rate = new Rational(30, 1);

        // Absolute frames
        Assert.True(TimecodeFormatter.TryParse("1,250", rate, RulerFormat.AbsoluteFrames, out var f1));
        Assert.Equal(1250, f1);

        // Seconds
        Assert.True(TimecodeFormatter.TryParse("2.500 s", rate, RulerFormat.Seconds, out var f2));
        Assert.Equal(75, f2);

        // Time and frames
        Assert.True(TimecodeFormatter.TryParse("00:00:02.15", rate, RulerFormat.TimeAndFrames, out var f3));
        Assert.Equal(75, f3);

        // 16mm Feet & Frames (2 feet + 15 frames = 2 * 40 + 15 = 95)
        Assert.True(TimecodeFormatter.TryParse("2+15", rate, RulerFormat.FeetAndFrames16mm, out var f4));
        Assert.Equal(95, f4);

        // 35mm Feet & Frames (3 feet + 4 frames = 3 * 16 + 4 = 52)
        Assert.True(TimecodeFormatter.TryParse("3+04", rate, RulerFormat.FeetAndFrames35mm, out var f5));
        Assert.Equal(52, f5);

        // Measures & beats (2.1.000 at 120 bpm, 4/4 = 1 measure elapsed = 4 beats = 2.0s = 60 frames)
        Assert.True(TimecodeFormatter.TryParse("2.1.000", rate, RulerFormat.MeasuresAndBeats, out var f6, bpm: 120, beatsPerMeasure: 4));
        Assert.Equal(60, f6);
    }

    [Fact]
    public void ProjectDocument_RulerFormat_SerializesAndSyncsWithString()
    {
        var doc = new ProjectDocument
        {
            Name = "Doc",
            RulerFormat = RulerFormat.AbsoluteFrames,
            BeatsPerMinute = 135.0,
            BeatsPerMeasure = 3,
            RulerStartTimeFrames = 100
        };

        // Getter returns display name
        Assert.Equal("Absolute Frames", doc.RulerTimeFormat);

        // Setter updates enum
        doc.RulerTimeFormat = "Time & Frames";
        Assert.Equal(RulerFormat.TimeAndFrames, doc.RulerFormat);

        doc.RulerTimeFormat = "SMPTE Drop (29.97 fps, Video)";
        Assert.Equal(RulerFormat.SmpteDrop, doc.RulerFormat);

        // DeepClone preserves all properties
        var clone = doc.DeepClone();
        Assert.Equal(doc.RulerFormat, clone.RulerFormat);
        Assert.Equal(doc.RulerTimeFormat, clone.RulerTimeFormat);
        Assert.Equal(doc.RulerStartTimeFrames, clone.RulerStartTimeFrames);
        Assert.Equal(doc.BeatsPerMinute, clone.BeatsPerMinute);
        Assert.Equal(doc.BeatsPerMeasure, clone.BeatsPerMeasure);

        // JSON round-trip
        var json = JsonSerializer.Serialize(doc);
        var deserialized = JsonSerializer.Deserialize<ProjectDocument>(json)!;
        Assert.Equal(doc.RulerFormat, deserialized.RulerFormat);
        Assert.Equal(doc.RulerTimeFormat, deserialized.RulerTimeFormat);
    }

    [Fact]
    public void EditorViewModel_SetRulerFormat_UpdatesProjectAndStatus()
    {
        var (vm, doc) = CreateVm();
        bool changedFired = false;
        vm.Changed += () => changedFired = true;

        vm.SetRulerFormat(RulerFormat.MeasuresAndBeats);

        Assert.True(changedFired);
        Assert.Equal(RulerFormat.MeasuresAndBeats, vm.Project.RulerFormat);
        Assert.Equal("Measures & Beats", vm.Project.RulerTimeFormat);
        Assert.Contains("Measures & Beats", vm.Status);
    }

    [Fact]
    public void EditorViewModel_SetTimeAtCursor_RepositionsCursorAndUpdatesStatus()
    {
        var (vm, doc) = CreateVm(new Rational(30, 1));
        doc.Tracks.Add(new TimelineTrack
        {
            Kind = TrackKind.Video,
            Clips = [new TimelineClip { StartFrame = 0, DurationFrames = 300 }]
        });

        vm.SetRulerFormat(RulerFormat.TimeAndFrames);
        vm.SetTimeAtCursor(90);

        Assert.Equal(90, vm.Position);
        Assert.Contains("00:00:03.00", vm.Status);
    }
}
