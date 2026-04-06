using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace RomValidator.Services;

public static class LoggerService
{
    private static readonly string LogFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "RomValidator.log");

    private static readonly object LogFileLock = new();
    private static BugReportService? _bugReportService;
    private static bool _isSendingBugReport;

    public static void SetBugReportService(BugReportService bugReportService)
    {
        _bugReportService = bugReportService;
    }

    public static void LogError(string component, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var logEntry = $"[{timestamp}] ERROR [{component}]: {message}";

        // Write to trace (visible in VS Output window)
        Trace.WriteLine(logEntry);

        // Write to file (persistent log)
        try
        {
            lock (LogFileLock)
            {
                File.AppendAllText(LogFilePath, logEntry + Environment.NewLine);
            }
        }
        catch
        {
            // Ignore file logging failures to avoid infinite loops
        }

        // Send to bug report API (fire-and-forget, with recursion guard)
        if (_bugReportService != null && !_isSendingBugReport)
        {
            _isSendingBugReport = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _bugReportService.SendBugReportAsync($"[{component}] {message}");
                }
                finally
                {
                    _isSendingBugReport = false;
                }
            });
        }
    }
}