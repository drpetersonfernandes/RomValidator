using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using RomValidator.Models;

namespace RomValidator.Services;

/// <summary>
/// Service for checking GitHub releases to determine if a newer version of the application is available.
/// Provides version comparison and update notification functionality.
/// </summary>
public class GitHubVersionChecker : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiBaseUrl;
    private readonly BugReportService? _bugReportService;

    /// <summary>
    /// Initializes a new instance of the GitHubVersionChecker class.
    /// </summary>
    /// <param name="repoOwner">The GitHub repository owner (username or organization).</param>
    /// <param name="repoName">The name of the GitHub repository.</param>
    /// <param name="bugReportService">Optional bug report service for error tracking.</param>
    public GitHubVersionChecker(string repoOwner, string repoName, BugReportService? bugReportService = null)
    {
        _apiBaseUrl = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases/latest";
        _bugReportService = bugReportService;

        _httpClient = new HttpClient();
        // GitHub API requires a User-Agent header
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RomValidator", GetCurrentApplicationVersion()?.ToString() ?? "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
    }

    /// <summary>
    /// Checks GitHub for a newer version of the application.
    /// </summary>
    /// <returns>A tuple containing: 
    /// - IsNewVersionAvailable: True if a newer version is available
    /// - ReleaseUrl: The URL to the latest release page
    /// - LatestVersionTag: The version tag of the latest release
    /// </returns>
    public async Task<(bool IsNewVersionAvailable, string? ReleaseUrl, string? LatestVersionTag)> CheckForNewVersionAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(_apiBaseUrl);
            response.EnsureSuccessStatusCode(); // Throws an exception for HTTP error codes (4xx, 5xx)

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>();

            if (release?.TagName == null || release.HtmlUrl == null)
            {
                LoggerService.LogError("GitHubVersionChecker", "Could not parse release info from API response.");
                return (false, null, null);
            }

            var currentVersion = GetCurrentApplicationVersion();
            if (currentVersion == null)
            {
                LoggerService.LogError("GitHubVersionChecker", "Could not determine current application version.");
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
                LoggerService.LogError("GitHubVersionChecker", $"Could not parse latest version tag '{release.TagName}' from GitHub.");
            }

            return (false, null, null); // No new version or parsing issue
        }
        catch (HttpRequestException httpEx)
        {
            LoggerService.LogError("GitHubVersionChecker", $"HTTP request error checking for updates: {httpEx.Message}");
            _ = _bugReportService?.SendBugReportAsync("HTTP error checking for updates from GitHub.", httpEx);
            return (false, null, null);
        }
        catch (Exception ex)
        {
            LoggerService.LogError("GitHubVersionChecker", $"General error checking for updates: {ex.Message}");
            _ = _bugReportService?.SendBugReportAsync("Error checking for updates from GitHub.", ex);
            return (false, null, null);
        }
    }

    private static Version? GetCurrentApplicationVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version;
    }

    /// <summary>
    /// Disposes of the HTTP client used by the service.
    /// </summary>
    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}