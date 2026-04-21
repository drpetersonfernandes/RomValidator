using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using RomValidator.Services;
using SharpSevenZip;

namespace RomValidator;

/// <summary>
/// Application class with global exception handling to ensure all bugs are reported.
/// </summary>
public partial class App
{
    private BugReportService? _bugReportService;
    private ApplicationStatsService? _applicationStatsService;
    private static CancellationTokenSource? _globalCancellationTokenSource;

    public App()
    {
        // Initialize global cancellation token source
        _globalCancellationTokenSource = new CancellationTokenSource();

        // Initialize bug report service first so we can report any initialization issues
        InitializeBugReportService();

        // Initialize application stats service
        InitializeApplicationStatsService();

        // Initialize SharpSevenZip library path
        InitializeSevenZipLibrary();

        // Subscribe to global exception handlers
        SetupGlobalExceptionHandling();
    }

    /// <summary>
    /// Initializes the SharpSevenZip native library path based on system architecture.
    /// Supports win-x64 and win-arm64.
    /// </summary>
    private void InitializeSevenZipLibrary()
    {
        try
        {
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string libraryPath;
            string architectureName;

            // Detect architecture and set appropriate library path
            if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
            {
                libraryPath = Path.Combine(appDirectory, "7z_arm64.dll");
                architectureName = "ARM64";
            }
            else
            {
                // Default to x64 for x64 and other architectures
                libraryPath = Path.Combine(appDirectory, "7z_x64.dll");
                architectureName = "x64";
            }

            if (File.Exists(libraryPath))
            {
                SharpSevenZipBase.SetLibraryPath(libraryPath);
            }
            else
            {
                // DLL is missing - report to developer and inform user
                var errorMessage = $"7z DLL not found for {architectureName} architecture at: {libraryPath}";
                System.Diagnostics.Debug.WriteLine($"Critical Error: {errorMessage}");

                // Log the error
                LoggerService.LogError("MissingSevenZipDll", errorMessage);

                // Report to developer via bug report service (it may be null if not initialized yet)
                if (_bugReportService != null)
                {
                    var missingDllException = new FileNotFoundException(errorMessage, libraryPath);
                    _ = _bugReportService.SendBugReportAsync("Missing 7z DLL", missingDllException, "The 7z native library DLL is missing from the application installation");
                }

                // Show user-friendly error dialog
                ShowMissingSevenZipDllDialog(libraryPath, architectureName);
            }
        }
        catch (Exception ex)
        {
            // If initialization fails, log but don't crash - SharpSevenZip may still work with auto-detection
            System.Diagnostics.Debug.WriteLine($"Failed to initialize SharpSevenZip library path: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows a user-friendly error dialog when the 7z DLL is missing.
    /// </summary>
    private static void ShowMissingSevenZipDllDialog(string missingLibraryPath, string architectureName)
    {
        try
        {
            var dialogMessage = $"The required 7-Zip library (7z_{architectureName}.dll) is missing from the application.\n\n" +
                                "This file is essential for the application to work with archive files.\n\n" +
                                "Missing file location:\n" +
                                missingLibraryPath + "\n\n" +
                                "Please reinstall the application to fix this issue.\n\n" +
                                "If the problem persists, please contact support.";

            MessageBox.Show(
                dialogMessage,
                "Critical Error - Missing Required Component",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // If we can't show the dialog, just ignore - we've already tried to log/report the issue
        }
    }

    /// <summary>
    /// Initializes the bug report service with the API configuration.
    /// </summary>
    private void InitializeBugReportService()
    {
        try
        {
            const string apiUrl = "https://www.purelogiccode.com/bugreport/api/send-bug-report";
            const string apiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";
            const string applicationName = "ROM Validator";
            _bugReportService = new BugReportService(apiUrl, apiKey, applicationName);
        }
        catch (Exception ex)
        {
            // If we can't create the bug report service, log to debug output
            System.Diagnostics.Debug.WriteLine($"Failed to initialize BugReportService: {ex.Message}");
        }
    }

    /// <summary>
    /// Initializes the application stats service to track application usage.
    /// </summary>
    private void InitializeApplicationStatsService()
    {
        try
        {
            const string statsBaseUrl = "https://www.purelogiccode.com/ApplicationStats";
            const string apiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";
            const string statsApplicationId = "rom-validator";
            _applicationStatsService = new ApplicationStatsService(statsBaseUrl, apiKey, statsApplicationId);
        }
        catch (Exception ex)
        {
            // If we can't create the stats service, log to debug output
            System.Diagnostics.Debug.WriteLine($"Failed to initialize ApplicationStatsService: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets up global exception handlers for both UI and non-UI thread exceptions.
    /// </summary>
    private void SetupGlobalExceptionHandling()
    {
        // Handle exceptions from the UI thread (Dispatcher)
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Handle exceptions from non-UI threads (AppDomain)
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        // Handle unobserved task exceptions
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// Records application usage statistics on startup.
    /// </summary>
    private void RecordApplicationUsage()
    {
        try
        {
            if (_applicationStatsService != null)
            {
                // Record usage asynchronously without blocking startup
                _ = _applicationStatsService.RecordUsageAsync().ContinueWith(static t =>
                {
                    if (t.IsFaulted)
                    {
                        LoggerService.LogError("Startup", $"Stats recording failed: {t.Exception?.InnerException?.Message}");
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to record application usage: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles unhandled exceptions from the UI thread (Dispatcher).
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true; // Prevent application from crashing

        try
        {
            var ex = e.Exception;
            const string context = "Global Dispatcher Exception";

            // Log the error locally first
            LoggerService.LogError("GlobalDispatcherException", $"Unhandled exception in UI thread: {ex.Message}");

            // Send bug report
            if (_bugReportService != null)
            {
                _ = _bugReportService.SendBugReportAsync(context, ex, "Unhandled exception in UI/Dispatcher thread");
            }

            // Show user-friendly error message
            ShowFatalErrorDialog(ex, "An error occurred in the application. The error has been reported.");
        }
        catch (Exception handlerEx)
        {
            // If the exception handler itself fails, try to log it
            System.Diagnostics.Debug.WriteLine($"Exception in global handler: {handlerEx.Message}");
        }
    }

    /// <summary>
    /// Handles unhandled exceptions from non-UI threads (AppDomain).
    /// </summary>
    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            if (e.ExceptionObject is Exception ex)
            {
                const string context = "Global AppDomain Exception";
                var isTerminating = e.IsTerminating ? "Application will terminate" : "Application continuing";

                // Log the error locally first
                LoggerService.LogError("GlobalAppDomainException", $"Unhandled exception in non-UI thread: {ex.Message}. {isTerminating}");

                // Send bug report
                if (_bugReportService != null)
                {
                    _ = _bugReportService.SendBugReportAsync(context, ex, $"Unhandled exception in non-UI thread. {isTerminating}");
                }

                // If the application is terminating, show a fatal error dialog
                if (e.IsTerminating)
                {
                    ShowFatalErrorDialog(ex, "A fatal error occurred in the application. The application will now close.");
                }
            }
        }
        catch (Exception handlerEx)
        {
            // If the exception handler itself fails, try to log it
            System.Diagnostics.Debug.WriteLine($"Exception in global handler: {handlerEx.Message}");
        }
    }

    /// <summary>
    /// Handles unobserved task exceptions (TaskScheduler).
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            var ex = e.Exception;
            const string context = "Global Task Exception";

            // Log the error locally first
            LoggerService.LogError("GlobalTaskException", $"Unobserved task exception: {ex.Message}");

            // Send bug report
            if (_bugReportService != null)
            {
                _ = _bugReportService.SendBugReportAsync(context, ex, "Unobserved task exception from TaskScheduler");
            }

            // Mark as observed to prevent application crash
            e.SetObserved();
        }
        catch (Exception handlerEx)
        {
            // If the exception handler itself fails, try to log it
            System.Diagnostics.Debug.WriteLine($"Exception in task exception handler: {handlerEx.Message}");
        }
    }

    /// <summary>
    /// Shows a user-friendly fatal error dialog.
    /// </summary>
    private static void ShowFatalErrorDialog(Exception ex, string message)
    {
        try
        {
            var dialogMessage = $"{message}\n\nError: {ex.Message}\n\nType: {ex.GetType().Name}\n\nThe error details have been sent for analysis.";

            MessageBox.Show(
                dialogMessage,
                "Application Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // If we can't show the dialog, just ignore - we've already tried to report the bug
        }
    }

    /// <summary>
    /// Gets the global BugReportService instance for use throughout the application.
    /// </summary>
    public BugReportService? GetBugReportService()
    {
        return _bugReportService;
    }

    /// <summary>
    /// Gets the global ApplicationStatsService instance for use throughout the application.
    /// </summary>
    public ApplicationStatsService? GetApplicationStatsService()
    {
        return _applicationStatsService;
    }

    /// <summary>
    /// Override OnStartup to record application usage.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RecordApplicationUsage();
    }

    /// <summary>
    /// Override OnExit to clean up the BugReportService.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _bugReportService?.Dispose();
            _bugReportService = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error disposing BugReportService: {ex.Message}");
        }

        try
        {
            _applicationStatsService?.Dispose();
            _applicationStatsService = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error disposing ApplicationStatsService: {ex.Message}");
        }

        try
        {
            _globalCancellationTokenSource?.Dispose();
            _globalCancellationTokenSource = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error disposing global cancellation token source: {ex.Message}");
        }

        base.OnExit(e);
    }

    /// <summary>
    /// Gets the global cancellation token for application shutdown.
    /// </summary>
    public static CancellationToken GetGlobalCancellationToken()
    {
        return _globalCancellationTokenSource?.Token ?? CancellationToken.None;
    }

    /// <summary>
    /// Cancels all ongoing operations and initiates application shutdown.
    /// </summary>
    public static void CancelAllOperations()
    {
        _globalCancellationTokenSource?.Cancel();
    }
}
