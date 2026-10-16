namespace OpenRend.Core;

public sealed record VideoEffectParameterDefinition(string Key, string Name, double Minimum, double Maximum, double DefaultValue, double Step = .01);
public sealed record VideoEffectDefinition(VideoEffectKind Kind, string Name, string Category, IReadOnlyList<VideoEffectParameterDefinition> Parameters);

public static class VideoEffectCatalog
{
    private static VideoEffectParameterDefinition P(string key, string name, double min, double max, double value, double step = .01) => new(key, name, min, max, value, step);
    private static VideoEffectDefinition D(VideoEffectKind kind, string name, string category, params VideoEffectParameterDefinition[] parameters) => new(kind, name, category, Array.AsReadOnly(parameters));
    public static IReadOnlyList<VideoEffectDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        D(VideoEffectKind.BrightnessContrast, "Brightness and Contrast", "Color", P("Amount", "Brightness", -1, 1, 0), P("SecondaryAmount", "Contrast", -1, 1, 0)),
        D(VideoEffectKind.Saturation, "Saturation Adjust", "Color", P("Amount", "Saturation", 0, 2, 1)),
        D(VideoEffectKind.Invert, "Invert", "Color", P("Amount", "Blend", 0, 1, 1)),
        D(VideoEffectKind.BlackAndWhite, "Black and White", "Color", P("Amount", "Blend", 0, 1, 1)),
        D(VideoEffectKind.Sepia, "Sepia", "Color", P("Amount", "Blend", 0, 1, 1)),
        D(VideoEffectKind.HslAdjust, "HSL Adjust", "Color", P("Hue", "Hue rotation", -180, 180, 0, 1), P("Saturation", "Saturation", 0, 2, 1), P("Lightness", "Lightness", -1, 1, 0)),
        D(VideoEffectKind.ColorBalance, "Color Balance", "Color", P("Red", "Red", -1, 1, 0), P("Green", "Green", -1, 1, 0), P("Blue", "Blue", -1, 1, 0)),
        D(VideoEffectKind.WhiteBalance, "White Balance", "Color", P("Temperature", "Temperature", -1, 1, 0), P("Tint", "Tint", -1, 1, 0)),
        D(VideoEffectKind.Levels, "Levels", "Color", P("InputBlack", "Input black", 0, .99, 0), P("InputWhite", "Input white", .01, 1, 1), P("Gamma", "Gamma", .1, 4, 1), P("OutputBlack", "Output black", 0, 1, 0), P("OutputWhite", "Output white", 0, 1, 1)),
        D(VideoEffectKind.Threshold, "Threshold", "Color", P("Amount", "Threshold", 0, 1, .5)),
        D(VideoEffectKind.BroadcastColors, "Broadcast Colors", "Color", P("Amount", "Legal-range blend", 0, 1, 1)),
        D(VideoEffectKind.BlackRestore, "Black Restore", "Color", P("Amount", "Shadow removal", 0, .95, .1)),
        D(VideoEffectKind.ChannelBlend, "Channel Blend", "Color", P("Red", "Red output gain", 0, 2, 1), P("Green", "Green output gain", 0, 2, 1), P("Blue", "Blue output gain", 0, 2, 1), P("Crossfeed", "Channel crossfeed", 0, 1, 0)),
        D(VideoEffectKind.GaussianBlur, "Gaussian Blur", "Blur", P("Amount", "Radius (% of image)", 0, 2, .4, .05)),
        D(VideoEffectKind.QuickBlur, "Quick Blur", "Blur", P("Amount", "Radius (% of image)", 0, 2, .4, .05)),
        D(VideoEffectKind.Median, "Median", "Blur", P("Amount", "Radius (pixels)", 0, 3, 1, 1)),
        D(VideoEffectKind.Sharpen, "Sharpen", "Detail", P("Amount", "Amount", 0, 2, .5)),
        D(VideoEffectKind.UnsharpMask, "Unsharp Mask", "Detail", P("Amount", "Amount", 0, 3, 1), P("Radius", "Radius (% of image)", 0, 2, .4, .05), P("Threshold", "Threshold", 0, 1, 0)),
        D(VideoEffectKind.Pixelate, "Pixelate", "Stylize", P("Amount", "Block size (% of image)", 0, 25, 5, .5)),
        D(VideoEffectKind.Mirror, "Mirror", "Distort", P("Horizontal", "Horizontal blend", 0, 1, 1), P("Vertical", "Vertical blend", 0, 1, 0)),
        D(VideoEffectKind.Border, "Border", "Stylize", P("Amount", "Width", 0, .5, .05), P("Red", "Red", 0, 1, 1), P("Green", "Green", 0, 1, 1), P("Blue", "Blue", 0, 1, 1)),
        D(VideoEffectKind.Vignette, "Vignette", "Stylize", P("Amount", "Darkening", 0, 1, .65), P("Radius", "Clear radius", 0, 1, .4), P("Softness", "Softness", .01, 1, .5)),
        D(VideoEffectKind.SoftContrast, "Soft Contrast", "Color", P("Amount", "Contrast", -1, 1, .35)),
        D(VideoEffectKind.Glow, "Glow", "Stylize", P("Amount", "Strength", 0, 2, .5), P("Radius", "Radius (% of image)", 0, 2, .8, .05), P("Threshold", "Threshold", 0, 1, .6)),
        D(VideoEffectKind.AddNoise, "Add Noise", "Stylize", P("Amount", "Amount", 0, 1, .15), P("Monochrome", "Monochrome blend", 0, 1, 1), P("Seed", "Seed", 0, 65535, 0, 1)),
        D(VideoEffectKind.FilmGrain, "Film Grain", "Stylize", P("Amount", "Amount", 0, 1, .15), P("Size", "Grain size (pixels)", 1, 8, 1, 1), P("Seed", "Seed", 0, 65535, 0, 1)),
        D(VideoEffectKind.RadialPixelate, "Radial Pixelate", "Stylize", P("Amount", "Radial bands", 1, 100, 20, 1), P("Sectors", "Angular sectors", 4, 180, 40, 1)),
        D(VideoEffectKind.Swirl, "Swirl", "Distort", P("Amount", "Rotation (degrees)", -360, 360, 90, 1), P("Radius", "Radius", .01, 1, .8)),
        D(VideoEffectKind.Wave, "Wave", "Distort", P("Amount", "Amplitude", 0, .2, .03), P("Frequency", "Waves", 0, 30, 4, .1), P("Phase", "Phase (degrees)", -360, 360, 0, 1)),
        D(VideoEffectKind.LensCorrection, "Lens Correction", "Distort", P("Amount", "Radial distortion", -1, 1, .15)),
        D(VideoEffectKind.ConvolutionKernel, "Convolution Kernel", "Detail", P("K00", "Top left", -4, 4, 0, .1), P("K01", "Top center", -4, 4, -1, .1), P("K02", "Top right", -4, 4, 0, .1), P("K10", "Middle left", -4, 4, -1, .1), P("K11", "Center", -4, 5, 5, .1), P("K12", "Middle right", -4, 4, -1, .1), P("K20", "Bottom left", -4, 4, 0, .1), P("K21", "Bottom center", -4, 4, -1, .1), P("K22", "Bottom right", -4, 4, 0, .1), P("Bias", "Bias", -1, 1, 0)),
        D(VideoEffectKind.FillLight, "Fill Light", "Color", P("Amount", "Fill", 0, 1, .3)),
        D(VideoEffectKind.ChromaKeyer, "Chroma Keyer", "Keying", P("Red", "Key red", 0, 1, 0), P("Green", "Key green", 0, 1, 1), P("Blue", "Key blue", 0, 1, 0), P("Tolerance", "Tolerance", 0, 1, .2), P("Softness", "Softness", .001, 1, .1)),
        D(VideoEffectKind.CookieCutter, "Cookie Cutter", "Keying", P("Width", "Ellipse width", .01, 1, .8), P("Height", "Ellipse height", .01, 1, .8), P("Feather", "Feather", .001, 1, .05), P("Invert", "Invert mask", 0, 1, 0)),
        D(VideoEffectKind.MinAndMax, "Min and Max", "Detail", P("Amount", "Minimum / maximum blend", -1, 1, 1)),
        D(VideoEffectKind.OffsetAndWrap, "Offset and Wrap", "Distort", P("Horizontal", "Horizontal offset", -1, 1, .1), P("Vertical", "Vertical offset", -1, 1, 0))
    });

    public static VideoEffectDefinition Get(VideoEffectKind kind) => Definitions.FirstOrDefault(definition => definition.Kind == kind)
        ?? throw new InvalidDataException("Unknown video effect kind.");
}
