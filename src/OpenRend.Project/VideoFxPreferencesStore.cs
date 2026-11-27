using System.Text.Json;
using OpenRend.Core;

namespace OpenRend.Project;

public static class VideoFxPreferencesStore
{
    public static HashSet<VideoEffectKind> Load(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            var names = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Video effect favorites are empty.");
            return VideoEffectCatalog.Definitions.Where(effect => names.Contains(effect.Kind.ToString(), StringComparer.Ordinal))
                .Select(effect => effect.Kind).ToHashSet();
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Video effect favorites JSON is invalid.", error);
        }
    }

    public static void Save(IReadOnlySet<VideoEffectKind> favorites, string path)
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
