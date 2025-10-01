using System.Text.Json.Serialization;

namespace RomValidator.Models;

public class BugReportPayload
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("applicationName")]
    public string? ApplicationName { get; set; }
}