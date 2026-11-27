using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace OpenRend.Core;

[JsonConverter(typeof(JsonStringEnumConverter<HSLCurveMode>))]
public enum HSLCurveMode
{
    HueVsHue = 0,
    HueVsSat = 1,
    HueVsLum = 2,
    SatVsSat = 3,
    LumVsSat = 4,
    SatVsLum = 5
}

public sealed class HSLCurvePoint
{
    public double X { get; set; }
    public double Y { get; set; } = 0.5; // 0.5 is neutral / unity
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CurveTangent? InTangent { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CurveTangent? OutTangent { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TangentsUnlinked { get; set; }
    [JsonIgnore] public bool IsSelected { get; set; }

    public HSLCurvePoint() { }

    public HSLCurvePoint(double x, double y)
    {
        X = Math.Clamp(x, 0.0, 1.0);
        Y = Math.Clamp(y, 0.0, 1.0);
    }

    public HSLCurvePoint DeepClone() => (HSLCurvePoint)MemberwiseClone();

    public void Validate()
    {
        if (!double.IsFinite(X) || X is < 0.0 or > 1.0 ||
            !double.IsFinite(Y) || Y is < 0.0 or > 1.0)
        {
            throw new InvalidDataException("HSL curve point coordinate is invalid or non-finite.");
        }
        if (!ValidTangent(InTangent, incoming: true) || !ValidTangent(OutTangent, incoming: false))
            throw new InvalidDataException("HSL curve tangent is invalid.");
    }

    private static bool ValidTangent(CurveTangent? tangent, bool incoming) => tangent is not { } offset ||
        double.IsFinite(offset.X) && double.IsFinite(offset.Y) && Math.Abs(offset.X) <= 1 && Math.Abs(offset.Y) <= 1 &&
        (incoming ? offset.X <= 0 : offset.X >= 0);
}

public sealed class HSLCurve
{
    public List<HSLCurvePoint> Points { get; set; } = [];

    public static HSLCurve CreateDefaultHue() => new()
    {
        Points =
        [
            new HSLCurvePoint(0.0 / 6.0, 0.5), // Red (0°)
            new HSLCurvePoint(1.0 / 6.0, 0.5), // Yellow (60°)
            new HSLCurvePoint(2.0 / 6.0, 0.5), // Green (120°)
            new HSLCurvePoint(3.0 / 6.0, 0.5), // Cyan (180°)
            new HSLCurvePoint(4.0 / 6.0, 0.5), // Blue (240°)
            new HSLCurvePoint(5.0 / 6.0, 0.5)  // Magenta (300°)
        ]
    };

    public static HSLCurve CreateDefaultLinear() => new()
    {
        Points =
        [
            new HSLCurvePoint(0.0, 0.5),
            new HSLCurvePoint(0.25, 0.5),
            new HSLCurvePoint(0.5, 0.5),
            new HSLCurvePoint(0.75, 0.5),
            new HSLCurvePoint(1.0, 0.5)
        ]
    };

    [JsonIgnore]
    public bool IsIdentity => Points.Count == 0 || Points.All(p => Math.Abs(p.Y - 0.5) < 1e-3 &&
        (p.InTangent?.Y ?? 0) == 0 && (p.OutTangent?.Y ?? 0) == 0);

    public void SortPoints() => Points.Sort((a, b) => a.X.CompareTo(b.X));

    public HSLCurve DeepClone() => new()
    {
        Points = Points.Select(p => p.DeepClone()).ToList()
    };

    public void Validate()
    {
        if (Points is null)
            throw new InvalidDataException("The HSL curve points are missing.");

        for (var i = 0; i < Points.Count; i++)
        {
            Points[i].Validate();
            if (i > 0 && Points[i].X < Points[i - 1].X)
                throw new InvalidDataException("HSL curve points must be ordered by X.");
        }
    }

    /// <summary>
    /// Evaluates curve output Y in [0, 1] for input X in [0, 1] with periodic wrap for hue curves.
    /// </summary>
    public double Evaluate(double x, bool isHuePeriodic = true)
    {
        if (Points.Count == 0) return 0.5;
        if (IsIdentity) return 0.5;
        if (Points.Count == 1) return Points[0].Y;

        x = Math.Clamp(x, 0.0, 1.0);

        if (!isHuePeriodic)
        {
            if (x <= Points[0].X) return Points[0].Y;
            if (x >= Points[^1].X) return Points[^1].Y;

            var i = 0;
            while (i < Points.Count - 1 && Points[i + 1].X < x) i++;
            var p0 = Points[i];
            var p1 = Points[i + 1];
            var span = p1.X - p0.X;
            return span <= 1e-7 ? p0.Y : EvaluateSegment(p0, p1, span, x - p0.X);
        }

        // Periodic wrap-around for Hue: find two closest control points
        var n = Points.Count;
        int leftIdx = -1;
        for (var i = 0; i < n; i++)
        {
            if (Points[i].X <= x) leftIdx = i;
        }

        if (leftIdx < 0 || leftIdx == n - 1)
        {
            // Measure the seam on one continuous turn of the hue axis.
            var pLast = Points[^1];
            var pFirst = Points[0];
            var span = (1.0 - pLast.X) + pFirst.X;
            if (span <= 1e-7) return leftIdx < 0 ? pFirst.Y : pLast.Y;
            var distance = leftIdx < 0 ? (1.0 - pLast.X) + x : x - pLast.X;
            return EvaluateSegment(pLast, pFirst, span, distance);
        }

        var left = Points[leftIdx];
        var right = Points[leftIdx + 1];
        var width = right.X - left.X;
        return width <= 1e-7 ? left.Y : EvaluateSegment(left, right, width, x - left.X);
    }

    private static double EvaluateSegment(HSLCurvePoint left, HSLCurvePoint right, double span, double distance)
    {
        if (left.OutTangent is null && right.InTangent is null)
        {
            // Preserve original interpolation for points without manual handles.
            var t = distance / span;
            var s = t * t * (3.0 - 2.0 * t);
            return Math.Clamp(left.Y + (right.Y - left.Y) * s, 0.0, 1.0);
        }

        var outgoing = left.OutTangent ?? new CurveTangent(span / 3, 0);
        var incoming = right.InTangent ?? new CurveTangent(-span / 3, 0);
        // Point moves can shorten a segment after its handles were stored.
        outgoing = outgoing with { X = Math.Clamp(outgoing.X, 0, span) };
        incoming = incoming with { X = Math.Clamp(incoming.X, -span, 0) };
        return ColorCurve.EvaluateBezierSegment(distance, 0, left.Y, span, right.Y, outgoing, incoming);
    }

    /// <summary>
    /// Builds a precomputed lookup table for fast per-pixel grading evaluation.
    /// </summary>
    public double[] BuildLut(int steps = 360, bool isHuePeriodic = true, bool includeEndpoint = false)
    {
        var lut = new double[steps];
        var intervals = includeEndpoint ? Math.Max(1, steps - 1) : steps;
        for (var i = 0; i < steps; i++)
        {
            var normalized = i / (double)intervals;
            lut[i] = Evaluate(normalized, isHuePeriodic);
        }
        return lut;
    }
}

public sealed class HSLCurvesData
{
    public HSLCurve HueVsHue { get; set; } = HSLCurve.CreateDefaultHue();
    public HSLCurve HueVsSat { get; set; } = HSLCurve.CreateDefaultHue();
    public HSLCurve HueVsLum { get; set; } = HSLCurve.CreateDefaultHue();
    public HSLCurve SatVsSat { get; set; } = HSLCurve.CreateDefaultLinear();
    public HSLCurve SatVsLum { get; set; } = new();
    public HSLCurve LumVsSat { get; set; } = HSLCurve.CreateDefaultLinear();

    [JsonIgnore]
    public bool HasAdjustments =>
        !HueVsHue.IsIdentity ||
        !HueVsSat.IsIdentity ||
        !HueVsLum.IsIdentity ||
        !SatVsSat.IsIdentity ||
        !SatVsLum.IsIdentity ||
        !LumVsSat.IsIdentity;

    public HSLCurvesData DeepClone() => new()
    {
        HueVsHue = HueVsHue.DeepClone(),
        HueVsSat = HueVsSat.DeepClone(),
        HueVsLum = HueVsLum.DeepClone(),
        SatVsSat = SatVsSat.DeepClone(),
        SatVsLum = SatVsLum.DeepClone(),
        LumVsSat = LumVsSat.DeepClone()
    };

    public bool ContentEquals(HSLCurvesData? other) => other is not null &&
        SameCurve(HueVsHue, other.HueVsHue) && SameCurve(HueVsSat, other.HueVsSat) &&
        SameCurve(HueVsLum, other.HueVsLum) && SameCurve(SatVsSat, other.SatVsSat) &&
        SameCurve(SatVsLum, other.SatVsLum) && SameCurve(LumVsSat, other.LumVsSat);

    private static bool SameCurve(HSLCurve left, HSLCurve right) => left.Points.Count == right.Points.Count &&
        left.Points.Zip(right.Points).All(pair =>
            (pair.First.X, pair.First.Y, pair.First.InTangent, pair.First.OutTangent, pair.First.TangentsUnlinked) ==
            (pair.Second.X, pair.Second.Y, pair.Second.InTangent, pair.Second.OutTangent, pair.Second.TangentsUnlinked));

    public void Validate()
    {
        HueVsHue?.Validate();
        HueVsSat?.Validate();
        HueVsLum?.Validate();
        SatVsSat?.Validate();
        SatVsLum?.Validate();
        LumVsSat?.Validate();
    }
}

