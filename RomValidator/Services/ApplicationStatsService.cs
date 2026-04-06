using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;

namespace RomValidator.Services;

public class ApplicationStatsService(string baseUrl, string apiKey, string applicationId) : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly string _statsUrl = $"{baseUrl.TrimEnd('/')}/stats";
    private readonly string _apiKey = apiKey;
    private readonly string _applicationId = applicationId;

    public async Task<bool> RecordUsageAsync()
    {
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

            var errorContent = await response.Content.ReadAsStringAsync();
            LoggerService.LogError("ApplicationStatsService",
                $"Stats API call failed with HTTP status {response.StatusCode}. Content: {errorContent}");
            return false;
        }
        catch (Exception ex)
        {
            LoggerService.LogError("ApplicationStatsService",
                $"Exception while recording application stats: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
