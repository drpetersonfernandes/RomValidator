using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;

namespace RomValidator.Services;

public static class LoggerService
{
    private static readonly string LogFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "RomValidator.log");

    public static void LogError(string component, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var logEntry = $"[{timestamp}] ERROR [{component}]: {message}";

        // Write to trace (visible in VS Output window)
        Trace.WriteLine(logEntry);

        // Write to file (persistent log)
        try
        {
            File.AppendAllText(LogFilePath, logEntry + Environment.NewLine);
        }
        catch
        {
            // Ignore file logging failures to avoid infinite loops
        }

        // Update status bar if MainWindow is available
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            (Application.Current.MainWindow as MainWindow)?.UpdateStatusBarMessage($"Error: {message}");
        });
    }
}