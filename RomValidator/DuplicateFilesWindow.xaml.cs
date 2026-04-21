using System.Collections.ObjectModel;
using System.Windows;

namespace RomValidator;

public class DuplicateGroup
{
    public string Hash { get; set; } = string.Empty;
    public string Filenames { get; set; } = string.Empty;
}

public partial class DuplicateFilesWindow
{
    public DuplicateFilesWindow()
    {
        InitializeComponent();
    }

    public void SetDuplicateData(Dictionary<string, List<string>> hashToFilenames, string? customTitle = null)
    {
        if (!string.IsNullOrEmpty(customTitle))
        {
            Title = customTitle;
        }

        var duplicateGroups = new ObservableCollection<DuplicateGroup>();
        foreach (var kvp in hashToFilenames)
        {
            if (kvp.Value.Count > 1)
            {
                duplicateGroups.Add(new DuplicateGroup
                {
                    Hash = kvp.Key,
                    Filenames = string.Join(", ", kvp.Value)
                });
            }
        }

        DuplicateCountText.Text = $"{duplicateGroups.Count} duplicate group(s) found";
        DuplicateListView.ItemsSource = duplicateGroups;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}