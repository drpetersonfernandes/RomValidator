using System.Net.Http;
using System.Net.Http.Json;
using RomValidator.Models;

namespace RomValidator.Services;

public class BugReportService(string apiUrl, string apiKey, string applicationName) : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly string _apiUrl = apiUrl;
    private readonly string _apiKey = apiKey;
    private readonly string _applicationName = applicationName;
    private const int MaxMessageLength = 500;

    public async Task<bool> SendBugReportAsync(string message, Exception? exception = null)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", _apiKey);

            var fullMessage = message;
            if (exception != null)
            {
                fullMessage += $"\nException Type: {exception.GetType().Name}";
                fullMessage += $"\nException Message: {exception.Message}";
                if (exception.StackTrace != null)
                {
                    fullMessage += $"\nStack Trace:\n{exception.StackTrace}";
                }

                if (exception.InnerException != null)
                {
                    fullMessage += $"\nInner Exception: {exception.InnerException.Message}";
                }
            }

            // Truncate the message to fit the API's expected length
            if (fullMessage.Length > MaxMessageLength)
            {
                fullMessage = fullMessage[..MaxMessageLength];
            }

            // Create the request payload
            var payload = new BugReportPayload
            {
                Message = fullMessage,
                ApplicationName = _applicationName
            };

            var content = JsonContent.Create(payload);

            // Send the request
            var response = await _httpClient.PostAsync(_apiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var apiResponse = await response.Content.ReadFromJsonAsync<BugReportApiResponse>();
                // Check if the API explicitly reported success (succeeded == 1)
                if (apiResponse?.Data?.Succeeded == 1)
                {
                    return true;
                }
                else
                {
                    var errors = apiResponse?.Data?.Errors != null ? string.Join(", ", apiResponse.Data.Errors) : "No specific errors reported.";
                    await Console.Error.WriteLineAsync($"BugReportService API reported failure. Errors: {errors}");
                    return false;
                }
            }
            else
            {
                // Log the non-success HTTP status code and content for debugging
                var errorContent = await response.Content.ReadAsStringAsync();
                await Console.Error.WriteLineAsync($"BugReportService API call failed with HTTP status {response.StatusCode}. Content: {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            // Log the exception that occurred while trying to send the bug report itself
            await Console.Error.WriteLineAsync($"Exception while attempting to send bug report: {ex.Message}");
            // Silently fail if there's an exception during a bug report sending itself
            return false;
        }
    }

    public void Dispose()
    {
        // Dispose the HttpClient to release resources
        _httpClient?.Dispose();

        // Suppress finalization
        GC.SuppressFinalize(this);
    }
}