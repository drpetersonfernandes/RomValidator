using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RomValidator.Pages;
using RomValidator.Services;

namespace RomValidator;

public partial class MainWindow : IDisposable
{
    // Cached brushes for UI consistency (PERF fix)
    private static readonly SolidColorBrush SActiveBrush = new(Color.FromRgb(0xE0, 0xE0, 0xE0));
    private static readonly SolidColorBrush SActiveBackgroundBrush = new(Color.FromRgb(0x2B, 0x8C, 0xFF));
    private static readonly Brush SInactiveBrush = Brushes.Transparent;

    // Services
    /// <summary>Gets the bug report service for error tracking.</summary>
    public BugReportService BugReportService { get; }

    /// <summary>Gets the GitHub version checker for update notifications.</summary>
    public GitHubVersionChecker VersionChecker { get; }

    // Pages
    private readonly ValidatePage _validatePage;
    private readonly GenerateDatPage _generateDatPage;

    /// <summary>
    /// Initializes a new instance of the MainWindow class.
    /// Sets up services, exception handling, and application pages.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // Reuse the BugReportService from App to avoid duplicate HttpClient instances
        BugReportService = ((App)Application.Current).GetBugReportService()
            ?? throw new InvalidOperationException("BugReportService must be initialized before MainWindow.");

        VersionChecker = new GitHubVersionChecker("drpetersonfernandes", "RomValidator", BugReportService);

        // Initialize Pages
        _validatePage = new ValidatePage(this);
        _generateDatPage = new GenerateDatPage(this);

        // Load initial page
        MainContentFrame.Navigate(_validatePage);
        UpdateActivePageIndicator(_validatePage);
    }

    private void UpdateActivePageIndicator(object activePage)
    {
        if (Equals(activePage, _validatePage))
        {
            ValidateRomsButton.BorderThickness = new Thickness(0, 0, 0, 3);
            ValidateRomsButton.BorderBrush = SActiveBrush;
            ValidateRomsButton.Background = SActiveBackgroundBrush;
            GenerateDatButton.BorderThickness = new Thickness(0);
            GenerateDatButton.BorderBrush = SInactiveBrush;
            GenerateDatButton.Background = (Brush)FindResource("AccentBlueBrush");
        }
        else if (Equals(activePage, _generateDatPage))
        {
            GenerateDatButton.BorderThickness = new Thickness(0, 0, 0, 3);
            GenerateDatButton.BorderBrush = SActiveBrush;
            GenerateDatButton.Background = SActiveBackgroundBrush;
            ValidateRomsButton.BorderThickness = new Thickness(0);
            ValidateRomsButton.BorderBrush = SInactiveBrush;
            ValidateRomsButton.Background = (Brush)FindResource("AccentBlueBrush");
        }
    }

    /// <summary>
    /// Updates the status bar message asynchronously from any thread.
    /// </summary>
    /// <param name="message">The message to display in the status bar.</param>
    /// <param name="cancellationToken">Cancellation token to stop the operation.</param>
    public async Task UpdateStatusBarMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                StatusBarMessageTextBlock.Text = message;
            });
        }
        catch (OperationCanceledException)
        {
            // Operation was cancelled, ignore
        }
        catch (Exception ex)
        {
            // Log the exception but don't crash the application
            System.Diagnostics.Debug.WriteLine($"Error updating status bar: {ex.Message}");
            _ = BugReportService.SendBugReportAsync("Error updating status bar", ex);
        }
    }

    /// <summary>
    /// Updates the status bar message synchronously from any thread.
    /// </summary>
    /// <param name="message">The message to display in the status bar.</param>
    public void UpdateStatusBarMessage(string message)
    {
        // Keep the synchronous version for backward compatibility
        // but implement it using the async version with a default cancellation token
        _ = UpdateStatusBarMessageAsync(message);
    }

    private void ValidateRoms_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Equals(MainContentFrame.Content, _validatePage))
            {
                MainContentFrame.Navigate(_validatePage);
                UpdateActivePageIndicator(_validatePage);
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogException("MainWindow", ex, "Error navigating to Validate page");
        }
    }

    private void GenerateDat_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Equals(MainContentFrame.Content, _generateDatPage))
            {
                MainContentFrame.Navigate(_generateDatPage);
                UpdateActivePageIndicator(_generateDatPage);
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogException("MainWindow", ex, "Error navigating to Generate DAT page");
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var aboutWindow = new AboutWindow(BugReportService) { Owner = this };
            aboutWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            LoggerService.LogException("MainWindow", ex, "Error opening About window");
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Close();
        }
        catch (Exception ex)
        {
            LoggerService.LogException("MainWindow", ex, "Error closing application");
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.F8)
            {
                e.Handled = true;
                var filePath = ScreenshotService.CaptureWindowScreenshot(this);
                _ = UpdateStatusBarMessageAsync($"Screenshot saved: {filePath}");
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogException("MainWindow", ex, "Error capturing screenshot");
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        try
        {
            // Don't cancel the closing event, just ensure proper cleanup
            Dispose();
        }
        catch (Exception ex)
        {
            LoggerService.LogException("MainWindow", ex, "Error during window closing");
        }
    }

    /// <summary>
    /// Disposes of resources used by the MainWindow.
    /// Cancels all ongoing operations and disposes of pages and services.
    /// </summary>
    public void Dispose()
    {
        // Cancel all ongoing operations first
        try
        {
            App.CancelAllOperations();
            Thread.Sleep(50);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during shutdown: {ex.Message}");
        }

        // Dispose pages while BugReportService is still alive for error reporting
        try
        {
            _validatePage.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ValidatePage dispose error: {ex.Message}");
            _ = BugReportService.SendBugReportAsync("Error disposing ValidatePage", ex);
        }

        try
        {
            _generateDatPage.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GenerateDatPage dispose error: {ex.Message}");
            _ = BugReportService.SendBugReportAsync("Error disposing GenerateDatPage", ex);
        }

        try
        {
            VersionChecker.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"VersionChecker dispose error: {ex.Message}");
            _ = BugReportService.SendBugReportAsync("Error disposing VersionChecker", ex);
        }

        // Dispose BugReportService last, after all error reporting is complete
        try
        {
            BugReportService.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BugReportService dispose error: {ex.Message}");
        }

        GC.SuppressFinalize(this);
    }
}