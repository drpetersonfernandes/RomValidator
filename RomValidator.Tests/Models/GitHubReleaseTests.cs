using System.Text.Json;
using RomValidator.Models;
using Xunit;

namespace RomValidator.Tests.Models;

public class GitHubReleaseTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public void GitHubReleaseDefaultValuesAreNull()
    {
        // Arrange & Act
        var release = new GitHubRelease();

        // Assert
        Assert.Null(release.TagName);
        Assert.Null(release.HtmlUrl);
        Assert.Null(release.Assets);
    }

    [Fact]
    public void GitHubReleasePropertiesCanBeSet()
    {
        // Arrange
        var assets = new List<GitHubAsset>
        {
            new() { Name = "RomValidator.zip", BrowserDownloadUrl = "https://example.com/download" }
        };

        var release = new GitHubRelease
        {
            TagName = "v2.7.2",
            HtmlUrl = "https://github.com/user/repo/releases/tag/v2.7.2",
            Assets = assets
        };

        // Assert
        Assert.Equal("v2.7.2", release.TagName);
        Assert.Equal("https://github.com/user/repo/releases/tag/v2.7.2", release.HtmlUrl);
        Assert.Single(release.Assets);
        Assert.Equal("RomValidator.zip", release.Assets[0].Name);
    }

    [Fact]
    public void GitHubReleaseSerializesToJsonCorrectly()
    {
        // Arrange
        var release = new GitHubRelease
        {
            TagName = "v3.0.0",
            HtmlUrl = "https://github.com/user/repo/releases/tag/v3.0.0",
            Assets = [new GitHubAsset { Name = "app.zip", BrowserDownloadUrl = "https://example.com/app.zip" }]
        };

        // Act
        var json = JsonSerializer.Serialize(release, JsonOptions);

        // Assert
        Assert.Contains("\"tag_name\":\"v3.0.0\"", json);
        Assert.Contains("\"html_url\":\"https://github.com/user/repo/releases/tag/v3.0.0\"", json);
        Assert.Contains("\"assets\"", json);
    }

    [Fact]
    public void GitHubReleaseDeserializesFromJsonCorrectly()
    {
        // Arrange
        const string json = """
                            {
                                "tag_name": "v2.7.2",
                                "html_url": "https://github.com/drpetersonfernandes/RomValidator/releases/tag/v2.7.2",
                                "assets": [
                                    {
                                        "name": "RomValidator_v2.7.2_win-x64.zip",
                                        "browser_download_url": "https://github.com/drpetersonfernandes/RomValidator/releases/download/v2.7.2/RomValidator_v2.7.2_win-x64.zip"
                                    }
                                ]
                            }
                            """;

        // Act
        var release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);

        // Assert
        Assert.NotNull(release);
        Assert.Equal("v2.7.2", release.TagName);
        Assert.Contains("drpetersonfernandes", release.HtmlUrl);
        Assert.NotNull(release.Assets);
        Assert.Single(release.Assets);
        Assert.Contains("RomValidator_v2.7.2_win-x64", release.Assets[0].Name);
    }

    [Fact]
    public void GitHubReleaseWithEmptyAssetsListDeserializesCorrectly()
    {
        // Arrange
        const string json = """
                            {
                                "tag_name": "v1.0.0",
                                "html_url": "https://github.com/user/repo/releases/tag/v1.0.0",
                                "assets": []
                            }
                            """;

        // Act
        var release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);

        // Assert
        Assert.NotNull(release);
        Assert.NotNull(release.Assets);
        Assert.Empty(release.Assets);
    }
}
