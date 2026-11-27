using System.Text.Json;

namespace OpenRend.Project;

public static class ToolBrowserFavoritesStore
{
    public static HashSet<TKind> Load<TKind>(string path) where TKind : struct, Enum
    {
        if (!File.Exists(path)) return [];
        try
        {
            var names = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Browser favorites are empty.");
            return Enum.GetValues<TKind>().Where(kind => names.Contains(kind.ToString(), StringComparer.Ordinal)).ToHashSet();
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Browser favorites JSON is invalid.", error);
        }
    }

    public static void Save<TKind>(IReadOnlySet<TKind> favorites, string path) where TKind : struct, Enum
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var names = favorites.Select(kind => kind.ToString()).Order(StringComparer.Ordinal).ToArray();
            File.WriteAllText(temporary, JsonSerializer.Serialize(names));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
