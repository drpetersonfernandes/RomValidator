using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using RomValidator.Models;

namespace RomValidator.Services;

public class GitHubVersionChecker : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiBaseUrl;

    public GitHubVersionChecker(string repoOwner, string repoName)
    {
        _apiBaseUrl = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases/latest";

        _httpClient = new HttpClient();
        // GitHub API requires a User-Agent header
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RomValidator", GetCurrentApplicationVersion()?.ToString() ?? "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
    }

    public async Task<(bool IsNewVersionAvailable, string? ReleaseUrl, string? LatestVersionTag)> CheckForNewVersionAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(_apiBaseUrl);
            response.EnsureSuccessStatusCode(); // Throws an exception for HTTP error codes (4xx, 5xx)

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>();

            if (release?.TagName == null || release.HtmlUrl == null)
            {
                await Console.Error.WriteLineAsync("GitHubVersionChecker: Could not parse release info from API response.");
                return (false, null, null);
            }

            var currentVersion = GetCurrentApplicationVersion();
            if (currentVersion == null)
            {
                await Console.Error.WriteLineAsync("GitHubVersionChecker: Could not determine current application version.");
                return (false, null, null);
            }

            // Clean the GitHub tag name to be parseable by System.Version
            // e.g., "release_1.0.0" -> "1.0.0", "v1.0.0" -> "1.0.0"
            var latestVersionTagCleaned = release.TagName.Replace("release_", "", StringComparison.OrdinalIgnoreCase).TrimStart('v');

            if (Version.TryParse(latestVersionTagCleaned, out var latestVersion))
            {
                if (latestVersion > currentVersion)
                {
                    return (true, release.HtmlUrl, release.TagName);
                }
            }
            else
            {
                await Console.Error.WriteLineAsync($"GitHubVersionChecker: Could not parse latest version tag '{release.TagName}' from GitHub.");
            }

            return (false, null, null); // No new version or parsing issue
        }
        catch (HttpRequestException httpEx)
        {
            await Console.Error.WriteLineAsync($"GitHubVersionChecker: HTTP request error checking for updates: {httpEx.Message}");
            return (false, null, null);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"GitHubVersionChecker: General error checking for updates: {ex.Message}");
            return (false, null, null);
        }
    }

    private static Version? GetCurrentApplicationVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}