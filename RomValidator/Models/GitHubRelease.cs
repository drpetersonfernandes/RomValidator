using System.Text.Json.Serialization;

namespace RomValidator.Models;

/// <summary>
/// Represents a GitHub release for version checking.
/// </summary>
public class GitHubRelease
{
    /// <summary>Gets or sets the release tag name (version).</summary>
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    /// <summary>Gets or sets the HTML URL for the release page.</summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    /// <summary>Gets or sets the list of assets for this release.</summary>
    [JsonPropertyName("assets")]
    public List<GitHubAsset>? Assets { get; set; }
}