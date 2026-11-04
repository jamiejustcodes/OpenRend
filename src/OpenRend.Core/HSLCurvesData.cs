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
    LumVsSat = 4
}

public sealed class HSLCurvePoint
{
    public double X { get; set; }
    public double Y { get; set; } = 0.5; // 0.5 is neutral / unity
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
    }
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
    public bool IsIdentity => Points.Count == 0 || Points.All(p => Math.Abs(p.Y - 0.5) < 1e-3);

    public void SortPoints() => Points.Sort((a, b) => a.X.CompareTo(b.X));

    public HSLCurve DeepClone() => new()
    {
        Points = Points.Select(p => p.DeepClone()).ToList()
    };

    public void Validate()
    {
        if (Points == null || Points.Count < 2)
            throw new InvalidDataException("An HSL curve requires at least two control points.");

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
            if (span <= 1e-7) return p0.Y;
            var t = (x - p0.X) / span;
            // Smooth Hermite interpolation
            var s = t * t * (3.0 - 2.0 * t);
            return Math.Clamp(p0.Y + (p1.Y - p0.Y) * s, 0.0, 1.0);
        }

        // Periodic wrap-around for Hue: find two closest control points
        var n = Points.Count;
        int leftIdx = -1;
        for (var i = 0; i < n; i++)
        {
            if (Points[i].X <= x) leftIdx = i;
        }

        if (leftIdx < 0)
        {
            // Between last point (wrapped) and first point
            var pLast = Points[^1];
            var pFirst = Points[0];
            var span = (1.0 - pLast.X) + pFirst.X;
            if (span <= 1e-7) return pFirst.Y;
            var dist = (1.0 - pLast.X) + x;
            var t = dist / span;
            var s = t * t * (3.0 - 2.0 * t);
            return Math.Clamp(pLast.Y + (pFirst.Y - pLast.Y) * s, 0.0, 1.0);
        }

        if (leftIdx == n - 1)
        {
            // Between last point and first point (wrapped)
            var pLast = Points[^1];
            var pFirst = Points[0];
            var span = (1.0 - pLast.X) + pFirst.X;
            if (span <= 1e-7) return pLast.Y;
            var dist = x - pLast.X;
            var t = dist / span;
            var s = t * t * (3.0 - 2.0 * t);
            return Math.Clamp(pLast.Y + (pFirst.Y - pLast.Y) * s, 0.0, 1.0);
        }

        {
            var p0 = Points[leftIdx];
            var p1 = Points[leftIdx + 1];
            var span = p1.X - p0.X;
            if (span <= 1e-7) return p0.Y;
            var t = (x - p0.X) / span;
            var s = t * t * (3.0 - 2.0 * t);
            return Math.Clamp(p0.Y + (p1.Y - p0.Y) * s, 0.0, 1.0);
        }
    }

    /// <summary>
    /// Builds a precomputed lookup table for fast per-pixel grading evaluation.
    /// </summary>
    public double[] BuildLut(int steps = 360, bool isHuePeriodic = true)
    {
        var lut = new double[steps];
        for (var i = 0; i < steps; i++)
        {
            var normalized = i / (double)steps;
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
    public HSLCurve LumVsSat { get; set; } = HSLCurve.CreateDefaultLinear();

    [JsonIgnore]
    public bool HasAdjustments =>
        !HueVsHue.IsIdentity ||
        !HueVsSat.IsIdentity ||
        !HueVsLum.IsIdentity ||
        !SatVsSat.IsIdentity ||
        !LumVsSat.IsIdentity;

    public HSLCurvesData DeepClone() => new()
    {
        HueVsHue = HueVsHue.DeepClone(),
        HueVsSat = HueVsSat.DeepClone(),
        HueVsLum = HueVsLum.DeepClone(),
        SatVsSat = SatVsSat.DeepClone(),
        LumVsSat = LumVsSat.DeepClone()
    };

    public void Validate()
    {
        HueVsHue?.Validate();
        HueVsSat?.Validate();
        HueVsLum?.Validate();
        SatVsSat?.Validate();
        LumVsSat?.Validate();
    }
}

