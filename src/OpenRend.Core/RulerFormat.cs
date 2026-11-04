using System.Text.Json.Serialization;

namespace OpenRend.Core;

/// <summary>
/// Vegas Pro 1:1 timeline ruler and timecode display formats.
/// Corresponds to ScriptPortal.Vegas.RulerFormat.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RulerFormat>))]
public enum RulerFormat
{
    Samples = 1,
    Time = 2,
    Seconds = 3,
    TimeAndFrames = 4,
    AbsoluteFrames = 5,
    MeasuresAndBeats = 6,
    FeetAndFrames16mm = 7,
    FeetAndFrames35mm = 8,
    SmpteFilmSyncIVTC = 9,
    SmpteFilmSync = 10,
    SmpteEBU = 11,
    SmpteNonDrop = 12,
    SmpteDrop = 13,
    Smpte30 = 14,
    AudioCDTime = 15,
    Nanoseconds = 16,
    Microseconds = 17
}

public static class RulerFormatExtensions
{
    /// <summary>
    /// Standard ordered list of ruler formats matching Vegas Pro Options -> Ruler Format menu.
    /// </summary>
    public static readonly RulerFormat[] AllStandardFormats =
    [
        RulerFormat.Samples,
        RulerFormat.Time,
        RulerFormat.Seconds,
        RulerFormat.TimeAndFrames,
        RulerFormat.AbsoluteFrames,
        RulerFormat.MeasuresAndBeats,
        RulerFormat.FeetAndFrames16mm,
        RulerFormat.FeetAndFrames35mm,
        RulerFormat.SmpteFilmSyncIVTC,
        RulerFormat.SmpteFilmSync,
        RulerFormat.SmpteEBU,
        RulerFormat.SmpteNonDrop,
        RulerFormat.SmpteDrop,
        RulerFormat.Smpte30,
        RulerFormat.AudioCDTime
    ];

    public static string ToDisplayName(this RulerFormat format) => format switch
    {
        RulerFormat.Samples => "Samples",
        RulerFormat.Time => "Time",
        RulerFormat.Seconds => "Seconds",
        RulerFormat.TimeAndFrames => "Time & Frames",
        RulerFormat.AbsoluteFrames => "Absolute Frames",
        RulerFormat.MeasuresAndBeats => "Measures & Beats",
        RulerFormat.FeetAndFrames16mm => "Feet and Frames 16mm (40 fpf)",
        RulerFormat.FeetAndFrames35mm => "Feet and Frames 35mm (16 fpf)",
        RulerFormat.SmpteFilmSyncIVTC => "SMPTE Film Sync IVTC (23.976 fps, Video)",
        RulerFormat.SmpteFilmSync => "SMPTE Film Sync (24 fps, Film)",
        RulerFormat.SmpteEBU => "SMPTE EBU (25 fps, Video)",
        RulerFormat.SmpteNonDrop => "SMPTE Non-Drop (29.97 fps, Video)",
        RulerFormat.SmpteDrop => "SMPTE Drop (29.97 fps, Video)",
        RulerFormat.Smpte30 => "SMPTE 30 (30 fps, Audio)",
        RulerFormat.AudioCDTime => "Audio CD Time",
        RulerFormat.Nanoseconds => "Nanoseconds",
        RulerFormat.Microseconds => "Microseconds",
        _ => "Time & Frames"
    };

    public static string ToMenuHeader(this RulerFormat format) => format switch
    {
        RulerFormat.Samples => "&Samples",
        RulerFormat.Time => "&Time",
        RulerFormat.Seconds => "Se&conds",
        RulerFormat.TimeAndFrames => "Time && Fra&mes",
        RulerFormat.AbsoluteFrames => "Absol&ute Frames",
        RulerFormat.MeasuresAndBeats => "Measures && &Beats",
        RulerFormat.FeetAndFrames16mm => "Feet and Frames &16mm (40 fpf)",
        RulerFormat.FeetAndFrames35mm => "Feet and Frames &35mm (16 fpf)",
        RulerFormat.SmpteFilmSyncIVTC => "SMPTE Fi&lm Sync IVTC (23.976 fps, Video)",
        RulerFormat.SmpteFilmSync => "SMPTE &Film Sync (24 fps, Film)",
        RulerFormat.SmpteEBU => "SMPTE &EBU (25 fps, Video)",
        RulerFormat.SmpteNonDrop => "SMPTE &Non-Drop (29.97 fps, Video)",
        RulerFormat.SmpteDrop => "SMPTE &Drop (29.97 fps, Video)",
        RulerFormat.Smpte30 => "SM&PTE 30 (30 fps, Audio)",
        RulerFormat.AudioCDTime => "&Audio CD Time",
        RulerFormat.Nanoseconds => "Nanoseconds",
        RulerFormat.Microseconds => "Microseconds",
        _ => format.ToDisplayName()
    };

    public static bool TryParseDisplayName(string? name, out RulerFormat format)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            format = RulerFormat.TimeAndFrames;
            return false;
        }

        var clean = System.Text.RegularExpressions.Regex.Replace(name.Replace("&", " and ").Replace("_", " "), @"\s+", " ").Trim();

        if (clean.Equals("Timecode (SMPTE)", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("SMPTE", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.SmpteDrop;
            return true;
        }

        if (clean.Contains("Drop", StringComparison.OrdinalIgnoreCase) &&
            !clean.Contains("Non-Drop", StringComparison.OrdinalIgnoreCase) &&
            !clean.Contains("NonDrop", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.SmpteDrop;
            return true;
        }

        if (clean.Contains("Non-Drop", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("NonDrop", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.SmpteNonDrop;
            return true;
        }

        if (clean.Contains("EBU", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.SmpteEBU;
            return true;
        }

        if (clean.Contains("IVTC", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("23.976", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.SmpteFilmSyncIVTC;
            return true;
        }

        if (clean.Contains("Film Sync", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("FilmSync", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.SmpteFilmSync;
            return true;
        }

        if (clean.Contains("SMPTE 30", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("SMPTE30", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.Smpte30;
            return true;
        }

        if (clean.Contains("Time", StringComparison.OrdinalIgnoreCase) &&
            clean.Contains("Frame", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.TimeAndFrames;
            return true;
        }

        if (clean.Contains("Absolute Frames", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Frames", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.AbsoluteFrames;
            return true;
        }

        if (clean.Contains("Measures", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Beats", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.MeasuresAndBeats;
            return true;
        }

        if (clean.Contains("16mm", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.FeetAndFrames16mm;
            return true;
        }

        if (clean.Contains("35mm", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.FeetAndFrames35mm;
            return true;
        }

        if (clean.Contains("CD", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.AudioCDTime;
            return true;
        }

        if (clean.Equals("Seconds", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.Seconds;
            return true;
        }

        if (clean.Equals("Samples", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.Samples;
            return true;
        }

        if (clean.Equals("Time", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.Time;
            return true;
        }

        if (clean.Contains("Nano", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.Nanoseconds;
            return true;
        }

        if (clean.Contains("Micro", StringComparison.OrdinalIgnoreCase))
        {
            format = RulerFormat.Microseconds;
            return true;
        }

        return Enum.TryParse(name, ignoreCase: true, out format);
    }
}
