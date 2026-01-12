using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Xml;
using System.Xml.Serialization;
using RomValidator.Models;
using RomValidator.Services;

namespace RomValidator;

public partial class ValidatePage : IDisposable
{
    private readonly MainWindow _mainWindow;
    private Dictionary<string, Rom> _romDatabase = [];
    private CancellationTokenSource? _cts;

    // Statistics
    private int _totalFilesToProcess;
    private int _successCount;
    private int _failCount;
    private int _unknownCount;
    private readonly Stopwatch _operationTimer = new();

    public ValidatePage(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeComponent();
        DisplayInstructions();
        ClearDatInfoDisplay();
        _mainWindow.UpdateStatusBarMessage("Ready.");
        _ = CheckForUpdatesOnStartupAsync();
    }

    private void DisplayInstructions()
    {
        LogMessage("Welcome to the ROM Validator.");
        LogMessage("This tool validates your ROM files against a standard DAT file to ensure they are accurate and uncorrupted.");
        LogMessage("");
        LogMessage("Please follow these steps:");
        LogMessage("1. Select the folder containing the ROM files you want to scan.");
        LogMessage("2. Select the corresponding .dat file.");
        LogMessage("3. Choose your file moving preferences using the checkboxes.");
        LogMessage("4. Click 'Start Validation'.");
        LogMessage("");
        LogMessage("--- Ready for validation ---");
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        _mainWindow.UpdateStatusBarMessage("Checking for updates...");
        var (isNewVersionAvailable, releaseUrl, latestVersionTag) = await _mainWindow.VersionChecker.CheckForNewVersionAsync();

        if (isNewVersionAvailable && releaseUrl != null && latestVersionTag != null)
        {
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
            LogMessage($"A new version ({latestVersionTag}) is available! Your current version is {currentVersion}.");
            _mainWindow.UpdateStatusBarMessage($"New version {latestVersionTag} available!");

            var result = MessageBox.Show(
                $"A new version ({latestVersionTag}) of ROM Validator is available!\n\n" +
                $"Your current version: {currentVersion}\n\n" +
                "Would you like to go to the release page to download it?",
                "New Version Available", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    ShowError($"Could not open release page: {ex.Message}");
                    _ = _mainWindow.BugReportService.SendBugReportAsync($"Error opening GitHub release page: {releaseUrl}", ex);
                }
            }
        }
        else
        {
            LogMessage("No new version found or unable to check for updates.");
            _mainWindow.UpdateStatusBarMessage("Application is up to date.");
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
                _mainWindow.UpdateStatusBarMessage("Error: Please select paths.");
                return;
            }

            if (!Directory.Exists(romsFolderPath))
            {
                ShowError($"The selected ROMs folder does not exist: {romsFolderPath}");
                _mainWindow.UpdateStatusBarMessage("Error: ROMs folder not found.");
                return;
            }

            if (!File.Exists(datFilePath))
            {
                ShowError($"The selected DAT file does not exist: {datFilePath}");
                _mainWindow.UpdateStatusBarMessage("Error: DAT file not found.");
                return;
            }

            // FIX 2: Dispose previous CancellationTokenSource if any, then create a new one
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            ResetOperationStats();
            SetControlsState(false);
            _operationTimer.Restart();

            LogViewer.Clear();
            LogMessage("--- Starting Validation Process ---");
            _mainWindow.UpdateStatusBarMessage("Validation started...");

            try
            {
                _mainWindow.UpdateStatusBarMessage("Loading DAT file...");
                var datLoaded = await LoadDatFileAsync(datFilePath);
                if (!datLoaded)
                {
                    ShowError("Failed to load or parse the DAT file. Please check the log for details.");
                    _mainWindow.UpdateStatusBarMessage("DAT file load failed.");
                    return;
                }

                _mainWindow.UpdateStatusBarMessage("DAT file loaded. Starting ROM validation...");
                await PerformValidationAsync(romsFolderPath, moveSuccess, moveFailed, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                LogMessage("Validation operation was canceled by the user.");
                _mainWindow.UpdateStatusBarMessage("Validation canceled.");
            }
            catch (Exception ex)
            {
                LogMessage($"An unexpected error occurred: {ex.Message}");
                ShowError($"An unexpected error occurred during validation: {ex.Message}");
                _mainWindow.UpdateStatusBarMessage("Validation failed with an error.");
                _ = _mainWindow.BugReportService.SendBugReportAsync("Exception during PerformValidationAsync", ex);
            }
            finally
            {
                _operationTimer.Stop();
                UpdateProcessingTimeDisplay();
                SetControlsState(true);
                LogOperationSummary();
            }
        }
        catch (Exception ex)
        {
            _ = _mainWindow.BugReportService.SendBugReportAsync("Exception during StartValidationButton_Click", ex);
        }
    }

    private async Task PerformValidationAsync(string romsFolderPath, bool moveSuccess, bool moveFailed, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var successPath = Path.Combine(romsFolderPath, "_success");
        var failPath = Path.Combine(romsFolderPath, "_fail");
        if (moveSuccess) Directory.CreateDirectory(successPath);
        if (moveFailed) Directory.CreateDirectory(failPath);

        LogMessage($"Move successful files: {moveSuccess}" + (moveSuccess ? $" (to {successPath})" : ""));
        LogMessage($"Move failed/unknown files: {moveFailed}" + (moveFailed ? $" (to {failPath})" : ""));

        var filesToScan = Directory.GetFiles(romsFolderPath);
        _totalFilesToProcess = filesToScan.Length;
        ProgressBar.Maximum = _totalFilesToProcess;
        UpdateStatsDisplay();
        LogMessage($"Found {_totalFilesToProcess} files to validate.");
        _mainWindow.UpdateStatusBarMessage($"Found {_totalFilesToProcess} files. Starting validation...");

        if (_totalFilesToProcess == 0)
        {
            _mainWindow.UpdateStatusBarMessage("No files found to validate.");
            return;
        }

        var filesActuallyProcessedCount = 0;
        var enableParallelProcessing = ParallelProcessingCheckBox.IsChecked == true;

        if (enableParallelProcessing)
        {
            await Parallel.ForEachAsync(filesToScan,
                new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = 3 },
                async (filePath, ct) =>
                {
                    await ProcessFileAsync(filePath, successPath, failPath, moveSuccess, moveFailed, ct);
                    var processedSoFar = Interlocked.Increment(ref filesActuallyProcessedCount);
                    // FIX 1: Pass enableParallelProcessing flag to UpdateProgressDisplay
                    UpdateProgressDisplay(processedSoFar, _totalFilesToProcess, Path.GetFileName(filePath), enableParallelProcessing);
                    UpdateStatsDisplay();
                    UpdateProcessingTimeDisplay();
                });
        }
        else
        {
            foreach (var filePath in filesToScan)
            {
                token.ThrowIfCancellationRequested();
                await ProcessFileAsync(filePath, successPath, failPath, moveSuccess, moveFailed, token);
                var processedSoFar = Interlocked.Increment(ref filesActuallyProcessedCount);
                // FIX 1: Pass enableParallelProcessing flag to UpdateProgressDisplay
                UpdateProgressDisplay(processedSoFar, _totalFilesToProcess, Path.GetFileName(filePath), enableParallelProcessing);
                UpdateStatsDisplay();
                UpdateProcessingTimeDisplay();
            }
        }

        _mainWindow.UpdateStatusBarMessage("Validation complete.");
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

    private async Task<(bool IsValid, string Message)> CheckHashesAsync(string filePath, Rom expectedRom, CancellationToken token)
    {
        try
        {
            var verifiedHashes = new List<string>();
            var mismatchedHashes = new List<string>();

            // Check SHA1 if provided
            if (!string.IsNullOrEmpty(expectedRom.Sha1))
            {
                var actualSha1 = await ComputeHashAsync(filePath, SHA1.Create(), token);
                if (!actualSha1.Equals(expectedRom.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    mismatchedHashes.Add($"SHA1 mismatch (expected: {expectedRom.Sha1}, got: {actualSha1})");
                }
                else
                {
                    verifiedHashes.Add($"SHA1: {actualSha1}");
                }
            }

            token.ThrowIfCancellationRequested();

            // Check MD5 if provided
            if (!string.IsNullOrEmpty(expectedRom.Md5))
            {
                var actualMd5 = await ComputeHashAsync(filePath, MD5.Create(), token);
                if (!actualMd5.Equals(expectedRom.Md5, StringComparison.OrdinalIgnoreCase))
                {
                    mismatchedHashes.Add($"MD5 mismatch (expected: {expectedRom.Md5}, got: {actualMd5})");
                }
                else
                {
                    verifiedHashes.Add($"MD5: {actualMd5}");
                }
            }

            token.ThrowIfCancellationRequested();

            // Check CRC32 if provided
            if (!string.IsNullOrEmpty(expectedRom.Crc))
            {
                var actualCrc = await ComputeCrc32Async(filePath, token);
                if (!actualCrc.Equals(expectedRom.Crc, StringComparison.OrdinalIgnoreCase))
                {
                    mismatchedHashes.Add($"CRC32 mismatch (expected: {expectedRom.Crc}, got: {actualCrc})");
                }
                else
                {
                    verifiedHashes.Add($"CRC32: {actualCrc}");
                }
            }

            // Determine result: ALL provided hashes must match
            if (mismatchedHashes.Count > 0)
            {
                // Failure: At least one hash didn't match
                var failureReason = string.Join("; ", mismatchedHashes);
                return (false, failureReason);
            }

            if (verifiedHashes.Count == 0)
            {
                // No hashes were provided in the DAT entry
                return (false, "No hashes provided in DAT entry");
            }

            // Success: All provided hashes matched
            var successMessage = string.Join(", ", verifiedHashes);
            return (true, successMessage);
        }
        catch (OperationCanceledException)
        {
            return (false, "Operation canceled");
        }
        catch (Exception ex)
        {
            _ = _mainWindow.BugReportService.SendBugReportAsync($"Error checking hashes for file '{filePath}'", ex);
            return (false, $"Error during hash check: {ex.Message}");
        }
    }

    private async Task<bool> LoadDatFileAsync(string datFilePath)
    {
        LogMessage($"Loading and parsing DAT file: {Path.GetFileName(datFilePath)}...");
        _mainWindow.UpdateStatusBarMessage($"Loading DAT: {Path.GetFileName(datFilePath)}...");
        ClearDatInfoDisplay();
        string? datFilePreview = null;

        try
        {
            // Read a preview of the DAT file for error reporting (first 5000 characters)
            try
            {
                datFilePreview = await File.ReadAllTextAsync(datFilePath);
                if (datFilePreview.Length > 5000)
                {
                    datFilePreview = datFilePreview[..5000] + "\n\n[... FILE TRUNCATED FOR PREVIEW ...]";
                }
            }
            catch
            {
                datFilePreview = "[Could not read file preview]";
            }

            // First validation pass - check for <datafile> root element
            await using (var validationStream = new FileStream(datFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
            {
                using var validationReader = XmlReader.Create(validationStream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });

                if (!validationReader.ReadToFollowing("datafile"))
                {
                    const string errorMsg = "Incompatible DAT file format.\n\n" +
                                            "This application only supports No-Intro XML DAT files.\n\n" +
                                            "The selected file does not contain the required <datafile> root element.\n\n" +
                                            "Please download a compatible DAT file from https://no-intro.org/";
                    LogMessage($"Error: {errorMsg}");

                    // Send sample to developer
                    var detailedError = $"User attempted to load incompatible DAT file: {Path.GetFileName(datFilePath)}\n\n" +
                                        $"Error: Missing <datafile> root element\n\n" +
                                        $"File Preview:\n{datFilePreview}";
                    _ = _mainWindow.BugReportService.SendBugReportAsync(detailedError);

                    ShowIncompatibleDatFileError(errorMsg);
                    _mainWindow.UpdateStatusBarMessage("DAT file format not supported.");
                    return false;
                }
            }

            // Quick check for common incompatible formats
            string firstLine;
            using (var sr = new StreamReader(datFilePath, Encoding.UTF8, true, 1024))
            {
                firstLine = await sr.ReadLineAsync() ?? string.Empty;
            }

            if (firstLine.Contains("clrmamepro", StringComparison.OrdinalIgnoreCase) &&
                !firstLine.Contains("<?xml", StringComparison.OrdinalIgnoreCase))
            {
                const string errorMsg = "Incompatible DAT file format.\n\n" +
                                        "This application only supports No-Intro XML DAT files.\n\n" +
                                        "The selected file appears to be a ClrMamePro text format DAT file, which is not supported.\n\n" +
                                        "Please download an XML format DAT file from https://no-intro.org/";
                LogMessage($"Error: {errorMsg}");

                // Send sample to developer
                var detailedError = $"User attempted to load ClrMamePro text format DAT file: {Path.GetFileName(datFilePath)}\n\n" +
                                    $"File Preview:\n{datFilePreview}";
                _ = _mainWindow.BugReportService.SendBugReportAsync(detailedError);

                ShowIncompatibleDatFileError(errorMsg);
                _mainWindow.UpdateStatusBarMessage("DAT file format not supported.");
                return false;
            }

            // Create serializer with our updated models
            var serializer = new XmlSerializer(typeof(Datafile));

            // Deserialize the DAT file
            await using var deserializeStream = new FileStream(datFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            using var xmlReader = XmlReader.Create(deserializeStream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });

            var datafile = (Datafile?)serializer.Deserialize(xmlReader);
            if (datafile?.Games is null || datafile.Games.Count == 0)
            {
                const string errorMsg = "Incompatible or empty DAT file.\n\n" +
                                        "This application only supports No-Intro XML DAT files.\n\n" +
                                        "The selected file was parsed but contains no game entries.\n\n" +
                                        "Please ensure you're using a valid No-Intro DAT file from https://no-intro.org/";
                LogMessage($"Error: {errorMsg}");

                // Send sample to developer
                var detailedError = $"User attempted to load empty/invalid DAT file: {Path.GetFileName(datFilePath)}\n\n" +
                                    $"File Preview:\n{datFilePreview}";
                _ = _mainWindow.BugReportService.SendBugReportAsync(detailedError);

                ShowIncompatibleDatFileError(errorMsg);
                _mainWindow.UpdateStatusBarMessage("Error: DAT file empty or invalid.");
                return false;
            }

            // Build ROM database (only from <rom> elements)
            _romDatabase = datafile.Games
                .SelectMany(static g => g.Roms)
                .Where(static r => !string.IsNullOrEmpty(r.Name))
                .GroupBy(static r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static g => g.Key, static g => g.First());

            // Validate we actually got ROM entries
            if (_romDatabase.Count == 0)
            {
                const string errorMsg = "Incompatible DAT file structure.\n\n" +
                                        "This application only supports No-Intro XML DAT files.\n\n" +
                                        "The selected file contains game entries but no ROM entries.\n\n" +
                                        "Please ensure you're using a valid No-Intro DAT file from https://no-intro.org/";
                LogMessage($"Error: {errorMsg}");

                // Send sample to developer
                var detailedError = $"User attempted to load DAT file with no ROM entries: {Path.GetFileName(datFilePath)}\n\n" +
                                    $"Games found: {datafile.Games.Count}\n" +
                                    $"ROM entries: 0\n\n" +
                                    $"File Preview:\n{datFilePreview}";
                _ = _mainWindow.BugReportService.SendBugReportAsync(detailedError);

                ShowIncompatibleDatFileError(errorMsg);
                _mainWindow.UpdateStatusBarMessage("Error: No ROM entries found.");
                return false;
            }

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
            _mainWindow.UpdateStatusBarMessage($"DAT loaded: {datafile.Header?.Name} ({_romDatabase.Count} ROMs).");
            return true;
        }
        catch (InvalidOperationException ex) when (ex.InnerException != null)
        {
            // Handle specific XML serialization errors
            var innerMsg = ex.InnerException.Message;

            var errorMsg = "Incompatible DAT file format.\n\n" +
                           "This application only supports No-Intro XML DAT files.\n\n" +
                           $"XML Parsing Error: {innerMsg}\n\n" +
                           "Common causes:\n" +
                           "• The file is in ClrMamePro text format (not XML)\n" +
                           "• The XML structure is invalid or corrupted\n\n" +
                           "Please download a compatible No-Intro XML DAT file from https://no-intro.org/";

            LogMessage($"Error: {errorMsg}");

            // Log full details for debugging
            var detailedError = $"XML Serialization error for DAT file: {Path.GetFileName(datFilePath)}\n\n" +
                                $"Error: {innerMsg}\n\n" +
                                $"Full Exception: {ex}\n\n" +
                                $"File Preview:\n{datFilePreview}";
            _ = _mainWindow.BugReportService.SendBugReportAsync(detailedError, ex);

            ShowIncompatibleDatFileError(errorMsg);
            ClearDatInfoDisplay();
            _mainWindow.UpdateStatusBarMessage("Error: Failed to parse DAT file.");
            return false;
        }
        catch (XmlException xmlEx)
        {
            var errorMsg = "Incompatible DAT file format.\n\n" +
                           "This application only supports No-Intro XML DAT files.\n\n" +
                           $"XML Error: {xmlEx.Message}\n\n" +
                           "The file does not appear to be valid XML or is corrupted.\n\n" +
                           "Please download a compatible No-Intro XML DAT file from https://no-intro.org/";

            LogMessage($"Error: {errorMsg}");

            // Send sample to developer
            var detailedError = $"XML parsing error for DAT file: {Path.GetFileName(datFilePath)}\n\n" +
                                $"Error: {xmlEx.Message}\n\n" +
                                $"Line: {xmlEx.LineNumber}, Position: {xmlEx.LinePosition}\n\n" +
                                $"File Preview:\n{datFilePreview}";
            _ = _mainWindow.BugReportService.SendBugReportAsync(detailedError, xmlEx);

            ShowIncompatibleDatFileError(errorMsg);
            ClearDatInfoDisplay();
            _mainWindow.UpdateStatusBarMessage("Error: Invalid XML format.");
            return false;
        }
        catch (Exception ex)
        {
            var errorMsg = $"Unexpected error loading DAT file.\n\n" +
                           $"Error: {ex.Message}\n\n" +
                           "This application only supports No-Intro XML DAT files.\n\n" +
                           "Please ensure you're using a valid No-Intro DAT file from https://no-intro.org/\n\n" +
                           "If the problem persists, the file may be corrupted.";

            LogMessage($"Error: {errorMsg}");

            // Send sample to developer
            var detailedError = $"Unexpected error loading DAT file: {Path.GetFileName(datFilePath)}\n\n" +
                                $"Error: {ex.Message}\n\n" +
                                $"Exception Type: {ex.GetType().Name}\n\n" +
                                $"File Preview:\n{datFilePreview}";
            _ = _mainWindow.BugReportService.SendBugReportAsync(detailedError, ex);

            ShowError(errorMsg);
            ClearDatInfoDisplay();
            _mainWindow.UpdateStatusBarMessage("Error: Failed to load DAT file.");
            return false;
        }
    }

    private void ShowIncompatibleDatFileError(string message)
    {
        MessageBox.Show(_mainWindow, message, "Incompatible DAT File Format",
            MessageBoxButton.OK, MessageBoxImage.Warning);
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
        using var crc32 = new Crc32Algorithm();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var hashBytes = await crc32.ComputeHashAsync(stream, token);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private async Task MoveFileAsync(string sourcePath, string destPath)
    {
        const int maxRetries = 5;
        const int delayMs = 100;

        if (!File.Exists(sourcePath))
        {
            LogMessage($"   -> File not found, cannot move: {Path.GetFileName(sourcePath)}");
            return;
        }

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);
                await Task.Run(() => File.Move(sourcePath, destPath, true));
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                if (attempt == maxRetries)
                {
                    LogMessage($"   -> FAILED to move {Path.GetFileName(sourcePath)} after {maxRetries} attempts. Error: {ex.Message}");
                    _mainWindow.UpdateStatusBarMessage($"Failed to move {Path.GetFileName(sourcePath)}.");
                    _ = _mainWindow.BugReportService.SendBugReportAsync($"Error moving file from '{sourcePath}' to '{destPath}' after {maxRetries} attempts", ex);
                    return;
                }

                await Task.Delay(delayMs * attempt);
            }
            catch (Exception ex)
            {
                LogMessage($"   -> FAILED to move {Path.GetFileName(sourcePath)}. Error: {ex.Message}");
                _mainWindow.UpdateStatusBarMessage($"Failed to move {Path.GetFileName(sourcePath)}.");
                _ = _mainWindow.BugReportService.SendBugReportAsync($"Error moving file from '{sourcePath}' to '{destPath}'", ex);
                return;
            }
        }
    }

    #region UI and Control Methods

    private void BrowseRomsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select the folder containing your ROM files" };
        if (dialog.ShowDialog() != true) return;

        RomsFolderTextBox.Text = dialog.FolderName;
        LogMessage($"ROMs folder selected: {dialog.FolderName}");
        _mainWindow.UpdateStatusBarMessage($"ROMs folder selected: {Path.GetFileName(dialog.FolderName)}");
    }

    private async void BrowseDatFileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog { Title = "Select the DAT file", Filter = "DAT Files (*.dat)|*.dat|All files (*.*)|*.*" };
            if (dialog.ShowDialog() != true) return;

            var selectedDatFileName = dialog.FileName;
            DatFileTextBox.Text = selectedDatFileName;
            LogMessage($"DAT file selected: {selectedDatFileName}");
            _mainWindow.UpdateStatusBarMessage($"DAT file selected: {Path.GetFileName(selectedDatFileName)}. Loading...");
            await LoadDatFileAsync(selectedDatFileName);
        }
        catch (Exception ex)
        {
            _ = _mainWindow.BugReportService.SendBugReportAsync("Exception during BrowseDatFileButton_Click", ex);
        }
    }

    private void DownloadDatFilesButton_Click(object sender, RoutedEventArgs e)
    {
        const string noIntroUrl = "https://no-intro.org/";
        try
        {
            Process.Start(new ProcessStartInfo(noIntroUrl) { UseShellExecute = true });
            LogMessage($"Opened browser to download DAT files from: {noIntroUrl}");
            _mainWindow.UpdateStatusBarMessage("Opened no-intro.org for DAT files.");
        }
        catch (Exception ex)
        {
            ShowError($"Unable to open browser to {noIntroUrl}: {ex.Message}");
            _ = _mainWindow.BugReportService.SendBugReportAsync($"Error opening no-intro.org for DAT files: {noIntroUrl}", ex);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // FIX 2: Safely cancel the CancellationTokenSource
        _cts?.Cancel();
        LogMessage("Cancellation requested. Finishing current operations...");
        _mainWindow.UpdateStatusBarMessage("Validation canceled.");
    }

    private void SetControlsState(bool enabled)
    {
        BrowseRomsFolderButton.IsEnabled = enabled;
        BrowseDatFileButton.IsEnabled = enabled;
        MoveSuccessCheckBox.IsEnabled = enabled;
        MoveFailedCheckBox.IsEnabled = enabled;
        ParallelProcessingCheckBox.IsEnabled = enabled;
        DownloadDatFilesButton.IsEnabled = enabled;
        StartValidationButton.IsEnabled = enabled;
        CancelButton.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.IsEnabled = !enabled;

        if (enabled)
        {
            ProgressBar.Value = 0;
            ProgressText.Text = "";
            _mainWindow.UpdateStatusBarMessage("Ready.");
        }
        else
        {
            _mainWindow.UpdateStatusBarMessage("Validation in progress...");
        }
    }

    private void LogMessage(string message)
    {
        var timestampedMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            LogViewer.AppendText($"{timestampedMessage}{Environment.NewLine}");
            LogViewer.ScrollToEnd();
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
        ProgressBar.Value = 0;
        ProgressText.Text = string.Empty;
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
        Application.Current.Dispatcher.InvokeAsync(() => { ProcessingTimeValue.Text = $@"{elapsed:hh\:mm\:ss}"; });
    }

    // FIX 1: Add a parameter to indicate parallel processing and adjust text accordingly
    private void UpdateProgressDisplay(int current, int total, string currentFileName, bool isParallel)
    {
        var percentage = total == 0 ? 0 : (double)current / total * 100;
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (isParallel)
            {
                // Generic message for parallel processing as filename cannot be reliably tied to the exact count
                ProgressText.Text = $"Validating files {current} of {total} ({percentage:F1}%)";
            }
            else
            {
                // Detailed message for sequential processing
                ProgressText.Text = $"Validating file {current} of {total}: {currentFileName} ({percentage:F1}%)";
            }

            ProgressBar.Value = current;
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
        _mainWindow.UpdateStatusBarMessage("Validation complete.");

        var summaryText = $"Validation complete.\n\nSuccessful: {_successCount}\nFailed: {_failCount}\nUnknown: {_unknownCount}";
        MessageBox.Show(_mainWindow, summaryText, "Validation Complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowError(string message)
    {
        MessageBox.Show(_mainWindow, message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public void Dispose()
    {
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}