using System.Text.Json;
using RomValidator.Models;
using Xunit;

namespace RomValidator.Tests.Models;

public class GitHubAssetTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public void GitHubAssetDefaultValuesAreNull()
    {
        // Arrange & Act
        var asset = new GitHubAsset();

        // Assert
        Assert.Null(asset.Name);
        Assert.Null(asset.BrowserDownloadUrl);
    }

    [Fact]
    public void GitHubAssetPropertiesCanBeSet()
    {
        // Arrange
        var asset = new GitHubAsset
        {
            Name = "RomValidator_v2.7.2_win-x64.zip",
            BrowserDownloadUrl = "https://github.com/user/repo/releases/download/v2.7.2/RomValidator_v2.7.2_win-x64.zip"
        };

        // Assert
        Assert.Equal("RomValidator_v2.7.2_win-x64.zip", asset.Name);
        Assert.Contains("v2.7.2", asset.BrowserDownloadUrl);
    }

    [Fact]
    public void GitHubAssetSerializesToJsonCorrectly()
    {
        // Arrange
        var asset = new GitHubAsset
        {
            Name = "app.zip",
            BrowserDownloadUrl = "https://example.com/app.zip"
        };

        // Act
        var json = JsonSerializer.Serialize(asset, JsonOptions);

        // Assert
        Assert.Contains("\"name\":\"app.zip\"", json);
        Assert.Contains("\"browser_download_url\":\"https://example.com/app.zip\"", json);
    }

    [Fact]
    public void GitHubAssetDeserializesFromJsonCorrectly()
    {
        // Arrange
        const string json = """
                            {
                                "name": "RomValidator_v2.7.2_win-x64.zip",
                                "browser_download_url": "https://github.com/drpetersonfernandes/RomValidator/releases/download/v2.7.2/RomValidator_v2.7.2_win-x64.zip"
                            }
                            """;

        // Act
        var asset = JsonSerializer.Deserialize<GitHubAsset>(json, JsonOptions);

        // Assert
        Assert.NotNull(asset);
        Assert.Contains("RomValidator_v2.7.2_win-x64", asset.Name);
        Assert.Contains("drpetersonfernandes", asset.BrowserDownloadUrl);
    }
}
