using System.Text.Json.Serialization;

namespace RomValidator.Models;

/// <summary>
/// Payload for sending bug reports to the API.
/// </summary>
public class BugReportPayload
{
    /// <summary>
    /// The main message containing all formatted details.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// The name of the application reporting the bug.
    /// </summary>
    [JsonPropertyName("applicationName")]
    public string? ApplicationName { get; set; }

    /// <summary>
    /// Optional: Structured environment details for API parsing.
    /// </summary>
    [JsonPropertyName("environmentDetails")]
    public EnvironmentDetails? EnvironmentDetails { get; set; }

    /// <summary>
    /// Optional: Structured error details for API parsing.
    /// </summary>
    [JsonPropertyName("errorDetails")]
    public ErrorDetails? ErrorDetails { get; set; }

    /// <summary>
    /// Optional: Structured exception details for API parsing.
    /// </summary>
    [JsonPropertyName("exceptionDetails")]
    public ExceptionDetails? ExceptionDetails { get; set; }
}

/// <summary>
/// Environment details for bug reporting.
/// </summary>
public class EnvironmentDetails
{
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("applicationName")]
    public string? ApplicationName { get; set; }

    [JsonPropertyName("applicationVersion")]
    public string? ApplicationVersion { get; set; }

    [JsonPropertyName("osVersion")]
    public string? OsVersion { get; set; }

    [JsonPropertyName("architecture")]
    public string? Architecture { get; set; }

    [JsonPropertyName("bitness")]
    public string? Bitness { get; set; }

    [JsonPropertyName("windowsVersion")]
    public string? WindowsVersion { get; set; }

    [JsonPropertyName("processorCount")]
    public int ProcessorCount { get; set; }

    [JsonPropertyName("baseDirectory")]
    public string? BaseDirectory { get; set; }

    [JsonPropertyName("tempPath")]
    public string? TempPath { get; set; }
}

/// <summary>
/// Error details for bug reporting.
/// </summary>
public class ErrorDetails
{
    [JsonPropertyName("context")]
    public string? Context { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("additionalInfo")]
    public string? AdditionalInfo { get; set; }
}

/// <summary>
/// Exception details for bug reporting.
/// </summary>
public class ExceptionDetails
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("stackTrace")]
    public string? StackTrace { get; set; }

    [JsonPropertyName("hResult")]
    public int? HResult { get; set; }

    [JsonPropertyName("targetSite")]
    public string? TargetSite { get; set; }

    [JsonPropertyName("innerException")]
    public ExceptionDetails? InnerException { get; set; }
}
