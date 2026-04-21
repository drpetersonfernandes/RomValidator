using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;

namespace RomValidator.Services;

/// <summary>
/// Service for recording application usage statistics to a remote API.
/// Tracks application launches and usage for analytics purposes.
/// </summary>
public class ApplicationStatsService(string baseUrl, string apiKey, string applicationId) : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly string _statsUrl = $"{baseUrl.TrimEnd('/')}/stats";
    private readonly string _apiKey = apiKey;
    private readonly string _applicationId = applicationId;
    private bool _hasRecordedUsage;

    /// <summary>
    /// Records application usage statistics to the remote API.
    /// This method is called once per application launch to track usage.
    /// </summary>
    /// <returns>True if the usage was recorded successfully, false otherwise.</returns>
    public async Task<bool> RecordUsageAsync()
    {
        if (_hasRecordedUsage)
        {
            return true; // Already recorded
        }

        _hasRecordedUsage = true; // Mark as attempted immediately to prevent duplicate calls per launch

        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

            var payload = new
            {
                applicationId = _applicationId,
                version
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, _statsUrl);
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            request.Content = JsonContent.Create(payload);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            // Don't log error for Rate Limit (429) to avoid bug reports (User feedback Apr 11, 2026)
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                return false;
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            LoggerService.LogError("ApplicationStatsService", $"Stats API call failed with HTTP status {response.StatusCode}. Content: {errorContent}");

            return false;
        }
        catch (Exception ex)
        {
            LoggerService.LogError("ApplicationStatsService", $"Exception while recording application stats: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Disposes of the HTTP client used by the service.
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
