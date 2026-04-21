using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using RomValidator.Models;

namespace RomValidator.Services;

/// <summary>
/// Service for reporting bugs to the bug report API.
/// This service ensures all exceptions are captured with detailed environment and error information.
/// </summary>
public class BugReportService : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly string _apiUrl;
    private readonly string _apiKey;
    private readonly string _applicationName;
    private const int MaxMessageLength = 30000;

    public BugReportService(string apiUrl, string apiKey, string applicationName)
    {
        _apiUrl = apiUrl;
        _apiKey = apiKey;
        _applicationName = applicationName;
    }

    /// <summary>
    /// Sends a bug report to the API with comprehensive environment and error details.
    /// </summary>
    /// <param name="context">Context or location where the error occurred</param>
    /// <param name="exception">The exception that occurred (optional)</param>
    /// <param name="additionalInfo">Additional information about the error (optional)</param>
    /// <returns>True if the report was sent successfully, false otherwise</returns>
    public async Task<bool> SendBugReportAsync(string context, Exception? exception = null, string? additionalInfo = null)
    {
        return await SendBugReportAsync(context, exception, additionalInfo, CancellationToken.None);
    }

    /// <summary>
    /// Sends a bug report to the API with comprehensive environment and error details.
    /// </summary>
    /// <param name="context">Context or location where the error occurred</param>
    /// <param name="exception">The exception that occurred (optional)</param>
    /// <param name="additionalInfo">Additional information about the error (optional)</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>True if the report was sent successfully, false otherwise</returns>
    public async Task<bool> SendBugReportAsync(string context, Exception? exception, string? additionalInfo, CancellationToken cancellationToken)
    {
        try
        {
            var reportMessage = BuildBugReportMessage(context, exception, additionalInfo);

            // Create payload matching the API's BugReportRequest structure
            // The API expects exactly these 6 fields - all environment details must be in the message field
            var payload = new
            {
                message = reportMessage,  // Contains all formatted environment and error details
                applicationName = _applicationName,
                version = GetApplicationVersion(),
                userInfo = additionalInfo,
                environment = context,
                stackTrace = exception?.StackTrace
            };

            // Send the request using HttpRequestMessage for thread safety
            using var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl);
            request.Headers.Add("X-API-KEY", _apiKey);
            request.Content = JsonContent.Create(payload);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                // The API returns a simple JSON with message and id fields
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                return responseContent.Contains("\"message\"") || responseContent.Contains("\"id\"");
            }

            // Silently fail - don't log to avoid recursive bug reports
            return false;
        }
        catch (Exception ex)
        {
            // Log the exception that occurred while trying to send the bug report itself
            LoggerService.LogError("BugReportService", $"Exception while attempting to send bug report: {ex.Message}");
            // Silently fail if there's an exception during a bug report sending itself
            return false;
        }
    }





    /// <summary>
    /// Builds a comprehensive bug report message with all required sections.
    /// </summary>
    private string BuildBugReportMessage(string context, Exception? exception, string? additionalInfo)
    {
        var sb = new StringBuilder();

        // === Environment Details Section ===
        sb.AppendLine("=== Environment Details ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Application Name: {_applicationName}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Application Version: {GetApplicationVersion()}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"OS Version: {Environment.OSVersion.VersionString}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Architecture: {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Bitness: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Windows Version: {GetWindowsVersion()}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Processor Count: {Environment.ProcessorCount}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Base Directory: {AppDomain.CurrentDomain.BaseDirectory}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Temp Path: {Path.GetTempPath()}");
        sb.AppendLine();

        // === Error Details Section ===
        sb.AppendLine("=== Error Details ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Error message: {context}");
        if (!string.IsNullOrEmpty(additionalInfo))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Additional Info: {additionalInfo}");
        }

        sb.AppendLine();

        // === Exception Details Section ===
        if (exception != null)
        {
            sb.AppendLine("=== Exception Details ===");
            sb.AppendLine(BuildExceptionDetails(exception));
        }

        var fullMessage = sb.ToString();

        // Truncate the message to fit the API's expected length
        if (fullMessage.Length > MaxMessageLength)
        {
            fullMessage = string.Concat(
                fullMessage.AsSpan(0, MaxMessageLength),
                "\n\n[MESSAGE TRUNCATED DUE TO LENGTH LIMITS]");
        }

        return fullMessage;
    }

    /// <summary>
    /// Builds detailed exception information including inner exceptions.
    /// </summary>
    private static string BuildExceptionDetails(Exception exception)
    {
        var sb = new StringBuilder();
        var currentException = exception;
        var exceptionCount = 0;

        while (currentException != null)
        {
            exceptionCount++;
            if (exceptionCount > 1)
            {
                sb.AppendLine();
                sb.AppendLine(CultureInfo.InvariantCulture, $"--- Inner Exception #{exceptionCount - 1} ---");
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"Type: {currentException.GetType().FullName}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Message: {currentException.Message}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Source: {currentException.Source ?? "N/A"}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"StackTrace: {currentException.StackTrace ?? "N/A"}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"TargetSite: {currentException.TargetSite?.ToString() ?? "N/A"}");

            // Add HResult for Windows-specific errors
            if (currentException.HResult != 0)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"HResult: 0x{currentException.HResult:X8}");
            }

            // Add any custom data if present
            if (currentException.Data.Count > 0)
            {
                sb.AppendLine("Data:");
                foreach (var key in currentException.Data.Keys)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  {key}: {currentException.Data[key]}");
                }
            }

            currentException = currentException.InnerException;
        }

        // Add aggregate exception details if applicable
        if (exception is AggregateException aggregateException)
        {
            var innerExceptions = aggregateException.InnerExceptions;
            if (innerExceptions.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(CultureInfo.InvariantCulture, $"--- Aggregate Exception Inner Exceptions ({innerExceptions.Count}) ---");
                for (var i = 0; i < innerExceptions.Count; i++)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"Inner Exception [{i}]: {innerExceptions[i].GetType().Name} - {innerExceptions[i].Message}");
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets the application version from the executing assembly.
    /// </summary>
    private static string GetApplicationVersion()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version?.ToString() ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// Gets detailed Windows version information.
    /// </summary>
    private static string GetWindowsVersion()
    {
        try
        {
            // For .NET 5+, we can use RuntimeInformation.OSDescription
            var osDescription = RuntimeInformation.OSDescription;

            // Add additional Windows-specific information if available
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    // Try to get the release ID from registry (Windows 10/11)
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                    if (key != null)
                    {
                        var releaseId = key.GetValue("ReleaseId")?.ToString();
                        var displayVersion = key.GetValue("DisplayVersion")?.ToString();
                        var buildNumber = key.GetValue("CurrentBuildNumber")?.ToString();

                        if (!string.IsNullOrEmpty(displayVersion))
                        {
                            return $"{osDescription} (Version {displayVersion}, Build {buildNumber})";
                        }

                        if (!string.IsNullOrEmpty(releaseId))
                        {
                            return $"{osDescription} (Release {releaseId}, Build {buildNumber})";
                        }
                    }
                }
                catch
                {
                    // Ignore registry access errors
                }
            }

            return osDescription;
        }
        catch
        {
            return "Unknown";
        }
    }

    public void Dispose()
    {
        // Dispose the HttpClient to release resources
        _httpClient.Dispose();

        // Suppress finalization
        GC.SuppressFinalize(this);
    }
}
