using System.Text.Json.Serialization;

namespace RomValidator.Models;

/// <summary>
/// Payload for sending bug reports to the API.
/// Matches the API's expected BugReportRequest structure.
/// </summary>
public class BugReportPayload
{
    /// <summary>
    /// The main message containing all formatted environment and error details.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// The name of the application reporting the bug.
    /// </summary>
    [JsonPropertyName("applicationName")]
    public string? ApplicationName { get; set; }

    /// <summary>
    /// The application version.
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>
    /// Additional user information about the error.
    /// </summary>
    [JsonPropertyName("userInfo")]
    public string? UserInfo { get; set; }

    /// <summary>
    /// Context or environment where the error occurred.
    /// </summary>
    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    /// <summary>
    /// Stack trace from the exception (if any).
    /// </summary>
    [JsonPropertyName("stackTrace")]
    public string? StackTrace { get; set; }
}
