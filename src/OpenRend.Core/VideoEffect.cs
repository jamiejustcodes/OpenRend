namespace OpenRend.Core;

public enum VideoEffectKind { BrightnessContrast, Saturation, Invert }

public sealed class VideoEffect
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public VideoEffectKind Kind { get; set; }
    public bool IsEnabled { get; set; } = true;
    public double Amount { get; set; }
    public double SecondaryAmount { get; set; }

    public static VideoEffect Create(VideoEffectKind kind)
    {
        var effect = new VideoEffect { Kind = kind, Amount = kind == VideoEffectKind.BrightnessContrast ? 0 : 1 };
        effect.Validate();
        return effect;
    }

    public VideoEffect DeepClone() => (VideoEffect)MemberwiseClone();

    public void Validate()
    {
        if (Id == Guid.Empty || !Enum.IsDefined(Kind) || !double.IsFinite(Amount) || !double.IsFinite(SecondaryAmount))
            throw new InvalidDataException("Video effect is invalid.");
        var valid = Kind switch
        {
            VideoEffectKind.BrightnessContrast => Amount is >= -1 and <= 1 && SecondaryAmount is >= -1 and <= 1,
            VideoEffectKind.Saturation => Amount is >= 0 and <= 2 && SecondaryAmount == 0,
            VideoEffectKind.Invert => Amount is >= 0 and <= 1 && SecondaryAmount == 0,
            _ => false
        };
        if (!valid) throw new InvalidDataException("Video effect parameters are outside their supported range.");
    }
}
