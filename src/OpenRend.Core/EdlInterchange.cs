using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenRend.Core;

/// <summary>
/// Edit transition type in a CMX 3600 EDL.
/// </summary>
public enum EdlTransitionType
{
    Cut,
    Dissolve,
    Wipe,
    KeyIn,
    KeyOut
}

/// <summary>
/// Target track channel type for an EDL record.
/// </summary>
public enum EdlTrackType
{
    Video,
    Audio1,
    Audio2,
    AudioBoth,
    BothVideoAudio
}

/// <summary>
/// A single edit decision event in a CMX 3600 EDL.
/// </summary>
public sealed class EdlEvent
{
    public int EventNumber { get; set; } = 1;
    public string ReelId { get; set; } = "AX";
    public EdlTrackType TrackType { get; set; } = EdlTrackType.Video;
    public EdlTransitionType TransitionType { get; set; } = EdlTransitionType.Cut;
    public int TransitionDurationFrames { get; set; } = 0;
    public long SourceInFrame { get; set; }
    public long SourceOutFrame { get; set; }
    public long TimelineInFrame { get; set; }
    public long TimelineOutFrame { get; set; }
    public string? ClipName { get; set; }
    public string? SourceFilePath { get; set; }
    public string? Comments { get; set; }
}

/// <summary>
/// Represents a parsed CMX 3600 Edit Decision List.
/// </summary>
public sealed class EdlDocument
{
    public string Title { get; set; } = "Untitled";
    public Rational FrameRate { get; set; } = new(30, 1);
    public bool IsDropFrame { get; set; }
    public List<EdlEvent> Events { get; set; } = [];
}

/// <summary>
/// Industry-standard CMX 3600 Edit Decision List (EDL) Import and Export.
/// 1:1 compatible with Vegas Pro, Kdenlive, DaVinci Resolve, Avid Media Composer, and Premiere Pro.
/// </summary>
public static class EdlInterchange
{
    private static readonly Regex EventRegex = new(
        @"^(?<num>\d{3,5})\s+(?<reel>[A-Za-z0-9_\-\.\:\*]+)\s+(?<track>V|A|A2|AA|B|NONE\/A\/V)\s+(?<trans>C|D|W\d*|KB|KO)(?:\s+(?<dur>\d+))?\s+(?<srcIn>\d{2}[:;]\d{2}[:;]\d{2}[:;]\d{2})\s+(?<srcOut>\d{2}[:;]\d{2}[:;]\d{2}[:;]\d{2})\s+(?<dstIn>\d{2}[:;]\d{2}[:;]\d{2}[:;]\d{2})\s+(?<dstOut>\d{2}[:;]\d{2}[:;]\d{2}[:;]\d{2})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ClipNameRegex = new(
        @"^\*\s*(?:FROM\s+CLIP\s+NAME|CLIP\s+NAME|SOURCE\s+FILE|TO\s+CLIP\s+NAME)\s*:\s*(?<name>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FcmRegex = new(
        @"^FCM\s*:\s*(?<mode>DROP\s*FRAME|NON-DROP\s*FRAME|NON\s*DROP\s*FRAME)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TitleRegex = new(
        @"^TITLE\s*:\s*(?<title>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Exports an OpenRend project document to CMX 3600 EDL format.
    /// </summary>
    public static string ExportToEdl(ProjectDocument project, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var docTitle = string.IsNullOrWhiteSpace(title) ? (project.Name ?? "Untitled") : title;
        var (timebase, isDropFrame) = GetRateParameters(project.FrameRate);

        var sb = new StringBuilder();
        sb.AppendLine($"TITLE: {SanitizeEdlString(docTitle)}");
        sb.AppendLine($"FCM: {(isDropFrame ? "DROP FRAME" : "NON-DROP FRAME")}");
        sb.AppendLine();

        int eventCounter = 1;
        var mediaById = project.Media.ToDictionary(m => m.Id);

        // Process tracks in order: Video tracks first, then Audio tracks
        var videoTracks = project.Tracks.Where(t => t.Kind == TrackKind.Video).ToList();
        var audioTracks = project.Tracks.Where(t => t.Kind == TrackKind.Audio).ToList();

        // Video clips
        foreach (var track in videoTracks)
        {
            var orderedClips = track.Clips.OrderBy(c => c.StartFrame).ToList();
            foreach (var clip in orderedClips)
            {
                mediaById.TryGetValue(clip.MediaId, out var media);
                var clipName = clip.Name ?? media?.Name ?? Path.GetFileName(media?.Path) ?? "Clip";
                var reelId = GenerateReelId(clipName, eventCounter);

                var srcIn = FormatTimecode(clip.SourceInFrame, project.FrameRate, isDropFrame);
                var srcOut = FormatTimecode(clip.SourceInFrame + clip.DurationFrames, project.FrameRate, isDropFrame);
                var dstIn = FormatTimecode(clip.StartFrame, project.FrameRate, isDropFrame);
                var dstOut = FormatTimecode(clip.EndFrame, project.FrameRate, isDropFrame);

                bool isDissolve = clip.FadeInFrames > 0 && clip.FadeInTransition?.Kind == VideoTransitionKind.Dissolve;
                int transDuration = isDissolve ? (int)clip.FadeInFrames : 0;

                if (isDissolve && transDuration > 0)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:D3}  {1,-8} {2,-2}     D    {3:D3} {4} {5} {6} {7}",
                        eventCounter, reelId, "V", transDuration, srcIn, srcOut, dstIn, dstOut));
                }
                else
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:D3}  {1,-8} {2,-2}     C        {3} {4} {5} {6}",
                        eventCounter, reelId, "V", srcIn, srcOut, dstIn, dstOut));
                }

                sb.AppendLine($"* FROM CLIP NAME: {clipName}");
                if (media is not null && !string.IsNullOrWhiteSpace(media.Path) && !media.Path.StartsWith("openrend://", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"* SOURCE FILE: {media.Path}");
                }
                sb.AppendLine();
                eventCounter++;
            }
        }

        // Audio clips
        int audioTrackIndex = 0;
        foreach (var track in audioTracks)
        {
            audioTrackIndex++;
            string trackTypeStr = audioTrackIndex switch
            {
                1 => "A",
                2 => "A2",
                _ => "AA"
            };

            var orderedClips = track.Clips.OrderBy(c => c.StartFrame).ToList();
            foreach (var clip in orderedClips)
            {
                mediaById.TryGetValue(clip.MediaId, out var media);
                var clipName = clip.Name ?? media?.Name ?? Path.GetFileName(media?.Path) ?? "AudioClip";
                var reelId = GenerateReelId(clipName, eventCounter);

                var srcIn = FormatTimecode(clip.SourceInFrame, project.FrameRate, isDropFrame);
                var srcOut = FormatTimecode(clip.SourceInFrame + clip.DurationFrames, project.FrameRate, isDropFrame);
                var dstIn = FormatTimecode(clip.StartFrame, project.FrameRate, isDropFrame);
                var dstOut = FormatTimecode(clip.EndFrame, project.FrameRate, isDropFrame);

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:D3}  {1,-8} {2,-2}     C        {3} {4} {5} {6}",
                    eventCounter, reelId, trackTypeStr, srcIn, srcOut, dstIn, dstOut));

                sb.AppendLine($"* FROM CLIP NAME: {clipName}");
                if (media is not null && !string.IsNullOrWhiteSpace(media.Path) && !media.Path.StartsWith("openrend://", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"* SOURCE FILE: {media.Path}");
                }
                sb.AppendLine();
                eventCounter++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses a CMX 3600 EDL string into an <see cref="EdlDocument"/>.
    /// </summary>
    public static EdlDocument ParseEdl(string edlContent, Rational? defaultRate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edlContent);

        var doc = new EdlDocument
        {
            FrameRate = defaultRate ?? new Rational(30, 1)
        };

        using var reader = new StringReader(edlContent);
        string? line;
        EdlEvent? currentEvent = null;

        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Header: TITLE
            var titleMatch = TitleRegex.Match(trimmed);
            if (titleMatch.Success)
            {
                doc.Title = titleMatch.Groups["title"].Value.Trim();
                continue;
            }

            // Header: FCM (Frame Count Mode)
            var fcmMatch = FcmRegex.Match(trimmed);
            if (fcmMatch.Success)
            {
                var mode = fcmMatch.Groups["mode"].Value.ToUpperInvariant();
                doc.IsDropFrame = mode.Contains("DROP") && !mode.Contains("NON");
                if (doc.IsDropFrame && defaultRate == null)
                {
                    doc.FrameRate = new Rational(30000, 1001);
                }
                continue;
            }

            // Remarks / Comments
            if (trimmed.StartsWith('*'))
            {
                var clipMatch = ClipNameRegex.Match(trimmed);
                if (clipMatch.Success && currentEvent is not null)
                {
                    var val = clipMatch.Groups["name"].Value.Trim();
                    if (string.IsNullOrEmpty(currentEvent.ClipName))
                    {
                        currentEvent.ClipName = val;
                    }
                    if (val.Contains('\\') || val.Contains('/') || val.Contains('.'))
                    {
                        currentEvent.SourceFilePath = val;
                    }
                }
                continue;
            }

            // Event line
            var eventMatch = EventRegex.Match(trimmed);
            if (eventMatch.Success)
            {
                int eventNum = int.Parse(eventMatch.Groups["num"].Value, CultureInfo.InvariantCulture);
                string reel = eventMatch.Groups["reel"].Value.Trim();
                string trackStr = eventMatch.Groups["track"].Value.ToUpperInvariant();
                string transStr = eventMatch.Groups["trans"].Value.ToUpperInvariant();
                string durStr = eventMatch.Groups["dur"].Value;

                var trackType = trackStr switch
                {
                    "V" => EdlTrackType.Video,
                    "A" => EdlTrackType.Audio1,
                    "A2" => EdlTrackType.Audio2,
                    "AA" => EdlTrackType.AudioBoth,
                    "B" => EdlTrackType.BothVideoAudio,
                    _ => EdlTrackType.Video
                };

                var transType = transStr switch
                {
                    "C" => EdlTransitionType.Cut,
                    "D" => EdlTransitionType.Dissolve,
                    "KB" => EdlTransitionType.KeyIn,
                    "KO" => EdlTransitionType.KeyOut,
                    _ when transStr.StartsWith('W') => EdlTransitionType.Wipe,
                    _ => EdlTransitionType.Cut
                };

                int transDur = int.TryParse(durStr, out var d) ? d : 0;

                var srcInStr = eventMatch.Groups["srcIn"].Value;
                var srcOutStr = eventMatch.Groups["srcOut"].Value;
                var dstInStr = eventMatch.Groups["dstIn"].Value;
                var dstOutStr = eventMatch.Groups["dstOut"].Value;

                long srcIn = ParseTimecode(srcInStr, doc.FrameRate, doc.IsDropFrame);
                long srcOut = ParseTimecode(srcOutStr, doc.FrameRate, doc.IsDropFrame);
                long dstIn = ParseTimecode(dstInStr, doc.FrameRate, doc.IsDropFrame);
                long dstOut = ParseTimecode(dstOutStr, doc.FrameRate, doc.IsDropFrame);

                currentEvent = new EdlEvent
                {
                    EventNumber = eventNum,
                    ReelId = reel,
                    TrackType = trackType,
                    TransitionType = transType,
                    TransitionDurationFrames = transDur,
                    SourceInFrame = srcIn,
                    SourceOutFrame = srcOut,
                    TimelineInFrame = dstIn,
                    TimelineOutFrame = dstOut
                };

                doc.Events.Add(currentEvent);
            }
        }

        return doc;
    }

    /// <summary>
    /// Imports a CMX 3600 EDL into a new OpenRend <see cref="ProjectDocument"/>.
    /// </summary>
    public static ProjectDocument ImportFromEdl(string edlContent, Rational? frameRate = null, int width = 1920, int height = 1080)
    {
        var doc = ParseEdl(edlContent, frameRate);
        if (doc.Events.Count == 0)
        {
            throw new InvalidDataException("The EDL file contains no edit decision events.");
        }

        var rate = doc.FrameRate;
        var project = new ProjectDocument
        {
            Name = string.IsNullOrWhiteSpace(doc.Title) ? "EDL Import" : doc.Title,
            Width = width,
            Height = height,
            FrameRate = rate
        };

        var videoTrack = new TimelineTrack
        {
            Id = Guid.NewGuid(),
            Name = "Video 1",
            Kind = TrackKind.Video,
            Opacity = 1.0
        };
        var audioTrack1 = new TimelineTrack
        {
            Id = Guid.NewGuid(),
            Name = "Audio 1",
            Kind = TrackKind.Audio,
            Gain = 1.0
        };
        var audioTrack2 = new TimelineTrack
        {
            Id = Guid.NewGuid(),
            Name = "Audio 2",
            Kind = TrackKind.Audio,
            Gain = 1.0
        };

        var mediaByPath = new Dictionary<string, MediaAsset>(StringComparer.OrdinalIgnoreCase);

        MediaAsset ResolveMedia(EdlEvent ev, bool isVideo)
        {
            var key = !string.IsNullOrWhiteSpace(ev.SourceFilePath) ? ev.SourceFilePath
                : !string.IsNullOrWhiteSpace(ev.ClipName) ? ev.ClipName
                : ev.ReelId;

            if (mediaByPath.TryGetValue(key, out var existing))
            {
                if (isVideo && !existing.HasVideo) existing.HasVideo = true;
                if (!isVideo && !existing.HasAudio) existing.HasAudio = true;
                return existing;
            }

            var path = !string.IsNullOrWhiteSpace(ev.SourceFilePath) && File.Exists(ev.SourceFilePath)
                ? ev.SourceFilePath
                : $"openrend://edl/{Uri.EscapeDataString(key)}";

            long maxFrame = Math.Max(ev.SourceOutFrame, ev.SourceInFrame + (ev.TimelineOutFrame - ev.TimelineInFrame));
            double durationSec = Math.Max(10.0, rate.Seconds(maxFrame + 300));

            var asset = new MediaAsset
            {
                Id = Guid.NewGuid(),
                Path = path,
                Width = isVideo ? width : 0,
                Height = isVideo ? height : 0,
                FrameRate = rate.Value,
                DurationSeconds = durationSec,
                HasVideo = isVideo,
                HasAudio = !isVideo
            };

            mediaByPath[key] = asset;
            project.Media.Add(asset);
            return asset;
        }

        foreach (var ev in doc.Events)
        {
            long duration = ev.TimelineOutFrame - ev.TimelineInFrame;
            if (duration <= 0) continue;

            if (ev.TrackType is EdlTrackType.Video or EdlTrackType.BothVideoAudio)
            {
                var asset = ResolveMedia(ev, isVideo: true);
                var clip = new TimelineClip
                {
                    Id = Guid.NewGuid(),
                    MediaId = asset.Id,
                    Name = ev.ClipName ?? asset.Name,
                    StartFrame = ev.TimelineInFrame,
                    DurationFrames = duration,
                    SourceInFrame = ev.SourceInFrame,
                    Opacity = 1.0,
                    Gain = 1.0
                };

                if (ev.TransitionType == EdlTransitionType.Dissolve && ev.TransitionDurationFrames > 0)
                {
                    clip.FadeInFrames = Math.Min(duration, ev.TransitionDurationFrames);
                    clip.FadeInTransition = new VideoTransition { Kind = VideoTransitionKind.Dissolve };
                }

                videoTrack.Clips.Add(clip);
            }

            if (ev.TrackType is EdlTrackType.Audio1 or EdlTrackType.AudioBoth or EdlTrackType.BothVideoAudio)
            {
                var asset = ResolveMedia(ev, isVideo: false);
                var clip = new TimelineClip
                {
                    Id = Guid.NewGuid(),
                    MediaId = asset.Id,
                    Name = ev.ClipName ?? asset.Name,
                    StartFrame = ev.TimelineInFrame,
                    DurationFrames = duration,
                    SourceInFrame = ev.SourceInFrame,
                    Opacity = 1.0,
                    Gain = 1.0
                };
                audioTrack1.Clips.Add(clip);
            }

            if (ev.TrackType is EdlTrackType.Audio2 or EdlTrackType.AudioBoth)
            {
                var asset = ResolveMedia(ev, isVideo: false);
                var clip = new TimelineClip
                {
                    Id = Guid.NewGuid(),
                    MediaId = asset.Id,
                    Name = ev.ClipName ?? asset.Name,
                    StartFrame = ev.TimelineInFrame,
                    DurationFrames = duration,
                    SourceInFrame = ev.SourceInFrame,
                    Opacity = 1.0,
                    Gain = 1.0
                };
                audioTrack2.Clips.Add(clip);
            }
        }

        if (videoTrack.Clips.Count > 0) project.Tracks.Add(videoTrack);
        if (audioTrack1.Clips.Count > 0) project.Tracks.Add(audioTrack1);
        if (audioTrack2.Clips.Count > 0) project.Tracks.Add(audioTrack2);

        project.Validate();
        return project;
    }

    /// <summary>
    /// Formats an integer frame index into standard SMPTE timecode (HH:MM:SS:FF or HH:MM:SS;FF).
    /// Supports both standard non-drop frame and SMPTE 12M drop-frame math.
    /// </summary>
    public static string FormatTimecode(long frames, Rational rate, bool dropFrame)
    {
        if (frames < 0) frames = 0;
        int fps = (int)Math.Round(rate.Value);
        if (fps <= 0) fps = 30;

        if (dropFrame && (fps == 30 || fps == 60))
        {
            long dropFactor = fps == 30 ? 2 : 4;
            long framesPer10Min = fps == 30 ? 17982 : 35964;
            long framesInMin0 = fps * 60;
            long framesPerSubsequentMin = framesInMin0 - dropFactor;

            long d = frames / framesPer10Min;
            long m = frames % framesPer10Min;

            long droppedFrames = d * 9 * dropFactor;
            if (m >= framesInMin0)
            {
                long elapsedMins = (m - framesInMin0) / framesPerSubsequentMin + 1;
                droppedFrames += elapsedMins * dropFactor;
            }

            long adjustedFrames = frames + droppedFrames;
            long ff = adjustedFrames % fps;
            long ss = (adjustedFrames / fps) % 60;
            long mm = ((adjustedFrames / fps) / 60) % 60;
            long hh = ((adjustedFrames / fps) / 3600);
            return $"{hh:D2}:{mm:D2}:{ss:D2};{ff:D2}";
        }
        else
        {
            long totalSeconds = frames / fps;
            long ff = frames % fps;
            long ss = totalSeconds % 60;
            long mm = (totalSeconds / 60) % 60;
            long hh = totalSeconds / 3600;
            return $"{hh:D2}:{mm:D2}:{ss:D2}:{ff:D2}";
        }
    }

    /// <summary>
    /// Parses a standard SMPTE timecode string into a zero-based integer frame count.
    /// </summary>
    public static long ParseTimecode(string tc, Rational rate, bool dropFrame)
    {
        if (string.IsNullOrWhiteSpace(tc)) return 0;

        var parts = tc.Trim().Split(':', ';');
        if (parts.Length != 4) return 0;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ||
            !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var f))
        {
            return 0;
        }

        int fps = (int)Math.Round(rate.Value);
        if (fps <= 0) fps = 30;

        bool hasSemicolon = tc.Contains(';');
        if (dropFrame || hasSemicolon)
        {
            long dropFactor = fps == 30 ? 2 : (fps == 60 ? 4 : 2);
            long totalMinutes = h * 60 + m;
            long droppedFrames = dropFactor * (totalMinutes - totalMinutes / 10);
            return (h * 3600 + m * 60 + s) * (long)fps + f - droppedFrames;
        }
        else
        {
            return (h * 3600L + m * 60L + s) * fps + f;
        }
    }

    private static (int timebase, bool ntsc) GetRateParameters(Rational rate)
    {
        if (rate.Numerator == 30000 && rate.Denominator == 1001) return (30, true);
        if (rate.Numerator == 60000 && rate.Denominator == 1001) return (60, true);
        if (rate.Numerator == 24000 && rate.Denominator == 1001) return (24, true);

        int timebase = (int)Math.Round(rate.Value);
        return (timebase, false);
    }

    private static string GenerateReelId(string clipName, int eventNumber)
    {
        if (string.IsNullOrWhiteSpace(clipName)) return $"AX";

        var clean = new string(clipName.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (clean.Length >= 8) return clean[..8];
        if (clean.Length > 0) return clean;
        return $"AX{eventNumber:D2}";
    }

    private static string SanitizeEdlString(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Untitled";
        var clean = new string(text.Where(c => c >= 32 && c <= 126).ToArray());
        return clean.Length > 80 ? clean[..80] : clean;
    }
}
