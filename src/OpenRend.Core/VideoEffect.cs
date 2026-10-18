namespace OpenRend.Core;

public enum VideoEffectKind
{
    BrightnessContrast, Saturation, Invert, BlackAndWhite, Sepia, HslAdjust, ColorBalance,
    WhiteBalance, Levels, Threshold, BroadcastColors, BlackRestore, ChannelBlend,
    GaussianBlur, QuickBlur, Median, Sharpen, UnsharpMask, Pixelate, Mirror, Border,
    Vignette, SoftContrast, Glow, AddNoise, FilmGrain, RadialPixelate, Swirl, Wave,
    LensCorrection, ConvolutionKernel, FillLight, ChromaKeyer, CookieCutter, MinAndMax, OffsetAndWrap,
    LinearBlur, RadialBlur, ChromaBlur, Spherize, PinchPunch, TvSimulator, Crop, Newsprint, GradientMap,
    Defocus, LabAdjust, MaskGenerator, PictureInPicture, LightRays, Starburst, Timecode, TinyPlanet,
    AiAutoReframe, AiColorization, AiDehaze, AiSharpen, AiSmooth, AiStyleTransfer, AiUpscale, AiZDepth, AiSmartMask, Lut3D, ColorGrading
}

public enum EffectScope
{
    Event,
    Track,
    Media,
    Output
}

public sealed class VideoEffect
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public VideoEffectKind Kind { get; set; }
    public bool IsEnabled { get; set; } = true;
    public CubeLut? Lut { get; set; }
    public CubeLut? LookLut { get; set; }
    public ColorCurvesData? Curves { get; set; }
    public double Amount { get; set; }
    public double SecondaryAmount { get; set; }
    public Dictionary<string, double> Parameters { get; set; } = [];

    public static VideoEffect Create(VideoEffectKind kind)
    {
        var effect = new VideoEffect { Kind = kind };
        foreach (var parameter in VideoEffectCatalog.Get(kind).Parameters)
            effect.SetParameter(parameter.Key, parameter.DefaultValue);
        effect.Validate();
        return effect;
    }

    public double GetParameter(string key)
    {
        var definition = VideoEffectCatalog.Get(Kind).Parameters.FirstOrDefault(parameter => parameter.Key == key)
            ?? throw new ArgumentException("Unknown video effect parameter.", nameof(key));
        return key switch
        {
            "Amount" => Amount,
            "SecondaryAmount" => SecondaryAmount,
            _ => Parameters.GetValueOrDefault(key, definition.DefaultValue)
        };
    }

    public void SetParameter(string key, double value)
    {
        var definition = VideoEffectCatalog.Get(Kind).Parameters.FirstOrDefault(parameter => parameter.Key == key)
            ?? throw new ArgumentException("Unknown video effect parameter.", nameof(key));
        if (!IsValidValue(definition, value))
            throw new InvalidDataException("Video effect parameter is outside its supported range.");
        if (key == "Amount") Amount = value;
        else if (key == "SecondaryAmount") SecondaryAmount = value;
        else Parameters[key] = value;
    }

    public VideoEffect DeepClone()
    {
        var copy = (VideoEffect)MemberwiseClone();
        copy.Parameters = new(Parameters, StringComparer.Ordinal);
        copy.Lut = Lut?.DeepClone();
        copy.LookLut = LookLut?.DeepClone();
        copy.Curves = Curves?.DeepClone();
        return copy;
    }

    public void Validate()
    {
        if (Id == Guid.Empty || !Enum.IsDefined(Kind) || Parameters is null || !double.IsFinite(Amount) || !double.IsFinite(SecondaryAmount))
            throw new InvalidDataException("Video effect is invalid.");
        if (Kind != VideoEffectKind.Lut3D && Kind != VideoEffectKind.ColorGrading && Lut is not null)
            throw new InvalidDataException("Only a LUT or Color Grading effect can contain Camera LUT data.");
        if (Kind != VideoEffectKind.ColorGrading && LookLut is not null)
            throw new InvalidDataException("Only a Color Grading effect can contain Look LUT data.");
        if (Kind != VideoEffectKind.ColorGrading && Curves is not null)
            throw new InvalidDataException("Only a Color Grading effect can contain Color Curves data.");
        Lut?.Validate();
        LookLut?.Validate();
        Curves?.Validate();
        var definitions = VideoEffectCatalog.Get(Kind).Parameters;
        if (Parameters.Any(pair => pair.Key is "Amount" or "SecondaryAmount" || !definitions.Any(parameter => parameter.Key == pair.Key)))
            throw new InvalidDataException("Video effect contains an unknown parameter.");
        foreach (var definition in definitions)
        {
            var value = GetParameter(definition.Key);
            if (!IsValidValue(definition, value))
                throw new InvalidDataException("Video effect parameters are outside their supported range.");
        }
        if ((!definitions.Any(parameter => parameter.Key == "Amount") && Amount != 0) ||
            (!definitions.Any(parameter => parameter.Key == "SecondaryAmount") && SecondaryAmount != 0))
            throw new InvalidDataException("Video effect contains an unused parameter.");
        if (Kind == VideoEffectKind.Levels && GetParameter("InputBlack") >= GetParameter("InputWhite"))
            throw new InvalidDataException("Input white must be greater than input black.");
    }

    private static bool IsValidValue(VideoEffectParameterDefinition definition, double value) =>
        double.IsFinite(value) && value >= definition.Minimum && value <= definition.Maximum &&
        (definition.Choices is not { } choices || value == Math.Truncate(value) && value >= 0 && value < choices.Count);
}
