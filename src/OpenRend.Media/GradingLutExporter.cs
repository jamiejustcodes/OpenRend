using System.Globalization;
using OpenRend.Core;

namespace OpenRend.Media;

public static class GradingLutExporter
{
    public static CubeLut Bake(VideoEffect grade, long frame, int size)
    {
        ArgumentNullException.ThrowIfNull(grade);
        if (grade.Kind != VideoEffectKind.ColorGrading)
            throw new ArgumentException("Select a color grading effect.", nameof(grade));
        if (size is < 2 or > CubeLut.MaximumSize)
            throw new ArgumentOutOfRangeException(nameof(size));
        grade.Validate();
        var evaluated = grade.EvaluateAt(frame);
        var sample = evaluated.IsEnabled
            ? VideoEffectProcessor.CreateGradeLutSampler(evaluated)
            : new Func<double, double, double, (double, double, double)>((r, g, b) => (r, g, b));
        var scale = size - 1.0;
        var samples = new double[size * size * size * 3];
        var index = 0;
        for (var blue = 0; blue < size; blue++)
        for (var green = 0; green < size; green++)
        for (var red = 0; red < size; red++)
        {
            var (r, g, b) = sample(red / scale, green / scale, blue / scale);
            samples[index++] = r;
            samples[index++] = g;
            samples[index++] = b;
        }
        return new() { Size = size, Samples = samples };
    }

    public static void Save(VideoEffect grade, long frame, int size, string path)
    {
        var output = Path.GetFullPath(path);
        var lut = Bake(grade, frame, size);
        var stagingPath = Path.Combine(Path.GetDirectoryName(output)!, $".openrend-lut-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var writer = new StreamWriter(new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write)))
            {
                writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"LUT_3D_SIZE {lut.Size}"));
                for (var i = 0; i < lut.Samples.Length; i += 3)
                    writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{lut.Samples[i]:R} {lut.Samples[i + 1]:R} {lut.Samples[i + 2]:R}"));
            }
            File.Move(stagingPath, output, overwrite: true);
        }
        finally
        {
            if (File.Exists(stagingPath)) File.Delete(stagingPath);
        }
    }
}
