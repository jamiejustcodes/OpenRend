using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace OpenRend.Core;

/// <summary>
/// Semantic version representation (Major.Minor.Patch-PreRelease+BuildMetadata)
/// supporting semantic comparison and release versioning.
/// </summary>
public readonly record struct SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    string? PreRelease = null,
    string? BuildMetadata = null) : IComparable<SemanticVersion>
{
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var span = text.Trim();
        if (span.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            span = span[1..];

        string? buildMetadata = null;
        var plusIndex = span.IndexOf('+');
        if (plusIndex >= 0)
        {
            buildMetadata = span[(plusIndex + 1)..];
            span = span[..plusIndex];
        }

        string? preRelease = null;
        var dashIndex = span.IndexOf('-');
        if (dashIndex >= 0)
        {
            preRelease = span[(dashIndex + 1)..];
            span = span[..dashIndex];
        }

        var parts = span.Split('.');
        if (parts.Length is < 1 or > 4) return false;

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) || major < 0)
            return false;

        var minor = 0;
        if (parts.Length > 1 && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor) || minor < 0))
            return false;

        var patch = 0;
        if (parts.Length > 2 && (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch) || patch < 0))
            return false;

        version = new SemanticVersion(major, minor, patch, preRelease, buildMetadata);
        return true;
    }

    public static SemanticVersion Parse(string text)
    {
        if (TryParse(text, out var version)) return version;
        throw new FormatException($"'{text}' is not a valid semantic version.");
    }

    public int CompareTo(SemanticVersion other)
    {
        var majorComp = Major.CompareTo(other.Major);
        if (majorComp != 0) return majorComp;

        var minorComp = Minor.CompareTo(other.Minor);
        if (minorComp != 0) return minorComp;

        var patchComp = Patch.CompareTo(other.Patch);
        if (patchComp != 0) return patchComp;

        // Semantic Versioning 2.0.0 rule: A normal version has greater precedence than a pre-release version
        var hasPre = !string.IsNullOrEmpty(PreRelease);
        var otherHasPre = !string.IsNullOrEmpty(other.PreRelease);

        if (!hasPre && otherHasPre) return 1;
        if (hasPre && !otherHasPre) return -1;
        if (hasPre && otherHasPre)
            return string.Compare(PreRelease, other.PreRelease, StringComparison.OrdinalIgnoreCase);

        return 0;
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public bool IsNewerThan(SemanticVersion other) => this > other;

    public override string ToString()
    {
        var result = $"{Major}.{Minor}.{Patch}";
        if (!string.IsNullOrEmpty(PreRelease)) result += $"-{PreRelease}";
        if (!string.IsNullOrEmpty(BuildMetadata)) result += $"+{BuildMetadata}";
        return result;
    }
}

