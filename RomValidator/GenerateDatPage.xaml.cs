using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;
using RomValidator.Models;
using RomValidator.Services;
using Header = RomValidator.Models.Header;

namespace RomValidator;

public partial class GenerateDatPage : IDisposable
{
    private readonly MainWindow _mainWindow;
    private CancellationTokenSource? _cts;
    private readonly object _ctsLock = new();
    private readonly ObservableCollection<GameFile> _fileDataCollection = [];
    private readonly List<GameFile> _processedFilesList = [];
    private int _processedFileCount;
    private readonly object _operationLock = new();

    public GenerateDatPage(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeComponent();
        HashListView.ItemsSource = _fileDataCollection;
        UpdateFileCountText(0);
        _mainWindow.UpdateStatusBarMessage("Ready to generate DAT file.");
    }

    private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folderDialog = new OpenFolderDialog { Title = "Select folder to hash", Multiselect = false };
        if (folderDialog.ShowDialog() != true || string.IsNullOrEmpty(folderDialog.FolderName)) return;

        ResetPage();
        FolderTextBox.Text = folderDialog.FolderName;
        NameTextBox.Text = new DirectoryInfo(folderDialog.FolderName).Name;
        StartButton.IsEnabled = true;
        _mainWindow.UpdateStatusBarMessage($"Folder selected: {folderDialog.FolderName}");
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(FolderTextBox.Text) || !Directory.Exists(FolderTextBox.Text))
            {
                MessageBox.Show(_mainWindow, "Please select a valid folder first.", "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                ClearAll();
                lock (_ctsLock)
                {
                    _cts = new CancellationTokenSource();
                }

                var progress = new Progress<GameFile>(update =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _fileDataCollection.Add(update);
                        UpdateFileCountText(_fileDataCollection.Count);
                        if (HashProgressBar.Maximum > 0)
                        {
                            HashProgressBar.Value = _fileDataCollection.Count;
                            ProgressText.Text = $"{_fileDataCollection.Count} / {(int)HashProgressBar.Maximum}";
                        }
                    });
                });

                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                ExportDatButton.IsEnabled = false;
                _mainWindow.UpdateStatusBarMessage("Hashing in progress...");

                await HashFilesAsync(FolderTextBox.Text, progress, _cts.Token);

                if (!_cts.IsCancellationRequested)
                {
                    MessageBox.Show(_mainWindow, $"Hashing complete! {_processedFileCount} files processed.", "Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    lock (_operationLock)
                    {
                        ExportDatButton.IsEnabled = _processedFilesList.Count > 0;
                    }

                    _mainWindow.UpdateStatusBarMessage($"Hashing complete. {_processedFileCount} files processed.");
                }
                else
                {
                    MessageBox.Show(_mainWindow, "Operation was cancelled", "Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
                    ExportDatButton.IsEnabled = false;
                    _mainWindow.UpdateStatusBarMessage("Hashing cancelled.");
                }
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show(_mainWindow, "Operation was cancelled", "Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
                ExportDatButton.IsEnabled = false;
                _mainWindow.UpdateStatusBarMessage("Hashing cancelled.");
            }
            catch (Exception ex)
            {
                _ = _mainWindow.BugReportService.SendBugReportAsync($"Error during hashing operation for folder: {FolderTextBox.Text}", ex);
                MessageBox.Show(_mainWindow, $"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _mainWindow.UpdateStatusBarMessage("Error during hashing.");
            }
            finally
            {
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                lock (_ctsLock)
                {
                    _cts?.Dispose();
                    _cts = null;
                }
            }
        }
        catch (Exception ex)
        {
            _ = _mainWindow.BugReportService.SendBugReportAsync($"Error during hashing operation for folder: {FolderTextBox.Text}", ex);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        lock (_ctsLock)
        {
            _cts?.Cancel();
        }

        StopButton.IsEnabled = false;
    }

    private async Task HashFilesAsync(string folderPath, IProgress<GameFile> progress, CancellationToken cancellationToken)
    {
        var files = await Task.Run(() => Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories).ToList(), cancellationToken);
        _processedFileCount = files.Count;

        // Track total ROMs processed to adjust progress bar for archives
        int totalRomCount = 0;
        
        await Dispatcher.InvokeAsync(() =>
        {
            HashProgressBar.Maximum = files.Count;
            ProgressText.Text = $"0 / {files.Count}";
        });

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken };

        await Parallel.ForEachAsync(files, parallelOptions, async (filePath, token) =>
        {
            var gameFiles = await HashCalculator.CalculateHashesAsync(filePath, token);
            var romsFromFile = gameFiles.Count;
            
            // Adjust progress bar maximum if this archive contains multiple ROMs
            if (romsFromFile > 1)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    HashProgressBar.Maximum += romsFromFile - 1;
                });
            }
            
            foreach (var gameFile in gameFiles)
            {
                if (gameFile.ErrorMessage != null && 
                    gameFile.ErrorMessage != "File is locked or access denied after retries" &&
                    !gameFile.ErrorMessage.StartsWith("Extracted from:"))
                {
                    _ = _mainWindow.BugReportService.SendBugReportAsync($"Error hashing file {filePath}: {gameFile.ErrorMessage}");
                }

                lock (_operationLock)
                {
                    _processedFilesList.Add(gameFile);
                }

                progress.Report(gameFile);
                totalRomCount++;
            }
        });
        
        // Update final count
        _processedFileCount = totalRomCount;
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = new string(Path.GetInvalidFileNameChars());
        var regex = new Regex($"[{Regex.Escape(invalidChars)}]");
        return regex.Replace(name, "_");
    }

    private async void ExportDatButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            lock (_operationLock)
            {
                if (_processedFilesList.Count == 0)
                {
                    MessageBox.Show(_mainWindow, "No files have been processed to export.", "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var sanitizedName = SanitizeFileName(NameTextBox.Text);
            if (string.IsNullOrWhiteSpace(sanitizedName))
            {
                sanitizedName = "Exported-DAT";
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "DAT File (*.dat)|*.dat|All Files (*.*)|*.*",
                DefaultExt = "dat",
                AddExtension = true,
                FileName = $"{sanitizedName} ({DateTime.Now:yyyyMMdd-HHmmss}).dat",
                Title = "Save DAT File"
            };

            if (saveFileDialog.ShowDialog() != true) return;

            try
            {
                var dataFile = new Datafile
                {
                    Header = new Header
                    {
                        Name = NameTextBox.Text,
                        Description = DescriptionTextBox.Text,
                        Author = AuthorTextBox.Text,
                        Version = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                    }
                };

                List<GameFile> filesToExport;
                lock (_operationLock)
                {
                    filesToExport = new List<GameFile>(_processedFilesList);
                }

                // Group files by GameName to create proper No-Intro DAT format
                dataFile.Games.AddRange(filesToExport
                    .Where(static file => file.ErrorMessage == null)
                    .GroupBy(static file => file.GameName)
                    .Select(static group => new Game
                    {
                        Name = group.Key,
                        Description = group.Key,
                        Roms = group.Select(static file => new Rom
                        {
                            Name = file.FileName,
                            Size = file.FileSize,
                            Crc = file.Crc32,
                            Md5 = file.Md5,
                            Sha1 = file.Sha1,
                            Sha256 = file.Sha256
                        }).ToList()
                    }));

                var serializer = new XmlSerializer(typeof(Datafile));
                var settings = new XmlWriterSettings { Indent = true, IndentChars = "\t", Encoding = new UTF8Encoding(false), Async = true };

                await using var writer = XmlWriter.Create(saveFileDialog.FileName, settings);
                serializer.Serialize(writer, dataFile);

                var result = MessageBox.Show(_mainWindow, "DAT file exported successfully! Would you like to open it?", "Export Complete", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo { FileName = saveFileDialog.FileName, UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                _ = _mainWindow.BugReportService.SendBugReportAsync("Error exporting DAT file.", ex);
                MessageBox.Show(_mainWindow, $"Error exporting file: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _ = _mainWindow.BugReportService.SendBugReportAsync("Error exporting DAT file.", ex);
            MessageBox.Show(_mainWindow, $"Error exporting file: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateFileCountText(int count)
    {
        FileCountText.Text = count == 0 ? "No files processed" : $"{count} files processed";
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        ResetPage();
    }

    public void ResetPage()
    {
        lock (_ctsLock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        ClearAll();
        FolderTextBox.Text = string.Empty;
        NameTextBox.Text = string.Empty;
        DescriptionTextBox.Text = string.Empty;
        AuthorTextBox.Text = string.Empty;
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        ExportDatButton.IsEnabled = false;
        _mainWindow.UpdateStatusBarMessage("Ready to generate DAT file.");
    }

    private void ClearAll()
    {
        _fileDataCollection.Clear();
        lock (_operationLock)
        {
            _processedFilesList.Clear();
        }

        HashProgressBar.Value = 0;
        ProgressText.Text = "";
        _processedFileCount = 0;
        UpdateFileCountText(0);
        ExportDatButton.IsEnabled = false;
    }

    public void Dispose()
    {
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}