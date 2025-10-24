using System.ComponentModel;
using System.Windows;
using RomValidator.Services;

namespace RomValidator;

public partial class MainWindow : IDisposable
{
    // Services
    public BugReportService BugReportService { get; private set; }
    public GitHubVersionChecker VersionChecker { get; private set; }

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
        VersionChecker = new GitHubVersionChecker("drpetersonfernandes", "RomValidator");

        // Initialize Pages
        _validatePage = new ValidatePage(this);
        _generateDatPage = new GenerateDatPage(this);

        // Load initial page
        MainContentFrame.Navigate(_validatePage);
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
        }
    }

    private void GenerateDat_Click(object sender, RoutedEventArgs e)
    {
        if (!Equals(MainContentFrame.Content, _generateDatPage))
        {
            MainContentFrame.Navigate(_generateDatPage);
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new AboutWindow { Owner = this };
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
        BugReportService?.Dispose();
        VersionChecker?.Dispose();
        _validatePage?.Dispose();
        _generateDatPage?.Dispose();
        GC.SuppressFinalize(this);
    }
}