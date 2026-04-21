using System.Collections.ObjectModel;
using System.Windows;

namespace RomValidator;

/// <summary>
/// Represents a group of duplicate files sharing the same hash value.
/// Used for displaying duplicate file information in the UI.
/// </summary>
public class DuplicateGroup
{
    /// <summary>Gets or sets the hash value shared by duplicate files.</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>Gets or sets the concatenated list of filenames sharing the same hash.</summary>
    public string Filenames { get; set; } = string.Empty;
}

public partial class DuplicateFilesWindow
{
    /// <summary>
    /// Initializes a new instance of the DuplicateFilesWindow class.
    /// </summary>
    public DuplicateFilesWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the duplicate file data to display in the window.
    /// </summary>
    /// <param name="hashToFilenames">Dictionary mapping hash values to lists of duplicate filenames.</param>
    /// <param name="customTitle">Optional custom title for the window.</param>
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