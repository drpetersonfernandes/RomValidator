using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
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
    // Compiled regex for sanitizing filenames (Issue C fix)
    private static readonly Regex InvalidFileNameCharsRegex = new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]", RegexOptions.Compiled);
    private const int MaxUiDisplayItems = 500; // Limit UI list size to prevent lag (Issue 8 fix)

    private readonly MainWindow _mainWindow;
    private CancellationTokenSource? _cts;
    private readonly object _ctsLock = new();
    private readonly ObservableCollection<GameFile> _fileDataCollection = [];
    private readonly List<GameFile> _processedFilesList = [];
    private int _processedFileCount;
    private readonly object _operationLock = new();

    // Batching for UI updates (Issue A fix)
    private readonly List<GameFile> _uiUpdateBuffer = [];
    private DispatcherTimer? _uiUpdateTimer;

    public GenerateDatPage(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeComponent();
        HashListView.ItemsSource = _fileDataCollection;
        UpdateFileCountText(0);

        // Initialize UI update timer for batching (Issue A fix)
        _uiUpdateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _uiUpdateTimer.Tick += UIUpdateTimer_Tick;

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
        CancellationTokenSource? operationCts = null; // Capture variable for this operation

        try
        {
            if (string.IsNullOrEmpty(FolderTextBox.Text) || !Directory.Exists(FolderTextBox.Text))
            {
                MessageBox.Show(_mainWindow, "Please select a valid folder first.", "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ClearAll();

            // Cancel previous operation and create new CancellationTokenSource
            lock (_ctsLock)
            {
                _cts?.Cancel(); // Signal cancellation to any running operation
                _cts = new CancellationTokenSource();
                operationCts = _cts; // Capture the instance for this operation
            }

            // Batch UI updates instead of invoking for each file (Issue A fix)
            var progress = new Progress<GameFile>(update =>
            {
                lock (_uiUpdateBuffer)
                {
                    _uiUpdateBuffer.Add(update);
                }

                // Start timer if not already running
                if (_uiUpdateTimer is { IsEnabled: false })
                {
                    _uiUpdateTimer.Start();
                }
            });

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            ExportDatButton.IsEnabled = false;
            _mainWindow.UpdateStatusBarMessage("Hashing in progress...");

            // Use the captured CTS instance
            await HashFilesAsync(FolderTextBox.Text, progress, operationCts.Token);

            if (!operationCts.IsCancellationRequested)
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

            // Dispose only this operation's CTS instance
            lock (_ctsLock)
            {
                // Only clear field if it's still pointing to our instance
                if (_cts == operationCts)
                {
                    _cts = null;
                }
            }

            operationCts?.Dispose();
        }
    }

    // Timer tick handler for batch UI updates (Issue A fix)
    private void UIUpdateTimer_Tick(object? sender, EventArgs e)
    {
        lock (_uiUpdateBuffer)
        {
            if (_uiUpdateBuffer.Count > 0)
            {
                var totalProcessed = _processedFilesList.Count;

                foreach (var item in _uiUpdateBuffer)
                {
                    _fileDataCollection.Add(item);
                    if (_fileDataCollection.Count > MaxUiDisplayItems)
                        _fileDataCollection.RemoveAt(0);
                }

                UpdateFileCountText(totalProcessed);
                if (HashProgressBar.Maximum > 0)
                {
                    HashProgressBar.Value = totalProcessed;
                    ProgressText.Text = $"{totalProcessed} / {(int)HashProgressBar.Maximum}";
                }

                _uiUpdateBuffer.Clear();
            }

            _uiUpdateTimer?.Stop();
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
        var totalRomCount = 0;
        var discoveredFilesCount = 0;

        // Start a background task to count files so the progress bar Max updates dynamically
        // without blocking the start of hashing (Issue 7 fix)
        _ = Task.Run(() =>
        {
            try
            {
                foreach (var _ in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var currentDiscovered = Interlocked.Increment(ref discoveredFilesCount);
                    Dispatcher.InvokeAsync(() => { HashProgressBar.Maximum = Math.Max(HashProgressBar.Maximum, currentDiscovered); });
                }
            }
            catch
            {
                /* Ignore enumeration errors in background count */
            }
        }, cancellationToken);

        // Stream the files instead of calling ToList() (Issue 7 fix)
        var fileEnumerable = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories);

        // Sequential processing
        foreach (var filePath in fileEnumerable)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var gameFiles = await HashCalculator.CalculateHashesAsync(filePath, cancellationToken);
            var romsFromFile = gameFiles.Count;

            if (romsFromFile > 1)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    HashProgressBar.Maximum += romsFromFile - 1;
                });
            }

            foreach (var gameFile in gameFiles)
            {
                if (gameFile.ErrorMessage != null)
                {
                    // Log error to bug report service or UI
                    if (gameFile.ErrorMessage != "File is locked or access denied after retries")
                    {
                        _ = _mainWindow.BugReportService.SendBugReportAsync($"Error hashing file {filePath}: {gameFile.ErrorMessage}");
                    }
                }

                lock (_operationLock)
                {
                    _processedFilesList.Add(gameFile);
                }

                progress.Report(gameFile);
                Interlocked.Increment(ref totalRomCount);
            }
        }

        _processedFileCount = totalRomCount;
    }

    private static string SanitizeFileName(string name)
    {
        return InvalidFileNameCharsRegex.Replace(name, "_");
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

                _mainWindow.UpdateStatusBarMessage("Serializing and saving DAT file...");
                await using var writer = XmlWriter.Create(saveFileDialog.FileName, settings);
                await Task.Run(() => serializer.Serialize(writer, dataFile)); // Offload sync serialization (Issue 12 fix)

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
            _uiUpdateTimer?.Stop();
            lock (_uiUpdateBuffer)
            {
                _uiUpdateBuffer.Clear();
            }

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
        _uiUpdateTimer?.Stop();
        _uiUpdateTimer = null;
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}