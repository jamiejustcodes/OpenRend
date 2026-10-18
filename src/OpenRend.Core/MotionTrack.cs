using System.Text.Json.Serialization;

namespace OpenRend.Core;

public enum TrackingModel
{
    Translation,
    RigidMotion,
    Similarity,
    Affine,
    Homography
}

public enum TrackingAlgorithm
{
    LucasKanade,
    TemplateMatching,
    Hybrid
}

public enum TrackingPrecision
{
    High,
    Medium,
    Low,
    Fast
}

public enum TrackingShape
{
    Rectangle,
    Oval,
    Bezier
}

public readonly record struct Point2D(double X, double Y)
{
    public static Point2D Zero => new(0, 0);

    public static Point2D operator +(Point2D a, Point2D b) => new(a.X + b.X, a.Y + b.Y);
    public static Point2D operator -(Point2D a, Point2D b) => new(a.X - b.X, a.Y - b.Y);
    public static Point2D operator *(Point2D a, double scalar) => new(a.X * scalar, a.Y * scalar);
    public static Point2D operator /(Point2D a, double scalar) => scalar == 0 ? Zero : new(a.X / scalar, a.Y / scalar);

    public double DistanceTo(Point2D other) => Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));

    public Point2D Rotate(double angleDegrees, Point2D origin)
    {
        var rad = angleDegrees * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var dx = X - origin.X;
        var dy = Y - origin.Y;
        return new Point2D(origin.X + dx * cos - dy * sin, origin.Y + dx * sin + dy * cos);
    }

    public Point2D Clamp(double min = 0.0, double max = 1.0) =>
        new(Math.Clamp(X, min, max), Math.Clamp(Y, min, max));
}

public sealed class TrackingKeyframe
{
    public long ProjectFrame { get; set; }
    public double CenterX { get; set; } = 0.5;
    public double CenterY { get; set; } = 0.5;
    public double Width { get; set; } = 0.2;
    public double Height { get; set; } = 0.2;
    public double Angle { get; set; }
    public double ScaleX { get; set; } = 1.0;
    public double ScaleY { get; set; } = 1.0;
    public double Shear { get; set; }
    public Point2D CornerTL { get; set; } = new(0.4, 0.4);
    public Point2D CornerTR { get; set; } = new(0.6, 0.4);
    public Point2D CornerBR { get; set; } = new(0.6, 0.6);
    public Point2D CornerBL { get; set; } = new(0.4, 0.6);
    public double Confidence { get; set; } = 1.0;

    public TrackingKeyframe() { }

    public TrackingKeyframe(long projectFrame, double cx, double cy, double w, double h, double angle = 0, double scaleX = 1, double scaleY = 1)
    {
        ProjectFrame = projectFrame;
        CenterX = cx;
        CenterY = cy;
        Width = w;
        Height = h;
        Angle = angle;
        ScaleX = scaleX;
        ScaleY = scaleY;
        CalculateCornersFromCenterAndSize();
    }

    public void CalculateCornersFromCenterAndSize()
    {
        var hw = Width * 0.5 * ScaleX;
        var hh = Height * 0.5 * ScaleY;
        var center = new Point2D(CenterX, CenterY);

        CornerTL = new Point2D(CenterX - hw, CenterY - hh).Rotate(Angle, center);
        CornerTR = new Point2D(CenterX + hw, CenterY - hh).Rotate(Angle, center);
        CornerBR = new Point2D(CenterX + hw, CenterY + hh).Rotate(Angle, center);
        CornerBL = new Point2D(CenterX - hw, CenterY + hh).Rotate(Angle, center);
    }

    public TrackingKeyframe DeepClone() => (TrackingKeyframe)MemberwiseClone();

    public void Validate()
    {
        if (ProjectFrame < 0) throw new InvalidDataException("Keyframe ProjectFrame must be non-negative.");
        if (!double.IsFinite(CenterX) || !double.IsFinite(CenterY) || !double.IsFinite(Width) || !double.IsFinite(Height) ||
            !double.IsFinite(Angle) || !double.IsFinite(ScaleX) || !double.IsFinite(ScaleY) || !double.IsFinite(Confidence))
        {
            throw new InvalidDataException("Keyframe values must be finite numbers.");
        }
    }
}

public sealed class MotionTrackData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Region 1";
    public bool IsEnabled { get; set; } = true;
    public bool IsInverted { get; set; }
    public TrackingShape Shape { get; set; } = TrackingShape.Rectangle;
    public TrackingModel Model { get; set; } = TrackingModel.Similarity;
    public TrackingAlgorithm Algorithm { get; set; } = TrackingAlgorithm.Hybrid;
    public TrackingPrecision Precision { get; set; } = TrackingPrecision.High;
    public List<CurvePoint> BezierPoints { get; set; } = [];
    public List<TrackingKeyframe> Keyframes { get; set; } = [];

    public MotionTrackData DeepClone() => new()
    {
        Id = Id,
        Name = Name,
        IsEnabled = IsEnabled,
        IsInverted = IsInverted,
        Shape = Shape,
        Model = Model,
        Algorithm = Algorithm,
        Precision = Precision,
        BezierPoints = BezierPoints.Select(p => p.DeepClone()).ToList(),
        Keyframes = Keyframes.Select(k => k.DeepClone()).ToList()
    };

    public void SortKeyframes() => Keyframes.Sort((a, b) => a.ProjectFrame.CompareTo(b.ProjectFrame));

    public TrackingKeyframe? Evaluate(long projectFrame)
    {
        if (Keyframes.Count == 0) return null;
        if (Keyframes.Count == 1 || projectFrame <= Keyframes[0].ProjectFrame)
            return Keyframes[0].DeepClone();
        if (projectFrame >= Keyframes[^1].ProjectFrame)
            return Keyframes[^1].DeepClone();

        var idx = 0;
        while (idx < Keyframes.Count - 1 && Keyframes[idx + 1].ProjectFrame < projectFrame)
            idx++;

        var k0 = Keyframes[idx];
        var k1 = Keyframes[idx + 1];

        if (k0.ProjectFrame == projectFrame) return k0.DeepClone();
        if (k1.ProjectFrame == projectFrame) return k1.DeepClone();

        var span = (double)(k1.ProjectFrame - k0.ProjectFrame);
        if (span <= 0) return k0.DeepClone();

        var t = (projectFrame - k0.ProjectFrame) / span;

        // Smooth cosine ease for natural motion interpolation
        var s = (1.0 - Math.Cos(t * Math.PI)) * 0.5;

        static double Lerp(double a, double b, double factor) => a + (b - a) * factor;
        static Point2D LerpPt(Point2D a, Point2D b, double factor) => new(Lerp(a.X, b.X, factor), Lerp(a.Y, b.Y, factor));

        return new TrackingKeyframe
        {
            ProjectFrame = projectFrame,
            CenterX = Lerp(k0.CenterX, k1.CenterX, s),
            CenterY = Lerp(k0.CenterY, k1.CenterY, s),
            Width = Lerp(k0.Width, k1.Width, s),
            Height = Lerp(k0.Height, k1.Height, s),
            Angle = Lerp(k0.Angle, k1.Angle, s),
            ScaleX = Lerp(k0.ScaleX, k1.ScaleX, s),
            ScaleY = Lerp(k0.ScaleY, k1.ScaleY, s),
            Shear = Lerp(k0.Shear, k1.Shear, s),
            CornerTL = LerpPt(k0.CornerTL, k1.CornerTL, s),
            CornerTR = LerpPt(k0.CornerTR, k1.CornerTR, s),
            CornerBR = LerpPt(k0.CornerBR, k1.CornerBR, s),
            CornerBL = LerpPt(k0.CornerBL, k1.CornerBL, s),
            Confidence = Math.Min(k0.Confidence, k1.Confidence)
        };
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Motion track region name cannot be empty.");
        foreach (var kf in Keyframes)
        {
            kf.Validate();
        }
        foreach (var bp in BezierPoints)
        {
            bp.Validate();
        }
    }
}
