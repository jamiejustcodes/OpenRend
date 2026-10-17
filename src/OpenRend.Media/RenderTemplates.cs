using System.Globalization;
using System.Text.Json;
using OpenRend.Core;

namespace OpenRend.Media;

public sealed record RenderTemplate(string Id, string Name, int VideoCrf, int AudioBitrateKbps)
{
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
    {
        // Each semicolon-separated term must match at least one template property.
        var terms = query.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(term => term.Trim()).ToArray();
        return All.Where(template => (!favoritesOnly || favorites.Contains(template.Id)) && terms.All(term =>
            new[] { template.Name, "H.264 AVC", "AAC", "MP4", "Stereo", "48000", "48 kHz", "8-bit", "Progressive", "SDR",
                $"{project.Width}x{project.Height}", $"{project.Width} x {project.Height}",
                project.FrameRate.ToString(), project.FrameRate.Value.ToString("0.###", CultureInfo.InvariantCulture),
                template.AudioBitrateKbps.ToString(CultureInfo.InvariantCulture) + " kbps" }
            .Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase))));
    }
}

public sealed record RenderPreferences
{
    public string TemplateId { get; init; } = RenderTemplateCatalog.DefaultId;
    public string[] Favorites { get; init; } = [];
    public string? OutputFolder { get; init; }
    public bool UseLoopRegion { get; init; }

    public void Validate()
    {
        RenderTemplateCatalog.Find(TemplateId);
        if (Favorites is null || Favorites.Distinct().Count() != Favorites.Length)
            throw new InvalidDataException("Render favorites are invalid.");
        foreach (var id in Favorites) RenderTemplateCatalog.Find(id);
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
