using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenRend.Media;

/// <summary>
/// Subtitle styling configuration matching Vegas Pro SubtitleEventControl and Titles &amp; Text OFX properties.
/// Evaluated identically across UI preview and background export.
/// </summary>
public sealed class SubtitleStyle
{
    public string PresetName { get; set; } = "Custom";
    public string FontFamily { get; set; } = "Arial";
    public float FontSize { get; set; } = 24f; // 0 indicates automatic calculation relative to frame height
    public bool Bold { get; set; } = true;
    public bool Italic { get; set; }
    public bool Uppercase { get; set; }
    public string HorizontalAlignment { get; set; } = "Center"; // Left, Center, Right
    public string VerticalAlignment { get; set; } = "Bottom";   // Top, Middle, Bottom
    public string TextColor { get; set; } = "#FFFFFF";
    public string OutlineColor { get; set; } = "#000000";
    public float OutlineWidth { get; set; } = 3.5f;
    public bool ShadowEnabled { get; set; } = true;
    public string ShadowColor { get; set; } = "#CC000000";
    public float ShadowOffsetX { get; set; } = 2.5f;
    public float ShadowOffsetY { get; set; } = 2.5f;
    public float ShadowBlur { get; set; } = 2.0f;
    public string BackgroundColor { get; set; } = "#00000000"; // Transparent default in Vegas
    public bool FitBackgroundColor { get; set; } = true;       // Crop background pill to text bounds
    public bool AutomaticHighlight { get; set; }               // Karaoke mode: highlight spoken word
    public string HighlightColor { get; set; } = "#FFE600";    // Accent fill for currently spoken word
    public int ActiveWordIndex { get; set; } = -1;             // -1 disables karaoke highlighting
    public int LinesMode { get; set; } = 2;                    // 1 or 2 lines layout
    public int MaxCharsPerLine { get; set; } = 37;
    public float Tracking { get; set; }                        // Letter spacing offset
    public float LineSpacing { get; set; } = 1.0f;             // Line spacing factor
    public float LocationX { get; set; } = 0.5f;               // 0.0 (left) to 1.0 (right) normalized preview center
    public float LocationY { get; set; } = 0.5f;               // 0.0 (top) to 1.0 (bottom) normalized preview center
    public bool HasCustomLocation { get; set; }                // True when positioned via Vegas on-screen interact handles

    [JsonIgnore]
    public string AnchorAlignment
    {
        get => $"{VerticalAlignment}_{HorizontalAlignment}";
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var parts = value.Replace(" ", "_").Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                VerticalAlignment = NormalizeVertical(parts[0]);
                HorizontalAlignment = NormalizeHorizontal(parts[1]);
            }
            else if (parts.Length == 1)
            {
                if (parts[0].Equals("Top", StringComparison.OrdinalIgnoreCase) ||
                    parts[0].Equals("Middle", StringComparison.OrdinalIgnoreCase) ||
                    parts[0].Equals("Center", StringComparison.OrdinalIgnoreCase) ||
                    parts[0].Equals("Bottom", StringComparison.OrdinalIgnoreCase))
                {
                    VerticalAlignment = NormalizeVertical(parts[0]);
                }
                else
                {
                    HorizontalAlignment = NormalizeHorizontal(parts[0]);
                }
            }
        }
    }

    private static string NormalizeVertical(string s) => s.ToLowerInvariant() switch
    {
        "top" => "Top",
        "middle" or "center" => "Middle",
        _ => "Bottom"
    };

    private static string NormalizeHorizontal(string s) => s.ToLowerInvariant() switch
    {
        "left" => "Left",
        "right" => "Right",
        _ => "Center"
    };

    public SubtitleStyle DeepClone() => (SubtitleStyle)MemberwiseClone();

    public static SubtitleStyle CreateVegasDefault() => new()
    {
        PresetName = "Subtitles",
        FontFamily = "Arial",
        FontSize = 20f,
        Bold = true,
        Italic = false,
        Uppercase = false,
        HorizontalAlignment = "Center",
        VerticalAlignment = "Bottom",
        TextColor = "#FFFFFF",
        OutlineColor = "#000000",
        OutlineWidth = 1.5f,
        ShadowEnabled = true,
        ShadowColor = "#CC000000",
        ShadowOffsetX = 2.0f,
        ShadowOffsetY = 2.0f,
        ShadowBlur = 1.5f,
        BackgroundColor = "#00000000",
        FitBackgroundColor = true,
        AutomaticHighlight = false,
        LinesMode = 2,
        MaxCharsPerLine = 37
    };

    public static SubtitleStyle CreateYouTubeShortsYellow() => new()
    {
        PresetName = "YouTube Shorts (Yellow)",
        FontFamily = "Arial Black",
        FontSize = 0f, // Scaled dynamically to frame height
        Bold = true,
        Italic = false,
        Uppercase = true,
        HorizontalAlignment = "Center",
        VerticalAlignment = "Middle",
        TextColor = "#FFE600",
        OutlineColor = "#000000",
        OutlineWidth = 8.0f,
        ShadowEnabled = true,
        ShadowColor = "#CC000000",
        ShadowOffsetX = 4.0f,
        ShadowOffsetY = 5.0f,
        ShadowBlur = 4.0f,
        BackgroundColor = "#00000000",
        FitBackgroundColor = true,
        AutomaticHighlight = false,
        HighlightColor = "#FFFFFF",
        LinesMode = 1,
        MaxCharsPerLine = 24
    };

    public static SubtitleStyle CreateYouTubeShortsWhite() => new()
    {
        PresetName = "YouTube Shorts (White)",
        FontFamily = "Arial Black",
        FontSize = 0f,
        Bold = true,
        Italic = false,
        Uppercase = true,
        HorizontalAlignment = "Center",
        VerticalAlignment = "Middle",
        TextColor = "#FFFFFF",
        OutlineColor = "#000000",
        OutlineWidth = 8.0f,
        ShadowEnabled = true,
        ShadowColor = "#CC000000",
        ShadowOffsetX = 4.0f,
        ShadowOffsetY = 5.0f,
        ShadowBlur = 4.0f,
        BackgroundColor = "#00000000",
        FitBackgroundColor = true,
        AutomaticHighlight = false,
        HighlightColor = "#FFE600",
        LinesMode = 1,
        MaxCharsPerLine = 24
    };

    public static SubtitleStyle CreateNeonPop() => new()
    {
        PresetName = "Neon Pop",
        FontFamily = "Impact",
        FontSize = 0f,
        Bold = true,
        Italic = false,
        Uppercase = true,
        HorizontalAlignment = "Center",
        VerticalAlignment = "Middle",
        TextColor = "#39FF14",
        OutlineColor = "#000000",
        OutlineWidth = 7.0f,
        ShadowEnabled = true,
        ShadowColor = "#CC000000",
        ShadowOffsetX = 4.0f,
        ShadowOffsetY = 4.0f,
        ShadowBlur = 3.0f,
        BackgroundColor = "#00000000",
        FitBackgroundColor = true,
        AutomaticHighlight = false,
        HighlightColor = "#FFE600",
        LinesMode = 1,
        MaxCharsPerLine = 24
    };

    public static SubtitleStyle CreateClassicWhite() => new()
    {
        PresetName = "Classic White",
        FontFamily = "Segoe UI",
        FontSize = 0f,
        Bold = true,
        Italic = false,
        Uppercase = false,
        HorizontalAlignment = "Center",
        VerticalAlignment = "Bottom",
        TextColor = "#FFFFFF",
        OutlineColor = "#000000",
        OutlineWidth = 3.0f,
        ShadowEnabled = false,
        BackgroundColor = "#90000000",
        FitBackgroundColor = true,
        AutomaticHighlight = false,
        LinesMode = 2,
        MaxCharsPerLine = 37
    };

    public static SubtitleStyle CreateBanner() => new()
    {
        PresetName = "Banner Subtitle",
        FontFamily = "Arial",
        FontSize = 0f,
        Bold = true,
        Italic = false,
        Uppercase = false,
        HorizontalAlignment = "Center",
        VerticalAlignment = "Bottom",
        TextColor = "#FFFFFF",
        OutlineColor = "#000000",
        OutlineWidth = 1.0f,
        ShadowEnabled = false,
        BackgroundColor = "#A0000000",
        FitBackgroundColor = false, // Spans entire frame width
        AutomaticHighlight = false,
        LinesMode = 2,
        MaxCharsPerLine = 42
    };

    public static SubtitleStyle FromPreset(string presetName) => presetName.ToLowerInvariant() switch
    {
        var s when s.Contains("yellow") => CreateYouTubeShortsYellow(),
        var s when s.Contains("white") && (s.Contains("shorts") || s.Contains("youtube")) => CreateYouTubeShortsWhite(),
        var s when s.Contains("neon") => CreateNeonPop(),
        var s when s.Contains("classic") => CreateClassicWhite(),
        var s when s.Contains("banner") => CreateBanner(),
        var s when s.Contains("subtitles") || s.Contains("default") => CreateVegasDefault(),
        _ => CreateVegasDefault()
    };

    public static Color ParseColor(string hex, Color defaultColor)
    {
        if (string.IsNullOrWhiteSpace(hex)) return defaultColor;
        hex = hex.Trim().TrimStart('#');
        try
        {
            if (hex.Length == 6)
            {
                var r = Convert.ToInt32(hex.Substring(0, 2), 16);
                var g = Convert.ToInt32(hex.Substring(2, 2), 16);
                var b = Convert.ToInt32(hex.Substring(4, 2), 16);
                return Color.FromArgb(255, r, g, b);
            }
            if (hex.Length == 8)
            {
                var a = Convert.ToInt32(hex.Substring(0, 2), 16);
                var r = Convert.ToInt32(hex.Substring(2, 2), 16);
                var g = Convert.ToInt32(hex.Substring(4, 2), 16);
                var b = Convert.ToInt32(hex.Substring(6, 2), 16);
                return Color.FromArgb(a, r, g, b);
            }
        }
        catch
        {
            // Fall back to default color on invalid hex input
        }
        return defaultColor;
    }

    public string ToQuery()
    {
        var q = $"preset={Uri.EscapeDataString(PresetName)}&font={Uri.EscapeDataString(FontFamily)}&size={FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}&bold={Bold}&italic={Italic}&uppercase={Uppercase}&align={AnchorAlignment}&color={Uri.EscapeDataString(TextColor)}&outline={Uri.EscapeDataString(OutlineColor)}&outlinewidth={OutlineWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}&shadow={ShadowEnabled}&shadowcolor={Uri.EscapeDataString(ShadowColor)}&bg={Uri.EscapeDataString(BackgroundColor)}&fitbg={FitBackgroundColor}";
        if (HasCustomLocation)
            q += $"&locx={LocationX.ToString(System.Globalization.CultureInfo.InvariantCulture)}&locy={LocationY.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return q;
    }

    public static SubtitleStyle FromQuery(string query)
    {
        var style = CreateVegasDefault();
        if (string.IsNullOrWhiteSpace(query)) return style;
        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var kv = pair.Split('=', 2);
            if (kv.Length < 2) continue;
            var key = Uri.UnescapeDataString(kv[0]).ToLowerInvariant();
            var val = Uri.UnescapeDataString(kv[1]);

            switch (key)
            {
                case "preset": style = FromPreset(val); break;
                case "font": style.FontFamily = val; break;
                case "size" when float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sz): style.FontSize = sz; break;
                case "bold" when bool.TryParse(val, out var b): style.Bold = b; break;
                case "italic" when bool.TryParse(val, out var it): style.Italic = it; break;
                case "uppercase" when bool.TryParse(val, out var u): style.Uppercase = u; break;
                case "align": style.AnchorAlignment = val; break;
                case "color": style.TextColor = val.StartsWith("#") ? val : "#" + val; break;
                case "outline": style.OutlineColor = val.StartsWith("#") ? val : "#" + val; break;
                case "outlinewidth" when float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ow): style.OutlineWidth = ow; break;
                case "shadow" when bool.TryParse(val, out var sh): style.ShadowEnabled = sh; break;
                case "shadowcolor": style.ShadowColor = val.StartsWith("#") ? val : "#" + val; break;
                case "bg": style.BackgroundColor = val.StartsWith("#") ? val : "#" + val; break;
                case "fitbg" when bool.TryParse(val, out var fbg): style.FitBackgroundColor = fbg; break;
                case "locx" when float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lx):
                    style.LocationX = lx;
                    style.HasCustomLocation = true;
                    break;
                case "locy" when float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ly):
                    style.LocationY = ly;
                    style.HasCustomLocation = true;
                    break;
            }
        }
        return style;
    }
}
