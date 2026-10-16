using OpenRend.Core;

namespace OpenRend.Media;

internal static partial class VideoEffectProcessor
{
    internal static bool HasActiveEffects(IReadOnlyList<VideoEffect> effects) => effects.Any(effect => effect.IsEnabled && !IsIdentity(effect));

    private static bool IsIdentity(VideoEffect effect) => effect.Kind switch
    {
        VideoEffectKind.BrightnessContrast => effect.Amount == 0 && effect.SecondaryAmount == 0,
        VideoEffectKind.Saturation => effect.Amount == 1,
        VideoEffectKind.Invert or VideoEffectKind.BlackAndWhite or VideoEffectKind.Sepia or VideoEffectKind.BlackRestore or
        VideoEffectKind.GaussianBlur or VideoEffectKind.QuickBlur or VideoEffectKind.Median or VideoEffectKind.Sharpen or
        VideoEffectKind.UnsharpMask or VideoEffectKind.Pixelate or VideoEffectKind.Border or VideoEffectKind.Vignette or
        VideoEffectKind.SoftContrast or VideoEffectKind.Glow or VideoEffectKind.AddNoise or VideoEffectKind.FilmGrain or
        VideoEffectKind.Swirl or VideoEffectKind.Wave or VideoEffectKind.LensCorrection or VideoEffectKind.FillLight or
        VideoEffectKind.MinAndMax => effect.Amount == 0,
        VideoEffectKind.HslAdjust => effect.GetParameter("Hue") == 0 && effect.GetParameter("Saturation") == 1 && effect.GetParameter("Lightness") == 0,
        VideoEffectKind.ColorBalance => effect.GetParameter("Red") == 0 && effect.GetParameter("Green") == 0 && effect.GetParameter("Blue") == 0,
        VideoEffectKind.WhiteBalance => effect.GetParameter("Temperature") == 0 && effect.GetParameter("Tint") == 0,
        VideoEffectKind.Levels => effect.GetParameter("InputBlack") == 0 && effect.GetParameter("InputWhite") == 1 && effect.GetParameter("Gamma") == 1 && effect.GetParameter("OutputBlack") == 0 && effect.GetParameter("OutputWhite") == 1,
        VideoEffectKind.ChannelBlend => effect.GetParameter("Red") == 1 && effect.GetParameter("Green") == 1 && effect.GetParameter("Blue") == 1 && effect.GetParameter("Crossfeed") == 0,
        VideoEffectKind.Mirror or VideoEffectKind.OffsetAndWrap => effect.GetParameter("Horizontal") == 0 && effect.GetParameter("Vertical") == 0,
        _ => false
    };

    internal static byte[] Apply(byte[] source, IReadOnlyList<VideoEffect> effects) => Apply(source, source.Length / 4, 1, effects);

    internal static byte[] Apply(byte[] source, int width, int height, IReadOnlyList<VideoEffect> effects, long projectFrame = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(effects);
        if (width <= 0 || height <= 0 || (long)width * height * 4 != source.Length) throw new ArgumentException("Effect image dimensions do not match its BGRA buffer.");
        foreach (var effect in effects) effect.Validate();
        if (!HasActiveEffects(effects)) return source;
        // Decoder frames remain immutable. Each chain stage owns its output before the next stage reads it.
        var output = (byte[])source.Clone();
        foreach (var effect in effects)
        {
            if (!effect.IsEnabled || IsIdentity(effect)) continue;
            double P(string key) => effect.GetParameter(key);
            var amount = effect.Amount;
            switch (effect.Kind)
            {
                case VideoEffectKind.GaussianBlur:
                case VideoEffectKind.QuickBlur:
                    output = Blur(output, width, height, Radius(amount, width, height), effect.Kind == VideoEffectKind.GaussianBlur); break;
                case VideoEffectKind.Sharpen:
                case VideoEffectKind.UnsharpMask:
                case VideoEffectKind.Glow:
                    var glow = effect.Kind == VideoEffectKind.Glow;
                    var blurredInput = output;
                    if (glow)
                    {
                        var threshold = P("Threshold");
                        blurredInput = Color(output, (r, g, b) => (Math.Max(0, r - threshold), Math.Max(0, g - threshold), Math.Max(0, b - threshold)));
                    }
                    var radius = effect.Kind == VideoEffectKind.Sharpen ? 1 : Radius(P("Radius"), width, height);
                    var blurred = Blur(blurredInput, width, height, radius, true);
                    var sharpenThreshold = effect.Kind == VideoEffectKind.UnsharpMask ? P("Threshold") * 255 : 0;
                    for (var i = 0; i < output.Length; i += 4)
                        for (var c = 0; c < 3; c++)
                        {
                            var detail = output[i + c] - blurred[i + c];
                            output[i + c] = Byte(glow ? output[i + c] + blurred[i + c] * amount : output[i + c] + (Math.Abs(detail) >= sharpenThreshold ? detail * amount : 0));
                        }
                    break;
                case VideoEffectKind.Median: output = Neighborhood(output, width, height, (int)Math.Round(amount), 0); break;
                case VideoEffectKind.MinAndMax: output = Neighborhood(output, width, height, 1, amount); break;
                case VideoEffectKind.Pixelate: output = Pixelate(output, width, height, Math.Max(1, Radius(amount, width, height))); break;
                case VideoEffectKind.ConvolutionKernel: output = Convolution(output, width, height, effect); break;
                case VideoEffectKind.Mirror:
                    var horizontal = P("Horizontal"); var vertical = P("Vertical");
                    output = Mirror(output, width, height, horizontal, vertical); break;
                case VideoEffectKind.OffsetAndWrap:
                    var dx = P("Horizontal") * width; var dy = P("Vertical") * height;
                    output = Resample(output, width, height, (x, y) => (Wrap(x - dx, width), Wrap(y - dy, height)), true); break;
                case VideoEffectKind.Swirl:
                    var swirlRadius = P("Radius");
                    output = Polar(output, width, height, (r, a) => (r, a + amount * Math.PI / 180 * Math.Max(0, 1 - r / swirlRadius))); break;
                case VideoEffectKind.RadialPixelate:
                    var sectors = P("Sectors");
                    output = Polar(output, width, height, (r, a) => ((Math.Floor(r * amount) + .5) / amount, (Math.Floor(a / (2 * Math.PI) * sectors) + .5) / sectors * 2 * Math.PI)); break;
                case VideoEffectKind.LensCorrection:
                    output = Polar(output, width, height, (r, a) => (r * (1 + amount * r * r), a)); break;
                case VideoEffectKind.Wave:
                    var frequency = P("Frequency"); var phase = P("Phase") * Math.PI / 180;
                    output = Resample(output, width, height, (x, y) => (x + amount * width * Math.Sin(2 * Math.PI * (y + .5) / height * frequency + phase), y)); break;
                case VideoEffectKind.Border:
                    var border = amount * Math.Min(width, height); var br = Channel(P("Red")); var bg = Channel(P("Green")); var bb = Channel(P("Blue"));
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) if (x < border || y < border || width - 1 - x < border || height - 1 - y < border)
                    { var i = (y * width + x) * 4; output[i] = bb; output[i + 1] = bg; output[i + 2] = br; }
                    break;
                case VideoEffectKind.Vignette:
                    var clear = P("Radius"); var softness = P("Softness");
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    {
                        var r = Math.Sqrt(Math.Pow((x + .5) / width * 2 - 1, 2) + Math.Pow((y + .5) / height * 2 - 1, 2)) / Math.Sqrt(2);
                        var t = Math.Clamp((r - clear) / softness, 0, 1); var gain = 1 - amount * t * t * (3 - 2 * t); var i = (y * width + x) * 4;
                        for (var c = 0; c < 3; c++) output[i + c] = Byte(output[i + c] * gain);
                    }
                    break;
                case VideoEffectKind.CookieCutter:
                    var maskWidth = P("Width"); var maskHeight = P("Height"); var feather = P("Feather"); var invert = P("Invert");
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    {
                        var r = Math.Sqrt(Math.Pow(((x + .5) / width * 2 - 1) / maskWidth, 2) + Math.Pow(((y + .5) / height * 2 - 1) / maskHeight, 2));
                        var alpha = Math.Clamp((1 - r) / feather, 0, 1); alpha = alpha * (1 - invert) + (1 - alpha) * invert;
                        var i = (y * width + x) * 4; output[i + 3] = Byte(output[i + 3] * alpha);
                    }
                    break;
                case VideoEffectKind.ChromaKeyer:
                    var kr = P("Red"); var kg = P("Green"); var kb = P("Blue"); var tolerance = P("Tolerance"); var keySoftness = P("Softness");
                    for (var i = 0; i < output.Length; i += 4)
                    {
                        var distance = Math.Sqrt(Math.Pow(output[i + 2] / 255.0 - kr, 2) + Math.Pow(output[i + 1] / 255.0 - kg, 2) + Math.Pow(output[i] / 255.0 - kb, 2)) / Math.Sqrt(3);
                        output[i + 3] = Byte(output[i + 3] * Math.Clamp((distance - tolerance) / keySoftness, 0, 1));
                    }
                    break;
                case VideoEffectKind.AddNoise:
                case VideoEffectKind.FilmGrain:
                    var grain = effect.Kind == VideoEffectKind.FilmGrain; var size = grain ? Math.Max(1, (int)Math.Round(P("Size"))) : 1;
                    var monochrome = grain ? 1 : P("Monochrome"); var seed = (uint)P("Seed");
                    // A frame-addressed hash makes seek, preview, export and retries reproduce the same grain.
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    {
                        var i = (y * width + x) * 4; var key = unchecked(seed + (uint)(x / size) * 73856093u + (uint)(y / size) * 19349663u + (uint)projectFrame * 83492791u + (uint)(projectFrame >> 32));
                        var mono = Noise(key); var luma = (.2126 * output[i + 2] + .7152 * output[i + 1] + .0722 * output[i]) / 255;
                        for (var c = 0; c < 3; c++)
                        { var noise = mono * monochrome + Noise(unchecked(key + (uint)(c + 1) * 2654435761u)) * (1 - monochrome); output[i + c] = Byte(output[i + c] + noise * amount * 255 * (grain ? (.25 + .75 * Math.Sqrt(luma * (1 - luma) * 4)) : 1)); }
                    }
                    break;
                default: output = ApplyColor(output, effect); break;
            }
        }
        return output;
    }

    private static byte[] ApplyColor(byte[] source, VideoEffect effect)
    {
        double P(string key) => effect.GetParameter(key);
        var a = effect.Amount; var secondary = effect.SecondaryAmount;
        Func<double, double, double, (double R, double G, double B)> operation;
        switch (effect.Kind)
        {
            case VideoEffectKind.BrightnessContrast: operation = (r, g, b) => ((r - .5) * (1 + secondary) + .5 + a, (g - .5) * (1 + secondary) + .5 + a, (b - .5) * (1 + secondary) + .5 + a); break;
            case VideoEffectKind.Invert:
            case VideoEffectKind.BlackAndWhite:
                return ApplyBoundedBlend(source, effect.Kind, a);
            case VideoEffectKind.Saturation:
                var saturation = a;
                operation = (r, g, b) => { var l = .2126 * r + .7152 * g + .0722 * b; return (l + (r - l) * saturation, l + (g - l) * saturation, l + (b - l) * saturation); }; break;
            case VideoEffectKind.Sepia: operation = (r, g, b) => (r * (1 - a) + (.393 * r + .769 * g + .189 * b) * a, g * (1 - a) + (.349 * r + .686 * g + .168 * b) * a, b * (1 - a) + (.272 * r + .534 * g + .131 * b) * a); break;
            case VideoEffectKind.ColorBalance:
                var red = P("Red"); var green = P("Green"); var blue = P("Blue"); operation = (r, g, b) => (r + red, g + green, b + blue); break;
            case VideoEffectKind.WhiteBalance:
                var temperature = P("Temperature") * .25; var tint = P("Tint") * .25; operation = (r, g, b) => (r * (1 + temperature), g * (1 - tint), b * (1 - temperature)); break;
            case VideoEffectKind.Levels:
                var black = P("InputBlack"); var white = P("InputWhite"); var gamma = P("Gamma"); var ob = P("OutputBlack"); var ow = P("OutputWhite");
                double Level(double value) => ob + Math.Pow(Math.Clamp((value - black) / (white - black), 0, 1), 1 / gamma) * (ow - ob);
                operation = (r, g, b) => (Level(r), Level(g), Level(b)); break;
            case VideoEffectKind.Threshold: operation = (r, g, b) => { var l = .2126 * r + .7152 * g + .0722 * b >= a ? 1.0 : 0; return (l, l, l); }; break;
            case VideoEffectKind.BroadcastColors: operation = (r, g, b) => (r * (1 - a) + (16 + 219 * r) / 255 * a, g * (1 - a) + (16 + 219 * g) / 255 * a, b * (1 - a) + (16 + 219 * b) / 255 * a); break;
            case VideoEffectKind.BlackRestore: operation = (r, g, b) => ((r - a) / (1 - a), (g - a) / (1 - a), (b - a) / (1 - a)); break;
            case VideoEffectKind.ChannelBlend:
                var rg = P("Red"); var gg = P("Green"); var bGain = P("Blue"); var cross = P("Crossfeed");
                operation = (r, g, b) => ((r * (1 - cross) + g * cross) * rg, (g * (1 - cross) + b * cross) * gg, (b * (1 - cross) + r * cross) * bGain); break;
            case VideoEffectKind.SoftContrast:
                double Curve(double value) => a >= 0 ? value + (value * value * (3 - 2 * value) - value) * a : value + (Math.Sqrt(value) * .5 + value * .5 - value) * -a;
                operation = (r, g, b) => (Curve(r), Curve(g), Curve(b)); break;
            case VideoEffectKind.FillLight: operation = (r, g, b) => (r + a * (1 - r) * (1 - r), g + a * (1 - g) * (1 - g), b + a * (1 - b) * (1 - b)); break;
            case VideoEffectKind.HslAdjust:
                var hue = P("Hue") / 360; var hslSaturation = P("Saturation"); var lightness = P("Lightness");
                operation = (r, g, b) => AdjustHsl(r, g, b, hue, hslSaturation, lightness); break;
            default: throw new InvalidDataException("Video effect has no renderer.");
        }
        return Color(source, operation);
    }

    private static byte[] ApplyBoundedBlend(byte[] source, VideoEffectKind kind, double amount)
    {
        var output = (byte[])source.Clone();
        for (var i = 0; i < source.Length; i += 4)
        {
            var gray = (source[i] + source[i + 1] + source[i + 2]) / 3.0;
            for (var channel = 0; channel < 3; channel++)
            {
                var value = source[i + channel];
                var target = kind == VideoEffectKind.Invert ? 255 - value : gray;
                // Blend in byte space, then truncate once. Alpha remains the original source coverage.
                output[i + channel] = (byte)(value + (target - value) * amount);
            }
        }
        return output;
    }

    private static byte[] Color(byte[] source, Func<double, double, double, (double R, double G, double B)> operation)
    {
        var output = (byte[])source.Clone();
        for (var i = 0; i < output.Length; i += 4)
        { var (r, g, b) = operation(source[i + 2] / 255.0, source[i + 1] / 255.0, source[i] / 255.0); output[i] = Channel(b); output[i + 1] = Channel(g); output[i + 2] = Channel(r); }
        return output;
    }

    private static (double R, double G, double B) AdjustHsl(double r, double g, double b, double rotation, double saturation, double lightness)
    {
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min; var l = (max + min) / 2; var s = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * l - 1));
        var h = delta == 0 ? 0 : max == r ? ((g - b) / delta) % 6 : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
        h = Wrap(h / 6 + rotation, 1); s = Math.Clamp(s * saturation, 0, 1); l = Math.Clamp(l + lightness, 0, 1);
        var c = (1 - Math.Abs(2 * l - 1)) * s; var x = c * (1 - Math.Abs((h * 6) % 2 - 1)); var m = l - c / 2;
        return ((int)(h * 6)) switch { 0 => (c + m, x + m, m), 1 => (x + m, c + m, m), 2 => (m, c + m, x + m), 3 => (m, x + m, c + m), 4 => (x + m, m, c + m), _ => (c + m, m, x + m) };
    }

    private static int Radius(double percent, int width, int height) => percent <= 0 ? 0 : Math.Max(1, (int)Math.Round(Math.Min(width, height) * percent / 100));
    private static double Wrap(double value, double size) => (value % size + size) % size;
    private static double Noise(uint value)
    { value ^= value >> 16; value *= 0x7feb352d; value ^= value >> 15; value *= 0x846ca68b; value ^= value >> 16; return value / (double)uint.MaxValue - .5; }
    private static byte Channel(double value) => Byte(value * 255);
    private static byte Byte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
