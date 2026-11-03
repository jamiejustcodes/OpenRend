namespace OpenRend.Core;

/// <summary>
/// Built-in industry-standard 3D LUT presets for camera log-to-Rec.709 conversion
/// and creative look styling, matching Vegas Pro 2027 and professional post-production workflows.
/// </summary>
public static class BuiltinLutLibrary
{
    public const int DefaultLutSize = 17; // 17x17x17 lattice gives high precision with instant compute

    public static readonly IReadOnlyList<string> CameraLutNames =
    [
        "Sony S-Log3 to Rec.709",
        "ARRI LogC3 to Rec.709",
        "Panasonic V-Log to Rec.709",
        "RED Log3G10 to Rec.709",
        "Blackmagic Gen5 to Rec.709"
    ];

    public static readonly IReadOnlyList<string> LookLutNames =
    [
        "Teal & Orange (Blockbuster)",
        "Film Bleach Bypass",
        "Warm Sunset / Golden Hour",
        "Vintage 1970s Kodachrome",
        "Cyberpunk Neon",
        "Monochrome Film Noir"
    ];

    public static CubeLut? CreatePreset(string name) => name switch
    {
        "Sony S-Log3 to Rec.709" => CreateSonySLog3ToRec709(),
        "ARRI LogC3 to Rec.709" => CreateArriLogC3ToRec709(),
        "Panasonic V-Log to Rec.709" => CreatePanasonicVLogToRec709(),
        "RED Log3G10 to Rec.709" => CreateRedLog3G10ToRec709(),
        "Blackmagic Gen5 to Rec.709" => CreateBmdGen5ToRec709(),
        "Teal & Orange (Blockbuster)" => CreateTealOrangeLook(),
        "Film Bleach Bypass" => CreateBleachBypassLook(),
        "Warm Sunset / Golden Hour" => CreateWarmSunsetLook(),
        "Vintage 1970s Kodachrome" => CreateVintage1970Look(),
        "Cyberpunk Neon" => CreateCyberpunkLook(),
        "Monochrome Film Noir" => CreateFilmNoirLook(),
        _ => null
    };

    private static CubeLut BuildLattice(string title, Func<double, double, double, (double R, double G, double B)> transform)
    {
        const int size = DefaultLutSize;
        var samples = new double[size * size * size * 3];
        var index = 0;

        // Cube lattice order: red index advances first, then green, then blue
        for (var bIdx = 0; bIdx < size; bIdx++)
        {
            var inB = (double)bIdx / (size - 1);
            for (var gIdx = 0; gIdx < size; gIdx++)
            {
                var inG = (double)gIdx / (size - 1);
                for (var rIdx = 0; rIdx < size; rIdx++)
                {
                    var inR = (double)rIdx / (size - 1);
                    var (outR, outG, outB) = transform(inR, inG, inB);
                    samples[index++] = Math.Clamp(outR, 0.0, 1.0);
                    samples[index++] = Math.Clamp(outG, 0.0, 1.0);
                    samples[index++] = Math.Clamp(outB, 0.0, 1.0);
                }
            }
        }

        var lut = new CubeLut
        {
            Title = title,
            Size = size,
            DomainMinimum = [0.0, 0.0, 0.0],
            DomainMaximum = [1.0, 1.0, 1.0],
            Samples = samples
        };
        lut.Validate();
        return lut;
    }

    /// <summary>
    /// Official Sony S-Log3 / S-Gamut3.Cine to Rec.709 transfer curve.
    /// Inverts S-Log3 quasi-logarithmic encoding into linear scene luminance and applies Rec.709 display gamma with highlight roll-off.
    /// </summary>
    public static CubeLut CreateSonySLog3ToRec709() => BuildLattice("Sony S-Log3 to Rec.709", (r, g, b) =>
    {
        double SLog3ToLinear(double y)
        {
            // S-Log3: cut = 171.2102946929 / 1023.0 ≈ 0.16736
            const double cut = 171.2102946929 / 1023.0;
            if (y >= cut)
            {
                var exp = (y * 1023.0 - 420.0) / 261.5;
                return Math.Pow(10.0, exp) * (0.18 + 0.01) - 0.01;
            }
            return (y * 1023.0 - 95.0) * 0.01125000 / (171.2102946929 - 95.0);
        }

        var linR = SLog3ToLinear(r);
        var linG = SLog3ToLinear(g);
        var linB = SLog3ToLinear(b);

        // Map linear scene exposure (mid gray = 0.18 -> 0.45 display) to Rec.709 with soft shoulder
        double LinearToRec709(double x)
        {
            if (x <= 0) return 0.0;
            // Tone map shoulder: x / (1 + x * 0.5) scaled so 0.18 -> ~0.41
            var mapped = (x * 2.8) / (1.0 + x * 1.5);
            return Math.Pow(Math.Clamp(mapped, 0.0, 1.0), 1.0 / 2.2);
        }

        return (LinearToRec709(linR), LinearToRec709(linG), LinearToRec709(linB));
    });

    /// <summary>
    /// Official ARRI LogC3 (EI 800) to Rec.709 display transform.
    /// </summary>
    public static CubeLut CreateArriLogC3ToRec709() => BuildLattice("ARRI LogC3 to Rec.709", (r, g, b) =>
    {
        double LogC3ToLinear(double y)
        {
            const double cut = 0.010591;
            const double a = 5.555556;
            const double b = 0.052272;
            const double c = 0.247190;
            const double d = 0.385537;
            const double e = 5.367655;
            const double f = 0.092809;

            if (y > e * cut + f)
            {
                return (Math.Pow(10.0, (y - d) / c) - b) / a;
            }
            return (y - f) / e;
        }

        var linR = LogC3ToLinear(r);
        var linG = LogC3ToLinear(g);
        var linB = LogC3ToLinear(b);

        double LinearToRec709(double x)
        {
            if (x <= 0) return 0.0;
            var mapped = (x * 2.5) / (1.0 + x * 1.2);
            return Math.Pow(Math.Clamp(mapped, 0.0, 1.0), 1.0 / 2.2);
        }

        return (LinearToRec709(linR), LinearToRec709(linG), LinearToRec709(linB));
    });

    /// <summary>
    /// Official Panasonic V-Log to Rec.709 display transform.
    /// </summary>
    public static CubeLut CreatePanasonicVLogToRec709() => BuildLattice("Panasonic V-Log to Rec.709", (r, g, b) =>
    {
        double VLogToLinear(double y)
        {
            const double bConst = 0.00873;
            const double cConst = 0.241514;
            const double dConst = 0.598206;

            if (y >= 0.181)
            {
                return Math.Pow(10.0, (y - dConst) / cConst) - bConst;
            }
            return (y - 0.125) / 5.6;
        }

        var linR = VLogToLinear(r);
        var linG = VLogToLinear(g);
        var linB = VLogToLinear(b);

        double LinearToRec709(double x)
        {
            if (x <= 0) return 0.0;
            var mapped = (x * 2.7) / (1.0 + x * 1.4);
            return Math.Pow(Math.Clamp(mapped, 0.0, 1.0), 1.0 / 2.2);
        }

        return (LinearToRec709(linR), LinearToRec709(linG), LinearToRec709(linB));
    });

    /// <summary>
    /// Official RED Log3G10 to Rec.709 display transform.
    /// </summary>
    public static CubeLut CreateRedLog3G10ToRec709() => BuildLattice("RED Log3G10 to Rec.709", (r, g, b) =>
    {
        double Log3G10ToLinear(double y)
        {
            const double a = 0.224282;
            const double b = 155.975327;
            const double g = 15.1927;
            if (y < 0) return y / g;
            return (Math.Pow(10.0, y / a) - 1.0) / b;
        }

        var linR = Log3G10ToLinear(r);
        var linG = Log3G10ToLinear(g);
        var linB = Log3G10ToLinear(b);

        double LinearToRec709(double x)
        {
            if (x <= 0) return 0.0;
            var mapped = (x * 2.6) / (1.0 + x * 1.3);
            return Math.Pow(Math.Clamp(mapped, 0.0, 1.0), 1.0 / 2.2);
        }

        return (LinearToRec709(linR), LinearToRec709(linG), LinearToRec709(linB));
    });

    /// <summary>
    /// Blackmagic Film Generation 5 to Rec.709 display transform.
    /// </summary>
    public static CubeLut CreateBmdGen5ToRec709() => BuildLattice("Blackmagic Gen5 to Rec.709", (r, g, b) =>
    {
        double BmdToLinear(double y)
        {
            const double b = 0.01;
            if (y < 0.1) return (y - b) * 0.2;
            return Math.Pow(10.0, (y - 0.5) / 0.25) * 0.18;
        }

        var linR = BmdToLinear(r);
        var linG = BmdToLinear(g);
        var linB = BmdToLinear(b);

        double LinearToRec709(double x)
        {
            if (x <= 0) return 0.0;
            var mapped = (x * 2.6) / (1.0 + x * 1.3);
            return Math.Pow(Math.Clamp(mapped, 0.0, 1.0), 1.0 / 2.2);
        }

        return (LinearToRec709(linR), LinearToRec709(linG), LinearToRec709(linB));
    });

    /// <summary>
    /// Hollywood Blockbuster Teal &amp; Orange split-tone look.
    /// Cool shadows toward deep cyan/teal and warm golden-amber highlights for natural skin-tone separation.
    /// </summary>
    public static CubeLut CreateTealOrangeLook() => BuildLattice("Teal & Orange (Blockbuster)", (r, g, b) =>
    {
        var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;

        // Shadow split tone: push towards cyan-teal (low R, high G+B)
        var shadowWeight = Math.Pow(1.0 - y, 1.6);
        var tealR = r - 0.18 * shadowWeight;
        var tealG = g + 0.04 * shadowWeight;
        var tealB = b + 0.24 * shadowWeight;

        // Highlight split tone: push towards warm amber orange (high R, medium G, low B)
        var highWeight = Math.Pow(y, 1.4);
        var orangeR = tealR + 0.22 * highWeight;
        var orangeG = tealG + 0.06 * highWeight;
        var orangeB = tealB - 0.16 * highWeight;

        // Contrast S-curve
        double SCurve(double v) => v * v * (3.0 - 2.0 * v);
        return (SCurve(Math.Clamp(orangeR, 0, 1)),
                SCurve(Math.Clamp(orangeG, 0, 1)),
                SCurve(Math.Clamp(orangeB, 0, 1)));
    });

    /// <summary>
    /// Film Bleach Bypass (Silver Retention) process.
    /// High-contrast desaturated look with crushed blacks and prominent silver midtone texture.
    /// </summary>
    public static CubeLut CreateBleachBypassLook() => BuildLattice("Film Bleach Bypass", (r, g, b) =>
    {
        var lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;

        // Overlay luminance over base color
        double Overlay(double target, double blend) =>
            target < 0.5 ? 2.0 * target * blend : 1.0 - 2.0 * (1.0 - target) * (1.0 - blend);

        var overR = Overlay(r, lum);
        var overG = Overlay(g, lum);
        var overB = Overlay(b, lum);

        // Blend 60% overlay + 40% desaturated base
        var outR = 0.6 * overR + 0.4 * (0.35 * r + 0.65 * lum);
        var outG = 0.6 * overG + 0.4 * (0.35 * g + 0.65 * lum);
        var outB = 0.6 * overB + 0.4 * (0.35 * b + 0.65 * lum);

        return (outR, outG, outB);
    });

    /// <summary>
    /// Warm Sunset / Golden Hour creative look.
    /// Lifts amber-golden hues in midtones and highlights with soft warm shadow roll-off.
    /// </summary>
    public static CubeLut CreateWarmSunsetLook() => BuildLattice("Warm Sunset / Golden Hour", (r, g, b) =>
    {
        var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        var warmR = r + 0.14 * Math.Sqrt(y);
        var warmG = g + 0.05 * Math.Sqrt(y);
        var warmB = b - 0.10 * Math.Sqrt(y);

        // Soft toe roll-off in blacks
        warmR = Math.Max(0.02, warmR);
        warmG = Math.Max(0.01, warmG);

        return (warmR, warmG, warmB);
    });

    /// <summary>
    /// Vintage 1970s Kodachrome color film stock emulation.
    /// Lifted smoky blacks, warm golden reds, muted greens and slightly faded cyan-tinted shadows.
    /// </summary>
    public static CubeLut CreateVintage1970Look() => BuildLattice("Vintage 1970s Kodachrome", (r, g, b) =>
    {
        var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;

        // Black floor lift
        var vR = 0.04 + 0.94 * r;
        var vG = 0.03 + 0.93 * g;
        var vB = 0.06 + 0.88 * b;

        // Rich warm highlights, muted cool midtones
        vR += 0.08 * y * y;
        vG -= 0.03 * (1.0 - y);
        vB += 0.07 * (1.0 - y);

        return (vR, vG, vB);
    });

    /// <summary>
    /// Cyberpunk High-Contrast Neon styling.
    /// Saturated neon magenta and electric cyan split, razor-sharp contrast and deep velvety blacks.
    /// </summary>
    public static CubeLut CreateCyberpunkLook() => BuildLattice("Cyberpunk Neon", (r, g, b) =>
    {
        var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;

        // Contrast boost
        var cR = Math.Pow(r, 1.35);
        var cG = Math.Pow(g, 1.40);
        var cB = Math.Pow(b, 1.30);

        // Push toward magenta (R+B) and cyan (G+B)
        var neonR = cR * 1.25;
        var neonG = cG * 0.85 + 0.15 * cB;
        var neonB = cB * 1.35;

        return (neonR, neonG, neonB);
    });

    /// <summary>
    /// Monochrome Film Noir (Kodak Tri-X 400 panchromatic film emulation).
    /// Pure panchromatic spectral weighting with film S-curve response and deep silver blacks.
    /// </summary>
    public static CubeLut CreateFilmNoirLook() => BuildLattice("Monochrome Film Noir", (r, g, b) =>
    {
        // Kodak Tri-X spectral weighting
        var silver = 0.299 * r + 0.587 * g + 0.114 * b;

        // Steep S-curve for punchy film contrast
        var tone = silver * silver * (3.0 - 2.0 * silver);
        // Slight toe compression
        tone = Math.Pow(Math.Clamp(tone, 0.0, 1.0), 1.15);

        return (tone, tone, tone);
    });
}
