using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using RomValidator.Services;

namespace RomValidator;

public partial class MainWindow : IDisposable
{
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
        _ = _applicationStatsService.RecordUsageAsync();

        // Initialize Pages
        _validatePage = new ValidatePage(this);
        _generateDatPage = new GenerateDatPage(this);

        // Load initial page
        MainContentFrame.Navigate(_validatePage);
        UpdateActivePageIndicator(_validatePage);
    }

    private void UpdateActivePageIndicator(object activePage)
    {
        var activeBrush = new SolidColorBrush(Colors.White);
        var inactiveBrush = Brushes.Transparent;

        if (Equals(activePage, _validatePage))
        {
            ValidateRomsButton.BorderThickness = new Thickness(0, 0, 0, 3);
            ValidateRomsButton.BorderBrush = activeBrush;
            ValidateRomsButton.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5));
            GenerateDatButton.BorderThickness = new Thickness(0);
            GenerateDatButton.BorderBrush = inactiveBrush;
            GenerateDatButton.Background = (Brush)FindResource("AccentBlueBrush");
        }
        else if (Equals(activePage, _generateDatPage))
        {
            GenerateDatButton.BorderThickness = new Thickness(0, 0, 0, 3);
            GenerateDatButton.BorderBrush = activeBrush;
            GenerateDatButton.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5));
            ValidateRomsButton.BorderThickness = new Thickness(0);
            ValidateRomsButton.BorderBrush = inactiveBrush;
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
        BugReportService.Dispose();
        VersionChecker.Dispose();
        _applicationStatsService.Dispose();
        _validatePage.Dispose();
        _generateDatPage.Dispose();
        GC.SuppressFinalize(this);
    }
}