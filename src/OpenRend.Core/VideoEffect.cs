namespace OpenRend.Core;

public enum VideoEffectKind
{
    BrightnessContrast, Saturation, Invert, BlackAndWhite, Sepia, HslAdjust, ColorBalance,
    WhiteBalance, Levels, Threshold, BroadcastColors, BlackRestore, ChannelBlend,
    GaussianBlur, QuickBlur, Median, Sharpen, UnsharpMask, Pixelate, Mirror, Border,
    Vignette, SoftContrast, Glow, AddNoise, FilmGrain, RadialPixelate, Swirl, Wave,
    LensCorrection, ConvolutionKernel, FillLight, ChromaKeyer, CookieCutter, MinAndMax, OffsetAndWrap
}

public sealed class VideoEffect
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public VideoEffectKind Kind { get; set; }
    public bool IsEnabled { get; set; } = true;
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
        if (!double.IsFinite(value) || value < definition.Minimum || value > definition.Maximum)
            throw new InvalidDataException("Video effect parameter is outside its supported range.");
        if (key == "Amount") Amount = value;
        else if (key == "SecondaryAmount") SecondaryAmount = value;
        else Parameters[key] = value;
    }

    public VideoEffect DeepClone()
    {
        var copy = (VideoEffect)MemberwiseClone();
        copy.Parameters = new(Parameters, StringComparer.Ordinal);
        return copy;
    }

    public void Validate()
    {
        if (Id == Guid.Empty || !Enum.IsDefined(Kind) || Parameters is null || !double.IsFinite(Amount) || !double.IsFinite(SecondaryAmount))
            throw new InvalidDataException("Video effect is invalid.");
        var definitions = VideoEffectCatalog.Get(Kind).Parameters;
        if (Parameters.Any(pair => pair.Key is "Amount" or "SecondaryAmount" || !definitions.Any(parameter => parameter.Key == pair.Key)))
            throw new InvalidDataException("Video effect contains an unknown parameter.");
        foreach (var definition in definitions)
        {
            var value = GetParameter(definition.Key);
            if (!double.IsFinite(value) || value < definition.Minimum || value > definition.Maximum)
                throw new InvalidDataException("Video effect parameters are outside their supported range.");
        }
        if ((!definitions.Any(parameter => parameter.Key == "Amount") && Amount != 0) ||
            (!definitions.Any(parameter => parameter.Key == "SecondaryAmount") && SecondaryAmount != 0))
            throw new InvalidDataException("Video effect contains an unused parameter.");
        if (Kind == VideoEffectKind.Levels && GetParameter("InputBlack") >= GetParameter("InputWhite"))
            throw new InvalidDataException("Input white must be greater than input black.");
    }
}
