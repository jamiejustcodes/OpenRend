using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenRend.Core;

namespace OpenRend.Project;

public sealed record PanCropPreset
{
    public string Name { get; init; } = "";
    public double Width { get; init; }
    public double Height { get; init; }
    public double CenterX { get; init; }
    public double CenterY { get; init; }
    public double RotationDegrees { get; init; }
    public double PivotX { get; init; }
    public double PivotY { get; init; }

    public static PanCropPreset FromState(string name, CropState state) => new()
    {
        Name = name,
        Width = state.Width,
        Height = state.Height,
        CenterX = state.CenterX,
        CenterY = state.CenterY,
        RotationDegrees = state.RotationDegrees,
        PivotX = state.PivotX,
        PivotY = state.PivotY
    };

    public CropState ToState() => new(Width, Height, CenterX, CenterY, RotationDegrees, PivotX, PivotY);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Preset name cannot be empty.");
        ToState().Validate();
    }
}

public sealed record PanCropPreferences
{
    public List<PanCropPreset> CustomPresets { get; init; } = [];

    public void Validate()
    {
        foreach (var p in CustomPresets) p.Validate();
    }
}

public static class PanCropPreferencesStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static PanCropPreferences Load(string path)
    {
        if (!File.Exists(path)) return new PanCropPreferences();
        try
        {
            var prefs = JsonSerializer.Deserialize<PanCropPreferences>(File.ReadAllText(path), Options)
                ?? new PanCropPreferences();
            prefs.Validate();
            return prefs;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return new PanCropPreferences();
        }
    }

    public static void Save(PanCropPreferences preferences, string path)
    {
        preferences.Validate();
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, Options));
        File.Move(temporary, fullPath, overwrite: true);
    }
}
