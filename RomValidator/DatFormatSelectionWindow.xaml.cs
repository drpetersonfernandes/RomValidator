using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RomValidator;

public enum DatFormatType
{
    NoIntro,
    ClrMamePro,
    AutoDetect
}

public partial class DatFormatSelectionWindow
{
    public DatFormatType SelectedFormat { get; private set; } = DatFormatType.AutoDetect;

    public DatFormatSelectionWindow()
    {
        InitializeComponent();
    }

    private void NoIntroBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        NoIntroRadioButton.IsChecked = true;
    }

    private void ClrMameProBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ClrMameProRadioButton.IsChecked = true;
    }

    private void AutoDetectBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        AutoDetectRadioButton.IsChecked = true;
    }

    private void FormatRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        SelectedFormat = sender switch
        {
            RadioButton rb when rb == NoIntroRadioButton => DatFormatType.NoIntro,
            RadioButton rb when rb == ClrMameProRadioButton => DatFormatType.ClrMamePro,
            _ => DatFormatType.AutoDetect
        };
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
