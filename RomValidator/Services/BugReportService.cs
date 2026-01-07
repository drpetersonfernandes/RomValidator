using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using RomValidator.Models;

namespace RomValidator.Services;

public class BugReportService(string apiUrl, string apiKey, string applicationName) : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly string _apiUrl = apiUrl;
    private readonly string _apiKey = apiKey;
    private readonly string _applicationName = applicationName;
    private const int MaxMessageLength = 20000;

    public async Task<bool> SendBugReportAsync(string message, Exception? exception = null)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", _apiKey);

            var sb = new StringBuilder();
            sb.AppendLine("-- System Info --");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Date (UTC): {DateTime.UtcNow:o}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"App Version: {Assembly.GetExecutingAssembly().GetName().Version}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"OS Version: {Environment.OSVersion.VersionString}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Architecture: {RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Bitness: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}");
            sb.AppendLine();
            sb.AppendLine("-- Report --");
            sb.AppendLine(message);

            if (exception != null)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"Exception Type: {exception.GetType().Name}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"Exception Message: {exception.Message}");
                if (exception.StackTrace != null)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"Stack Trace:\n{exception.StackTrace}");
                }

                if (exception.InnerException != null)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"Inner Exception: {exception.InnerException.Message}");
                }
            }

            var fullMessage = sb.ToString();
            // Truncate the message to fit the API's expected length
            if (fullMessage.Length > MaxMessageLength)
            {
                fullMessage = fullMessage[..MaxMessageLength];
                fullMessage += "\n\n[MESSAGE TRUNCATED DUE TO LENGTH LIMITS]";
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