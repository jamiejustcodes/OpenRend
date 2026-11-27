using System;
using System.Collections.Generic;
using System.IO;

namespace OpenRend.Core;

public enum MediaGeneratorKind
{
    SolidColor,
    TestPattern,
    ColorGradient,
    Checkerboard,
    NoiseTexture,
    CreditRoll,
    TitlesAndText
}

public sealed class MediaGeneratorData
{
    public MediaGeneratorKind Kind { get; set; } = MediaGeneratorKind.SolidColor;
    public string PresetName { get; set; } = "(Default)";
    public string Title { get; set; } = "Solid Color";
    public Dictionary<string, string> Parameters { get; set; } = [];

    public void Validate()
    {
        if (!Enum.IsDefined(Kind)) throw new InvalidDataException("Invalid generator kind.");
        if (string.IsNullOrWhiteSpace(PresetName)) PresetName = "(Default)";
    }

    public MediaGeneratorData DeepClone() => new()
    {
        Kind = Kind,
        PresetName = PresetName,
        Title = Title,
        Parameters = new Dictionary<string, string>(Parameters)
    };
}

public sealed record MediaGeneratorPreset(
    MediaGeneratorKind Kind,
    string Name,
    string Description,
    IReadOnlyDictionary<string, string> Parameters
);

public static class MediaGeneratorCatalog
{
    public const double DefaultDurationSeconds = 5;

    private static MediaGeneratorPreset SolidColor(string name, string hex) =>
        new(MediaGeneratorKind.SolidColor, name, hex, new Dictionary<string, string> { ["hex"] = hex });

    public static readonly IReadOnlyList<MediaGeneratorPreset> Presets = new List<MediaGeneratorPreset>
    {
        // Test Pattern Presets
        new(MediaGeneratorKind.TestPattern, "(Default)", "75% color bars with lower test patches", new Dictionary<string, string> { ["type"] = "smpte_ntsc" }),
        new(MediaGeneratorKind.TestPattern, "SMPTE Bars (NTSC)", "75% color bars with lower test patches", new Dictionary<string, string> { ["type"] = "smpte_ntsc" }),
        new(MediaGeneratorKind.TestPattern, "SMPTE Bars (PAL)", "Full-height PAL color bars", new Dictionary<string, string> { ["type"] = "smpte_pal" }),
        new(MediaGeneratorKind.TestPattern, "Color Bars (NTSC)", "75% saturation NTSC color bars", new Dictionary<string, string> { ["type"] = "color_bars_ntsc" }),
        new(MediaGeneratorKind.TestPattern, "Color Bars (PAL)", "100% saturation PAL color bars", new Dictionary<string, string> { ["type"] = "color_bars_pal" }),
        new(MediaGeneratorKind.TestPattern, "White Porches", "Step staircase of luma levels", new Dictionary<string, string> { ["type"] = "white_porches" }),
        new(MediaGeneratorKind.TestPattern, "Yellow Porches", "Step staircase of yellow chroma levels", new Dictionary<string, string> { ["type"] = "yellow_porches" }),
        new(MediaGeneratorKind.TestPattern, "Pluge and Porches", "Dark alignment stripes", new Dictionary<string, string> { ["type"] = "pluge" }),
        new(MediaGeneratorKind.TestPattern, "Ramp", "Full range horizontal grayscale gradient ramp", new Dictionary<string, string> { ["type"] = "ramp" }),
        new(MediaGeneratorKind.TestPattern, "Gradient", "Full visible color spectrum gradient", new Dictionary<string, string> { ["type"] = "gradient" }),

        // Solid Color Presets
        SolidColor("Red", "#E53935"),
        SolidColor("Green", "#43A047"),
        SolidColor("Blue", "#1E88E5"),
        SolidColor("White", "#FFFFFF"),
        SolidColor("Black", "#000000"),
        SolidColor("Yellow", "#FDD835"),
        SolidColor("Cyan", "#00ACC1"),
        SolidColor("Magenta", "#D81B60"),

        // Checkerboard Presets
        new(MediaGeneratorKind.Checkerboard, "(Default)", "Standard black and white checkerboard", new Dictionary<string, string> { ["size"] = "64", ["color1"] = "#FFFFFF", ["color2"] = "#000000" }),
        new(MediaGeneratorKind.Checkerboard, "Large Checkers", "Large 128px checker squares", new Dictionary<string, string> { ["size"] = "128", ["color1"] = "#FFFFFF", ["color2"] = "#000000" }),
        new(MediaGeneratorKind.Checkerboard, "Small Checkers", "Small 32px checker squares", new Dictionary<string, string> { ["size"] = "32", ["color1"] = "#FFFFFF", ["color2"] = "#000000" }),
        new(MediaGeneratorKind.Checkerboard, "Transparent Checkers", "Transparency grid background", new Dictionary<string, string> { ["size"] = "32", ["color1"] = "#FFFFFFFF", ["color2"] = "#00000000" }),

        // Color Gradient Presets
        new(MediaGeneratorKind.ColorGradient, "Linear Black to White", "Linear diagonal gradient from black to white", new Dictionary<string, string> { ["type"] = "linear", ["color1"] = "#000000", ["color2"] = "#FFFFFF" }),
        new(MediaGeneratorKind.ColorGradient, "Linear White to Black", "Linear diagonal gradient from white to black", new Dictionary<string, string> { ["type"] = "linear", ["color1"] = "#FFFFFF", ["color2"] = "#000000" }),
        new(MediaGeneratorKind.ColorGradient, "Radial Soft White", "Radial spotlight falloff", new Dictionary<string, string> { ["type"] = "radial", ["color1"] = "#FFFFFF", ["color2"] = "#000000" }),
        new(MediaGeneratorKind.ColorGradient, "Elliptical Transparent to Black", "Cinematic vignette framing", new Dictionary<string, string> { ["type"] = "vignette", ["color1"] = "#00000000", ["color2"] = "#FF000000" }),

        // Noise Texture Presets
        new(MediaGeneratorKind.NoiseTexture, "(Default)", "Procedural grayscale noise", new Dictionary<string, string> { ["type"] = "perlin", ["scale"] = "20" }),
        new(MediaGeneratorKind.NoiseTexture, "Clouds", "Coarse grayscale noise", new Dictionary<string, string> { ["type"] = "clouds", ["scale"] = "40" }),
        new(MediaGeneratorKind.NoiseTexture, "Turbulence", "High-frequency turbulent noise", new Dictionary<string, string> { ["type"] = "turbulence", ["scale"] = "15" })
    };

    public static string CreateUri(MediaGeneratorKind kind, string presetName, IReadOnlyDictionary<string, string> parameters)
    {
        var query = new List<string> { $"kind={kind}", $"preset={Uri.EscapeDataString(presetName)}" };
        foreach (var (k, v) in parameters)
        {
            query.Add($"{Uri.EscapeDataString(k)}={Uri.EscapeDataString(v)}");
        }
        return "openrend://generator?" + string.Join("&", query);
    }
}

