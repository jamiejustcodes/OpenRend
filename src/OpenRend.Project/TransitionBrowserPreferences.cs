using System.Text.Json;

namespace OpenRend.Project;

public sealed record TransitionBrowserPreferences
{
    public bool FavoriteDissolve { get; init; }
}

public static class TransitionBrowserPreferencesStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static TransitionBrowserPreferences Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) return new();
        try
        {
            return JsonSerializer.Deserialize<TransitionBrowserPreferences>(File.ReadAllText(path), Options)
                ?? throw new InvalidDataException("Transition browser preferences are empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Transition browser preferences JSON is invalid.", error);
        }
    }

    public static void Save(TransitionBrowserPreferences settings, string path)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
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
