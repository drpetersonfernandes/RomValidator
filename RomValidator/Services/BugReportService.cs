using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RomValidator.Services;

// Define the payload structure expected by the BugReport API
// This mirrors the 'BugReport' class in the BugReportEmailService's Program.cs
file sealed class BugReportPayload
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("applicationName")]
    public string? ApplicationName { get; set; }
}

// Define the response structure from the BugReport API
// This mirrors the 'Smtp2GoResponse' and 'Smtp2GoData' classes in the BugReportEmailService's Program.cs
public class BugReportApiResponse
{
    [JsonPropertyName("data")]
    public BugReportApiData? Data { get; set; }
}

public class BugReportApiData
{
    [JsonPropertyName("succeeded")]
    public int Succeeded { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }
}

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

    // Max message length based on BugReportApi's ErrorLog.Message StringLength(500)
    private const int MaxMessageLength = 500;

    /// <summary>
    /// Sends a bug report to the API
    /// </summary>
    /// <param name="message">A concise summary of the error or bug report.</param>
    /// <param name="exception">Optional exception to include details from (message, stack trace).</param>
    /// <returns>True if the bug report was successfully sent and processed by the API, false otherwise.</returns>
    public async Task<bool> SendBugReportAsync(string message, Exception? exception = null)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", _apiKey);

            // Construct the full message including exception details
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

            // Truncate the message to fit the API's expected length (500 characters)
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
