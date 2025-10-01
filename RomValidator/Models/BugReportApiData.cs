using System.Text.Json.Serialization;

namespace RomValidator.Models;

public class BugReportApiData
{
    [JsonPropertyName("succeeded")]
    public int Succeeded { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }
}