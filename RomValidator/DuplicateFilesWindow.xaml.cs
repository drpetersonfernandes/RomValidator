using System.Collections.ObjectModel;
using System.Windows;

namespace RomValidator;

public partial class DuplicateFilesWindow
{
    public class DuplicateGroup
    {
        public string Hash { get; set; } = string.Empty;
        public string Filenames { get; set; } = string.Empty;
    }

    public DuplicateFilesWindow()
    {
        InitializeComponent();
    }

    public void SetDuplicateData(int totalDuplicates, Dictionary<string, List<string>> hashToFilenames, string? customTitle = null)
    {
        if (!string.IsNullOrEmpty(customTitle))
        {
            Title = customTitle;
        }

        DuplicateCountText.Text = $"{totalDuplicates} duplicate group(s) found";

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

        DuplicateListView.ItemsSource = duplicateGroups;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}