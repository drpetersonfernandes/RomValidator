using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using RomValidator.Services;

namespace RomValidator;

public partial class MainWindow : IDisposable
{
    // Cached brushes for UI consistency (PERF fix)
    private static readonly SolidColorBrush SActiveBrush = new(Colors.White);
    private static readonly SolidColorBrush SActiveBackgroundBrush = new(Color.FromRgb(0x1E, 0x88, 0xE5));
    private static readonly Brush SInactiveBrush = Brushes.Transparent;

    // Services
    public BugReportService BugReportService { get; }
    public GitHubVersionChecker VersionChecker { get; }
    private readonly ApplicationStatsService _applicationStatsService;

    // Pages
    private readonly ValidatePage _validatePage;
    private readonly GenerateDatPage _generateDatPage;

    public MainWindow()
    {
        InitializeComponent();

        // Initialize Services
        const string apiUrl = "https://www.purelogiccode.com/bugreport/api/send-bug-report";
        const string apiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";
        const string applicationName = "ROM Validator";
        BugReportService = new BugReportService(apiUrl, apiKey, applicationName);
        LoggerService.SetBugReportService(BugReportService);
        VersionChecker = new GitHubVersionChecker("drpetersonfernandes", "RomValidator", BugReportService);

        // Initialize Application Stats Service
        const string statsBaseUrl = "https://www.purelogiccode.com/ApplicationStats";
        const string statsApplicationId = "rom-validator";
        _applicationStatsService = new ApplicationStatsService(statsBaseUrl, apiKey, statsApplicationId);
        _ = _applicationStatsService.RecordUsageAsync().ContinueWith(static t =>
        {
            if (t.IsFaulted)
            {
                LoggerService.LogError("Startup", $"Stats recording failed: {t.Exception?.InnerException?.Message}");
            }
        }, TaskContinuationOptions.OnlyOnFaulted);

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

    public void UpdateStatusBarMessage(string message)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            StatusBarMessageTextBlock.Text = message;
        });
    }

    private void ValidateRoms_Click(object sender, RoutedEventArgs e)
    {
        if (!Equals(MainContentFrame.Content, _validatePage))
        {
            MainContentFrame.Navigate(_validatePage);
            UpdateActivePageIndicator(_validatePage);
        }
    }

    private void GenerateDat_Click(object sender, RoutedEventArgs e)
    {
        if (!Equals(MainContentFrame.Content, _generateDatPage))
        {
            MainContentFrame.Navigate(_generateDatPage);
            UpdateActivePageIndicator(_generateDatPage);
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new AboutWindow(BugReportService) { Owner = this };
        aboutWindow.ShowDialog();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        Dispose();
    }

    public void Dispose()
    {
        // Dispose services individually with error handling to prevent cascade failures
        try
        {
            BugReportService.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BugReportService dispose error: {ex.Message}");
            _ = BugReportService.SendBugReportAsync("Error disposing BugReportService", ex);
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

        try
        {
            _applicationStatsService.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ApplicationStatsService dispose error: {ex.Message}");
            _ = BugReportService.SendBugReportAsync("Error disposing ApplicationStatsService", ex);
        }

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

        GC.SuppressFinalize(this);
    }
}