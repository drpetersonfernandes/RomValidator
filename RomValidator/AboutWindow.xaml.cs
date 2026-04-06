using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using RomValidator.Services;

namespace RomValidator;

public partial class AboutWindow
{
    public BugReportService BugReportService { get; }

    public AboutWindow(BugReportService bugReportService)
    {
        BugReportService = bugReportService;
        InitializeComponent();
        AppVersionTextBlock.Text = $"Version: {GetApplicationVersion()}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _ = BugReportService.SendBugReportAsync($"Error opening browser for URL: {e.Uri.AbsoluteUri}", ex);
            MessageBox.Show($"Unable to open browser: {ex.Message}", "Error", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        e.Handled = true;
    }

    private static string GetApplicationVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version?.ToString() ?? "Unknown";
    }
}