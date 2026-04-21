using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace RomValidator.Services;

/// <summary>
/// Service for logging errors and sending them to the bug report API.
/// </summary>
public static class LoggerService
{
    private static readonly string LogFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "RomValidator.log");

    private static readonly object LogFileLock = new();
    private static BugReportService? _bugReportService;
    private static bool _isSendingBugReport;

    /// <summary>
    /// Sets the BugReportService for sending bug reports.
    /// </summary>
    public static void SetBugReportService(BugReportService bugReportService)
    {
        _bugReportService = bugReportService;
    }

    /// <summary>
    /// Logs an error message and sends a bug report.
    /// </summary>
    public static void LogError(string component, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var logEntry = $"[{timestamp}] ERROR [{component}]: {message}";

        // Write to trace (visible in VS Output window)
        Trace.WriteLine(logEntry);

        // Write to file (persistent log)
        WriteToLogFile(logEntry);

        // Send to bug report API (fire-and-forget, with recursion guard)
        SendBugReport(component, message, null);
    }

    /// <summary>
    /// Logs an exception with detailed information and sends a bug report.
    /// </summary>
    public static void LogException(string component, Exception exception, string? context = null)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var fullContext = context != null ? $"{component} - {context}" : component;

        // Build detailed log entry
        var sb = new StringBuilder();
        sb.AppendLine($"[{timestamp}] EXCEPTION [{fullContext}]:");
        sb.AppendLine($"    Type: {exception.GetType().FullName}");
        sb.AppendLine($"    Message: {exception.Message}");
        sb.AppendLine($"    Source: {exception.Source ?? "N/A"}");
        sb.AppendLine($"    StackTrace: {exception.StackTrace ?? "N/A"}");

        if (exception.InnerException != null)
        {
            sb.AppendLine($"    Inner Exception: {exception.InnerException.GetType().Name} - {exception.InnerException.Message}");
        }

        var logEntry = sb.ToString();

        // Write to trace (visible in VS Output window)
        Trace.WriteLine(logEntry);

        // Write to file (persistent log)
        WriteToLogFile(logEntry);

        // Send to bug report API (fire-and-forget, with recursion guard)
        SendBugReport(fullContext, $"Exception: {exception.Message}", exception);
    }

    /// <summary>
    /// Logs a warning message (does not send bug report).
    /// </summary>
    public static void LogWarning(string component, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var logEntry = $"[{timestamp}] WARNING [{component}]: {message}";

        // Write to trace
        Trace.WriteLine(logEntry);

        // Write to file
        WriteToLogFile(logEntry);
    }

    /// <summary>
    /// Logs an informational message (does not send bug report).
    /// </summary>
    public static void LogInfo(string component, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var logEntry = $"[{timestamp}] INFO [{component}]: {message}";

        // Write to trace
        Trace.WriteLine(logEntry);

        // Write to file
        WriteToLogFile(logEntry);
    }

    /// <summary>
    /// Logs a debug message (only in debug builds, does not send bug report).
    /// </summary>
    [Conditional("DEBUG")]
    public static void LogDebug(string component, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var logEntry = $"[{timestamp}] DEBUG [{component}]: {message}";

        // Write to debug output
        Debug.WriteLine(logEntry);

        // Write to file
        WriteToLogFile(logEntry);
    }

    /// <summary>
    /// Writes a log entry to the log file with proper locking.
    /// </summary>
    private static void WriteToLogFile(string logEntry)
    {
        try
        {
            lock (LogFileLock)
            {
                File.AppendAllText(LogFilePath, logEntry + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            // Log to debug output at minimum - don't silently fail
            Debug.WriteLine($"Logger error writing to file: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a bug report asynchronously (fire-and-forget) with recursion protection.
    /// </summary>
    private static void SendBugReport(string context, string message, Exception? exception)
    {
        if (_bugReportService != null && !_isSendingBugReport)
        {
            _isSendingBugReport = true;
            var cancellationToken = App.GetGlobalCancellationToken();
            
            _ = Task.Run(async () =>
            {
                try
                {
                    if (exception != null)
                    {
                        await _bugReportService.SendBugReportAsync(context, exception, message, cancellationToken);
                    }
                    else
                    {
                        await _bugReportService.SendBugReportAsync(context, null, message, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Application is shutting down, ignore cancellation
                }
                catch (Exception ex)
                {
                    // Prevent recursive bug reports - just log to debug
                    Debug.WriteLine($"Failed to send bug report: {ex.Message}");
                }
                finally
                {
                    _isSendingBugReport = false;
                }
            }, cancellationToken);
        }
    }
}
