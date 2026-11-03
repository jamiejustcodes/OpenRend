using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using OpenRend.Core;

namespace OpenRend.Media;

public static class MediaGeneratorSynthesizer
{
    public static byte[] Render(MediaAsset media, int width, int height, long frame = 0)
    {
        if (width <= 0 || height <= 0) return Array.Empty<byte>();

        var kind = media.Generator?.Kind ?? DetectKind(media.Path);
        var preset = media.Generator?.PresetName ?? ExtractParam(media.Path, "preset") ?? "(Default)";
        var parameters = media.Generator?.Parameters ?? ExtractParams(media.Path);

        return kind switch
        {
            MediaGeneratorKind.TestPattern => RenderTestPattern(preset, parameters, width, height),
            MediaGeneratorKind.SolidColor => RenderSolidColor(parameters, width, height),
            MediaGeneratorKind.Checkerboard => RenderCheckerboard(parameters, width, height),
            MediaGeneratorKind.ColorGradient => RenderColorGradient(parameters, width, height),
            MediaGeneratorKind.NoiseTexture => RenderNoiseTexture(parameters, width, height, frame),
            _ => RenderSolidColor(parameters, width, height)
        };
    }

    private static MediaGeneratorKind DetectKind(string path)
    {
        if (path.Contains("kind=TestPattern", StringComparison.OrdinalIgnoreCase)) return MediaGeneratorKind.TestPattern;
        if (path.Contains("kind=ColorGradient", StringComparison.OrdinalIgnoreCase)) return MediaGeneratorKind.ColorGradient;
        if (path.Contains("kind=Checkerboard", StringComparison.OrdinalIgnoreCase)) return MediaGeneratorKind.Checkerboard;
        if (path.Contains("kind=NoiseTexture", StringComparison.OrdinalIgnoreCase)) return MediaGeneratorKind.NoiseTexture;
        return MediaGeneratorKind.SolidColor;
    }

    private static IReadOnlyDictionary<string, string> ExtractParams(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var qIdx = path.IndexOf('?');
        if (qIdx < 0) return dict;
        var query = path.Substring(qIdx + 1);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                var k = Uri.UnescapeDataString(part.Substring(0, eq));
                var v = Uri.UnescapeDataString(part.Substring(eq + 1));
                dict[k] = v;
            }
        }
        return dict;
    }

    private static string? ExtractParam(string path, string name)
    {
        var dict = ExtractParams(path);
        return dict.TryGetValue(name, out var val) ? val : null;
    }

    private static uint ToBgra(byte r, byte g, byte b, byte a = 255) =>
        (uint)((a << 24) | (r << 16) | (g << 8) | b);

    private static uint ParseColor(string? hex, uint fallback = 0xFF000000)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6)
        {
            if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var val))
            {
                byte r = (byte)((val >> 16) & 0xFF);
                byte g = (byte)((val >> 8) & 0xFF);
                byte b = (byte)(val & 0xFF);
                return ToBgra(r, g, b, 255);
            }
        }
        else if (hex.Length == 8)
        {
            if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var val))
            {
                byte a = (byte)((val >> 24) & 0xFF);
                byte r = (byte)((val >> 16) & 0xFF);
                byte g = (byte)((val >> 8) & 0xFF);
                byte b = (byte)(val & 0xFF);
                return ToBgra(r, g, b, a);
            }
        }
        return fallback;
    }

    // --- 1. Solid Color ---
    public static byte[] RenderSolidColor(IReadOnlyDictionary<string, string> parameters, int width, int height)
    {
        parameters.TryGetValue("hex", out var hex);
        var bgra = ParseColor(hex, 0xFF000000); // default Black

        var pixels = new byte[width * height * 4];
        MemoryMarshal.Cast<byte, uint>(pixels.AsSpan()).Fill(bgra);
        return pixels;
    }

    // --- 2. Test Pattern ---
    public static byte[] RenderTestPattern(string preset, IReadOnlyDictionary<string, string> parameters, int width, int height)
    {
        var type = parameters.TryGetValue("type", out var t) ? t.ToLowerInvariant() : "smpte_ntsc";
        var pixels = new byte[width * height * 4];
        var span = MemoryMarshal.Cast<byte, uint>(pixels.AsSpan());

        if (type == "ramp")
        {
            for (var y = 0; y < height; y++)
            {
                var row = span.Slice(y * width, width);
                for (var x = 0; x < width; x++)
                {
                    var val = (byte)Math.Clamp(x * 255 / Math.Max(1, width - 1), 0, 255);
                    row[x] = ToBgra(val, val, val);
                }
            }
            return pixels;
        }

        if (type == "gradient")
        {
            for (var y = 0; y < height; y++)
            {
                var row = span.Slice(y * width, width);
                for (var x = 0; x < width; x++)
                {
                    var h = (double)x / width;
                    var (r, g, b) = HsvToRgb(h * 360.0, 1.0, 1.0 - ((double)y / height) * 0.5);
                    row[x] = ToBgra(r, g, b);
                }
            }
            return pixels;
        }

        if (type == "white_porches")
        {
            byte[] steps = [0, 51, 102, 153, 204, 255];
            for (var y = 0; y < height; y++)
            {
                var row = span.Slice(y * width, width);
                for (var x = 0; x < width; x++)
                {
                    var idx = Math.Clamp(x * steps.Length / width, 0, steps.Length - 1);
                    var v = steps[idx];
                    row[x] = ToBgra(v, v, v);
                }
            }
            return pixels;
        }

        if (type == "yellow_porches")
        {
            double[] steps = [0.1, 0.25, 0.45, 0.65, 0.85, 1.0];
            for (var y = 0; y < height; y++)
            {
                var row = span.Slice(y * width, width);
                for (var x = 0; x < width; x++)
                {
                    var idx = Math.Clamp(x * steps.Length / width, 0, steps.Length - 1);
                    var v = (byte)Math.Clamp(steps[idx] * 255.0, 0, 255);
                    row[x] = ToBgra(v, v, 0);
                }
            }
            return pixels;
        }

        // Standard 75% SMPTE Colors (7 bars)
        uint[] top7 =
        [
            ToBgra(192, 192, 192), // 75% Gray
            ToBgra(192, 192, 0),   // 75% Yellow
            ToBgra(0, 192, 192),   // 75% Cyan
            ToBgra(0, 192, 0),     // 75% Green
            ToBgra(192, 0, 192),   // 75% Magenta
            ToBgra(192, 0, 0),     // 75% Red
            ToBgra(0, 0, 192)      // 75% Blue
        ];

        if (type == "color_bars_pal" || type == "smpte_pal")
        {
            // Full height 8 bars (with 100% white on left or standard EBU)
            uint[] pal8 =
            [
                ToBgra(255, 255, 255),
                ToBgra(255, 255, 0),
                ToBgra(0, 255, 255),
                ToBgra(0, 255, 0),
                ToBgra(255, 0, 255),
                ToBgra(255, 0, 0),
                ToBgra(0, 0, 255),
                ToBgra(0, 0, 0)
            ];
            for (var y = 0; y < height; y++)
            {
                var row = span.Slice(y * width, width);
                for (var x = 0; x < width; x++)
                {
                    var bar = Math.Clamp(x * pal8.Length / width, 0, pal8.Length - 1);
                    row[x] = pal8[bar];
                }
            }
            return pixels;
        }

        // Standard SMPTE Bars (NTSC) layout:
        // Top section: 67% of height, 7 bars
        // Middle castellation section: 8% of height, alternating blue/black/magenta/cyan/gray
        // Bottom section: 25% of height, -I, 100% White, +Q, Black, Pluge (-2%, 0%, +2%), Black
        var hTop = (int)Math.Round(height * (2.0 / 3.0));
        var hMid = (int)Math.Round(height * (3.0 / 4.0));

        uint[] castellation =
        [
            ToBgra(0, 0, 192),     // Blue
            ToBgra(19, 19, 19),    // 7.5% Black
            ToBgra(192, 0, 192),   // Magenta
            ToBgra(19, 19, 19),    // 7.5% Black
            ToBgra(0, 192, 192),   // Cyan
            ToBgra(19, 19, 19),    // 7.5% Black
            ToBgra(192, 192, 192)  // Gray
        ];

        uint colMinusI = ToBgra(0, 33, 76);    // -I
        uint colWhite100 = ToBgra(255, 255, 255); // 100% White
        uint colPlusQ = ToBgra(50, 0, 106);    // +Q
        uint colBlack0 = ToBgra(0, 0, 0);      // 0% Black
        uint colPlugeNeg = ToBgra(0, 0, 0);    // -2% Black (superblack)
        uint colPlugeMid = ToBgra(19, 19, 19);  // 7.5% Black setup
        uint colPlugePos = ToBgra(38, 38, 38);  // +2% Black

        for (var y = 0; y < height; y++)
        {
            var row = span.Slice(y * width, width);
            if (y < hTop)
            {
                // Top 7 bars
                for (var x = 0; x < width; x++)
                {
                    var bar = Math.Clamp(x * 7 / width, 0, 6);
                    row[x] = top7[bar];
                }
            }
            else if (y < hMid)
            {
                // Middle castellation
                for (var x = 0; x < width; x++)
                {
                    var bar = Math.Clamp(x * 7 / width, 0, 6);
                    row[x] = castellation[bar];
                }
            }
            else
            {
                // Bottom section: 6 blocks across width
                // 0: -I, 1: 100% White, 2: +Q, 3: 0% Black, 4: Pluge, 5: 0% Black
                for (var x = 0; x < width; x++)
                {
                    var block = Math.Clamp(x * 6 / width, 0, 5);
                    if (block == 0) row[x] = colMinusI;
                    else if (block == 1) row[x] = colWhite100;
                    else if (block == 2) row[x] = colPlusQ;
                    else if (block == 3 || block == 5) row[x] = colBlack0;
                    else
                    {
                        // Pluge block divided into 3 stripes
                        var localX = (x * 6) % width;
                        var sub = Math.Clamp(localX * 3 / width, 0, 2);
                        row[x] = sub switch
                        {
                            0 => colPlugeNeg,
                            1 => colPlugeMid,
                            _ => colPlugePos
                        };
                    }
                }
            }
        }

        return pixels;
    }

    // --- 3. Checkerboard ---
    public static byte[] RenderCheckerboard(IReadOnlyDictionary<string, string> parameters, int width, int height)
    {
        var size = parameters.TryGetValue("size", out var s) && int.TryParse(s, out var sz) ? Math.Max(4, sz) : 64;
        parameters.TryGetValue("color1", out var c1);
        parameters.TryGetValue("color2", out var c2);
        var col1 = ParseColor(c1, 0xFFFFFFFF); // default White
        var col2 = ParseColor(c2, 0xFF000000); // default Black

        var pixels = new byte[width * height * 4];
        var span = MemoryMarshal.Cast<byte, uint>(pixels.AsSpan());

        for (var y = 0; y < height; y++)
        {
            var row = span.Slice(y * width, width);
            var checkY = (y / size) % 2;
            for (var x = 0; x < width; x++)
            {
                var checkX = (x / size) % 2;
                row[x] = (checkX ^ checkY) == 0 ? col1 : col2;
            }
        }

        return pixels;
    }

    // --- 4. Color Gradient ---
    public static byte[] RenderColorGradient(IReadOnlyDictionary<string, string> parameters, int width, int height)
    {
        var type = parameters.TryGetValue("type", out var t) ? t.ToLowerInvariant() : "linear";
        parameters.TryGetValue("color1", out var c1);
        parameters.TryGetValue("color2", out var c2);
        var col1 = ParseColor(c1, 0xFF000000);
        var col2 = ParseColor(c2, 0xFFFFFFFF);

        var pixels = new byte[width * height * 4];
        var span = MemoryMarshal.Cast<byte, uint>(pixels.AsSpan());

        byte a1 = (byte)((col1 >> 24) & 0xFF), r1 = (byte)((col1 >> 16) & 0xFF), g1 = (byte)((col1 >> 8) & 0xFF), b1 = (byte)(col1 & 0xFF);
        byte a2 = (byte)((col2 >> 24) & 0xFF), r2 = (byte)((col2 >> 16) & 0xFF), g2 = (byte)((col2 >> 8) & 0xFF), b2 = (byte)(col2 & 0xFF);

        if (type == "radial" || type == "vignette")
        {
            var cx = width / 2.0;
            var cy = height / 2.0;
            var maxDist = Math.Sqrt(cx * cx + cy * cy);
            for (var y = 0; y < height; y++)
            {
                var row = span.Slice(y * width, width);
                for (var x = 0; x < width; x++)
                {
                    var dist = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    var tNorm = Math.Clamp(dist / maxDist, 0.0, 1.0);
                    // Smoothstep falloff
                    var w = tNorm * tNorm * (3.0 - 2.0 * tNorm);
                    var r = (byte)(r1 + (r2 - r1) * w);
                    var g = (byte)(g1 + (g2 - g1) * w);
                    var b = (byte)(b1 + (b2 - b1) * w);
                    var a = (byte)(a1 + (a2 - a1) * w);
                    row[x] = ToBgra(r, g, b, a);
                }
            }
        }
        else // Linear diagonal
        {
            for (var y = 0; y < height; y++)
            {
                var row = span.Slice(y * width, width);
                for (var x = 0; x < width; x++)
                {
                    var w = Math.Clamp(((double)x / width + (double)y / height) * 0.5, 0.0, 1.0);
                    var r = (byte)(r1 + (r2 - r1) * w);
                    var g = (byte)(g1 + (g2 - g1) * w);
                    var b = (byte)(b1 + (b2 - b1) * w);
                    var a = (byte)(a1 + (a2 - a1) * w);
                    row[x] = ToBgra(r, g, b, a);
                }
            }
        }

        return pixels;
    }

    // --- 5. Noise Texture ---
    public static byte[] RenderNoiseTexture(IReadOnlyDictionary<string, string> parameters, int width, int height, long frame)
    {
        var scale = parameters.TryGetValue("scale", out var s) && double.TryParse(s, out var sc) ? Math.Max(2.0, sc) : 20.0;
        var type = parameters.TryGetValue("type", out var t) ? t.ToLowerInvariant() : "perlin";

        var pixels = new byte[width * height * 4];
        var span = MemoryMarshal.Cast<byte, uint>(pixels.AsSpan());

        for (var y = 0; y < height; y++)
        {
            var row = span.Slice(y * width, width);
            for (var x = 0; x < width; x++)
            {
                var nx = x / scale;
                var ny = y / scale;
                var val = SimpleNoise(nx, ny, frame * 0.02);
                if (type == "turbulence")
                {
                    val = Math.Abs(val * 2.0 - 1.0);
                }
                var byteVal = (byte)Math.Clamp(val * 255.0, 0, 255);
                row[x] = ToBgra(byteVal, byteVal, byteVal);
            }
        }

        return pixels;
    }

    private static double SimpleNoise(double x, double y, double z)
    {
        var n = Math.Sin(x * 1.5 + Math.Sin(y * 1.2 + z)) * Math.Cos(y * 1.7 + Math.Cos(x * 0.8 + z));
        return (n + 1.0) * 0.5;
    }

    private static (byte r, byte g, byte b) HsvToRgb(double h, double s, double v)
    {
        while (h < 0) h += 360;
        while (h >= 360) h -= 360;
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        var m = v - c;
        double r1 = 0, g1 = 0, b1 = 0;
        if (h < 60) { r1 = c; g1 = x; }
        else if (h < 120) { r1 = x; g1 = c; }
        else if (h < 180) { g1 = c; b1 = x; }
        else if (h < 240) { g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; b1 = c; }
        else { r1 = c; b1 = x; }
        return ((byte)((r1 + m) * 255), (byte)((g1 + m) * 255), (byte)((b1 + m) * 255));
    }
}

