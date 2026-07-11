using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace RomValidator.Services;

/// <summary>
/// A Serilog sink that forwards Error and Fatal log events to the bug report API.
/// Each event is submitted via BugReportService with full environment and exception details.
/// Protected against recursion so a failure inside the sink does not trigger another bug report.
/// </summary>
internal sealed class BugReportSink : ILogEventSink, IDisposable
{
    private readonly BugReportService _bugReportService;
    private volatile bool _isSending;

    public BugReportSink(BugReportService bugReportService)
    {
        _bugReportService = bugReportService;
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Error)
            return;

        if (_isSending)
            return;

        var component = "Serilog";
        if (logEvent.Properties.TryGetValue("Component", out var componentValue) &&
            componentValue is ScalarValue { Value: string compStr })
        {
            component = compStr;
        }

        // Never forward failures originating from the bug reporting pipeline itself,
        // otherwise a failing report would recursively generate more reports.
        if (string.Equals(component, "BugReportService", StringComparison.Ordinal) ||
            string.Equals(component, "BugReportSink", StringComparison.Ordinal))
        {
            return;
        }

        _isSending = true;
        try
        {
            var message = logEvent.RenderMessage(CultureInfo.InvariantCulture);

            Exception? exception = null;
            string? additionalInfo = null;
            if (logEvent.Exception != null)
            {
                exception = logEvent.Exception;
            }
            else if (logEvent.Properties.TryGetValue("ErrorMessage", out var errorMsgValue) &&
                     errorMsgValue is ScalarValue { Value: string errStr })
            {
                additionalInfo = errStr;
            }

            _ = _bugReportService.SendBugReportAsync(
                message,
                exception,
                additionalInfo,
                CancellationToken.None);
        }
        catch
        {
            // Silently ignore failures from the sink itself to prevent cascading errors
        }
        finally
        {
            _isSending = false;
        }
    }

    public void Dispose()
    {
    }
}
