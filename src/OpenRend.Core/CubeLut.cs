using System.Globalization;

namespace OpenRend.Core;

public sealed class CubeLut
{
    public const int MaximumSize = 65;
    public string Title { get; set; } = "";
    public int Size { get; set; }
    public double[] DomainMinimum { get; set; } = [0, 0, 0];
    public double[] DomainMaximum { get; set; } = [1, 1, 1];
    public double[] Samples { get; set; } = [];

    public CubeLut DeepClone() => new()
    {
        Title = Title, Size = Size, DomainMinimum = (double[])DomainMinimum.Clone(),
        DomainMaximum = (double[])DomainMaximum.Clone(), Samples = (double[])Samples.Clone()
    };

    public void Validate()
    {
        if (Title is null || Title.Length > 1024 || Size is < 2 or > MaximumSize ||
            DomainMinimum is not { Length: 3 } || DomainMaximum is not { Length: 3 } ||
            Samples is null || Samples.Length != Size * Size * Size * 3 || Samples.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("The 3D LUT is invalid or exceeds the supported size (2–65).");
        for (var channel = 0; channel < 3; channel++)
            if (!double.IsFinite(DomainMinimum[channel]) || !double.IsFinite(DomainMaximum[channel]) ||
                !double.IsFinite(DomainMaximum[channel] - DomainMinimum[channel]) || DomainMinimum[channel] >= DomainMaximum[channel])
                throw new InvalidDataException("Each LUT domain maximum must exceed its finite minimum.");
    }
}

public static class CubeLutParser
{
    public const int MaximumTextLength = 32 * 1024 * 1024;

    public static CubeLut Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumTextLength) throw new InvalidDataException("The LUT file is too large.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static CubeLut Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumTextLength) throw new InvalidDataException("The LUT text is too large.");
        var result = new CubeLut();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<double>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } raw)
        {
            if (raw.Length > 4096) throw new InvalidDataException("A LUT line is too long.");
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var directive = parts[0];
            if (directive is "TITLE" or "LUT_3D_SIZE" or "DOMAIN_MIN" or "DOMAIN_MAX")
            {
                if (values.Count != 0 || !seen.Add(directive)) throw new InvalidDataException("LUT headers must be unique and precede samples.");
                if (directive == "TITLE")
                {
                    var title = line[5..].Trim();
                    if (title.Length < 2 || title[0] != '"' || title[^1] != '"' || title[1..^1].Contains('"'))
                        throw new InvalidDataException("The LUT title must be quoted.");
                    result.Title = title[1..^1];
                }
                else if (directive == "LUT_3D_SIZE")
                {
                    if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size is < 2 or > CubeLut.MaximumSize)
                        throw new InvalidDataException("Only 3D LUT sizes from 2 through 65 are supported.");
                    result.Size = size;
                }
                else
                {
                    var triplet = Triplet(parts.Skip(1).ToArray());
                    if (directive == "DOMAIN_MIN") result.DomainMinimum = triplet;
                    else result.DomainMaximum = triplet;
                }
                continue;
            }
            if (result.Size == 0) throw new InvalidDataException("A LUT_3D_SIZE header must precede the samples; 1D and combined LUTs are unsupported.");
            if (values.Count >= result.Size * result.Size * result.Size * 3) throw new InvalidDataException("The LUT has too many samples.");
            values.AddRange(Triplet(parts));
        }
        result.Samples = values.ToArray();
        result.Validate();
        return result;
    }

    private static double[] Triplet(string[] parts)
    {
        if (parts.Length != 3) throw new InvalidDataException("Each LUT sample or domain must have three numbers.");
        var values = new double[3];
        for (var i = 0; i < 3; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]))
                throw new InvalidDataException("LUT values must be finite numbers.");
        return values;
    }

    private static string StripComment(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            if (line[i] == '#' && !quoted) return line[..i];
        }
        return line;
    }
}
