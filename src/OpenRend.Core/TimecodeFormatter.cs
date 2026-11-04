using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenRend.Core;

/// <summary>
/// Vegas Pro 1:1 timecode formatting and parsing engine.
/// Operates on rational project frame rates and integer frames.
/// </summary>
public static class TimecodeFormatter
{
    private static readonly Regex TimecodeRegex = new(
        @"^(?:(?:(?<h>\d+):)?(?<m>\d{1,2}):)?(?<s>\d{1,2})(?:[.:;](?<f>\d+))?$",
        RegexOptions.Compiled);

    private static readonly Regex FeetFramesRegex = new(
        @"^(?<feet>\d+)\+(?<frames>\d{1,2})$",
        RegexOptions.Compiled);

    private static readonly Regex MeasuresBeatsRegex = new(
        @"^(?<m>\d+)\.(?<b>\d+)(?:\.(?<t>\d+))?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Legacy/standard OpenRend timecode format (SMPTE drop for 29.97/59.94, non-drop colon for standard rates).
    /// </summary>
    public static string FormatStandard(long frame, Rational rate)
    {
        var fps = Math.Max(1, (int)Math.Round(rate.Numerator / (double)rate.Denominator));
        var drop = rate.Denominator == 1001 && rate.Numerator is 30000 or 60000 ? fps / 15 : 0;
        if (drop > 0)
        {
            var tenMinutes = fps * 600 - drop * 9;
            var remainder = frame % tenMinutes;
            frame += drop * 9 * (frame / tenMinutes) + (remainder >= drop ? drop * ((remainder - drop) / (fps * 60 - drop)) : 0);
        }
        var total = frame / fps;
        return $"{total / 3600:00}:{total / 60 % 60:00}:{total % 60:00}{(drop > 0 ? ';' : ':')}{frame % fps:00}";
    }

    /// <summary>
    /// Formats an integer timeline frame according to a Vegas Pro RulerFormat.
    /// </summary>
    public static string Format(
        long frame,
        Rational rate,
        RulerFormat format = RulerFormat.TimeAndFrames,
        int sampleRate = 48000,
        double bpm = 120.0,
        int beatsPerMeasure = 4,
        long startTimeFrames = 0,
        bool rulerTick = false)
    {
        var effectiveFrame = Math.Max(0, frame + startTimeFrames);

        switch (format)
        {
            case RulerFormat.Time:
            {
                var seconds = rate.Seconds(effectiveFrame);
                var totalSec = (long)Math.Floor(seconds);
                var ms = (int)Math.Round((seconds - totalSec) * 1000);
                if (ms >= 1000) { totalSec++; ms -= 1000; }
                return $"{totalSec / 3600:00}:{totalSec / 60 % 60:00}:{totalSec % 60:00}.{ms:000}";
            }

            case RulerFormat.Seconds:
            {
                var seconds = rate.Seconds(effectiveFrame);
                return rulerTick
                    ? $"{seconds.ToString("0.000", CultureInfo.InvariantCulture)} s"
                    : seconds.ToString("0.000", CultureInfo.InvariantCulture);
            }

            case RulerFormat.TimeAndFrames:
            {
                var fps = Math.Max(1, (int)Math.Round(rate.Numerator / (double)rate.Denominator));
                var totalSec = effectiveFrame / fps;
                var remFrames = effectiveFrame % fps;
                return $"{totalSec / 3600:00}:{totalSec / 60 % 60:00}:{totalSec % 60:00}.{remFrames:00}";
            }

            case RulerFormat.AbsoluteFrames:
                return effectiveFrame.ToString("N0", CultureInfo.InvariantCulture);

            case RulerFormat.MeasuresAndBeats:
            {
                var seconds = rate.Seconds(effectiveFrame);
                var safeBpm = Math.Max(1.0, bpm);
                var safeBeats = Math.Max(1, beatsPerMeasure);
                var beatSec = 60.0 / safeBpm;
                var totalBeats = seconds / beatSec;
                var measure = (long)(totalBeats / safeBeats);
                var beat = (int)(totalBeats % safeBeats);
                var fraction = totalBeats - Math.Floor(totalBeats);
                var ticks = (int)Math.Round(fraction * 1000);
                if (ticks >= 1000)
                {
                    ticks = 0;
                    beat++;
                    if (beat >= safeBeats) { beat = 0; measure++; }
                }
                return $"{measure + 1}.{beat + 1}.{ticks:000}";
            }

            case RulerFormat.FeetAndFrames16mm:
            {
                var feet = effectiveFrame / 40;
                var rem = effectiveFrame % 40;
                return $"{feet}+{rem:00}";
            }

            case RulerFormat.FeetAndFrames35mm:
            {
                var feet = effectiveFrame / 16;
                var rem = effectiveFrame % 16;
                return $"{feet}+{rem:00}";
            }

            case RulerFormat.SmpteFilmSyncIVTC:
            {
                const int fps = 24;
                var totalSec = effectiveFrame / fps;
                var rem = effectiveFrame % fps;
                return $"{totalSec / 3600:00}:{totalSec / 60 % 60:00}:{totalSec % 60:00}:{rem:00}";
            }

            case RulerFormat.SmpteFilmSync:
            {
                const int fps = 24;
                var totalSec = effectiveFrame / fps;
                var rem = effectiveFrame % fps;
                return $"{totalSec / 3600:00}:{totalSec / 60 % 60:00}:{totalSec % 60:00}:{rem:00}";
            }

            case RulerFormat.SmpteEBU:
            {
                const int fps = 25;
                var totalSec = effectiveFrame / fps;
                var rem = effectiveFrame % fps;
                return $"{totalSec / 3600:00}:{totalSec / 60 % 60:00}:{totalSec % 60:00}:{rem:00}";
            }

            case RulerFormat.SmpteNonDrop:
            {
                const int fps = 30;
                var totalSec = effectiveFrame / fps;
                var rem = effectiveFrame % fps;
                return $"{totalSec / 3600:00}:{totalSec / 60 % 60:00}:{totalSec % 60:00}:{rem:00}";
            }

            case RulerFormat.SmpteDrop:
            {
                var fps = Math.Max(1, (int)Math.Round(rate.Numerator / (double)rate.Denominator));
                var drop = fps >= 50 ? 4 : 2;
                var tenMinutes = fps * 600 - drop * 9;
                var remainder = effectiveFrame % tenMinutes;
                var adjusted = effectiveFrame + drop * 9 * (effectiveFrame / tenMinutes) +
                    (remainder >= drop ? drop * ((remainder - drop) / (fps * 60 - drop)) : 0);
                var total = adjusted / fps;
                return $"{total / 3600:00}:{total / 60 % 60:00}:{total % 60:00};{adjusted % fps:00}";
            }

            case RulerFormat.Smpte30:
            {
                const int fps = 30;
                var totalSec = effectiveFrame / fps;
                var rem = effectiveFrame % fps;
                return $"{totalSec / 3600:00}:{totalSec / 60 % 60:00}:{totalSec % 60:00}:{rem:00}";
            }

            case RulerFormat.AudioCDTime:
            {
                var cdFrames = (long)Math.Round(rate.Seconds(effectiveFrame) * 75);
                var mins = cdFrames / (75 * 60);
                var secs = (cdFrames / 75) % 60;
                var rem = cdFrames % 75;
                return $"{mins:00}:{secs:00}:{rem:00}";
            }

            case RulerFormat.Samples:
            {
                var safeSampleRate = Math.Max(1, sampleRate);
                var samples = (long)Math.Round(rate.Seconds(effectiveFrame) * safeSampleRate);
                return samples.ToString("N0", CultureInfo.InvariantCulture);
            }

            case RulerFormat.Nanoseconds:
            {
                var nanos = (long)Math.Round(rate.Seconds(effectiveFrame) * 1_000_000_000);
                return $"{nanos:N0} ns";
            }

            case RulerFormat.Microseconds:
            {
                var micros = (long)Math.Round(rate.Seconds(effectiveFrame) * 1_000_000);
                return $"{micros:N0} \u00B5s";
            }

            default:
                return FormatStandard(effectiveFrame, rate);
        }
    }

    /// <summary>
    /// Parses a timecode string into a project frame count, respecting the specified RulerFormat.
    /// Returns false if the input cannot be parsed.
    /// </summary>
    public static bool TryParse(
        string? input,
        Rational rate,
        RulerFormat format,
        out long frame,
        int sampleRate = 48000,
        double bpm = 120.0,
        int beatsPerMeasure = 4,
        long startTimeFrames = 0)
    {
        frame = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var text = input.Trim().TrimEnd('s', 'S').Trim();

        // 1. Format-specific parsers
        switch (format)
        {
            case RulerFormat.AbsoluteFrames:
                if (long.TryParse(text.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var absFrames))
                {
                    frame = Math.Max(0, absFrames - startTimeFrames);
                    return true;
                }
                break;

            case RulerFormat.Samples:
                if (long.TryParse(text.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var samples))
                {
                    var safeSampleRate = Math.Max(1, sampleRate);
                    var sec = samples / (double)safeSampleRate;
                    frame = Math.Max(0, rate.Frames(sec) - startTimeFrames);
                    return true;
                }
                break;

            case RulerFormat.Seconds:
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                {
                    frame = Math.Max(0, rate.Frames(seconds) - startTimeFrames);
                    return true;
                }
                break;

            case RulerFormat.FeetAndFrames16mm:
            {
                var match = FeetFramesRegex.Match(text);
                if (match.Success)
                {
                    var feet = long.Parse(match.Groups["feet"].Value, CultureInfo.InvariantCulture);
                    var f = long.Parse(match.Groups["frames"].Value, CultureInfo.InvariantCulture);
                    frame = Math.Max(0, feet * 40 + f - startTimeFrames);
                    return true;
                }
                break;
            }

            case RulerFormat.FeetAndFrames35mm:
            {
                var match = FeetFramesRegex.Match(text);
                if (match.Success)
                {
                    var feet = long.Parse(match.Groups["feet"].Value, CultureInfo.InvariantCulture);
                    var f = long.Parse(match.Groups["frames"].Value, CultureInfo.InvariantCulture);
                    frame = Math.Max(0, feet * 16 + f - startTimeFrames);
                    return true;
                }
                break;
            }

            case RulerFormat.MeasuresAndBeats:
            {
                var match = MeasuresBeatsRegex.Match(text);
                if (match.Success)
                {
                    var measure = Math.Max(1, long.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture)) - 1;
                    var beat = Math.Max(1, int.Parse(match.Groups["b"].Value, CultureInfo.InvariantCulture)) - 1;
                    var ticks = match.Groups["t"].Success ? int.Parse(match.Groups["t"].Value, CultureInfo.InvariantCulture) : 0;

                    var safeBpm = Math.Max(1.0, bpm);
                    var safeBeats = Math.Max(1, beatsPerMeasure);
                    var beatSec = 60.0 / safeBpm;
                    var totalBeats = measure * safeBeats + beat + ticks / 1000.0;
                    var sec = totalBeats * beatSec;
                    frame = Math.Max(0, rate.Frames(sec) - startTimeFrames);
                    return true;
                }
                break;
            }
        }

        // 2. Standard SMPTE or Timecode regex parser (HH:MM:SS:FF, HH:MM:SS;FF, HH:MM:SS.FF, MM:SS:FF)
        var tcMatch = TimecodeRegex.Match(text);
        if (tcMatch.Success)
        {
            var h = tcMatch.Groups["h"].Success ? long.Parse(tcMatch.Groups["h"].Value, CultureInfo.InvariantCulture) : 0;
            var m = tcMatch.Groups["m"].Success ? long.Parse(tcMatch.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
            var s = tcMatch.Groups["s"].Success ? long.Parse(tcMatch.Groups["s"].Value, CultureInfo.InvariantCulture) : 0;
            var f = tcMatch.Groups["f"].Success ? long.Parse(tcMatch.Groups["f"].Value, CultureInfo.InvariantCulture) : 0;

            if (format == RulerFormat.Time)
            {
                var sec = h * 3600 + m * 60 + s + f / 1000.0;
                frame = Math.Max(0, rate.Frames(sec) - startTimeFrames);
                return true;
            }

            if (format == RulerFormat.AudioCDTime)
            {
                var totalCdFrames = (h * 3600 + m * 60 + s) * 75 + f;
                frame = Math.Max(0, rate.Frames(totalCdFrames / 75.0) - startTimeFrames);
                return true;
            }

            var fps = format switch
            {
                RulerFormat.SmpteFilmSync or RulerFormat.SmpteFilmSyncIVTC => 24,
                RulerFormat.SmpteEBU => 25,
                RulerFormat.SmpteNonDrop or RulerFormat.Smpte30 => 30,
                _ => Math.Max(1, (int)Math.Round(rate.Numerator / (double)rate.Denominator))
            };

            var isDrop = format == RulerFormat.SmpteDrop || (text.Contains(';') && fps is 30 or 60);

            if (isDrop)
            {
                var drop = fps >= 50 ? 4 : 2;
                var totalMinutes = h * 60 + m;
                var calculated = (totalMinutes * 60 + s) * fps + f - drop * (totalMinutes - totalMinutes / 10);
                frame = Math.Max(0, calculated - startTimeFrames);
                return true;
            }

            var calculatedFrames = (h * 3600 + m * 60 + s) * fps + f;
            frame = Math.Max(0, calculatedFrames - startTimeFrames);
            return true;
        }

        // 3. Pure numeric integer fallback (frames)
        if (long.TryParse(text.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawFrames))
        {
            frame = Math.Max(0, rawFrames - startTimeFrames);
            return true;
        }

        // 4. Pure float fallback (seconds)
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawSeconds))
        {
            frame = Math.Max(0, rate.Frames(rawSeconds) - startTimeFrames);
            return true;
        }

        return false;
    }
}
