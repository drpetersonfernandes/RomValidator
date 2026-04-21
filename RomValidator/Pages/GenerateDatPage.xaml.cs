using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;
using RomValidator.Models;
using RomValidator.Models.NoIntro;
using RomValidator.Services;

namespace RomValidator.Pages;

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

    private int _discoveredFilesCount;
    private int _archiveExpansionCount;

    /// <summary>
    /// Initializes a new instance of the GenerateDatPage class.
    /// </summary>
    /// <param name="mainWindow">The main window instance for status updates.</param>
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

    private async void StartButton_ClickAsync(object sender, RoutedEventArgs e)
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

            // Sync progress bar to actual count so it reaches 100%
            HashProgressBar.Maximum = _processedFileCount;
            HashProgressBar.Value = _processedFileCount;
            ProgressText.Text = $"{_processedFileCount} / {_processedFileCount}";

            if (!operationCts.IsCancellationRequested)
            {
                MessageBox.Show(_mainWindow, $"Hashing complete! {_processedFileCount} files processed.", "Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                lock (_operationLock)
                {
                    ExportDatButton.IsEnabled = _processedFilesList.Count > 0;
                }

                _mainWindow.UpdateStatusBarMessage($"Hashing complete. {_processedFileCount} files processed.");

                // Automatically trigger save dialog after hash calculation
                lock (_operationLock)
                {
                    if (_processedFilesList.Count > 0)
                    {
                        // Use Dispatcher to ensure UI thread execution
                        Dispatcher.InvokeAsync(() => ExportDatButton_ClickAsync(this, new RoutedEventArgs()));
                    }
                }
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
        _discoveredFilesCount = 0;
        _archiveExpansionCount = 0;
        var lastUiUpdate = DateTime.UtcNow;
        const int uiUpdateIntervalMs = 100; // Throttle UI updates to every 100ms (Issue 5 fix)

        // Track duplicates during processing
        var hashToFilenames = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var duplicateGroups = 0;

        // Use EnumerationOptions to ignore inaccessible folders and files (Issue 4 fix)
        var enumerationOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false, // Changed to false for top directory only
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System // Optional: skip hidden/system files for performance
        };

        // Start a background task to count files so we can estimate progress bar Max
        // The actual Maximum will be set atomically in the main loop (Issue 2 & 3 fix)
        _ = Task.Run(() => CountFilesInBackground(folderPath, enumerationOptions, ref _discoveredFilesCount, cancellationToken, _mainWindow.BugReportService), cancellationToken);

        // Stream the files using EnumerationOptions to skip inaccessible items (Issue 4 fix)
        var fileEnumerable = Directory.EnumerateFiles(folderPath, "*", enumerationOptions);

        // Sequential processing
        foreach (var filePath in fileEnumerable)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var gameFiles = await HashCalculator.CalculateHashesAsync(filePath, cancellationToken, _mainWindow.BugReportService);
            var romsFromFile = gameFiles.Count;

            // Read the discovered count from the background task (do NOT increment here —
            // CountFilesInBackground already handles that)
            var currentDiscovered = _discoveredFilesCount;

            // Track how many extra items come from archives (files inside archives minus the archive file itself)
            if (romsFromFile > 1)
            {
                Interlocked.Add(ref _archiveExpansionCount, romsFromFile - 1);
            }

            var currentExpansion = _archiveExpansionCount; // Read is atomic for int

            // Throttle UI updates to prevent flooding the UI thread (Issue 5 fix)
            // Only dispatch to UI thread every 100ms to avoid queuing thousands of operations
            var now = DateTime.UtcNow;
            if ((now - lastUiUpdate).TotalMilliseconds >= uiUpdateIntervalMs)
            {
                lastUiUpdate = now;
                var newMaximum = currentDiscovered + currentExpansion;
                await Dispatcher.InvokeAsync(() =>
                {
                    HashProgressBar.Maximum = newMaximum;
                });
            }

            foreach (var gameFile in gameFiles)
            {
                if (gameFile.ErrorMessage != null)
                {
                    // Log error to bug report service or UI
                    if (!string.Equals(gameFile.ErrorMessage, "File is locked or access denied after retries", StringComparison.Ordinal))
                    {
                        _ = _mainWindow.BugReportService.SendBugReportAsync($"Error hashing file {filePath}: {gameFile.ErrorMessage}");
                    }
                }
                else if (!string.IsNullOrEmpty(gameFile.Sha256))
                {
                        // Check for duplicates (same hash, different filename)
                        lock (hashToFilenames)
                        {
                            if (!hashToFilenames.TryGetValue(gameFile.Sha256, out var filenames))
                            {
                                filenames = new List<string>();
                                hashToFilenames[gameFile.Sha256] = filenames;
                            }

                            // Only add if not already in list (case-insensitive)
                            if (!filenames.Any(f => string.Equals(f, gameFile.FileName, StringComparison.OrdinalIgnoreCase)))
                            {
                                filenames.Add(gameFile.FileName);

                                // If we have more than one filename for this hash, log a warning
                                if (filenames.Count == 2) // First time we detect a duplicate for this hash
                                {
                                    duplicateGroups++;
                                }

                                if (filenames.Count > 1)
                                {
                                    LoggerService.LogWarning("DAT Generation",
                                        $"Duplicate ROM detected: Hash {gameFile.Sha256} has multiple filenames: {string.Join(", ", filenames)}");
                                }
                            }
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

        // Show duplicate warning after hashing completes
        if (duplicateGroups > 0)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                // Filter to only include hashes with multiple filenames
                var duplicateHashToFilenames = hashToFilenames
                    .Where(static kvp => kvp.Value.Count > 1)
                    .ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value);

                var duplicateWindow = new DuplicateFilesWindow
                {
                    Owner = _mainWindow
                };
                duplicateWindow.SetDuplicateData(duplicateHashToFilenames, "Duplicate ROMs Detected");
                duplicateWindow.ShowDialog();
            });
        }
    }

    private static string SanitizeFileName(string name)
    {
        return InvalidFileNameCharsRegex.Replace(name, "_");
    }

    private async void ExportDatButton_ClickAsync(object sender, RoutedEventArgs e)
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

                // Detect filename collisions: same filename but different hashes
                var filenameCollisions = filesToExport
                    .Where(static file => file.ErrorMessage == null)
                    .GroupBy(static file => file.FileName, StringComparer.OrdinalIgnoreCase)
                    .Where(static group => group.Select(static f => f.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                    .ToList();

                if (filenameCollisions.Count > 0)
                {
                    var collisionMessage = new StringBuilder();
                    collisionMessage.AppendLine("WARNING: Filename collisions detected!");
                    collisionMessage.AppendLine("The following ROM filenames have multiple different ROMs (different hashes):");
                    collisionMessage.AppendLine();
                    foreach (var collision in filenameCollisions)
                    {
                        collisionMessage.AppendLine(CultureInfo.InvariantCulture, $"  '{collision.Key}':");
                        foreach (var file in collision)
                        {
                            collisionMessage.AppendLine(CultureInfo.InvariantCulture, $"    - From: {file.ArchiveFileName ?? "(loose file)"}");
                            collisionMessage.AppendLine(CultureInfo.InvariantCulture, $"      SHA256: {file.Sha256}");
                        }

                        collisionMessage.AppendLine();
                    }

                    collisionMessage.AppendLine("These files will overwrite each other in the DAT. Consider renaming them.");

                    _mainWindow.UpdateStatusBarMessage($"Warning: {filenameCollisions.Count} filename collision(s) detected!");
                    MessageBox.Show(_mainWindow, collisionMessage.ToString(), "Filename Collisions Detected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // Note: Duplicate ROM notifications are already shown after hash calculation completes
                // (see HashFilesAsync method lines 323-341), so we don't show them again here

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

                // Serialize with proper No-Intro namespaces
                await Task.Run(() =>
                {
                    var namespaces = new XmlSerializerNamespaces();
                    namespaces.Add("xsi", "http://www.w3.org/2001/XMLSchema-instance");
                    serializer.Serialize(writer, dataFile, namespaces);
                }); // Offload sync serialization (Issue 12 fix)

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

    /// <summary>
    /// Resets the page to its initial state, clearing all data and stopping ongoing operations.
    /// </summary>
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
        HashProgressBar.Maximum = 0; // Reset Maximum to prevent visual issues on subsequent runs (Issue 3 fix)
        ProgressText.Text = "";
        _processedFileCount = 0;
        UpdateFileCountText(0);
        ExportDatButton.IsEnabled = false;
    }

    private static void CountFilesInBackground(string folderPath, EnumerationOptions options, ref int counter, CancellationToken cancellationToken, BugReportService? bugReportService = null)
    {
        try
        {
            foreach (var _ in Directory.EnumerateFiles(folderPath, "*", options))
            {
                if (cancellationToken.IsCancellationRequested) break;

                Interlocked.Increment(ref counter);
            }
        }
        catch (Exception ex)
        {
            _ = bugReportService?.SendBugReportAsync("Error counting files in background.", ex);
        }
    }

    /// <summary>
    /// Disposes of resources used by the GenerateDatPage.
    /// Stops timers and cancels ongoing operations.
    /// </summary>
    public void Dispose()
    {
        // Stop and dispose timer properly
        if (_uiUpdateTimer != null)
        {
            _uiUpdateTimer.Stop();
            _uiUpdateTimer.Tick -= UIUpdateTimer_Tick;
            _uiUpdateTimer = null;
        }

        // Clear UI buffer to release references
        lock (_uiUpdateBuffer)
        {
            _uiUpdateBuffer.Clear();
        }

        // Cancel and dispose CTS with error handling
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, ignore
        }

        GC.SuppressFinalize(this);
    }
}