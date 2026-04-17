using System.IO;
using System.Runtime.CompilerServices;

namespace RomValidator.Services;

/// <summary>
/// Utility class for executing methods with automatic exception handling and bug reporting.
/// Provides a centralized way to ensure all exceptions are captured and reported.
/// </summary>
public static class ExceptionHandler
{
    private static BugReportService? _bugReportService;

    /// <summary>
    /// Sets the global BugReportService for automatic bug reporting.
    /// </summary>
    public static void SetBugReportService(BugReportService bugReportService)
    {
        _bugReportService = bugReportService;
    }

    #region Action Wrappers

    /// <summary>
    /// Executes an action with automatic exception handling and bug reporting.
    /// </summary>
    /// <param name="action">The action to execute</param>
    /// <param name="context">Context description for the bug report</param>
    /// <param name="rethrow">Whether to rethrow the exception after reporting (default: true)</param>
    /// <param name="memberName">Automatically captured method name</param>
    /// <param name="filePath">Automatically captured file path</param>
    /// <param name="lineNumber">Automatically captured line number</param>
    public static void Execute(
        Action action,
        string? context = null,
        bool rethrow = true,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        try
        {
            action();
        }
        catch (OperationCanceledException)
        {
            // Don't report cancellation exceptions - these are expected behavior
            throw;
        }
        catch (Exception ex)
        {
            ReportException(ex, context, memberName, filePath, lineNumber);
            if (rethrow)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// Executes a function with automatic exception handling and bug reporting.
    /// </summary>
    /// <param name="func">The function to execute</param>
    /// <param name="context">Context description for the bug report</param>
    /// <param name="rethrow">Whether to rethrow the exception after reporting (default: true)</param>
    /// <param name="memberName">Automatically captured method name</param>
    /// <param name="filePath">Automatically captured file path</param>
    /// <param name="lineNumber">Automatically captured line number</param>
    public static T Execute<T>(
        Func<T> func,
        string? context = null,
        bool rethrow = true,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        try
        {
            return func();
        }
        catch (OperationCanceledException)
        {
            // Don't report cancellation exceptions - these are expected behavior
            throw;
        }
        catch (Exception ex)
        {
            ReportException(ex, context, memberName, filePath, lineNumber);
            if (rethrow)
            {
                throw;
            }

            // ReSharper disable once NullableWarningSuppressionIsUsed
            return default!;
        }
    }

    #endregion

    #region Async Wrappers

    /// <summary>
    /// Executes an async action with automatic exception handling and bug reporting.
    /// </summary>
    /// <param name="func">The async action to execute</param>
    /// <param name="context">Context description for the bug report</param>
    /// <param name="rethrow">Whether to rethrow the exception after reporting (default: true)</param>
    /// <param name="memberName">Automatically captured method name</param>
    /// <param name="filePath">Automatically captured file path</param>
    /// <param name="lineNumber">Automatically captured line number</param>
    public static async Task ExecuteAsync(
        Func<Task> func,
        string? context = null,
        bool rethrow = true,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        try
        {
            await func();
        }
        catch (OperationCanceledException)
        {
            // Don't report cancellation exceptions - these are expected behavior
            throw;
        }
        catch (Exception ex)
        {
            ReportException(ex, context, memberName, filePath, lineNumber);
            if (rethrow)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// Executes an async function with automatic exception handling and bug reporting.
    /// </summary>
    /// <param name="func">The async function to execute</param>
    /// <param name="context">Context description for the bug report</param>
    /// <param name="rethrow">Whether to rethrow the exception after reporting (default: true)</param>
    /// <param name="memberName">Automatically captured method name</param>
    /// <param name="filePath">Automatically captured file path</param>
    /// <param name="lineNumber">Automatically captured line number</param>
    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> func,
        string? context = null,
        bool rethrow = true,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        try
        {
            return await func();
        }
        catch (OperationCanceledException)
        {
            // Don't report cancellation exceptions - these are expected behavior
            throw;
        }
        catch (Exception ex)
        {
            ReportException(ex, context, memberName, filePath, lineNumber);
            if (rethrow)
            {
                throw;
            }

            // ReSharper disable once NullableWarningSuppressionIsUsed
            return default!;
        }
    }

    #endregion

    #region Event Handler Wrappers

    /// <summary>
    /// Wraps an event handler with automatic exception handling and bug reporting.
    /// Swallows the exception to prevent application crash (useful for UI event handlers).
    /// </summary>
    /// <param name="handler">The event handler to wrap</param>
    /// <param name="context">Context description for the bug report</param>
    /// <param name="memberName">Automatically captured method name</param>
    public static void ExecuteEventHandler(
        Action handler,
        string? context = null,
        [CallerMemberName] string memberName = "")
    {
        Execute(handler, context, false, memberName);
    }

    /// <summary>
    /// Wraps an async event handler with automatic exception handling and bug reporting.
    /// Swallows the exception to prevent application crash (useful for UI event handlers).
    /// </summary>
    /// <param name="handler">The async event handler to wrap</param>
    /// <param name="context">Context description for the bug report</param>
    /// <param name="memberName">Automatically captured method name</param>
    public static Task ExecuteEventHandlerAsync(
        Func<Task> handler,
        string? context = null,
        [CallerMemberName] string memberName = "")
    {
        return ExecuteAsync(handler, context, false, memberName);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Reports an exception to the bug report service and logger.
    /// </summary>
    private static void ReportException(
        Exception ex,
        string? context,
        string memberName,
        string filePath,
        int lineNumber)
    {
        try
        {
            // Build comprehensive context information
            var fileName = Path.GetFileName(filePath);
            var location = $"{fileName}:{lineNumber} ({memberName})";
            var fullContext = string.IsNullOrEmpty(context)
                ? location
                : $"{context} at {location}";

            // Log the exception locally first
            LoggerService.LogException("ExceptionHandler", ex, fullContext);

            // Send bug report if service is available
            if (_bugReportService != null)
            {
                _ = _bugReportService.SendBugReportAsync(fullContext, ex, $"Exception captured in {memberName}");
            }
        }
        catch (Exception handlerEx)
        {
            // If reporting fails, at least try to log it
            System.Diagnostics.Debug.WriteLine($"Failed to report exception: {handlerEx.Message}");
        }
    }

    #endregion
}
