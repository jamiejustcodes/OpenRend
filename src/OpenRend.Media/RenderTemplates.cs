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
    [JsonIgnore]
    public bool IsCustom => Id is not null && Id.StartsWith("custom-", StringComparison.Ordinal);

    public void Validate(bool requireCustom = false)
    {
        if (string.IsNullOrWhiteSpace(Id) || (requireCustom &&
            (!IsCustom || !Guid.TryParseExact(Id[7..], "N", out _))))
            throw new InvalidDataException("The custom render template ID is invalid.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || Name != Name.Trim() || Name.Any(char.IsControl))
            throw new InvalidDataException("Enter a template name of 1 to 120 characters without control characters.");
        if (VideoCrf is < 0 or > 51) throw new InvalidDataException("H.264 quality must be between 0 and 51.");
        if (AudioBitrateKbps is < 32 or > 512) throw new InvalidDataException("AAC bitrate must be between 32 and 512 kbps.");
        if (!Enum.IsDefined(EncoderSpeed)) throw new InvalidDataException("The encoder speed is not supported.");
    }

    public string Description(ProjectDocument project) => $"{project.Width} × {project.Height}, {project.FrameRate.Value:0.###} fps; H.264, AAC stereo 48 kHz, {AudioBitrateKbps} kbps";
}

public static class RenderTemplateCatalog
{
    public const string DefaultId = "project-mp4";
    public static IReadOnlyList<RenderTemplate> All { get; } = Array.AsReadOnly<RenderTemplate>([
        new(DefaultId, "Project settings", 20, 192),
        new("project-mp4-high", "Project settings — High quality", 17, 256)
    ]);

    public static RenderTemplate Find(string id) => All.FirstOrDefault(template => template.Id == id)
        ?? throw new InvalidDataException("The selected render template is not supported.");

    public static IEnumerable<RenderTemplate> Filter(ProjectDocument project, string query, bool favoritesOnly, IReadOnlyCollection<string> favorites)
        => Filter(All, project, query, favoritesOnly, favorites);

    public static IEnumerable<RenderTemplate> Filter(IEnumerable<RenderTemplate> templates, ProjectDocument project, string query, bool favoritesOnly, IReadOnlyCollection<string> favorites)
    {
        // Each semicolon-separated term must match at least one template property.
        var terms = query.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(term => term.Trim()).ToArray();
        return templates.Where(template => (!favoritesOnly || favorites.Contains(template.Id)) && terms.All(term =>
            new[] { template.Name, "H.264 AVC", "AAC", "MP4", "Stereo", "48000", "48 kHz", "8-bit", "Progressive", "SDR",
                $"{project.Width}x{project.Height}", $"{project.Width} x {project.Height}",
                project.FrameRate.ToString(), project.FrameRate.Value.ToString("0.###", CultureInfo.InvariantCulture),
                template.AudioBitrateKbps.ToString(CultureInfo.InvariantCulture) + " kbps", template.EncoderSpeed.ToString() }
            .Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase))));
    }
}

public sealed record RenderPreferences
{
    public string TemplateId { get; init; } = RenderTemplateCatalog.DefaultId;
    public string[] Favorites { get; init; } = [];
    public string? OutputFolder { get; init; }
    public bool UseLoopRegion { get; init; }
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
