using System.Globalization;
using System.Text.Json.Serialization;

namespace OpenRend.Core;

public sealed class CurvePoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double InHandleX { get; set; } = -0.125;
    public double InHandleY { get; set; } = 0.0;
    public double OutHandleX { get; set; } = 0.125;
    public double OutHandleY { get; set; } = 0.0;
    [JsonIgnore] public bool IsSelected { get; set; }

    public CurvePoint() { }

    public CurvePoint(double x, double y)
    {
        X = Math.Clamp(x, 0.0, 1.0);
        Y = Math.Clamp(y, 0.0, 1.0);
    }

    public CurvePoint(double x, double y, double inHx, double inHy, double outHx, double outHy)
    {
        X = Math.Clamp(x, 0.0, 1.0);
        Y = Math.Clamp(y, 0.0, 1.0);
        InHandleX = inHx;
        InHandleY = inHy;
        OutHandleX = outHx;
        OutHandleY = outHy;
    }

    public CurvePoint DeepClone() => (CurvePoint)MemberwiseClone();

    public void Validate()
    {
        if (!double.IsFinite(X) || X is < 0.0 or > 1.0 ||
            !double.IsFinite(Y) || Y is < 0.0 or > 1.0 ||
            !double.IsFinite(InHandleX) || !double.IsFinite(InHandleY) ||
            !double.IsFinite(OutHandleX) || !double.IsFinite(OutHandleY))
        {
            throw new InvalidDataException("Curve point coordinate is invalid or non-finite.");
        }
    }
}

public sealed class ColorCurve
{
    public List<CurvePoint> Points { get; set; } = [];

    public static ColorCurve CreateDefault() => new()
    {
        Points =
        [
            new CurvePoint(0.0, 0.0, -1.0 / 3.0, -1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0),
            new CurvePoint(1.0, 1.0, -1.0 / 3.0, -1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0)
        ]
    };

    public static ColorCurve Identity() => CreateDefault();

    public void SortPoints() => Points.Sort((a, b) => a.X.CompareTo(b.X));

    [JsonIgnore]
    public bool IsIdentity => Points.Count == 2 &&
        Math.Abs(Points[0].X - 0.0) < 1e-4 && Math.Abs(Points[0].Y - 0.0) < 1e-4 &&
        Math.Abs(Points[1].X - 1.0) < 1e-4 && Math.Abs(Points[1].Y - 1.0) < 1e-4 &&
        Math.Abs(Points[0].OutHandleY - Points[0].OutHandleX) < 1e-3 &&
        Math.Abs(Points[1].InHandleY - Points[1].InHandleX) < 1e-3;

    public ColorCurve DeepClone() => new()
    {
        Points = Points.Select(p => p.DeepClone()).ToList()
    };

    public void Validate()
    {
        if (Points == null || Points.Count < 2)
            throw new InvalidDataException("A color curve requires at least two control points.");

        for (var i = 0; i < Points.Count; i++)
        {
            Points[i].Validate();
            if (i > 0 && Points[i].X < Points[i - 1].X)
                throw new InvalidDataException("Curve points must be strictly monotonically ordered by X.");
        }
    }

    /// <summary>
    /// Evaluates the curve output Y for given input X in [0, 1].
    /// </summary>
    public double Evaluate(double x)
    {
        if (Points.Count == 0) return x;
        if (IsIdentity) return Math.Clamp(x, 0.0, 1.0);
        if (x <= Points[0].X) return Points[0].Y;
        if (x >= Points[^1].X) return Points[^1].Y;

        // Find the bounding segment
        var i = 0;
        while (i < Points.Count - 1 && Points[i + 1].X < x) i++;

        var p0 = Points[i];
        var p1 = Points[i + 1];

        var dx = p1.X - p0.X;
        if (dx <= 1e-7) return p0.Y;

        var c0x = p0.X + p0.OutHandleX;
        var c0y = p0.Y + p0.OutHandleY;
        var c1x = p1.X + p1.InHandleX;
        var c1y = p1.Y + p1.InHandleY;

        // Binary search for t in [0, 1] matching X(t) = x
        var low = 0.0;
        var high = 1.0;
        for (var step = 0; step < 16; step++)
        {
            var mid = (low + high) * 0.5;
            var midX = CubicBezier(mid, p0.X, c0x, c1x, p1.X);
            if (midX < x) low = mid;
            else high = mid;
        }

        var t = (low + high) * 0.5;
        var y = CubicBezier(t, p0.Y, c0y, c1y, p1.Y);
        return Math.Clamp(y, 0.0, 1.0);
    }

    private static double CubicBezier(double t, double p0, double p1, double p2, double p3)
    {
        var u = 1.0 - t;
        return u * u * u * p0 + 3.0 * u * u * t * p1 + 3.0 * u * t * t * p2 + t * t * t * p3;
    }

    /// <summary>
    /// Builds a fast 256-entry lookup table for evaluating 8-bit channel values.
    /// </summary>
    public byte[] BuildLut()
    {
        var lut = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var normalized = i / 255.0;
            var evaluated = Evaluate(normalized);
            lut[i] = (byte)Math.Clamp((int)Math.Round(evaluated * 255.0), 0, 255);
        }
        return lut;
    }

    /// <summary>
    /// Formats curve dots matching Vegas Pro format: "{count}:{x0}:{y0}:{inHx0}:{inHy0}:{outHx0}:{outHy0}:..."
    /// </summary>
    public string ToCurveString()
    {
        var parts = new List<string>(1 + Points.Count * 6) { Points.Count.ToString(CultureInfo.InvariantCulture) };
        foreach (var p in Points)
        {
            parts.Add(p.X.ToString("F5", CultureInfo.InvariantCulture));
            parts.Add(p.Y.ToString("F5", CultureInfo.InvariantCulture));
            parts.Add(p.InHandleX.ToString("F5", CultureInfo.InvariantCulture));
            parts.Add(p.InHandleY.ToString("F5", CultureInfo.InvariantCulture));
            parts.Add(p.OutHandleX.ToString("F5", CultureInfo.InvariantCulture));
            parts.Add(p.OutHandleY.ToString("F5", CultureInfo.InvariantCulture));
        }
        return string.Join(":", parts);
    }

    /// <summary>
    /// Parses curve dots matching Vegas Pro format.
    /// </summary>
    public static ColorCurve FromCurveString(string str)
    {
        if (string.IsNullOrWhiteSpace(str)) return CreateDefault();
        var parts = str.Split(':');
        if (parts.Length < 7) return CreateDefault();

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 2)
            return CreateDefault();

        var curve = new ColorCurve();
        var idx = 1;
        for (var i = 0; i < count && idx + 5 < parts.Length; i++)
        {
            double.TryParse(parts[idx++], NumberStyles.Float, CultureInfo.InvariantCulture, out var x);
            double.TryParse(parts[idx++], NumberStyles.Float, CultureInfo.InvariantCulture, out var y);
            double.TryParse(parts[idx++], NumberStyles.Float, CultureInfo.InvariantCulture, out var inHx);
            double.TryParse(parts[idx++], NumberStyles.Float, CultureInfo.InvariantCulture, out var inHy);
            double.TryParse(parts[idx++], NumberStyles.Float, CultureInfo.InvariantCulture, out var outHx);
            double.TryParse(parts[idx++], NumberStyles.Float, CultureInfo.InvariantCulture, out var outHy);

            curve.Points.Add(new CurvePoint(x, y, inHx, inHy, outHx, outHy));
        }

        curve.Points.Sort((a, b) => a.X.CompareTo(b.X));
        return curve;
    }
}

public sealed class ColorCurvesData
{
    public ColorCurve Master { get; set; } = ColorCurve.CreateDefault();
    public ColorCurve Red { get; set; } = ColorCurve.CreateDefault();
    public ColorCurve Green { get; set; } = ColorCurve.CreateDefault();
    public ColorCurve Blue { get; set; } = ColorCurve.CreateDefault();

    [JsonIgnore]
    public bool HasAdjustments => !Master.IsIdentity || !Red.IsIdentity || !Green.IsIdentity || !Blue.IsIdentity;

    public ColorCurvesData DeepClone() => new()
    {
        Master = Master.DeepClone(),
        Red = Red.DeepClone(),
        Green = Green.DeepClone(),
        Blue = Blue.DeepClone()
    };

    public void Validate()
    {
        Master?.Validate();
        Red?.Validate();
        Green?.Validate();
        Blue?.Validate();
    }
}
