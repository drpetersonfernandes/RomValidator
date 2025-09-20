using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Windows;
using System.Xml;
using System.Xml.Serialization;
using System.Reflection; // Added for GetExecutingAssembly().GetName().Version

namespace RomValidator;

public partial class MainWindow : IDisposable
{
    private Dictionary<string, Rom> _romDatabase = [];
    private CancellationTokenSource _cts = new();

    // Statistics
    private int _totalFilesToProcess;
    private int _successCount;
    private int _failCount;
    private int _unknownCount;
    private readonly Stopwatch _operationTimer = new();

    // Bug Reporting Service
    private readonly BugReportService? _bugReportService;

    // New: GitHub Version Checker
    private readonly GitHubVersionChecker _versionChecker;
    private const string GitHubRepoOwner = "drpetersonfernandes";
    private const string GitHubRepoName = "RomValidator";


    public MainWindow()
    {
        InitializeComponent();
        DisplayInstructions();

        const string apiUrl = "https://www.purelogiccode.com/bugreport/api/send-bug-report";
        const string apiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";
        const string applicationName = "ROM Validator";
        _bugReportService = new BugReportService(apiUrl, apiKey, applicationName);

        _versionChecker = new GitHubVersionChecker(GitHubRepoOwner, GitHubRepoName); // Initialize version checker

        ClearDatInfoDisplay();
        UpdateStatusBarMessage("Ready."); // Initialize status bar message

        // Check for updates on startup
        _ = CheckForUpdatesOnStartupAsync(); // Fire and forget, does not block UI
    }

    private void DisplayInstructions()
    {
        LogMessage("Welcome to the ROM Validator.");
        LogMessage("This tool validates your ROM files against a standard DAT file to ensure they are accurate and uncorrupted.");
        LogMessage("");
        LogMessage("How it works:");
        LogMessage("- It checks each file's size and hash (SHA1, MD5, or CRC32) against the DAT file.");
        LogMessage("- By default, it will create '_success' and '_fail' subfolders in your ROMs folder.");
        LogMessage("- You can use the checkboxes to disable moving files after validation.");
        LogMessage("");
        LogMessage("Please follow these steps:");
        LogMessage("1. Select the folder containing the ROM files you want to scan.");
        LogMessage("2. Select the corresponding .dat file.");
        LogMessage("3. Choose your file moving preferences using the checkboxes.");
        LogMessage("4. Click 'Start Validation'.");
        LogMessage("");
        LogMessage("--- Ready for validation ---");
        LogMessage("");
    }

    // New method to check for updates on startup
    private async Task CheckForUpdatesOnStartupAsync()
    {
        UpdateStatusBarMessage("Checking for updates...");
        var (isNewVersionAvailable, releaseUrl, latestVersionTag) = await _versionChecker.CheckForNewVersionAsync();

        if (isNewVersionAvailable && releaseUrl != null && latestVersionTag != null)
        {
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
            LogMessage($"A new version ({latestVersionTag}) is available! Your current version is {currentVersion}.");
            UpdateStatusBarMessage($"New version {latestVersionTag} available!");

            var result = MessageBox.Show(
                $"A new version ({latestVersionTag}) of ROM Validator is available!\n\n" +
                $"Your current version: {currentVersion}\n\n" +
                "Would you like to go to the release page to download it?",
                "New Version Available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    ShowError($"Could not open release page: {ex.Message}");
                    _ = _bugReportService?.SendBugReportAsync($"Error opening GitHub release page: {releaseUrl}", ex);
                }
            }
        }
        else
        {
            LogMessage("No new version found or unable to check for updates.");
            UpdateStatusBarMessage("Application is up to date.");
        }
    }


    private async void StartValidationButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var romsFolderPath = RomsFolderTextBox.Text;
            var datFilePath = DatFileTextBox.Text;
            var moveSuccess = MoveSuccessCheckBox.IsChecked == true;
            var moveFailed = MoveFailedCheckBox.IsChecked == true;

            if (string.IsNullOrEmpty(romsFolderPath) || string.IsNullOrEmpty(datFilePath))
            {
                ShowError("Please select both a ROMs folder and a DAT file.");
                UpdateStatusBarMessage("Error: Please select paths.");
                return;
            }

            if (!Directory.Exists(romsFolderPath))
            {
                ShowError($"The selected ROMs folder does not exist: {romsFolderPath}");
                UpdateStatusBarMessage("Error: ROMs folder not found.");
                return;
            }

            if (!File.Exists(datFilePath))
            {
                ShowError($"The selected DAT file does not exist: {datFilePath}");
                UpdateStatusBarMessage("Error: DAT file not found.");
                return;
            }

            _cts = new CancellationTokenSource();
            ResetOperationStats();
            SetControlsState(false); // Disable controls and show progress
            _operationTimer.Restart();

            LogViewer.Clear();
            LogMessage("--- Starting Validation Process ---");
            UpdateStatusBarMessage("Validation started...");

            try
            {
                UpdateStatusBarMessage("Loading DAT file...");
                var datLoaded = await LoadDatFileAsync(datFilePath);
                if (!datLoaded)
                {
                    ShowError("Failed to load or parse the DAT file. Please check the log for details.");
                    UpdateStatusBarMessage("DAT file load failed.");
                    return; // Stop the process
                }

                UpdateStatusBarMessage("DAT file loaded. Starting ROM validation...");

                await PerformValidationAsync(romsFolderPath, datFilePath, moveSuccess, moveFailed, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                LogMessage("Validation operation was canceled by the user.");
                UpdateStatusBarMessage("Validation canceled.");
            }
            catch (Exception ex)
            {
                LogMessage($"An unexpected error occurred: {ex.Message}");
                ShowError($"An unexpected error occurred during validation: {ex.Message}");
                UpdateStatusBarMessage("Validation failed with an error.");
                // Bug Report Call 1: Exception during PerformValidationAsync
                _ = _bugReportService?.SendBugReportAsync("Exception during PerformValidationAsync", ex);
            }
            finally
            {
                _operationTimer.Stop();
                UpdateProcessingTimeDisplay();
                SetControlsState(true); // Re-enable controls and hide progress
                LogOperationSummary();
            }
        }
        catch (Exception ex)
        {
            LogMessage($"An unhandled error occurred in StartValidationButton_Click: {ex.Message}");
            ShowError($"An unhandled error occurred: {ex.Message}");
            UpdateStatusBarMessage("An unhandled error occurred.");
            // Bug Report Call 2: Unhandled exception in StartValidationButton_Click
            _ = _bugReportService?.SendBugReportAsync("Unhandled exception in StartValidationButton_Click", ex);
        }
    }

    private async Task PerformValidationAsync(string romsFolderPath, string datFilePath, bool moveSuccess, bool moveFailed, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        // 2. Prepare output directories
        var successPath = Path.Combine(romsFolderPath, "_success");
        var failPath = Path.Combine(romsFolderPath, "_fail");
        if (moveSuccess) Directory.CreateDirectory(successPath);
        if (moveFailed) Directory.CreateDirectory(failPath);

        LogMessage($"Move successful files: {moveSuccess}" + (moveSuccess ? $" (to {successPath})" : ""));
        LogMessage($"Move failed/unknown files: {moveFailed}" + (moveFailed ? $" (to {failPath})" : ""));


        // 3. Get files and start processing
        var filesToScan = Directory.GetFiles(romsFolderPath);
        _totalFilesToProcess = filesToScan.Length;
        ProgressBar.Maximum = _totalFilesToProcess;
        UpdateStatsDisplay();
        LogMessage($"Found {_totalFilesToProcess} files to validate.");
        UpdateStatusBarMessage($"Found {_totalFilesToProcess} files. Starting validation...");


        if (_totalFilesToProcess == 0)
        {
            UpdateStatusBarMessage("No files found to validate.");
            return;
        }

        var filesActuallyProcessedCount = 0;

        // Determine parallel processing option
        var enableParallelProcessing = ParallelProcessingCheckBox.IsChecked == true;

        if (enableParallelProcessing)
        {
            // Parallel processing with a max degree of parallelism
            await Parallel.ForEachAsync(filesToScan,
                new ParallelOptions
                {
                    CancellationToken = token,
                    MaxDegreeOfParallelism = 3 // Limit to 3 files at a time
                },
                async (filePath, ct) =>
                {
                    await ProcessFileAsync(filePath, successPath, failPath, moveSuccess, moveFailed, ct);
                    var processedSoFar = Interlocked.Increment(ref filesActuallyProcessedCount);
                    UpdateProgressDisplay(processedSoFar, _totalFilesToProcess, Path.GetFileName(filePath));
                    UpdateStatsDisplay();
                    UpdateProcessingTimeDisplay();
                });
        }
        else
        {
            // Sequential processing
            foreach (var filePath in filesToScan)
            {
                token.ThrowIfCancellationRequested();

                await ProcessFileAsync(filePath, successPath, failPath, moveSuccess, moveFailed, token);
                var processedSoFar = Interlocked.Increment(ref filesActuallyProcessedCount);
                UpdateProgressDisplay(processedSoFar, _totalFilesToProcess, Path.GetFileName(filePath));
                UpdateStatsDisplay();
                UpdateProcessingTimeDisplay();
            }
        }

        UpdateStatusBarMessage("Validation complete.");
    }

    private async Task ProcessFileAsync(string filePath, string successPath, string failPath, bool moveSuccess, bool moveFailed, CancellationToken token)
    {
        var fileName = Path.GetFileName(filePath);
        token.ThrowIfCancellationRequested();

        if (!_romDatabase.TryGetValue(fileName, out var expectedRom))
        {
            Interlocked.Increment(ref _unknownCount);
            LogMessage($"[UNKNOWN] {fileName} - Not found in DAT file.");
            if (moveFailed) await MoveFileAsync(filePath, Path.Combine(failPath, fileName));
            return;
        }

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length != expectedRom.Size)
        {
            Interlocked.Increment(ref _failCount);
            LogMessage($"[FAILED] {fileName} - Size mismatch. Expected: {expectedRom.Size}, Got: {fileInfo.Length}");
            if (moveFailed) await MoveFileAsync(filePath, Path.Combine(failPath, fileName));
            return;
        }

        var (hashMatch, matchDetails) = await CheckHashesAsync(filePath, expectedRom, token);

        if (hashMatch)
        {
            Interlocked.Increment(ref _successCount);
            LogMessage($"[SUCCESS] {fileName} - {matchDetails}");
            if (moveSuccess) await MoveFileAsync(filePath, Path.Combine(successPath, fileName));
        }
        else
        {
            Interlocked.Increment(ref _failCount);
            LogMessage($"[FAILED] {fileName} - {matchDetails}");
            if (moveFailed) await MoveFileAsync(filePath, Path.Combine(failPath, fileName));
        }
    }

    private async Task<(bool, string)> CheckHashesAsync(string filePath, Rom expectedRom, CancellationToken token)
    {
        try
        {
            if (!string.IsNullOrEmpty(expectedRom.Sha1))
            {
                var actualSha1 = await ComputeHashAsync(filePath, SHA1.Create(), token);
                if (actualSha1.Equals(expectedRom.Sha1, StringComparison.OrdinalIgnoreCase))
                    return (true, $"SHA1: {actualSha1}");
            }

            token.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(expectedRom.Md5))
            {
                var actualMd5 = await ComputeHashAsync(filePath, MD5.Create(), token);
                if (actualMd5.Equals(expectedRom.Md5, StringComparison.OrdinalIgnoreCase))
                    return (true, $"MD5: {actualMd5}");
            }

            token.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(expectedRom.Crc)) return (false, "Hash mismatch");

            var actualCrc = await ComputeCrc32Async(filePath, token);
            if (actualCrc.Equals(expectedRom.Crc, StringComparison.OrdinalIgnoreCase))
                return (true, $"CRC32: {actualCrc}");

            return (false, "Hash mismatch");
        }
        catch (Exception ex)
        {
            // Catching exceptions during hash computation for a specific file
            // Bug Report Call 3: Error checking hashes for file
            _ = _bugReportService?.SendBugReportAsync($"Error checking hashes for file '{filePath}'", ex); // Refined message
            return (false, "Error during hash check");
        }
    }

    private async Task<bool> LoadDatFileAsync(string datFilePath)
    {
        LogMessage($"Loading and parsing DAT file: {Path.GetFileName(datFilePath)}...");
        UpdateStatusBarMessage($"Loading DAT: {Path.GetFileName(datFilePath)}...");
        ClearDatInfoDisplay(); // Clear previous DAT info

        try
        {
            var serializer = new XmlSerializer(typeof(Datafile));
            await using var fileStream = new FileStream(datFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            using var xmlReader = XmlReader.Create(fileStream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });

            var datafile = (Datafile?)serializer.Deserialize(xmlReader);
            if (datafile?.Games is null)
            {
                LogMessage("Error: DAT file is empty or has an invalid structure.");
                UpdateStatusBarMessage("Error: DAT file empty or invalid.");
                return false;
            }

            _romDatabase = datafile.Games
                .Where(g => g.Rom is { Name.Length: > 0 })
                .GroupBy(g => g.Rom.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Rom);

            // Update DAT Info Display
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                DatNameTextBlock.Text = datafile.Header?.Name ?? "N/A";
                DatDescriptionTextBlock.Text = datafile.Header?.Description ?? "N/A";
                DatVersionTextBlock.Text = datafile.Header?.Version ?? "N/A";
                DatAuthorTextBlock.Text = datafile.Header?.Author ?? "N/A";
                DatHomepageTextBlock.Text = datafile.Header?.Homepage ?? "N/A";
                DatUrlTextBlock.Text = datafile.Header?.Url ?? "N/A";
                DatRomCountTextBlock.Text = _romDatabase.Count.ToString(CultureInfo.InvariantCulture);
            });

            LogMessage($"Successfully loaded {_romDatabase.Count} unique ROM entries from '{datafile.Header?.Name}'.");
            UpdateStatusBarMessage($"DAT loaded: {datafile.Header?.Name} ({_romDatabase.Count} ROMs).");
            return true;
        }
        catch (Exception ex)
        {
            LogMessage($"Error reading DAT file: {ex.Message}");
            // Bug Report Call 4: Error loading DAT file (during validation start or explicit load)
            _ = _bugReportService?.SendBugReportAsync($"Error loading DAT file '{datFilePath}'", ex);
            ClearDatInfoDisplay(); // Clear info on error
            UpdateStatusBarMessage($"Error loading DAT: {ex.Message}");
            return false;
        }
    }

    private static async Task<string> ComputeHashAsync(string filePath, HashAlgorithm algorithm, CancellationToken token)
    {
        using (algorithm)
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            var hashBytes = await algorithm.ComputeHashAsync(stream, token);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }

    private static async Task<string> ComputeCrc32Async(string filePath, CancellationToken token)
    {
        var crc32 = new Crc32();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        await crc32.AppendAsync(stream, token);
        var hashBytes = crc32.GetCurrentHash();
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private async Task MoveFileAsync(string sourcePath, string destPath)
    {
        const int maxRetries = 5;
        const int delayMs = 100;

        // Check if file exists and is accessible
        if (!File.Exists(sourcePath))
        {
            LogMessage($"   -> File not found, cannot move: {Path.GetFileName(sourcePath)}");
            return;
        }

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Ensure destination directory exists
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                await Task.Run(() => File.Move(sourcePath, destPath, true));
                return; // Success
            }
            catch (IOException ex) when (IsFileLocked(ex))
            {
                if (attempt == maxRetries)
                {
                    LogMessage($"   -> FAILED to move {Path.GetFileName(sourcePath)} after {maxRetries} attempts. File appears to be in use by another process. Error: {ex.Message}");
                    UpdateStatusBarMessage($"Failed to move {Path.GetFileName(sourcePath)} - file in use.");
                    // Bug Report Call 5: Error moving file due to file lock
                    _ = _bugReportService?.SendBugReportAsync($"Error moving file from '{sourcePath}' to '{destPath}' - file locked after {maxRetries} attempts", ex);
                    return;
                }

                // Wait before retrying with exponential backoff
                await Task.Delay(delayMs * attempt);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogMessage($"   -> ACCESS DENIED moving {Path.GetFileName(sourcePath)}. Error: {ex.Message}");
                UpdateStatusBarMessage($"Access denied moving {Path.GetFileName(sourcePath)}.");
                _ = _bugReportService?.SendBugReportAsync($"Access denied moving file from '{sourcePath}' to '{destPath}'", ex);
                return;
            }
            catch (DirectoryNotFoundException ex)
            {
                LogMessage($"   -> Directory not found when moving {Path.GetFileName(sourcePath)}. Error: {ex.Message}");
                UpdateStatusBarMessage($"Directory error moving {Path.GetFileName(sourcePath)}.");
                _ = _bugReportService?.SendBugReportAsync($"Directory not found when moving file from '{sourcePath}' to '{destPath}'", ex);
                return;
            }
            catch (Exception ex)
            {
                LogMessage($"   -> FAILED to move {Path.GetFileName(sourcePath)}. Error: {ex.Message}");
                UpdateStatusBarMessage($"Failed to move {Path.GetFileName(sourcePath)}.");
                _ = _bugReportService?.SendBugReportAsync($"Error moving file from '{sourcePath}' to '{destPath}'", ex);
                return;
            }
        }
    }

    // Helper method to determine if an IOException is caused by file locking
    private static bool IsFileLocked(IOException ex)
    {
        const int errorSharingViolation = 32;
        const int errorLockViolation = 33;

        var errorCode = System.Runtime.InteropServices.Marshal.GetHRForException(ex) & 0xFFFF;
        return errorCode == errorSharingViolation || errorCode == errorLockViolation;
    }

    #region UI and Control Methods

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new AboutWindow { Owner = this };
        aboutWindow.ShowDialog();
    }

    private void BrowseRomsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select the folder containing your ROM files" };
        if (dialog.ShowDialog() != true) return;

        RomsFolderTextBox.Text = dialog.FolderName;
        LogMessage($"ROMs folder selected: {dialog.FolderName}");
        UpdateStatusBarMessage($"ROMs folder selected: {Path.GetFileName(dialog.FolderName)}");
    }

    // MODIFIED METHOD: Make it async and call LoadDatFileAsync
    private async void BrowseDatFileButton_Click(object sender, RoutedEventArgs e)
    {
        string? selectedDatFileName = null; // Declare outside try-catch
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select the DAT file",
                Filter = "DAT Files (*.dat)|*.dat|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            selectedDatFileName = dialog.FileName; // Assign to the outer variable
            DatFileTextBox.Text = selectedDatFileName;
            LogMessage($"DAT file selected: {selectedDatFileName}");
            UpdateStatusBarMessage($"DAT file selected: {Path.GetFileName(selectedDatFileName)}. Loading...");

            // Immediately load and display DAT info after selection
            await LoadDatFileAsync(selectedDatFileName);
            // Status updated by LoadDatFileAsync or its error handling
        }
        catch (Exception ex)
        {
            // Bug Report Call 6: Error loading DAT file (during browse)
            // Use selectedDatFileName for more context,
            // it might be null if the exception occurred very early
            _ = _bugReportService?.SendBugReportAsync($"Error loading DAT file '{selectedDatFileName ?? "N/A"}'", ex);
            UpdateStatusBarMessage($"Error selecting DAT: {ex.Message}");
        }
    }

    // NEW: Handler for the "Download Dat Files" button
    private void DownloadDatFilesButton_Click(object sender, RoutedEventArgs e)
    {
        const string noIntroUrl = "https://no-intro.org/";
        try
        {
            Process.Start(new ProcessStartInfo(noIntroUrl) { UseShellExecute = true });
            LogMessage($"Opened browser to download DAT files from: {noIntroUrl}");
            UpdateStatusBarMessage("Opened no-intro.org for DAT files.");
        }
        catch (Exception ex)
        {
            ShowError($"Unable to open browser to {noIntroUrl}: {ex.Message}");
            LogMessage($"ERROR: Failed to open browser for DAT download. {ex.Message}");
            UpdateStatusBarMessage("Failed to open browser for DAT files.");
            _ = _bugReportService?.SendBugReportAsync($"Error opening no-intro.org for DAT files: {noIntroUrl}", ex);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        LogMessage("Cancellation requested. Finishing current operations...");
        UpdateStatusBarMessage("Validation canceled.");
    }

    private void SetControlsState(bool enabled)
    {
        RomsFolderTextBox.IsEnabled = enabled;
        BrowseRomsFolderButton.IsEnabled = enabled;
        DatFileTextBox.IsEnabled = enabled;
        BrowseDatFileButton.IsEnabled = enabled;
        StartValidationButton.IsEnabled = enabled;
        MoveSuccessCheckBox.IsEnabled = enabled;
        MoveFailedCheckBox.IsEnabled = enabled;
        ParallelProcessingCheckBox.IsEnabled = enabled;
        DownloadDatFilesButton.IsEnabled = enabled; // Ensure this button is also controlled

        // Progress bar and text visibility
        ProgressText.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        ProgressBar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;

        if (enabled)
        {
            ClearProgressDisplay();
            UpdateStatusBarMessage("Ready."); // Reset status bar when controls are enabled
        }
        else
        {
            UpdateStatusBarMessage("Validation in progress..."); // Set status bar when validation starts
        }
    }

    private void LogMessage(string message)
    {
        var timestampedMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            LogViewer.AppendText($"{timestampedMessage}{Environment.NewLine}");
            LogViewer.ScrollToEnd();
            // The status bar is updated by UpdateStatusBarMessage for specific, concise messages.
            // LogViewer shows detailed, timestamped messages.
        });
    }

    private void UpdateStatusBarMessage(string message)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            StatusBarMessageTextBlock.Text = message;
        });
    }

    private void ResetOperationStats()
    {
        _totalFilesToProcess = 0;
        _successCount = 0;
        _failCount = 0;
        _unknownCount = 0;
        _operationTimer.Reset();
        UpdateStatsDisplay();
        UpdateProcessingTimeDisplay();
        ClearProgressDisplay();
    }

    private void UpdateStatsDisplay()
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            TotalFilesValue.Text = _totalFilesToProcess.ToString(CultureInfo.InvariantCulture);
            SuccessValue.Text = _successCount.ToString(CultureInfo.InvariantCulture);
            FailedValue.Text = _failCount.ToString(CultureInfo.InvariantCulture);
            UnknownValue.Text = _unknownCount.ToString(CultureInfo.InvariantCulture);
        });
    }

    private void UpdateProcessingTimeDisplay()
    {
        var elapsed = _operationTimer.Elapsed;
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ProcessingTimeValue.Text = $@"{elapsed:hh\:mm\:ss}";
        });
    }

    private void UpdateProgressDisplay(int current, int total, string currentFileName)
    {
        var percentage = total == 0 ? 0 : (double)current / total * 100;
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ProgressText.Text = $"Validating file {current} of {total}: {currentFileName} ({percentage:F1}%)";
            ProgressBar.Value = current;
        });
    }

    private void ClearProgressDisplay()
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ProgressBar.Value = 0;
            ProgressBar.Visibility = Visibility.Collapsed;
            ProgressText.Text = string.Empty;
            ProgressText.Visibility = Visibility.Collapsed;
        });
    }

    private void ClearDatInfoDisplay()
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            DatNameTextBlock.Text = "N/A";
            DatDescriptionTextBlock.Text = "N/A";
            DatVersionTextBlock.Text = "N/A";
            DatAuthorTextBlock.Text = "N/A";
            DatHomepageTextBlock.Text = "N/A";
            DatUrlTextBlock.Text = "N/A";
            DatRomCountTextBlock.Text = "0";
        });
    }

    private void LogOperationSummary()
    {
        LogMessage("");
        LogMessage("--- Validation Completed ---");
        LogMessage($"Total files scanned: {_totalFilesToProcess}");
        LogMessage($"Successful: {_successCount}");
        LogMessage($"Failed: {_failCount}");
        LogMessage($"Unknown: {_unknownCount}");
        LogMessage($@"Total time: {_operationTimer.Elapsed:hh\:mm\:ss}");
        UpdateStatusBarMessage("Validation complete.");

        var summaryText = $"Validation complete.\n\n" +
                          $"Successful: {_successCount}\n" +
                          $"Failed: {_failCount}\n" +
                          $"Unknown: {_unknownCount}";

        MessageBox.Show(this, summaryText, "Validation Complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowError(string message)
    {
        MessageBox.Show(this, message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        Dispose();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _bugReportService?.Dispose();
        _versionChecker?.Dispose(); // Dispose the new version checker
        GC.SuppressFinalize(this);
    }

    #endregion
}