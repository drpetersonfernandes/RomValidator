using System.Diagnostics;
using Serilog;
using Serilog.Events;

namespace RomValidator.Services;

/// <summary>
/// Facade over Serilog that preserves the existing call-site API while routing
/// Error+ events to the bug report API through a custom sink.
/// </summary>
public static class LoggerService
{
    private static ILogger _logger = Serilog.Core.Logger.None;

    public static void Initialize(ILogger logger)
    {
        _logger = logger;
    }

    public static void LogError(string component, string message)
    {
        Write(LogEventLevel.Error, component, message, exception: null);
    }

    public static void LogException(string component, Exception exception, string? context = null)
    {
        var fullContext = context != null ? $"{component} - {context}" : component;
        Write(LogEventLevel.Error, fullContext, exception.Message, exception);
    }

    public static void LogWarning(string component, string message)
    {
        Write(LogEventLevel.Warning, component, message, exception: null);
    }

    public static void LogInfo(string component, string message)
    {
        Write(LogEventLevel.Information, component, message, exception: null);
    }

    [Conditional("DEBUG")]
    public static void LogDebug(string component, string message)
    {
        Write(LogEventLevel.Debug, component, message, exception: null);
    }

    private static void Write(LogEventLevel level, string component, string message, Exception? exception)
    {
        var componentLogger = _logger.ForContext("Component", component);
        componentLogger.Write(level, exception, message);
    }
}
