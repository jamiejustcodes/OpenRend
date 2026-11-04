using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenRend.Project;

/// <summary>
/// Vegas Pro 1:1 general preferences: periodic autosave intervals, backup files (.bak),
/// and project-adjacent autosave directories.
/// </summary>
public sealed record GeneralPreferences
{
    public bool EnableAutoSave { get; init; } = true;
    public int AutoSaveIntervalMinutes { get; init; } = 5;
    public bool CreateProjectBackups { get; init; } = true;
    public bool UseProjectDirectoryForAutoSave { get; init; } = true;

    public void Validate()
    {
        if (AutoSaveIntervalMinutes is < 1 or > 120)
            throw new InvalidDataException("AutoSave interval must be between 1 and 120 minutes.");
    }
}

public static class GeneralPreferencesStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static GeneralPreferences Load(string path)
    {
        if (!File.Exists(path)) return new GeneralPreferences();
        GeneralPreferences settings;
        try
        {
            settings = JsonSerializer.Deserialize<GeneralPreferences>(File.ReadAllText(path), Options)
                ?? throw new InvalidDataException("General preferences are empty.");
        }
        catch (JsonException error) { throw new InvalidDataException("General preferences JSON is invalid.", error); }
        settings.Validate();
        return settings;
    }

    public static void Save(GeneralPreferences settings, string path)
    {
        settings.Validate();
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

