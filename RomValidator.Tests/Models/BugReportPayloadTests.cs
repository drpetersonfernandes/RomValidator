using System.Text.Json;
using RomValidator.Models;
using Xunit;

namespace RomValidator.Tests.Models;

public class BugReportPayloadTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void BugReportPayloadSerializesToJsonCorrectly()
    {
        var payload = new BugReportPayload
        {
            Message = "Test error message",
            ApplicationName = "ROM Validator",
            Version = "2.7.2",
            UserInfo = "test@example.com",
            Environment = "Cleanup",
            StackTrace = "at TempDirectoryHelper.CleanupTempDirectory()"
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);

        Assert.Contains("\"message\":\"Test error message\"", json);
        Assert.Contains("\"applicationName\":\"ROM Validator\"", json);
        Assert.Contains("\"version\":\"2.7.2\"", json);
        Assert.Contains("\"userInfo\":\"test@example.com\"", json);
        Assert.Contains("\"environment\":\"Cleanup\"", json);
        Assert.Contains("\"stackTrace\":\"at TempDirectoryHelper.CleanupTempDirectory()\"", json);
    }

    [Fact]
    public void BugReportPayloadDeserializesFromJsonCorrectly()
    {
        const string json = """
                            {
                                "message": "NullReferenceException in UserService",
                                "applicationName": "ROM Validator",
                                "version": "2.7.1",
                                "userInfo": "user@example.com",
                                "environment": "Production",
                                "stackTrace": "at UserService.GetUser() in UserService.cs:line 42"
                            }
                            """;

        var payload = JsonSerializer.Deserialize<BugReportPayload>(json, JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal("NullReferenceException in UserService", payload.Message);
        Assert.Equal("ROM Validator", payload.ApplicationName);
        Assert.Equal("2.7.1", payload.Version);
        Assert.Equal("user@example.com", payload.UserInfo);
        Assert.Equal("Production", payload.Environment);
        Assert.Contains("UserService.GetUser()", payload.StackTrace);
    }

    [Fact]
    public void BugReportPayloadDefaultValuesAreNull()
    {
        var payload = new BugReportPayload();

        Assert.Null(payload.Message);
        Assert.Null(payload.ApplicationName);
        Assert.Null(payload.Version);
        Assert.Null(payload.UserInfo);
        Assert.Null(payload.Environment);
        Assert.Null(payload.StackTrace);
    }

    [Fact]
    public void BugReportPayloadPropertiesCanBeSet()
    {
        var payload = new BugReportPayload
        {
            Message = "Test",
            ApplicationName = "App",
            Version = "1.0",
            UserInfo = "info",
            Environment = "env",
            StackTrace = "trace"
        };

        Assert.Equal("Test", payload.Message);
        Assert.Equal("App", payload.ApplicationName);
        Assert.Equal("1.0", payload.Version);
        Assert.Equal("info", payload.UserInfo);
        Assert.Equal("env", payload.Environment);
        Assert.Equal("trace", payload.StackTrace);
    }
}
