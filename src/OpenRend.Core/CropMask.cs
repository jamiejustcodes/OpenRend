using System.Globalization;

namespace OpenRend.Core;

public enum MaskMode { Positive, Negative }

public enum MaskFeatherType { None, In, Out, Both }

/// <summary>
/// A Bézier anchor point with optional incoming and outgoing directional tangent vectors in source-pixel coordinates.
/// </summary>
public sealed class MaskPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double InTangentX { get; set; }
    public double InTangentY { get; set; }
    public double OutTangentX { get; set; }
    public double OutTangentY { get; set; }
    public bool IsSplit { get; set; }

    public MaskPoint() { }

    public MaskPoint(double x, double y, double inX = 0, double inY = 0, double outX = 0, double outY = 0, bool isSplit = false)
    {
        X = x;
        Y = y;
        InTangentX = inX;
        InTangentY = inY;
        OutTangentX = outX;
        OutTangentY = outY;
        IsSplit = isSplit;
    }

    public MaskPoint DeepClone() => (MaskPoint)MemberwiseClone();

    public static MaskPoint Lerp(MaskPoint a, MaskPoint b, double t) => new(
        a.X + (b.X - a.X) * t,
        a.Y + (b.Y - a.Y) * t,
        a.InTangentX + (b.InTangentX - a.InTangentX) * t,
        a.InTangentY + (b.InTangentY - a.InTangentY) * t,
        a.OutTangentX + (b.OutTangentX - a.OutTangentX) * t,
        a.OutTangentY + (b.OutTangentY - a.OutTangentY) * t,
        t >= 0.5 ? b.IsSplit : a.IsSplit);
}

/// <summary>
/// A closed or open Bézier path consisting of anchor points, compositing mode, and edge feathering.
/// </summary>
public sealed class MaskPath
{
    public List<MaskPoint> Points { get; set; } = [];
    public bool IsClosed { get; set; } = true;
    public MaskMode Mode { get; set; } = MaskMode.Positive;
    public MaskFeatherType FeatherType { get; set; } = MaskFeatherType.None;
    public double Feather { get; set; } // 0.0 to 100.0% of frame radius
    public double Opacity { get; set; } = 100.0; // 0.0 to 100.0%
    public bool AntiAlias { get; set; } = true;

    public MaskPath DeepClone() => new()
    {
        Points = Points.Select(p => p.DeepClone()).ToList(),
        IsClosed = IsClosed,
        Mode = Mode,
        FeatherType = FeatherType,
        Feather = Feather,
        Opacity = Opacity,
        AntiAlias = AntiAlias
    };

    public void Validate()
    {
        const double limit = 1_000_000_000;
        if (Points == null) throw new InvalidDataException("Mask path points are missing.");
        if (!double.IsFinite(Feather) || Feather < 0 || Feather > limit) throw new InvalidDataException("Feather must be finite and positive.");
        if (!double.IsFinite(Opacity) || Opacity is < 0 or > 100) throw new InvalidDataException("Opacity must be between 0 and 100.");
        foreach (var pt in Points)
        {
            if (!double.IsFinite(pt.X) || !double.IsFinite(pt.Y) ||
                !double.IsFinite(pt.InTangentX) || !double.IsFinite(pt.InTangentY) ||
                !double.IsFinite(pt.OutTangentX) || !double.IsFinite(pt.OutTangentY))
                throw new InvalidDataException("Mask point coordinates must be finite.");
        }
    }

    public static MaskPath Lerp(MaskPath a, MaskPath b, double t)
    {
        var res = new MaskPath
        {
            IsClosed = t >= 0.5 ? b.IsClosed : a.IsClosed,
            Mode = t >= 0.5 ? b.Mode : a.Mode,
            FeatherType = t >= 0.5 ? b.FeatherType : a.FeatherType,
            Feather = a.Feather + (b.Feather - a.Feather) * t,
            Opacity = a.Opacity + (b.Opacity - a.Opacity) * t,
            AntiAlias = t >= 0.5 ? b.AntiAlias : a.AntiAlias
        };

        var count = Math.Max(a.Points.Count, b.Points.Count);
        for (var i = 0; i < count; i++)
        {
            var pA = i < a.Points.Count ? a.Points[i] : (b.Points.Count > 0 ? b.Points[^1] : new MaskPoint());
            var pB = i < b.Points.Count ? b.Points[i] : (a.Points.Count > 0 ? a.Points[^1] : new MaskPoint());
            res.Points.Add(MaskPoint.Lerp(pA, pB, t));
        }
        return res;
    }
}

/// <summary>
/// A collection of Bézier mask paths composing the spatial matte for an event.
/// </summary>
public sealed class MaskShape
{
    public List<MaskPath> Paths { get; set; } = [];

    public MaskShape DeepClone() => new() { Paths = Paths.Select(p => p.DeepClone()).ToList() };

    public void Validate()
    {
        if (Paths == null) throw new InvalidDataException("Mask paths are missing.");
        foreach (var p in Paths) p.Validate();
    }

    public static MaskShape Lerp(MaskShape a, MaskShape b, double t)
    {
        var res = new MaskShape();
        var maxPaths = Math.Max(a.Paths.Count, b.Paths.Count);
        for (var i = 0; i < maxPaths; i++)
        {
            if (i < a.Paths.Count && i < b.Paths.Count)
            {
                res.Paths.Add(MaskPath.Lerp(a.Paths[i], b.Paths[i], t));
            }
            else if (i < a.Paths.Count)
            {
                res.Paths.Add(a.Paths[i].DeepClone());
            }
            else
            {
                res.Paths.Add(b.Paths[i].DeepClone());
            }
        }
        return res;
    }

    public static MaskShape CreateRectangle(double x, double y, double width, double height)
    {
        var shape = new MaskShape();
        var path = new MaskPath { IsClosed = true };
        path.Points.Add(new MaskPoint(x, y));
        path.Points.Add(new MaskPoint(x + width, y));
        path.Points.Add(new MaskPoint(x + width, y + height));
        path.Points.Add(new MaskPoint(x, y + height));
        shape.Paths.Add(path);
        return shape;
    }

    public static MaskShape CreateOval(double cx, double cy, double rx, double ry)
    {
        // Optimal cubic Bézier kappa for circle/ellipse approximation: 4/3 * (sqrt(2) - 1) ≈ 0.5522847498
        const double k = 0.5522847498307936;
        var dx = rx * k;
        var dy = ry * k;

        var shape = new MaskShape();
        var path = new MaskPath { IsClosed = true };
        path.Points.Add(new MaskPoint(cx, cy - ry, -dx, 0, dx, 0));
        path.Points.Add(new MaskPoint(cx + rx, cy, 0, -dy, 0, dy));
        path.Points.Add(new MaskPoint(cx, cy + ry, dx, 0, -dx, 0));
        path.Points.Add(new MaskPoint(cx - rx, cy, 0, dy, 0, -dy));
        shape.Paths.Add(path);
        return shape;
    }
}

public sealed class MaskKeyframe
{
    public long Frame { get; set; }
    public MaskShape Shape { get; set; } = new();
    public CropInterpolation Interpolation { get; set; } = CropInterpolation.Linear;
    public double Smoothness { get; set; } = 100.0;
    public MaskKeyframe DeepClone() => new()
    {
        Frame = Frame,
        Shape = Shape.DeepClone(),
        Interpolation = Interpolation,
        Smoothness = Smoothness
    };
}

public sealed class MaskAnimation
{
    public bool IsEnabled { get; set; } = true;
    public List<MaskKeyframe> Keyframes { get; set; } = [];

    public MaskAnimation DeepClone() => new()
    {
        IsEnabled = IsEnabled,
        Keyframes = Keyframes.Select(k => k.DeepClone()).ToList()
    };

    public void Validate()
    {
        if (Keyframes is null || Keyframes.Count == 0) throw new InvalidDataException("Mask keyframes are missing.");
        long previous = -1;
        foreach (var key in Keyframes)
        {
            if (key is null || key.Frame < 0 || key.Frame <= previous || !Enum.IsDefined(key.Interpolation))
                throw new InvalidDataException("Mask keyframes must be unique and ordered.");
            if (!double.IsFinite(key.Smoothness) || key.Smoothness is < 0 or > 100)
                throw new InvalidDataException("Mask keyframe smoothness must be between 0 and 100.");
            key.Shape.Validate();
            previous = key.Frame;
        }
    }

    public MaskShape Evaluate(long frame)
    {
        if (Keyframes.Count == 0) return new MaskShape();
        if (frame <= Keyframes[0].Frame) return Keyframes[0].Shape;
        if (frame >= Keyframes[^1].Frame) return Keyframes[^1].Shape;

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
        if (left.Interpolation == CropInterpolation.Hold) return left.Shape;

        var progress = (double)(frame - left.Frame) / (right.Frame - left.Frame);
        var centered = progress - 0.5;
        var s = Math.Clamp(left.Smoothness / 100.0, 0.0, 1.0);
        var weight = left.Interpolation switch
        {
            CropInterpolation.Slow => progress * progress,
            CropInterpolation.Fast => 1.0 - (1.0 - progress) * (1.0 - progress),
            CropInterpolation.Smooth => progress * (1.0 - s) + (progress * progress * (3.0 - 2.0 * progress)) * s,
            CropInterpolation.Sharp => 0.5 + 4.0 * centered * centered * centered,
            _ => progress
        };

        return MaskShape.Lerp(left.Shape, right.Shape, weight);
    }
}
