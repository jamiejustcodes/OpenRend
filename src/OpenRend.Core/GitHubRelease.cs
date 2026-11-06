using System.Text.Json.Serialization;

namespace OpenRend.Core;

/// <summary>
/// Represents a GitHub release object returned by GitHub REST API.
/// </summary>
public sealed record GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; init; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("body")]
    public string? Body { get; init; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; init; } = "";

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; init; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; init; }

    [JsonPropertyName("draft")]
    public bool Draft { get; init; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset> Assets { get; init; } = [];

    [JsonIgnore]
    public SemanticVersion? ParsedVersion =>
        SemanticVersion.TryParse(TagName, out var tagVer)
            ? tagVer
            : (Name != null && SemanticVersion.TryParse(Name, out var nameVer) ? nameVer : null);

    /// <summary>
    /// Finds the best asset for updating the Windows application.
    /// Prefers the Inno Setup executable (.exe) installer, falling back to the win-x64 zip bundle.
    /// </summary>
    public GitHubReleaseAsset? GetUpdateAsset()
    {
        // 1. Prefer Inno Setup installer executable (.exe)
        var exeAsset = Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (exeAsset is not null) return exeAsset;

        // 2. Fall back to win-x64 zip archive
        var zipAsset = Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        return zipAsset;
    }
}

/// <summary>
/// Represents an asset attached to a GitHub release.
/// </summary>
public sealed record GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; init; } = "";

    [JsonPropertyName("content_type")]
    public string? ContentType { get; init; }
}

