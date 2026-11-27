using System.Text.Json.Serialization;

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

public enum ColorGradingSequence
{
    Legacy,
    CurvesBeforeWheels
}

public sealed class VideoEffect
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public VideoEffectKind Kind { get; set; }
    public bool IsEnabled { get; set; } = true;
    public CubeLut? Lut { get; set; }
    public CubeLut? LookLut { get; set; }
    public ColorCurvesData? Curves { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HSLCurvesData? HSLCurves { get; set; }
    // An omitted value keeps the order used by older project files.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ColorGradingSequence GradingSequence { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VideoEffectAnimation? Animation { get; set; }
    public double Amount { get; set; }
    public double SecondaryAmount { get; set; }
    public Dictionary<string, double> Parameters { get; set; } = [];

    public static VideoEffect Create(VideoEffectKind kind) => Create(kind, null);

    public static VideoEffect Create(VideoEffectKind kind, IReadOnlyDictionary<string, double>? parameters)
    {
        var effect = new VideoEffect { Kind = kind };
        if (kind == VideoEffectKind.ColorGrading) effect.GradingSequence = ColorGradingSequence.CurvesBeforeWheels;
        foreach (var parameter in VideoEffectCatalog.Get(kind).Parameters)
            effect.SetParameter(parameter.Key, parameter.DefaultValue);
        if (parameters is not null)
            foreach (var (key, value) in parameters) effect.SetParameter(key, value);
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
        copy.HSLCurves = HSLCurves?.DeepClone();
        copy.Animation = Animation?.DeepClone();
        return copy;
    }

    public bool ContentEquals(VideoEffect other) =>
        Id == other.Id && Kind == other.Kind && IsEnabled == other.IsEnabled &&
        GradingSequence == other.GradingSequence &&
        (Lut is { } camera ? camera.ContentEquals(other.Lut) : other.Lut is null) &&
        (LookLut is { } look ? look.ContentEquals(other.LookLut) : other.LookLut is null) &&
        (Curves is { } rgb ? rgb.ContentEquals(other.Curves) : other.Curves is null) &&
        (HSLCurves is { } hsl ? hsl.ContentEquals(other.HSLCurves) : other.HSLCurves is null) &&
        SameAnimation(Animation, other.Animation) &&
        VideoEffectCatalog.Get(Kind).Parameters.All(parameter => GetParameter(parameter.Key) == other.GetParameter(parameter.Key));

    private static bool SameAnimation(VideoEffectAnimation? first, VideoEffectAnimation? second)
    {
        if (first is null || second is null) return first == second;
        return first.IsEnabled == second.IsEnabled && first.Keyframes.Count == second.Keyframes.Count &&
            first.Keyframes.Zip(second.Keyframes).All(pair => SameKeyframe(pair.First, pair.Second));

        static bool SameKeyframe(VideoEffectKeyframe left, VideoEffectKeyframe right) =>
            (left.Frame, left.Amount, left.SecondaryAmount, left.Interpolation, left.Smoothness) ==
            (right.Frame, right.Amount, right.SecondaryAmount, right.Interpolation, right.Smoothness) &&
            left.Parameters.Count == right.Parameters.Count &&
            left.Parameters.All(parameter => right.Parameters.TryGetValue(parameter.Key, out var value) && value == parameter.Value);
    }

    /// <summary>
    /// Evaluates the effect at the specified frame, interpolating keyframed parameters if animation is enabled.
    /// </summary>
    public VideoEffect EvaluateAt(long frame)
    {
        if (Animation is { IsEnabled: true, Keyframes.Count: > 0 })
        {
            var (evalAmount, evalSecondary, evalParams) = Animation.Evaluate(frame, this);
            var clone = DeepClone();
            clone.Amount = evalAmount;
            clone.SecondaryAmount = evalSecondary;
            clone.Parameters = evalParams;
            return clone;
        }
        return this;
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
        if (Kind != VideoEffectKind.ColorGrading && HSLCurves is not null)
            throw new InvalidDataException("Only a Color Grading effect can contain HSL Curves data.");
        if (!Enum.IsDefined(GradingSequence) ||
            (Kind != VideoEffectKind.ColorGrading && GradingSequence != ColorGradingSequence.Legacy))
            throw new InvalidDataException("Color grading processing sequence is invalid.");
        Lut?.Validate();
        LookLut?.Validate();
        Curves?.Validate();
        HSLCurves?.Validate();
        Animation?.Validate();
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
