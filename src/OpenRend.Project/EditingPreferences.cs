using System.Text.Json;
using System.Text.Json.Serialization;
using OpenRend.Core;

namespace OpenRend.Project;

public sealed record EditingPreferences
{
    public double CutOverlapSeconds { get; init; } = .25;
    public CutTransitionAlignment CutOverlapAlignment { get; init; } = CutTransitionAlignment.CenteredOnCut;

    public void Validate()
    {
        if (!double.IsFinite(CutOverlapSeconds) || CutOverlapSeconds is <= 0 or > 3600 ||
            !Enum.IsDefined(CutOverlapAlignment))
            throw new InvalidDataException("Cut-to-overlap preferences are invalid.");
    }

    public long OverlapFrames(Rational rate)
    {
        Validate();
        return Math.Max(1, rate.Frames(CutOverlapSeconds));
    }
}

public static class EditingPreferencesStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static EditingPreferences Load(string path)
    {
        if (!File.Exists(path)) return new EditingPreferences();
        EditingPreferences settings;
        try { settings = JsonSerializer.Deserialize<EditingPreferences>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Editing preferences are empty."); }
        catch (JsonException error) { throw new InvalidDataException("Editing preferences JSON is invalid.", error); }
        settings.Validate();
        return settings;
    }

    public static void Save(EditingPreferences settings, string path)
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
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
