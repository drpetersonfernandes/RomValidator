using System.Text.Json.Serialization;

namespace RomValidator.Models;

public class BugReportApiResponse
{
    [JsonPropertyName("data")]
    public BugReportApiData? Data { get; set; }
}