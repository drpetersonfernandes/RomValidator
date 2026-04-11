using System.Text.Json.Serialization;

namespace RomValidator.Models;

/// <summary>
/// Represents a GitHub release asset for update checking.
/// </summary>
public class GitHubAsset
{
    /// <summary>Gets or sets the asset name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the browser download URL for the asset.</summary>
    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}