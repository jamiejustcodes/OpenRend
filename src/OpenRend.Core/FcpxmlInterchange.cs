using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace OpenRend.Core;

/// <summary>
/// Apple Final Cut Pro X XML (&lt;fcpxml version="1.9"&gt;) Import and Export.
/// Matches Vegas Pro Project Interchange architecture (VegasScripts.TimelineToFcpxmlExporter / FcpXImporter).
/// Compatible with Apple Final Cut Pro, DaVinci Resolve, Vegas Pro, and Adobe Premiere Pro.
/// </summary>
public static class FcpxmlInterchange
{
    private const string FcpxmlVersion = "1.9";

    /// <summary>
    /// Exports an OpenRend project document to Final Cut Pro X XML (&lt;fcpxml version="1.9"&gt;).
    /// </summary>
    public static string ExportToXml(ProjectDocument project, string? eventName = null, string? projectName = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var evtName = string.IsNullOrWhiteSpace(eventName) ? (project.Name ?? "OpenRend Event") : eventName;
        var prjName = string.IsNullOrWhiteSpace(projectName) ? (project.Name ?? "OpenRend Project") : projectName;

        var (frameDurationStr, formatName) = GetFormatParameters(project.FrameRate, project.Height);
        var totalDurationSec = project.FrameRate.Seconds(project.DurationFrames);
        var totalDurationStr = FormatTimeRational(project.DurationFrames, project.FrameRate);

        // 1. Resources: format and media assets
        var resourcesElem = new XElement("resources");
        var projectFormatId = "r1";
        resourcesElem.Add(new XElement("format",
            new XAttribute("id", projectFormatId),
            new XAttribute("name", formatName),
            new XAttribute("frameDuration", frameDurationStr),
            new XAttribute("width", project.Width),
            new XAttribute("height", project.Height)
        ));

        var assetIdMap = new Dictionary<Guid, string>();
        int assetIndex = 2;

        foreach (var media in project.Media)
        {
            var assetId = $"r{assetIndex++}";
            assetIdMap[media.Id] = assetId;

            var assetDurationStr = FormatTimeRational(project.FrameRate.Frames(media.DurationSeconds), project.FrameRate);
            var assetElem = new XElement("asset",
                new XAttribute("id", assetId),
                new XAttribute("name", Path.GetFileName(media.Path)),
                new XAttribute("src", FormatPathUrl(media.Path)),
                new XAttribute("start", "0s"),
                new XAttribute("duration", assetDurationStr),
                new XAttribute("format", projectFormatId),
                new XAttribute("hasVideo", media.HasVideo ? "1" : "0"),
                new XAttribute("hasAudio", media.HasAudio ? "1" : "0")
            );
            resourcesElem.Add(assetElem);
        }

        // 2. Spine assembly
        var spineElem = new XElement("spine");

        var videoTracks = project.Tracks.Where(t => t.Kind == TrackKind.Video).ToList();
        var audioTracks = project.Tracks.Where(t => t.Kind == TrackKind.Audio).ToList();

        // Primary storyline is built from the first video track (or a placeholder gap if no video tracks exist)
        var primaryVideoTrack = videoTracks.FirstOrDefault();
        long currentSpineFrame = 0;

        if (primaryVideoTrack is not null && primaryVideoTrack.Clips.Count > 0)
        {
            var orderedClips = primaryVideoTrack.Clips.OrderBy(c => c.StartFrame).ToList();
            foreach (var clip in orderedClips)
            {
                // If there is empty space before this clip, insert a gap element
                if (clip.StartFrame > currentSpineFrame)
                {
                    var gapDuration = clip.StartFrame - currentSpineFrame;
                    spineElem.Add(new XElement("gap",
                        new XAttribute("name", "Gap"),
                        new XAttribute("offset", FormatTimeRational(currentSpineFrame, project.FrameRate)),
                        new XAttribute("duration", FormatTimeRational(gapDuration, project.FrameRate)),
                        new XAttribute("start", "0s")
                    ));
                }

                var assetClip = CreateAssetClipElement(clip, project, assetIdMap, lane: 0);
                spineElem.Add(assetClip);
                currentSpineFrame = clip.EndFrame;
            }

            // Fill trailing gap if project extends past last clip
            if (project.DurationFrames > currentSpineFrame)
            {
                var gapDuration = project.DurationFrames - currentSpineFrame;
                spineElem.Add(new XElement("gap",
                    new XAttribute("name", "Gap"),
                    new XAttribute("offset", FormatTimeRational(currentSpineFrame, project.FrameRate)),
                    new XAttribute("duration", FormatTimeRational(gapDuration, project.FrameRate)),
                    new XAttribute("start", "0s")
                ));
            }
        }
        else if (project.DurationFrames > 0)
        {
            // Empty video spine gap covering whole project duration
            spineElem.Add(new XElement("gap",
                new XAttribute("name", "Gap"),
                new XAttribute("offset", "0s"),
                new XAttribute("duration", totalDurationStr),
                new XAttribute("start", "0s")
            ));
        }

        // Secondary video tracks (lane 1, 2, ...) attached to spine
        for (int i = 1; i < videoTracks.Count; i++)
        {
            int lane = i;
            var track = videoTracks[i];
            foreach (var clip in track.Clips.OrderBy(c => c.StartFrame))
            {
                var clipElem = CreateAssetClipElement(clip, project, assetIdMap, lane: lane);
                spineElem.Add(clipElem);
            }
        }

        // Audio tracks (lane -1, -2, ...) attached to spine
        for (int i = 0; i < audioTracks.Count; i++)
        {
            int lane = -(i + 1);
            var track = audioTracks[i];
            foreach (var clip in track.Clips.OrderBy(c => c.StartFrame))
            {
                var clipElem = CreateAssetClipElement(clip, project, assetIdMap, lane: lane);
                spineElem.Add(clipElem);
            }
        }

        // Append Sequence Markers to the spine or sequence
        foreach (var marker in project.Markers.OrderBy(m => m.Frame))
        {
            spineElem.Add(new XElement("marker",
                new XAttribute("start", FormatTimeRational(marker.Frame, project.FrameRate)),
                new XAttribute("duration", frameDurationStr),
                new XAttribute("value", marker.Label),
                new XAttribute("note", marker.Label)
            ));
        }

        // 3. Document Root Assembly
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("fcpxml", null, null, null),
            new XElement("fcpxml",
                new XAttribute("version", FcpxmlVersion),
                resourcesElem,
                new XElement("library",
                    new XElement("event",
                        new XAttribute("name", evtName),
                        new XElement("project",
                            new XAttribute("name", prjName),
                            new XElement("sequence",
                                new XAttribute("format", projectFormatId),
                                new XAttribute("duration", totalDurationStr),
                                new XAttribute("tcStart", "0s"),
                                new XAttribute("tcFormat", "NDF"),
                                new XAttribute("audioLayout", "stereo"),
                                spineElem
                            )
                        )
                    )
                )
            )
        );

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false
        };

        using (var writer = XmlWriter.Create(sb, settings))
        {
            doc.Save(writer);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Imports a Final Cut Pro X XML (&lt;fcpxml&gt;) document into a new OpenRend project document.
    /// </summary>
    public static ProjectDocument ImportFromXml(string xmlContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xmlContent);

        var doc = XDocument.Parse(xmlContent);
        var root = doc.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "fcpxml", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Invalid FCPXML: document root must be <fcpxml>.");
        }

        // 1. Resources: parse formats and assets
        var formats = new Dictionary<string, (int Width, int Height, Rational Rate)>(StringComparer.Ordinal);
        var assets = new Dictionary<string, (string Name, string Path, double Duration, bool HasVideo, bool HasAudio, string FormatId)>(StringComparer.Ordinal);

        var resourcesElem = root.Element("resources");
        if (resourcesElem is not null)
        {
            foreach (var fmt in resourcesElem.Elements("format"))
            {
                var id = fmt.Attribute("id")?.Value;
                if (string.IsNullOrEmpty(id)) continue;

                int width = 1920;
                int height = 1080;
                if (int.TryParse(fmt.Attribute("width")?.Value, out var w) && w > 0) width = w;
                if (int.TryParse(fmt.Attribute("height")?.Value, out var h) && h > 0) height = h;

                var frameDurStr = fmt.Attribute("frameDuration")?.Value;
                var rate = ParseRateFromFrameDuration(frameDurStr);

                formats[id] = (width, height, rate);
            }

            foreach (var ast in resourcesElem.Elements("asset"))
            {
                var id = ast.Attribute("id")?.Value;
                if (string.IsNullOrEmpty(id)) continue;

                var name = ast.Attribute("name")?.Value ?? "Media";
                var src = ParsePathUrl(ast.Attribute("src")?.Value ?? string.Empty);
                var durationStr = ast.Attribute("duration")?.Value;
                var duration = TryParseFcpxmlSeconds(durationStr, out var dSec) ? dSec : 0.0;
                var hasVideo = !string.Equals(ast.Attribute("hasVideo")?.Value, "0", StringComparison.OrdinalIgnoreCase);
                var hasAudio = !string.Equals(ast.Attribute("hasAudio")?.Value, "0", StringComparison.OrdinalIgnoreCase);
                var formatId = ast.Attribute("format")?.Value ?? string.Empty;

                assets[id] = (name, src, duration, hasVideo, hasAudio, formatId);
            }
        }

        // 2. Locate Sequence
        var seqElem = root.Descendants("sequence").FirstOrDefault()
            ?? throw new InvalidDataException("Invalid FCPXML: no <sequence> element found.");

        var projectFormatId = seqElem.Attribute("format")?.Value;
        int projWidth = 1920;
        int projHeight = 1080;
        Rational projFrameRate = new Rational(30, 1);

        if (!string.IsNullOrEmpty(projectFormatId) && formats.TryGetValue(projectFormatId, out var fmtInfo))
        {
            projWidth = fmtInfo.Width;
            projHeight = fmtInfo.Height;
            projFrameRate = fmtInfo.Rate;
        }

        var projectName = seqElem.Ancestors("project").FirstOrDefault()?.Attribute("name")?.Value
            ?? seqElem.Ancestors("event").FirstOrDefault()?.Attribute("name")?.Value
            ?? "Imported FCPXML Project";

        // 3. Media Assets
        var mediaAssetMap = new Dictionary<string, MediaAsset>(StringComparer.OrdinalIgnoreCase);
        var mediaList = new List<MediaAsset>();

        foreach (var (assetId, astInfo) in assets)
        {
            int mediaW = projWidth;
            int mediaH = projHeight;
            if (!string.IsNullOrEmpty(astInfo.FormatId) && formats.TryGetValue(astInfo.FormatId, out var astFmt))
            {
                mediaW = astFmt.Width;
                mediaH = astFmt.Height;
            }

            var media = new MediaAsset
            {
                Id = Guid.NewGuid(),
                Path = astInfo.Path,
                DurationSeconds = astInfo.Duration > 0 ? astInfo.Duration : 3600.0,
                Width = astInfo.HasVideo ? mediaW : 0,
                Height = astInfo.HasVideo ? mediaH : 0,
                HasVideo = astInfo.HasVideo,
                HasAudio = astInfo.HasAudio
            };

            mediaAssetMap[assetId] = media;
            mediaList.Add(media);
        }

        // 4. Parse Spine and Attached Lanes
        var spineElem = seqElem.Element("spine");
        var videoLanes = new SortedDictionary<int, List<TimelineClip>>();
        var audioLanes = new SortedDictionary<int, List<TimelineClip>>();
        var markers = new List<TimelineMarker>();

        if (spineElem is not null)
        {
            long spineCursorFrame = 0;

            foreach (var node in spineElem.Elements())
            {
                var nodeName = node.Name.LocalName;

                if (string.Equals(nodeName, "gap", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseFcpxmlSeconds(node.Attribute("duration")?.Value, out var gapSec))
                    {
                        spineCursorFrame += projFrameRate.Frames(gapSec);
                    }
                    continue;
                }

                if (string.Equals(nodeName, "marker", StringComparison.OrdinalIgnoreCase))
                {
                    var label = node.Attribute("value")?.Value ?? node.Attribute("note")?.Value ?? $"Marker {markers.Count + 1}";
                    var startSec = TryParseFcpxmlSeconds(node.Attribute("start")?.Value, out var sSec) ? sSec : 0.0;
                    markers.Add(new TimelineMarker
                    {
                        Id = Guid.NewGuid(),
                        Index = markers.Count + 1,
                        Frame = Math.Max(0, projFrameRate.Frames(startSec)),
                        Label = label,
                        ColorId = 0
                    });
                    continue;
                }

                if (string.Equals(nodeName, "asset-clip", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(nodeName, "clip", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(nodeName, "video", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(nodeName, "audio", StringComparison.OrdinalIgnoreCase))
                {
                    int lane = 0;
                    if (int.TryParse(node.Attribute("lane")?.Value, out var l)) lane = l;

                    long clipStartFrame = spineCursorFrame;
                    if (node.Attribute("offset") is not null && TryParseFcpxmlSeconds(node.Attribute("offset")?.Value, out var offsetSec))
                    {
                        clipStartFrame = projFrameRate.Frames(offsetSec);
                    }

                    var durationSec = TryParseFcpxmlSeconds(node.Attribute("duration")?.Value, out var dSec) ? dSec : 1.0;
                    var durationFrames = Math.Max(1, projFrameRate.Frames(durationSec));

                    var startSec = TryParseFcpxmlSeconds(node.Attribute("start")?.Value, out var sSec) ? sSec : 0.0;
                    var sourceInFrame = Math.Max(0, projFrameRate.Frames(startSec));

                    var refId = node.Attribute("ref")?.Value
                        ?? node.Element("video")?.Attribute("ref")?.Value
                        ?? node.Element("audio")?.Attribute("ref")?.Value;

                    MediaAsset? media = null;
                    if (!string.IsNullOrEmpty(refId) && mediaAssetMap.TryGetValue(refId, out var foundMedia))
                    {
                        media = foundMedia;
                    }
                    else if (mediaList.Count > 0)
                    {
                        media = mediaList[0];
                    }

                    if (media is null)
                    {
                        // Synthesize placeholder media if none matched
                        media = new MediaAsset
                        {
                            Id = Guid.NewGuid(),
                            Path = node.Attribute("name")?.Value ?? "Media.mp4",
                            DurationSeconds = durationSec,
                            Width = projWidth,
                            Height = projHeight,
                            HasVideo = true,
                            HasAudio = true
                        };
                        mediaList.Add(media);
                    }

                    // Parse Volume / Gain if present
                    double gain = 1.0;
                    var adjustVolume = node.Descendants("adjust-volume").FirstOrDefault();
                    if (adjustVolume is not null)
                    {
                        var amt = adjustVolume.Attribute("amount")?.Value?.TrimEnd('d', 'B', ' ');
                        if (double.TryParse(amt, NumberStyles.Float, CultureInfo.InvariantCulture, out var dB))
                        {
                            gain = Math.Pow(10.0, dB / 20.0);
                        }
                    }

                    var clip = new TimelineClip
                    {
                        Id = Guid.NewGuid(),
                        MediaId = media.Id,
                        Name = node.Attribute("name")?.Value ?? media.Name,
                        StartFrame = clipStartFrame,
                        DurationFrames = durationFrames,
                        SourceInFrame = sourceInFrame,
                        Gain = Math.Clamp(gain, 0.0, 10.0),
                        Opacity = 1.0
                    };

                    // Distribute into video or audio lanes
                    bool isAudioClip = string.Equals(nodeName, "audio", StringComparison.OrdinalIgnoreCase) || lane < 0;
                    if (isAudioClip)
                    {
                        if (!audioLanes.TryGetValue(lane, out var aClips))
                        {
                            aClips = new List<TimelineClip>();
                            audioLanes[lane] = aClips;
                        }
                        aClips.Add(clip);
                    }
                    else
                    {
                        if (!videoLanes.TryGetValue(lane, out var vClips))
                        {
                            vClips = new List<TimelineClip>();
                            videoLanes[lane] = vClips;
                        }
                        vClips.Add(clip);
                    }

                    // Parse nested markers on clip
                    foreach (var mElem in node.Elements("marker"))
                    {
                        var mLabel = mElem.Attribute("value")?.Value ?? mElem.Attribute("note")?.Value ?? $"Marker {markers.Count + 1}";
                        var mStartSec = TryParseFcpxmlSeconds(mElem.Attribute("start")?.Value, out var ms) ? ms : 0.0;
                        markers.Add(new TimelineMarker
                        {
                            Id = Guid.NewGuid(),
                            Index = markers.Count + 1,
                            Frame = Math.Max(0, clipStartFrame + projFrameRate.Frames(mStartSec - startSec)),
                            Label = mLabel,
                            ColorId = 0
                        });
                    }

                    // For lane 0 on the spine, advance cursor
                    if (lane == 0)
                    {
                        spineCursorFrame = clipStartFrame + durationFrames;
                    }
                }
            }
        }

        // 5. Build Tracks
        var tracks = new List<TimelineTrack>();

        // Video tracks: lane 0 first, then lane 1, 2, ...
        int videoIndex = 1;
        foreach (var (lane, clips) in videoLanes)
        {
            var track = new TimelineTrack
            {
                Id = Guid.NewGuid(),
                Kind = TrackKind.Video,
                Name = $"Video {videoIndex++}",
                Clips = clips
            };
            tracks.Add(track);
        }

        // If no video tracks were created, add an empty primary track
        if (tracks.Count == 0 || !tracks.Any(t => t.Kind == TrackKind.Video))
        {
            tracks.Insert(0, new TimelineTrack
            {
                Id = Guid.NewGuid(),
                Kind = TrackKind.Video,
                Name = "Video 1",
                Clips = new List<TimelineClip>()
            });
        }

        // Audio tracks: lane -1 first, then -2, -3, ...
        int audioIndex = 1;
        foreach (var (lane, clips) in audioLanes.Reverse())
        {
            var track = new TimelineTrack
            {
                Id = Guid.NewGuid(),
                Kind = TrackKind.Audio,
                Name = $"Audio {audioIndex++}",
                Clips = clips
            };
            tracks.Add(track);
        }

        return new ProjectDocument
        {
            Name = projectName,
            Width = projWidth,
            Height = projHeight,
            FrameRate = projFrameRate,
            Media = mediaList,
            Tracks = tracks,
            Markers = markers
        };
    }

    private static XElement CreateAssetClipElement(TimelineClip clip, ProjectDocument project, Dictionary<Guid, string> assetIdMap, int lane)
    {
        assetIdMap.TryGetValue(clip.MediaId, out var assetRef);
        assetRef ??= "r1";

        var clipElem = new XElement("asset-clip",
            new XAttribute("ref", assetRef),
            new XAttribute("name", clip.Name ?? "Clip"),
            new XAttribute("offset", FormatTimeRational(clip.StartFrame, project.FrameRate)),
            new XAttribute("duration", FormatTimeRational(clip.DurationFrames, project.FrameRate)),
            new XAttribute("start", FormatTimeRational(clip.SourceInFrame, project.FrameRate))
        );

        if (lane != 0)
        {
            clipElem.Add(new XAttribute("lane", lane));
        }

        // Audio Gain / Volume element
        if (Math.Abs(clip.Gain - 1.0) > 0.001)
        {
            double dB = clip.Gain <= 0.0001 ? -96.0 : 20.0 * Math.Log10(clip.Gain);
            clipElem.Add(new XElement("adjust-volume",
                new XAttribute("amount", $"{(dB >= 0 ? "+" : "")}{dB.ToString("0.0#", CultureInfo.InvariantCulture)}dB")
            ));
        }

        return clipElem;
    }

    private static (string FrameDuration, string FormatName) GetFormatParameters(Rational rate, int height)
    {
        string resName = height switch
        {
            >= 2160 => "4K",
            >= 1440 => "1440p",
            >= 1080 => "1080p",
            >= 720 => "720p",
            _ => $"{height}p"
        };

        // Exact rational checks for standard video frame rates
        if (rate.Numerator == 30000 && rate.Denominator == 1001) return ("1001/30000s", $"FFVideoFormat{resName}2997");
        if (rate.Numerator == 60000 && rate.Denominator == 1001) return ("1001/60000s", $"FFVideoFormat{resName}5994");
        if (rate.Numerator == 24000 && rate.Denominator == 1001) return ("1001/24000s", $"FFVideoFormat{resName}2398");
        if (rate.Numerator == 24 && rate.Denominator == 1) return ("1/24s", $"FFVideoFormat{resName}24");
        if (rate.Numerator == 25 && rate.Denominator == 1) return ("1/25s", $"FFVideoFormat{resName}25");
        if (rate.Numerator == 30 && rate.Denominator == 1) return ("1/30s", $"FFVideoFormat{resName}30");
        if (rate.Numerator == 50 && rate.Denominator == 1) return ("1/50s", $"FFVideoFormat{resName}50");
        if (rate.Numerator == 60 && rate.Denominator == 1) return ("1/60s", $"FFVideoFormat{resName}60");

        double fps = rate.Value;
        if (Math.Abs(fps - 29.97002997) < 0.005) return ("1001/30000s", $"FFVideoFormat{resName}2997");
        if (Math.Abs(fps - 59.94005994) < 0.005) return ("1001/60000s", $"FFVideoFormat{resName}5994");
        if (Math.Abs(fps - 23.97602397) < 0.005) return ("1001/24000s", $"FFVideoFormat{resName}2398");
        if (Math.Abs(fps - 24.0) < 0.005) return ("1/24s", $"FFVideoFormat{resName}24");
        if (Math.Abs(fps - 25.0) < 0.005) return ("1/25s", $"FFVideoFormat{resName}25");
        if (Math.Abs(fps - 30.0) < 0.005) return ("1/30s", $"FFVideoFormat{resName}30");
        if (Math.Abs(fps - 50.0) < 0.005) return ("1/50s", $"FFVideoFormat{resName}50");
        if (Math.Abs(fps - 60.0) < 0.005) return ("1/60s", $"FFVideoFormat{resName}60");

        // General rational duration: Denominator / Numerator
        var divisor = Gcd(rate.Denominator, rate.Numerator);
        var num = rate.Denominator / divisor;
        var den = rate.Numerator / divisor;
        return ($"{num}/{den}s", $"FFVideoFormat{resName}{(int)Math.Round(fps)}");
    }

    private static Rational ParseRateFromFrameDuration(string? frameDuration)
    {
        if (string.IsNullOrWhiteSpace(frameDuration)) return new Rational(30, 1);

        var trimmed = frameDuration.Trim().TrimEnd('s', 'S');
        var slashIdx = trimmed.IndexOf('/');
        if (slashIdx >= 0)
        {
            var numStr = trimmed[..slashIdx];
            var denStr = trimmed[(slashIdx + 1)..];
            if (long.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num) &&
                long.TryParse(denStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var den) &&
                num > 0 && den > 0)
            {
                // frameDuration is seconds per frame (dt = num/den)
                // Frame rate (fps) is 1/dt = den/num
                if (num == 1001)
                {
                    if (den == 30000) return new Rational(30000, 1001); // 29.970 fps
                    if (den == 60000) return new Rational(60000, 1001); // 59.940 fps
                    if (den == 24000) return new Rational(24000, 1001); // 23.976 fps
                }
                var gcd = Gcd(den, num);
                return new Rational(checked((int)(den / gcd)), checked((int)(num / gcd)));
            }
        }
        else if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var sec) && sec > 0)
        {
            int fps = (int)Math.Round(1.0 / sec);
            return new Rational(Math.Max(1, fps), 1);
        }

        return new Rational(30, 1);
    }

    /// <summary>
    /// Formats project frames into an exact FCPXML rational time string (e.g. "1001/30000s" or "5s").
    /// </summary>
    public static string FormatTimeRational(long frames, Rational rate)
    {
        if (frames == 0) return "0s";

        // Seconds = frames * Denominator / Numerator
        long totalNum = frames * rate.Denominator;
        long totalDen = rate.Numerator;

        var gcd = Gcd(Math.Abs(totalNum), totalDen);
        totalNum /= gcd;
        totalDen /= gcd;

        if (totalDen == 1) return $"{totalNum}s";
        return $"{totalNum}/{totalDen}s";
    }

    /// <summary>
    /// Parses an FCPXML time string (e.g. "10s", "1001/30000s", "2.5s") into seconds.
    /// </summary>
    public static bool TryParseFcpxmlSeconds(string? text, out double seconds)
    {
        seconds = 0.0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim().TrimEnd('s', 'S');
        var slashIdx = trimmed.IndexOf('/');
        if (slashIdx >= 0)
        {
            var numStr = trimmed[..slashIdx];
            var denStr = trimmed[(slashIdx + 1)..];
            if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
                double.TryParse(denStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
                den != 0)
            {
                seconds = num / den;
                return true;
            }
            return false;
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
    }

    private static string FormatPathUrl(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return path;

        try
        {
            var uri = new Uri(path);
            return uri.AbsoluteUri;
        }
        catch
        {
            var normalized = path.Replace('\\', '/');
            if (!normalized.StartsWith('/')) normalized = "/" + normalized;
            return "file://localhost" + normalized;
        }
    }

    private static string ParsePathUrl(string pathUrl)
    {
        if (string.IsNullOrWhiteSpace(pathUrl)) return string.Empty;
        if (pathUrl.StartsWith("openrend://", StringComparison.OrdinalIgnoreCase)) return pathUrl;

        try
        {
            if (Uri.TryCreate(pathUrl, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                return Path.GetFullPath(uri.LocalPath);
            }
        }
        catch
        {
            // Fallback
        }

        if (pathUrl.StartsWith("file://localhost/", StringComparison.OrdinalIgnoreCase))
        {
            var raw = pathUrl["file://localhost/".Length..];
            return Uri.UnescapeDataString(raw).Replace('/', '\\');
        }
        if (pathUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var raw = pathUrl["file://".Length..];
            return Uri.UnescapeDataString(raw).Replace('/', '\\');
        }

        return pathUrl;
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }
        return a;
    }
}

