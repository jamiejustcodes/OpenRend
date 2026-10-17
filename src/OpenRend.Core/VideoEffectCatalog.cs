namespace OpenRend.Core;

public sealed record VideoEffectParameterDefinition(string Key, string Name, double Minimum, double Maximum, double DefaultValue, double Step = .01, IReadOnlyList<string>? Choices = null);
public sealed record VideoEffectDefinition(VideoEffectKind Kind, string Name, string Category, IReadOnlyList<VideoEffectParameterDefinition> Parameters);

public static class VideoEffectCatalog
{
    private static VideoEffectParameterDefinition P(string key, string name, double min, double max, double value, double step = .01) => new(key, name, min, max, value, step);
    private static VideoEffectParameterDefinition Choice(string key, string name, params string[] choices) => new(key, name, 0, choices.Length - 1, 0, 1, Array.AsReadOnly(choices));
    private static VideoEffectDefinition D(VideoEffectKind kind, string name, string category, params VideoEffectParameterDefinition[] parameters) => new(kind, name, category, Array.AsReadOnly(parameters));
    public static IReadOnlyList<VideoEffectDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        D(VideoEffectKind.BrightnessContrast, "Brightness and Contrast", "Color", P("Amount", "Brightness", -1, 1, 0), P("SecondaryAmount", "Contrast", -1, 1, 0)),
        D(VideoEffectKind.Lut3D, "LUT Filter", "Color", P("Amount", "Strength", 0, 1, 1), Choice("Interpolation", "Interpolation", "Trilinear", "Tetrahedral")),
        D(VideoEffectKind.Saturation, "Saturation Adjust", "Color", P("Amount", "Saturation", 0, 2, 1)),
        D(VideoEffectKind.Invert, "Invert", "Color", P("Amount", "Blend amount", 0, 1, 1)),
        D(VideoEffectKind.BlackAndWhite, "Black and White", "Color", P("Amount", "Blend amount", 0, 1, 1)),
        D(VideoEffectKind.Sepia, "Sepia", "Color", P("Amount", "Blend", 0, 1, 1)),
        D(VideoEffectKind.HslAdjust, "HSL Adjust", "Color", P("Hue", "Hue rotation", -180, 180, 0, 1), P("Saturation", "Saturation", 0, 2, 1), P("Lightness", "Lightness", -1, 1, 0)),
        D(VideoEffectKind.ColorBalance, "Color Balance", "Color", P("Red", "Red", -1, 1, 0), P("Green", "Green", -1, 1, 0), P("Blue", "Blue", -1, 1, 0)),
        D(VideoEffectKind.WhiteBalance, "White Balance", "Color", P("Temperature", "Temperature", -1, 1, 0), P("Tint", "Tint", -1, 1, 0)),
        D(VideoEffectKind.Levels, "Levels", "Color", Choice("Channel", "Channel", "All", "Red", "Green", "Blue", "Alpha"), P("InputBlack", "Input black", 0, .99, 0), P("InputWhite", "Input white", .01, 1, 1), P("Gamma", "Gamma", .1, 4, 1), P("OutputBlack", "Output black", 0, 1, 0), P("OutputWhite", "Output white", 0, 1, 1)),
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
        D(VideoEffectKind.OffsetAndWrap, "Offset and Wrap", "Distort", P("Horizontal", "Horizontal offset", -1, 1, .1), P("Vertical", "Vertical offset", -1, 1, 0)),
        D(VideoEffectKind.LinearBlur, "Linear Blur", "Blur", P("Amount", "Amount", 0, 1, 0), P("Angle", "Angle", -360, 360, 0, 1)),
        D(VideoEffectKind.RadialBlur, "Radial Blur", "Blur", Choice("Type", "Type", "Proportional", "Fixed radius", "Gaussian"), P("Amount", "Strength", 0, 1, 0), P("CenterX", "Center X", 0, 1, .5), P("CenterY", "Center Y", 0, 1, .5)),
        D(VideoEffectKind.ChromaBlur, "Chroma Blur", "Blur", P("Horizontal", "Horizontal pixels", 0, 50, 0, 1), P("Vertical", "Vertical pixels", 0, 50, 0, 1)),
        D(VideoEffectKind.Spherize, "Spherize", "Distort", P("Amount", "Amount", -1, 1, 0), P("CenterX", "Center X", 0, 1, .5), P("CenterY", "Center Y", 0, 1, .5), P("Horizontal", "Horizontal scale", .01, 2, 1), P("Vertical", "Vertical scale", .01, 2, 1)),
        D(VideoEffectKind.PinchPunch, "Pinch/Punch", "Distort", P("Amount", "Amount", -1, 1, 0), P("CenterX", "Center X", 0, 1, .5), P("CenterY", "Center Y", 0, 1, .5), P("Horizontal", "Horizontal scale", .01, 2, 1), P("Vertical", "Vertical scale", .01, 2, 1)),
        D(VideoEffectKind.TvSimulator, "TV Simulator", "Stylize", P("Amount", "Interlacing", 0, 1, 0), P("LineSync", "Line sync", 0, 1, 0), P("Phosphorescence", "Phosphorescence", 0, 1, 0), P("Static", "Static noise", 0, 1, 0)),
        D(VideoEffectKind.Crop, "Crop", "Keying", P("XCrop", "X Crop", 0, .5, 0), P("YCrop", "Y Crop", 0, .5, 0), P("Angle", "Angle", -360, 360, 0, 1), P("Opacity", "Opacity", 0, 1, 1)),
        D(VideoEffectKind.Newsprint, "Newsprint", "Stylize", Choice("Type", "Type", "Color", "Monochrome", "Offset color"), P("Amount", "Dot size", 1, 50, 1, 1)),
        D(VideoEffectKind.GradientMap, "Gradient Map", "Color", Choice("Mode", "Mode", "Blend", "Add"), P("Amount", "Amount", 0, 1, 0)),
        D(VideoEffectKind.Defocus, "Defocus", "Blur", P("Amount", "Radius", 0, 50, 0), Choice("IrisShape", "Iris shape", "Circle", "Pentagon", "Hexagon", "Heptagon", "Octagon"), P("BloomThreshold", "Bloom threshold", 0, 100, 90), P("BloomStrength", "Bloom strength", 0, 20, 5)),
        D(VideoEffectKind.LabAdjust, "LAB Adjust", "Color", P("Lightness", "Lightness", 0, 2, 1), P("ChannelA", "Channel a", -1, 1, 0), P("ChannelB", "Channel b", -1, 1, 0), Choice("InvertLuminance", "Invert luminance", "Off", "On"), P("Desaturate", "Desaturate", 0, 1, 0)),
        D(VideoEffectKind.MaskGenerator, "Mask Generator", "Keying", Choice("Type", "Type", "Luminance", "Red", "Green", "Blue", "Alpha"), Choice("Invert", "Invert", "Off", "On"), P("LowIn", "Low in", 0, 1, 0), P("HighIn", "High in", 0, 1, 1), P("LowOut", "Low out", 0, 1, 0), P("HighOut", "High out", 0, 1, 1)),
        D(VideoEffectKind.Timecode, "Timecode", "Render", Choice("Format", "Timecode format", "Time", "Seconds", "Time & Frames", "Absolute Frames", "SMPTE Drop", "SMPTE Non-Drop"), Choice("HorizontalAlignment", "Horizontal alignment", "Left", "Middle", "Right"), Choice("VerticalAlignment", "Vertical alignment", "Top", "Middle", "Bottom"), P("Amount", "Size", .25, 2, 1)),
        D(VideoEffectKind.PictureInPicture, "Picture In Picture", "Distort", P("LocationX", "Location X", 0, 1, .5), P("LocationY", "Location Y", 0, 1, .5), P("Scale", "Scale", .01, 2, 1), P("Angle", "Angle", -360, 360, 0, 1), P("Opacity", "Opacity", 0, 1, 1), Choice("Mode", "Mode", "Fixed Shape", "Fixed Parallelogram", "Free Form")),
        D(VideoEffectKind.LightRays, "Light Rays", "Glow", P("LightSourceX", "Light source X", 0, 1, .5), P("LightSourceY", "Light source Y", 0, 1, .5), P("Sensitivity", "Sensitivity", 0, 1, .5), P("Amount", "Strength", 0, 1, 0), P("Blend", "Blend", 0, 1, 0)),
        D(VideoEffectKind.Starburst, "Starburst", "Glow", P("Threshold", "Threshold", 0, 100, 70), P("Brilliance", "Brilliance", 0, 2000, 800), P("Amount", "Strength", 0, 100, 0), P("Radius", "Radius", 1, 50, 15), P("Points", "Points", 2, 16, 4, 1)),
        D(VideoEffectKind.TinyPlanet, "Tiny Planet", "Distort", P("Latitude", "Latitude", -180, 180, 0, 1), P("Longitude", "Longitude", -180, 180, 0, 1), P("Z", "Z", .1, 100, 20)),
        D(VideoEffectKind.AiAutoReframe, "AI Auto Reframe", "AI", Choice("Preview", "Show Preview for", "Off", "Landscape (16:9)", "Portrait (9:16)", "Fullscreen (4:3)", "Square (1:1)", "Scope (2.4:1)", "Custom"), P("CenterX", "Frame center X", 0, 1, .5), P("CenterY", "Frame center Y", 0, 1, .5), P("Smoothness", "Smoothness", 0, 1, .5), Choice("FollowSubject", "Follow subject", "Yes", "No")),
        D(VideoEffectKind.AiColorization, "AI Colorization", "AI", Choice("Model", "Model", "Basic", "Enhanced"), Choice("Precision", "Precision", "Low", "Medium", "High", "Match Input Size"), P("ColorShiftA", "Shift between Green and Red", -1, 1, 0), P("ColorShiftB", "Shift between Blue and Yellow", -1, 1, 0), P("ColorMagnificationA", "Gain on Green and Red", 0, 2, 1), P("ColorMagnificationB", "Gain on Blue and Yellow", 0, 2, 1), Choice("InvertColorA", "Invert Green and Red", "No", "Yes"), Choice("InvertColorB", "Invert Blue and Yellow", "No", "Yes")),
        D(VideoEffectKind.AiDehaze, "AI Dehaze", "AI", Choice("Details", "Details", "Low", "Medium", "High"), P("Amount", "Amount", 0, 1, .5), P("Border", "Border", 0, 1, 0), P("Gain", "Gain", 0, 2, 1), P("Offset", "Offset", -1, 1, 0)),
        D(VideoEffectKind.AiSharpen, "AI Sharpen", "AI", Choice("Details", "Details", "Low", "Medium", "High"), P("Amount", "Amount", 0, 2, .5), P("Border", "Border", 0, 1, 0), P("Gain", "Gain", 0, 2, 1), P("Offset", "Offset", -1, 1, 0)),
        D(VideoEffectKind.AiSmooth, "AI Smoothen", "AI", Choice("Details", "Details", "Low", "Medium", "High"), P("Amount", "Amount", 0, 1, .5), P("Border", "Border", 0, 1, 0), P("Gain", "Gain", 0, 2, 1), P("Offset", "Offset", -1, 1, 0)),
        D(VideoEffectKind.AiStyleTransfer, "AI Style Transfer", "AI", Choice("Style", "Style", "Self Portrait (Picasso)", "Night Alley Walk (Afremov)", "The Great Wave of Kanagawa (Hokusai)", "Fruit (Hunt)", "Light (Afremov)", "Small Worlds 1 (Kandinsky)", "Floral pattern", "Sunrise Flowers (Lambert)", "Abstract Painting (Picasso)", "Black and White (KLH)", "The Starry Night (van Gogh)", "The Pandorica Opens (van Gogh)", "The Weeping Woman (Picasso)", "Bark pattern", "Font pattern", "Leaf pattern", "Rick and Morty", "Candy", "Mosaic", "Pointillism", "Rain Princess (Leonid Afremov)", "Udnie (Francis Picabia)", "Scream (Edvard Munch)", "Simpsons"), Choice("Details", "Details", "Low", "Medium", "High", "Original"), Choice("HiResOutput", "Hi-Res output", "Off", "On"), P("Amount", "Blend amount", 0, 1, 1)),
        D(VideoEffectKind.AiUpscale, "AI Upscale", "AI", P("Scaling", "Scaling", 1, 4, 2, .1), Choice("Models", "Model", "Smooth", "Sharp", "Denoise"), Choice("ImproveSharpness", "Improve Sharpness", "No", "Yes"), Choice("FitOption", "Fit", "short side", "long side")),
        D(VideoEffectKind.AiZDepth, "AI Z-Depth", "AI", Choice("Mode", "Mode", "Absolute Values", "Normalized Values"), P("DepthMin", "Furthest", 0, 1, 0), P("DepthMax", "Nearest", 0, 1, 1), Choice("Visualization", "Visualization", "None", "Thresholded depth", "Depth", "Opacity"), Choice("Invert", "Invert", "No", "Yes"), P("Feather", "Feather", 0, 1, 0)),
        D(VideoEffectKind.AiSmartMask, "AI Smart Mask 2.0", "AI", Choice("MaskChooser", "Mask", "Mask 1 (blue)", "Mask 2 (green)", "Mask 3 (red)", "Mask 4 (yellow)", "Mask 5 (violet)", "Mask 6 (cyan)", "Mask 7 (magenta)", "Mask 8 (turquoise)"), Choice("Precision", "Precision", "Low, fast", "High, slow"), P("Confidence", "Confidence offset", -1, 1, 0), P("Feather", "Feather", 0, 1, .1), Choice("Invert", "Invert", "No", "Yes"), Choice("Visualization", "View Masking Results", "Normal", "View Matte", "Mask Overlay"))
    });

    public static VideoEffectDefinition Get(VideoEffectKind kind) => Definitions.FirstOrDefault(definition => definition.Kind == kind)
        ?? throw new InvalidDataException("Unknown video effect kind.");
}
