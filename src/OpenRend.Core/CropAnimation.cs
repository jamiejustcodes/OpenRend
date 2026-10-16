namespace OpenRend.Core;

public enum CropInterpolation { Linear, Hold }

/// <summary>A source-pixel rectangle rotated clockwise about a source-pixel pivot.</summary>
public readonly record struct CropState(double Width, double Height, double CenterX, double CenterY,
    double RotationDegrees, double PivotX, double PivotY)
{
    public static CropState Identity(int width, int height)
    {
        var w = Math.Max(1, width);
        var h = Math.Max(1, height);
        return new(w, h, w / 2.0, h / 2.0, 0, w / 2.0, h / 2.0);
    }

    public void Validate()
    {
        // Bounded source coordinates keep interpolation and render transforms finite.
        const double limit = 1_000_000_000;
        if (!double.IsFinite(Width) || !double.IsFinite(Height) || Width < 0.000001 || Height < 0.000001 || Width > limit || Height > limit ||
            !Bounded(CenterX) || !Bounded(CenterY) || !Bounded(RotationDegrees) || !Bounded(PivotX) || !Bounded(PivotY))
            throw new InvalidDataException("Crop geometry is invalid.");
        static bool Bounded(double value) => double.IsFinite(value) && Math.Abs(value) <= limit;
    }

    internal static CropState Lerp(CropState a, CropState b, double amount) => new(
        Mix(a.Width, b.Width, amount), Mix(a.Height, b.Height, amount),
        Mix(a.CenterX, b.CenterX, amount), Mix(a.CenterY, b.CenterY, amount),
        Mix(a.RotationDegrees, b.RotationDegrees, amount), Mix(a.PivotX, b.PivotX, amount), Mix(a.PivotY, b.PivotY, amount));

    private static double Mix(double a, double b, double amount) => a + (b - a) * amount;
}

public sealed class CropKeyframe
{
    public long Frame { get; set; }
    public CropState State { get; set; }
    // The left keyframe owns the interpolation of the following segment.
    public CropInterpolation Interpolation { get; set; }
    public CropKeyframe DeepClone() => (CropKeyframe)MemberwiseClone();
}

public sealed class CropAnimation
{
    public List<CropKeyframe> Keyframes { get; set; } = [];

    public CropAnimation DeepClone() => new() { Keyframes = Keyframes.Select(key => key.DeepClone()).ToList() };

    public void Validate()
    {
        if (Keyframes is null || Keyframes.Count == 0) throw new InvalidDataException("Crop keyframes are missing.");
        long previous = -1;
        foreach (var key in Keyframes)
        {
            if (key is null || key.Frame < 0 || key.Frame <= previous || !Enum.IsDefined(key.Interpolation)) throw new InvalidDataException("Crop keyframes must be unique and ordered.");
            key.State.Validate();
            previous = key.Frame;
        }
    }

    public CropState Evaluate(long frame)
    {
        if (Keyframes.Count == 0) throw new InvalidOperationException("Crop keyframes are missing.");
        if (frame <= Keyframes[0].Frame) return Keyframes[0].State;
        if (frame >= Keyframes[^1].Frame) return Keyframes[^1].State;
        var low = 0;
        var high = Keyframes.Count - 1;
        while (high - low > 1)
        {
            var middle = low + (high - low) / 2;
            if (Keyframes[middle].Frame <= frame) low = middle;
            else high = middle;
        }
        var left = Keyframes[low];
        var right = Keyframes[high];
        if (left.Interpolation == CropInterpolation.Hold) return left.State;
        // Subtract integer frames before converting, preserving precision on long projects.
        return CropState.Lerp(left.State, right.State, (double)(frame - left.Frame) / (right.Frame - left.Frame));
    }
}
