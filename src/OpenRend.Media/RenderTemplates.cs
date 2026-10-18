using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenRend.Core;

namespace OpenRend.Media;

[JsonConverter(typeof(JsonStringEnumConverter<EncoderSpeed>))]
public enum EncoderSpeed { Ultrafast, Superfast, Veryfast, Faster, Fast, Medium, Slow, Slower, Veryslow }

public sealed record RenderTemplate(string Id, string Name, int VideoCrf, int AudioBitrateKbps)
{
    public EncoderSpeed EncoderSpeed { get; init; } = EncoderSpeed.Fast;
    public string FormatId { get; init; } = "mp4-avc";
    public string FormatName { get; init; } = "MAGIX AVC/AAC MP4";
    public string Extension { get; init; } = ".mp4";
    public string VideoCodec { get; init; } = "libx264";
    public string AudioCodec { get; init; } = "aac";
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool IsAudioOnly { get; init; }
    public int AudioSampleRate { get; init; } = 48000;
    public int AudioBitsPerSample { get; init; } = 16;
    public int? ProResProfile { get; init; }

    [JsonIgnore]
    public bool IsCustom => Id is not null && Id.StartsWith("custom-", StringComparison.Ordinal);

    public void Validate(bool requireCustom = false)
    {
        if (string.IsNullOrWhiteSpace(Id) || (requireCustom &&
            (!IsCustom || !Guid.TryParseExact(Id[7..], "N", out _))))
            throw new InvalidDataException("The custom render template ID is invalid.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || Name != Name.Trim() || Name.Any(char.IsControl))
            throw new InvalidDataException("Enter a template name of 1 to 120 characters without control characters.");
        if (!IsAudioOnly && VideoCodec != "prores_ks" && VideoCrf is < 0 or > 51)
            throw new InvalidDataException("H.264/HEVC quality must be between 0 and 51.");
        if (AudioCodec is "aac" or "libmp3lame" && AudioBitrateKbps is < 32 or > 512)
            throw new InvalidDataException("Audio bitrate must be between 32 and 512 kbps.");
        if (!Enum.IsDefined(EncoderSpeed)) throw new InvalidDataException("The encoder speed is not supported.");
        if (Width.HasValue && (Width.Value <= 0 || (Width.Value & 1) != 0))
            throw new InvalidDataException("Width must be a positive even integer.");
        if (Height.HasValue && (Height.Value <= 0 || (Height.Value & 1) != 0))
            throw new InvalidDataException("Height must be a positive even integer.");
    }

    public string Description(ProjectDocument project)
    {
        if (IsAudioOnly)
        {
            var codec = AudioCodec == "pcm_s24le" ? "24-bit PCM" : AudioCodec == "pcm_s16le" ? "16-bit PCM" : AudioCodec.ToUpperInvariant();
            var rateStr = $"{AudioSampleRate:N0} Hz";
            var bitStr = AudioBitrateKbps > 0 ? $", {AudioBitrateKbps} kbps" : "";
            return $"{FormatName}; {codec}, {rateStr}, Stereo{bitStr}";
        }
        var w = Width ?? project.Width;
        var h = Height ?? project.Height;
        var fps = project.FrameRate.Value;
        var vCodec = VideoCodec == "libx265" ? "HEVC" : VideoCodec == "prores_ks" ? "Apple ProRes" : "H.264 AVC";
        return $"{w} × {h}, {fps:0.###} fps; {vCodec}, {AudioCodec.ToUpperInvariant()} stereo {AudioSampleRate / 1000} kHz, {AudioBitrateKbps} kbps";
    }

    public long EstimatedBytes(ProjectDocument project, long durationFrames)
    {
        if (durationFrames <= 0) return 0;
        var seconds = (double)durationFrames * project.FrameRate.Denominator / project.FrameRate.Numerator;
        if (IsAudioOnly)
        {
            if (AudioCodec.StartsWith("pcm", StringComparison.OrdinalIgnoreCase))
            {
                var bytesPerSample = AudioBitsPerSample / 8;
                return (long)(AudioSampleRate * 2 * bytesPerSample * seconds);
            }
            return (long)(AudioBitrateKbps * 1000 / 8.0 * seconds);
        }
        var pixels = (long)(Width ?? project.Width) * (Height ?? project.Height);
        var baseBpp = VideoCodec switch
        {
            "prores_ks" => 1.5,
            "libx265" => 0.06,
            _ => 0.10
        };
        var crfFactor = Math.Pow(0.94, VideoCrf - 20);
        var fps = project.FrameRate.Value;
        var videoBps = pixels * fps * baseBpp * crfFactor;
        var audioBps = AudioBitrateKbps * 1000.0;
        return (long)((videoBps + audioBps) / 8.0 * seconds);
    }
}

public sealed record RenderFormatInfo(string Id, string Name, string Extension, bool IsAudioOnly);

public static class RenderTemplateCatalog
{
    public const string DefaultId = "project-mp4";

    public static IReadOnlyList<RenderFormatInfo> Formats { get; } = Array.AsReadOnly<RenderFormatInfo>([
        new("all", "All Formats", ".*", false),
        new("mp4-avc", "MAGIX AVC/AAC MP4", ".mp4", false),
        new("mp4-hevc", "MAGIX HEVC/AAC MP4", ".mp4", false),
        new("mov-prores", "Apple ProRes", ".mov", false),
        new("audio-wav", "Sony Wave Audio", ".wav", true),
        new("audio-mp3", "MP3 Audio", ".mp3", true)
    ]);

    public static IReadOnlyList<RenderTemplate> All { get; } = Array.AsReadOnly<RenderTemplate>([
        // MAGIX AVC / AAC MP4
        new(DefaultId, "Project settings", 20, 192) { FormatId = "mp4-avc", FormatName = "MAGIX AVC/AAC MP4", Extension = ".mp4", VideoCodec = "libx264", AudioCodec = "aac" },
        new("project-mp4-high", "Project settings — High quality", 17, 256) { FormatId = "mp4-avc", FormatName = "MAGIX AVC/AAC MP4", Extension = ".mp4", VideoCodec = "libx264", AudioCodec = "aac" },
        new("project-mp4-4k", "Internet 4K 2160p", 18, 320) { FormatId = "mp4-avc", FormatName = "MAGIX AVC/AAC MP4", Extension = ".mp4", VideoCodec = "libx264", AudioCodec = "aac", Width = 3840, Height = 2160 },
        new("project-mp4-720p", "Internet HD 720p", 22, 160) { FormatId = "mp4-avc", FormatName = "MAGIX AVC/AAC MP4", Extension = ".mp4", VideoCodec = "libx264", AudioCodec = "aac", Width = 1280, Height = 720 },

        // MAGIX HEVC / AAC MP4
        new("project-hevc-4k", "MAGIX HEVC Internet UHD 2160p", 22, 256) { FormatId = "mp4-hevc", FormatName = "MAGIX HEVC/AAC MP4", Extension = ".mp4", VideoCodec = "libx265", AudioCodec = "aac", Width = 3840, Height = 2160 },
        new("project-hevc-1080p", "MAGIX HEVC Internet HD 1080p", 24, 192) { FormatId = "mp4-hevc", FormatName = "MAGIX HEVC/AAC MP4", Extension = ".mp4", VideoCodec = "libx265", AudioCodec = "aac", Width = 1920, Height = 1080 },

        // Apple ProRes MOV
        new("project-prores-422", "Apple ProRes 422", 0, 0) { FormatId = "mov-prores", FormatName = "Apple ProRes", Extension = ".mov", VideoCodec = "prores_ks", AudioCodec = "pcm_s24le", ProResProfile = 2, AudioBitsPerSample = 24 },
        new("project-prores-hq", "Apple ProRes 422 HQ", 0, 0) { FormatId = "mov-prores", FormatName = "Apple ProRes", Extension = ".mov", VideoCodec = "prores_ks", AudioCodec = "pcm_s24le", ProResProfile = 3, AudioBitsPerSample = 24 },

        // Sony Wave Audio
        new("project-wav-24", "Sony Wave 48,000 Hz, 24-bit, Stereo, PCM", 0, 0) { FormatId = "audio-wav", FormatName = "Sony Wave Audio", Extension = ".wav", VideoCodec = "none", AudioCodec = "pcm_s24le", IsAudioOnly = true, AudioSampleRate = 48000, AudioBitsPerSample = 24 },
        new("project-wav-16", "Sony Wave 44,100 Hz, 16-bit, Stereo, PCM", 0, 0) { FormatId = "audio-wav", FormatName = "Sony Wave Audio", Extension = ".wav", VideoCodec = "none", AudioCodec = "pcm_s16le", IsAudioOnly = true, AudioSampleRate = 44100, AudioBitsPerSample = 16 },

        // MP3 Audio
        new("project-mp3-320", "MP3 Audio 320 kbps, 48,000 Hz, Stereo", 0, 320) { FormatId = "audio-mp3", FormatName = "MP3 Audio", Extension = ".mp3", VideoCodec = "none", AudioCodec = "libmp3lame", IsAudioOnly = true, AudioSampleRate = 48000 },
        new("project-mp3-192", "MP3 Audio 192 kbps, 48,000 Hz, Stereo", 0, 192) { FormatId = "audio-mp3", FormatName = "MP3 Audio", Extension = ".mp3", VideoCodec = "none", AudioCodec = "libmp3lame", IsAudioOnly = true, AudioSampleRate = 48000 }
    ]);

    public static RenderTemplate Find(string id) => All.FirstOrDefault(template => template.Id == id)
        ?? throw new InvalidDataException("The selected render template is not supported.");

    public static IEnumerable<RenderTemplate> Filter(ProjectDocument project, string query, bool favoritesOnly, IReadOnlyCollection<string> favorites, string? formatId = null, bool matchProject = false)
        => Filter(All, project, query, favoritesOnly, favorites, formatId, matchProject);

    public static IEnumerable<RenderTemplate> Filter(IEnumerable<RenderTemplate> templates, ProjectDocument project, string query, bool favoritesOnly, IReadOnlyCollection<string> favorites, string? formatId = null, bool matchProject = false)
    {
        var terms = query.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(term => term.Trim()).ToArray();
        return templates.Where(template =>
        {
            if (favoritesOnly && !favorites.Contains(template.Id)) return false;
            if (!string.IsNullOrEmpty(formatId) && formatId != "all" && !string.Equals(template.FormatId, formatId, StringComparison.OrdinalIgnoreCase)) return false;
            if (matchProject)
            {
                if (template.IsAudioOnly) return false;
                var effectiveWidth = template.Width ?? project.Width;
                var effectiveHeight = template.Height ?? project.Height;
                if (effectiveWidth != project.Width || effectiveHeight != project.Height) return false;
            }
            if (terms.Length == 0) return true;

            var searchable = new List<string>
            {
                template.Name,
                template.FormatName,
                template.Extension,
                template.Extension.TrimStart('.'),
                template.VideoCodec,
                template.AudioCodec,
                "Stereo"
            };

            if (template.VideoCodec == "libx264")
            {
                searchable.Add("H.264");
                searchable.Add("H.264 AVC");
                searchable.Add("AVC");
            }
            else if (template.VideoCodec == "libx265")
            {
                searchable.Add("H.265");
                searchable.Add("HEVC");
            }
            else if (template.VideoCodec == "prores_ks")
            {
                searchable.Add("ProRes");
                searchable.Add("Apple ProRes");
            }

            if (template.AudioCodec == "aac")
            {
                searchable.Add("AAC");
            }
            else if (template.AudioCodec.StartsWith("pcm", StringComparison.OrdinalIgnoreCase))
            {
                searchable.Add("PCM");
                searchable.Add("WAV");
                searchable.Add(template.AudioBitsPerSample == 24 ? "24-bit" : "16-bit");
            }
            else if (template.AudioCodec == "libmp3lame")
            {
                searchable.Add("MP3");
            }

            if (!template.IsAudioOnly)
            {
                searchable.Add("8-bit");
                searchable.Add("Progressive");
                searchable.Add("SDR");
                searchable.Add($"{project.Width}x{project.Height}");
                searchable.Add($"{project.Width} x {project.Height}");
                searchable.Add($"{template.Width ?? project.Width}x{template.Height ?? project.Height}");
                searchable.Add($"{template.Width ?? project.Width} x {template.Height ?? project.Height}");
                searchable.Add(project.FrameRate.ToString());
                searchable.Add(project.FrameRate.Value.ToString("0.###", CultureInfo.InvariantCulture));
                searchable.Add(template.EncoderSpeed.ToString());
            }

            searchable.Add(template.AudioSampleRate.ToString(CultureInfo.InvariantCulture));
            searchable.Add($"{template.AudioSampleRate / 1000} kHz");
            if (template.AudioBitrateKbps > 0)
            {
                searchable.Add($"{template.AudioBitrateKbps} kbps");
            }

            return terms.All(term => searchable.Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase)));
        });
    }
}

public sealed record RenderPreferences
{
    public string TemplateId { get; init; } = RenderTemplateCatalog.DefaultId;
    public string[] Favorites { get; init; } = [];
    public string? OutputFolder { get; init; }
    public bool UseLoopRegion { get; init; }
    public bool StretchToFill { get; init; }
    public ChannelRemapping ChannelMapping { get; init; } = ChannelRemapping.None;
    public bool GenerateLoudnessLog { get; init; }
    public string SelectedFormatId { get; init; } = "all";
    public bool MatchProjectSettings { get; init; }
    public RenderTemplate[] CustomTemplates { get; init; } = [];

    [JsonIgnore]
    public IEnumerable<RenderTemplate> Templates => RenderTemplateCatalog.All.Concat(CustomTemplates);

    public RenderTemplate FindTemplate(string id) => Templates.FirstOrDefault(template => template.Id == id)
        ?? throw new InvalidDataException("The selected render template is not supported.");

    public RenderPreferences SaveCustomTemplate(string name, RenderTemplate settings, string? existingId = null)
    {
        Validate();
        ArgumentNullException.ThrowIfNull(settings);
        if (existingId is not null && !CustomTemplates.Any(template => template.Id == existingId))
            throw new InvalidDataException("Only a saved custom template can be replaced.");
        var saved = settings with { Id = existingId ?? "custom-" + Guid.NewGuid().ToString("N"), Name = name?.Trim() ?? "" };
        saved.Validate(requireCustom: true);
        var updated = this with { TemplateId = saved.Id,
            CustomTemplates = CustomTemplates.Where(template => template.Id != saved.Id).Append(saved).ToArray() };
        updated.Validate();
        return updated;
    }

    public RenderPreferences RenameCustomTemplate(string id, string name)
    {
        var existing = FindTemplate(id);
        var updated = SaveCustomTemplate(name, existing, id);
        return updated with { TemplateId = TemplateId };
    }

    public RenderPreferences DeleteCustomTemplate(string id)
    {
        Validate();
        if (!CustomTemplates.Any(template => template.Id == id))
            throw new InvalidDataException("Only a saved custom template can be deleted.");
        return this with { CustomTemplates = CustomTemplates.Where(template => template.Id != id).ToArray(),
            Favorites = Favorites.Where(favorite => favorite != id).ToArray(),
            TemplateId = TemplateId == id ? RenderTemplateCatalog.DefaultId : TemplateId };
    }

    public void Validate()
    {
        if (CustomTemplates is null || CustomTemplates.Any(template => template is null))
            throw new InvalidDataException("Custom render templates are invalid.");
        foreach (var template in CustomTemplates) template.Validate(requireCustom: true);
        if (Templates.Select(template => template.Id).Distinct(StringComparer.Ordinal).Count() != Templates.Count() ||
            Templates.Select(template => template.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Templates.Count())
            throw new InvalidDataException("Render template IDs and names must be unique.");
        FindTemplate(TemplateId);
        if (Favorites is null || Favorites.Distinct().Count() != Favorites.Length)
            throw new InvalidDataException("Render favorites are invalid.");
        foreach (var id in Favorites) FindTemplate(id);
        if (OutputFolder is not null && (string.IsNullOrWhiteSpace(OutputFolder) || !Path.IsPathFullyQualified(OutputFolder)))
            throw new InvalidDataException("The render output folder must be an absolute path.");
    }
}

public static class RenderPreferencesStore
{
    public static RenderPreferences Load(string path)
    {
        if (!File.Exists(path)) return new();
        RenderPreferences settings;
        try { settings = JsonSerializer.Deserialize<RenderPreferences>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Render preferences are empty."); }
        catch (JsonException error) { throw new InvalidDataException("Render preferences JSON is invalid.", error); }
        settings.Validate();
        return settings;
    }

    public static void Save(RenderPreferences settings, string path)
    {
        settings.Validate();
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
