using System.Drawing;
using OpenRend.Core;

namespace OpenRend.Media;

public static class WhiteBalanceSampler
{
    public const string TemperatureKey = "TemperatureAdjust";
    public const string TintKey = "TintAdjust";
    public const double TemperatureLimit = 4000;
    public const double TintLimit = 100;

    public readonly record struct Adjustment(double Temperature, double Tint)
    {
        public static Adjustment FromGrade(VideoEffect grade) =>
            new(grade.GetParameter(TemperatureKey), grade.GetParameter(TintKey));

        public void ApplyTo(VideoEffect grade)
        {
            grade.SetParameter(TemperatureKey, Temperature);
            grade.SetParameter(TintKey, Tint);
        }
    }

    public enum SampleStatus { Adjusted, AlreadyNeutral, TooDark, NoImprovement }

    public readonly record struct Result(SampleStatus Status, Adjustment Adjustment);

    private readonly record struct Color(double Red, double Green, double Blue)
    {
        public double RedBlue => Red - Blue;
        public double GreenMagenta => Green - (Red + Blue) / 2;
        public double Error => RedBlue * RedBlue + GreenMagenta * GreenMagenta;
    }

    private readonly record struct Trial(double Temperature, double Tint, Color Color)
    {
        public Adjustment Adjustment => new(Temperature * TemperatureLimit, Tint * TintLimit);
    }

    public static Result Solve(
        Adjustment current, byte[] displayedFrame, int width, int height, Rectangle area,
        Func<Adjustment, byte[]> render, CancellationToken cancellationToken = default)
    {
        var color = Measure(displayedFrame, width, height, area);
        if (Math.Max(color.Red, Math.Max(color.Green, color.Blue)) < 8.0 / 255)
            return new(SampleStatus.TooDark, current);
        const double toleranceSquared = 4.0 / (255 * 255);
        if (color.Error <= toleranceSquared) return new(SampleStatus.AlreadyNeutral, current);

        var best = new Trial(current.Temperature / TemperatureLimit, current.Tint / TintLimit, color);
        for (var iteration = 0; iteration < 3 && best.Color.Error > toleranceSquared; iteration++)
        {
            var baseline = best;
            const double step = .15;
            var tempStep = baseline.Temperature + step <= 1 ? step : -step;
            var tintStep = baseline.Tint + step <= 1 ? step : -step;
            var temperature = Evaluate(baseline.Temperature + tempStep, baseline.Tint);
            var tint = Evaluate(baseline.Temperature, baseline.Tint + tintStep);
            Consider(temperature);
            Consider(tint);

            // Measure how the actual composite responds, including the effects before and after this grade.
            var a = (temperature.Color.RedBlue - baseline.Color.RedBlue) / tempStep;
            var b = (tint.Color.RedBlue - baseline.Color.RedBlue) / tintStep;
            var c = (temperature.Color.GreenMagenta - baseline.Color.GreenMagenta) / tempStep;
            var d = (tint.Color.GreenMagenta - baseline.Color.GreenMagenta) / tintStep;
            var determinant = a * d - b * c;
            if (Math.Abs(determinant) > 1e-6 && best.Color.Error > toleranceSquared)
            {
                var x = baseline.Temperature + (b * baseline.Color.GreenMagenta - d * baseline.Color.RedBlue) / determinant;
                var y = baseline.Tint + (c * baseline.Color.RedBlue - a * baseline.Color.GreenMagenta) / determinant;
                Consider(Evaluate(Math.Clamp(x, -1, 1), Math.Clamp(y, -1, 1)));
            }

            if (best == baseline) break;
        }

        return best.Color.Error < color.Error
            ? new(SampleStatus.Adjusted, best.Adjustment)
            : new(SampleStatus.NoImprovement, current);

        Trial Evaluate(double temperature, double tint)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trial = new Trial(temperature, tint, default);
            return trial with { Color = Measure(render(trial.Adjustment), width, height, area) };
        }

        void Consider(Trial trial)
        {
            if (trial.Color.Error < best.Color.Error) best = trial;
        }
    }

    private static Color Measure(byte[] pixels, int width, int height, Rectangle area)
    {
        if (area.Width <= 0 || area.Height <= 0 || area.X < 0 || area.Y < 0 ||
            area.Width > width - area.X || area.Height > height - area.Y ||
            pixels.Length != checked(width * height * 4))
            throw new ArgumentOutOfRangeException(nameof(area));

        long red = 0, green = 0, blue = 0;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            var offset = (y * width + area.Left) * 4;
            for (var x = area.Left; x < area.Right; x++, offset += 4)
            {
                blue += pixels[offset];
                green += pixels[offset + 1];
                red += pixels[offset + 2];
            }
        }
        var scale = 255.0 * area.Width * area.Height;
        return new(red / scale, green / scale, blue / scale);
    }
}
