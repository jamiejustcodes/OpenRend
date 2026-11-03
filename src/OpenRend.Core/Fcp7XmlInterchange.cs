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
/// Apple Final Cut Pro 7 XML Interchange (&lt;xmeml version="4"&gt;) Import and Export.
/// Matches Vegas Pro Project Interchange architecture (VegasScripts.Fcp7Exporter / Fcp7Importer).
/// Compatible with Vegas Pro, Premiere Pro, DaVinci Resolve, and Final Cut Pro.
/// </summary>
public static class Fcp7XmlInterchange
{
    /// <summary>
    /// Exports an OpenRend project document to Final Cut Pro 7 XML (&lt;xmeml version="4"&gt;).
    /// </summary>
    public static string ExportToXml(ProjectDocument project, string? sequenceName = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var seqName = string.IsNullOrWhiteSpace(sequenceName) ? (project.Name ?? "Sequence 1") : sequenceName;

        var (timebase, ntsc) = GetRateParameters(project.FrameRate);
        var totalDuration = project.DurationFrames;

        var xmeml = new XElement("xmeml",
            new XAttribute("version", "4"),
            new XElement("project",
                new XElement("name", project.Name ?? "Untitled Project"),
                new XElement("children",
                    new XElement("sequence",
                        new XAttribute("id", "sequence-1"),
                        new XElement("name", seqName),
                        new XElement("duration", totalDuration),
                        CreateRateElement(timebase, ntsc),
                        new XElement("timecode",
                            CreateRateElement(timebase, ntsc),
                            new XElement("string", FormatTimecode(0, project.FrameRate)),
                            new XElement("frame", 0),
                            new XElement("displayformat", ntsc ? "DF" : "NDF")
                        ),
                        new XElement("media",
                            CreateVideoMediaElement(project, timebase, ntsc),
                            CreateAudioMediaElement(project, timebase, ntsc)
                        )
                    )
                )
            )
        );

        // Append Sequence Markers
        var seqElem = xmeml.Descendants("sequence").FirstOrDefault();
        if (seqElem is not null && project.Markers.Count > 0)
        {
            foreach (var m in project.Markers.OrderBy(m => m.Frame))
            {
                seqElem.Add(new XElement("marker",
                    new XElement("name", m.Label),
                    new XElement("comment", m.Label),
                    new XElement("in", m.Frame),
                    new XElement("out", m.Frame)
                ));
            }
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("xmeml", null, null, null),
            xmeml
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
    /// Imports a Final Cut Pro 7 XML (&lt;xmeml&gt;) into a new OpenRend project document.
    /// </summary>
    public static ProjectDocument ImportFromXml(string xmlContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xmlContent);

        var doc = XDocument.Parse(xmlContent);
        var seqElem = doc.Descendants("sequence").FirstOrDefault()
            ?? throw new InvalidDataException("Invalid FCP 7 XML: no <sequence> element found.");

        var seqName = seqElem.Element("name")?.Value ?? "Imported Sequence";

        // Frame rate
        var rateElem = seqElem.Element("rate")
            ?? seqElem.Element("timecode")?.Element("rate");
        var frameRate = ParseRateElement(rateElem);

        // Resolution
        int width = 1920;
        int height = 1080;
        var sampleElem = seqElem.Descendants("samplecharacteristics").FirstOrDefault();
        if (sampleElem is not null)
        {
            if (int.TryParse(sampleElem.Element("width")?.Value, out var w) && w > 0) width = w;
            if (int.TryParse(sampleElem.Element("height")?.Value, out var h) && h > 0) height = h;
        }

        var mediaAssetMap = new Dictionary<string, MediaAsset>(StringComparer.OrdinalIgnoreCase);
        var mediaList = new List<MediaAsset>();
        var tracks = new List<TimelineTrack>();

        // 1. Video Tracks
        var videoTracksElem = seqElem.Element("media")?.Element("video")?.Elements("track");
        if (videoTracksElem is not null)
        {
            int videoIndex = 1;
            foreach (var trackElem in videoTracksElem)
            {
                var clips = new List<TimelineClip>();
                foreach (var clipElem in trackElem.Elements("clipitem"))
                {
                    var clip = ParseClipItem(clipElem, TrackKind.Video, frameRate, width, height, mediaAssetMap, mediaList);
                    if (clip is not null) clips.Add(clip);
                }

                var track = new TimelineTrack
                {
                    Id = Guid.NewGuid(),
                    Kind = TrackKind.Video,
                    Name = $"Video {videoIndex++}",
                    Clips = clips
                };
                tracks.Add(track);
            }
        }

        // 2. Audio Tracks
        var audioTracksElem = seqElem.Element("media")?.Element("audio")?.Elements("track");
        if (audioTracksElem is not null)
        {
            int audioIndex = 1;
            foreach (var trackElem in audioTracksElem)
            {
                var clips = new List<TimelineClip>();
                foreach (var clipElem in trackElem.Elements("clipitem"))
                {
                    var clip = ParseClipItem(clipElem, TrackKind.Audio, frameRate, width, height, mediaAssetMap, mediaList);
                    if (clip is not null) clips.Add(clip);
                }

                var track = new TimelineTrack
                {
                    Id = Guid.NewGuid(),
                    Kind = TrackKind.Audio,
                    Name = $"Audio {audioIndex++}",
                    Clips = clips
                };
                tracks.Add(track);
            }
        }

        // 3. Markers
        var markers = new List<TimelineMarker>();
        int markerIdx = 1;
        foreach (var markerElem in seqElem.Elements("marker"))
        {
            var name = markerElem.Element("name")?.Value ?? $"Marker {markerIdx}";
            if (long.TryParse(markerElem.Element("in")?.Value, out var frame))
            {
                markers.Add(new TimelineMarker
                {
                    Id = Guid.NewGuid(),
                    Index = markerIdx++,
                    Frame = Math.Max(0, frame),
                    Label = name,
                    ColorId = 0
                });
            }
        }

        var project = new ProjectDocument
        {
            Name = seqName,
            Width = width,
            Height = height,
            FrameRate = frameRate,
            Media = mediaList,
            Tracks = tracks,
            Markers = markers
        };

        return project;
    }

    private static XElement CreateRateElement(int timebase, bool ntsc)
    {
        return new XElement("rate",
            new XElement("timebase", timebase),
            new XElement("ntsc", ntsc ? "TRUE" : "FALSE")
        );
    }

    private static (int Timebase, bool Ntsc) GetRateParameters(Rational rate)
    {
        double fps = rate.Value;
        if (Math.Abs(fps - 29.97) < 0.05) return (30, true);
        if (Math.Abs(fps - 59.94) < 0.05) return (60, true);
        if (Math.Abs(fps - 23.976) < 0.05) return (24, true);
        if (Math.Abs(fps - 24.0) < 0.05) return (24, false);
        if (Math.Abs(fps - 25.0) < 0.05) return (25, false);
        if (Math.Abs(fps - 30.0) < 0.05) return (30, false);
        if (Math.Abs(fps - 50.0) < 0.05) return (50, false);
        if (Math.Abs(fps - 60.0) < 0.05) return (60, false);

        int tb = (int)Math.Round(fps);
        return (tb > 0 ? tb : 30, false);
    }

    private static Rational ParseRateElement(XElement? rateElem)
    {
        if (rateElem is null) return new Rational(30, 1);
        int timebase = 30;
        bool ntsc = false;

        if (int.TryParse(rateElem.Element("timebase")?.Value, out var tb) && tb > 0)
            timebase = tb;

        var ntscText = rateElem.Element("ntsc")?.Value?.Trim();
        if (string.Equals(ntscText, "TRUE", StringComparison.OrdinalIgnoreCase))
            ntsc = true;

        if (ntsc)
        {
            if (timebase == 30) return new Rational(30000, 1001); // 29.970 fps
            if (timebase == 60) return new Rational(60000, 1001); // 59.940 fps
            if (timebase == 24) return new Rational(24000, 1001); // 23.976 fps
        }

        return new Rational(timebase, 1);
    }

    private static XElement CreateVideoMediaElement(ProjectDocument project, int timebase, bool ntsc)
    {
        var videoElem = new XElement("video",
            new XElement("format",
                new XElement("samplecharacteristics",
                    CreateRateElement(timebase, ntsc),
                    new XElement("width", project.Width),
                    new XElement("height", project.Height),
                    new XElement("anamorphic", "FALSE"),
                    new XElement("pixelaspectratio", "square"),
                    new XElement("fielddominance", "none")
                )
            )
        );

        var mediaById = project.Media.ToDictionary(m => m.Id);
        int clipItemIdx = 1;

        foreach (var track in project.Tracks.Where(t => t.Kind == TrackKind.Video))
        {
            var trackElem = new XElement("track");
            foreach (var clip in track.Clips.OrderBy(c => c.StartFrame))
            {
                mediaById.TryGetValue(clip.MediaId, out var asset);
                var clipItem = CreateClipItemElement(clip, asset, "clipitem-" + clipItemIdx++, TrackKind.Video, timebase, ntsc, project.Width, project.Height);
                trackElem.Add(clipItem);
            }
            videoElem.Add(trackElem);
        }

        return videoElem;
    }

    private static XElement CreateAudioMediaElement(ProjectDocument project, int timebase, bool ntsc)
    {
        var audioElem = new XElement("audio",
            new XElement("format",
                new XElement("samplecharacteristics",
                    new XElement("depth", 16),
                    new XElement("samplerate", 48000)
                )
            )
        );

        var mediaById = project.Media.ToDictionary(m => m.Id);
        int clipItemIdx = 1;

        foreach (var track in project.Tracks.Where(t => t.Kind == TrackKind.Audio))
        {
            var trackElem = new XElement("track");
            foreach (var clip in track.Clips.OrderBy(c => c.StartFrame))
            {
                mediaById.TryGetValue(clip.MediaId, out var asset);
                var clipItem = CreateClipItemElement(clip, asset, "clipitem-a" + clipItemIdx++, TrackKind.Audio, timebase, ntsc, 0, 0);
                trackElem.Add(clipItem);
            }
            audioElem.Add(trackElem);
        }

        return audioElem;
    }

    private static XElement CreateClipItemElement(TimelineClip clip, MediaAsset? asset, string id, TrackKind kind, int timebase, bool ntsc, int width, int height)
    {
        var clipName = clip.Name ?? (asset is not null ? Path.GetFileName(asset.Path) : "Clip");
        var filePath = asset?.Path ?? string.Empty;
        var pathUrl = FormatPathUrl(filePath);

        var item = new XElement("clipitem",
            new XAttribute("id", id),
            new XElement("name", clipName),
            new XElement("duration", clip.DurationFrames),
            CreateRateElement(timebase, ntsc),
            new XElement("start", clip.StartFrame),
            new XElement("end", clip.EndFrame),
            new XElement("in", clip.SourceInFrame),
            new XElement("out", clip.SourceInFrame + clip.DurationFrames)
        );

        if (!string.IsNullOrEmpty(filePath))
        {
            var fileElem = new XElement("file",
                new XAttribute("id", "file-" + (asset?.Id.ToString("N") ?? Guid.NewGuid().ToString("N"))),
                new XElement("name", Path.GetFileName(filePath)),
                new XElement("pathurl", pathUrl),
                CreateRateElement(timebase, ntsc),
                new XElement("duration", clip.DurationFrames)
            );

            if (kind == TrackKind.Video)
            {
                fileElem.Add(new XElement("media",
                    new XElement("video",
                        new XElement("samplecharacteristics",
                            new XElement("width", asset?.Width > 0 ? asset.Width : width),
                            new XElement("height", asset?.Height > 0 ? asset.Height : height)
                        )
                    )
                ));
            }
            else
            {
                fileElem.Add(new XElement("media",
                    new XElement("audio",
                        new XElement("samplecharacteristics",
                            new XElement("samplerate", 48000)
                        )
                    )
                ));
            }

            item.Add(fileElem);
        }

        // Opacity or Volume parameters
        if (kind == TrackKind.Video && Math.Abs(clip.Opacity - 1.0) > 0.001)
        {
            item.Add(new XElement("filter",
                new XElement("effect",
                    new XElement("name", "Opacity"),
                    new XElement("effectid", "opacity"),
                    new XElement("effecttype", "motion"),
                    new XElement("parameter",
                        new XElement("parameterid", "opacity"),
                        new XElement("name", "opacity"),
                        new XElement("value", (clip.Opacity * 100.0).ToString("0.0", CultureInfo.InvariantCulture))
                    )
                )
            ));
        }
        else if (kind == TrackKind.Audio && Math.Abs(clip.Gain - 1.0) > 0.001)
        {
            double dB = clip.Gain <= 0.0001 ? -60.0 : 20.0 * Math.Log10(clip.Gain);
            item.Add(new XElement("filter",
                new XElement("effect",
                    new XElement("name", "Audio Levels"),
                    new XElement("effectid", "audiolevels"),
                    new XElement("parameter",
                        new XElement("parameterid", "level"),
                        new XElement("name", "Level"),
                        new XElement("value", dB.ToString("0.0", CultureInfo.InvariantCulture))
                    )
                )
            ));
        }

        return item;
    }

    private static TimelineClip? ParseClipItem(XElement clipElem, TrackKind kind, Rational frameRate, int defWidth, int defHeight, Dictionary<string, MediaAsset> assetMap, List<MediaAsset> assetList)
    {
        var name = clipElem.Element("name")?.Value ?? "Clip";
        if (!long.TryParse(clipElem.Element("start")?.Value, out var start)) return null;
        if (!long.TryParse(clipElem.Element("end")?.Value, out var end) || end <= start) return null;
        long duration = end - start;

        long sourceIn = 0;
        if (long.TryParse(clipElem.Element("in")?.Value, out var sIn) && sIn >= 0)
            sourceIn = sIn;

        var fileElem = clipElem.Element("file");
        string rawUrl = fileElem?.Element("pathurl")?.Value ?? string.Empty;
        string resolvedPath = ParsePathUrl(rawUrl);

        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            resolvedPath = $"openrend://generator/{kind.ToString().ToLowerInvariant()}";
        }

        if (!assetMap.TryGetValue(resolvedPath, out var asset))
        {
            int w = defWidth;
            int h = defHeight;
            var wElem = fileElem?.Descendants("width").FirstOrDefault();
            var hElem = fileElem?.Descendants("height").FirstOrDefault();
            if (int.TryParse(wElem?.Value, out var pw) && pw > 0) w = pw;
            if (int.TryParse(hElem?.Value, out var ph) && ph > 0) h = ph;

            asset = new MediaAsset
            {
                Id = Guid.NewGuid(),
                Path = resolvedPath,
                DurationSeconds = frameRate.Seconds(duration + sourceIn + 100),
                Width = w,
                Height = h,
                FrameRate = frameRate.Value,
                HasVideo = kind == TrackKind.Video,
                HasAudio = kind == TrackKind.Audio
            };
            assetMap[resolvedPath] = asset;
            assetList.Add(asset);
        }

        double opacity = 1.0;
        double gain = 1.0;

        var opVal = clipElem.Descendants("parameter")
            .FirstOrDefault(p => string.Equals(p.Element("parameterid")?.Value, "opacity", StringComparison.OrdinalIgnoreCase))
            ?.Element("value")?.Value;
        if (double.TryParse(opVal, NumberStyles.Float, CultureInfo.InvariantCulture, out var opPercent))
        {
            opacity = Math.Clamp(opPercent / 100.0, 0.0, 1.0);
        }

        var levelVal = clipElem.Descendants("parameter")
            .FirstOrDefault(p => string.Equals(p.Element("parameterid")?.Value, "level", StringComparison.OrdinalIgnoreCase))
            ?.Element("value")?.Value;
        if (double.TryParse(levelVal, NumberStyles.Float, CultureInfo.InvariantCulture, out var dB))
        {
            gain = dB <= -59.0 ? 0.0 : Math.Pow(10.0, dB / 20.0);
        }

        return new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = asset.Id,
            Name = name,
            StartFrame = start,
            DurationFrames = duration,
            SourceInFrame = sourceIn,
            Opacity = opacity,
            Gain = gain
        };
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
            if (Uri.TryCreate(pathUrl, UriKind.Absolute, out var uri))
            {
                if (uri.IsFile)
                {
                    return Path.GetFullPath(uri.LocalPath);
                }
            }
        }
        catch
        {
            // Fallback to substring parsing below
        }

        if (pathUrl.StartsWith("file://localhost/", StringComparison.OrdinalIgnoreCase))
        {
            var raw = pathUrl.Substring("file://localhost/".Length);
            return Uri.UnescapeDataString(raw).Replace('/', '\\');
        }
        if (pathUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var raw = pathUrl.Substring("file://".Length);
            return Uri.UnescapeDataString(raw).Replace('/', '\\');
        }

        return pathUrl;
    }

    private static string FormatTimecode(long frames, Rational rate)
    {
        long fps = Math.Max(1, (long)Math.Round(rate.Value));
        long totalSeconds = frames / fps;
        long f = frames % fps;
        long s = totalSeconds % 60;
        long m = (totalSeconds / 60) % 60;
        long h = totalSeconds / 3600;
        return $"{h:D2}:{m:D2}:{s:D2};{f:D2}";
    }
}
