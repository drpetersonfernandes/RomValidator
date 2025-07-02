using System.Net.Http;
using System.Net.Http.Json;

namespace RomValidator;

/// <inheritdoc />
/// <summary>
/// Service responsible for sending bug reports to the BugReport API
/// </summary>
public class BugReportService(string apiUrl, string apiKey, string applicationName) : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly string _apiUrl = apiUrl;
    private readonly string _apiKey = apiKey;
    private readonly string _applicationName = applicationName;

    /// <summary>
    /// Sends a bug report to the API
    /// </summary>
    /// <param name="message">The error message or bug report</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task<bool> SendBugReportAsync(string message)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", _apiKey);

            // Create the request payload
            var content = JsonContent.Create(new
            {
                message,
                applicationName = _applicationName
            });

            // Send the request
            var response = await _httpClient.PostAsync(_apiUrl, content);

            // Return true if successful
            return response.IsSuccessStatusCode;
        }
        catch
        {
            // Silently fail if there's an exception during a bug report sending itself
            return false;
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Releases all resources used by the current instance of the class.
    /// </summary>
    public void Dispose()
    {
        // Dispose the HttpClient to release resources
        _httpClient?.Dispose();

        // Suppress finalization
        GC.SuppressFinalize(this);
    }
}