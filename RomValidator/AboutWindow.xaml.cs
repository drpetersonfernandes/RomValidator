using System.Reflection;
using System.Windows;

namespace RomValidator;

public partial class AboutWindow
{
    public AboutWindow()
    {
        InitializeComponent();
        AppVersionTextBlock.Text = $"Version: {GetApplicationVersion()}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string GetApplicationVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version?.ToString() ?? "Unknown";
    }
}