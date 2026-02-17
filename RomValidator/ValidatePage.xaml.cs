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
    private Dictionary<string, List<Rom>> _romDatabase = [];
    private Dictionary<string, Rom> _romDatabaseBySha1 = [];
    private Dictionary<string, Rom> _romDatabaseByMd5 = [];
    private Dictionary<string, Rom> _romDatabaseByCrc = [];
    private CancellationTokenSource? _cts;
    private readonly object _ctsLock = new();

    // Statistics
    private int _totalFilesToProcess;
    private int _successCount;
    private int _failCount;
    private int _unknownCount;
    private int _renamedCount;
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
        CancellationTokenSource? operationCts = null; // Capture variable for this operation
        try
        {
            var romsFolderPath = RomsFolderTextBox.Text;
            var datFilePath = DatFileTextBox.Text;
            var moveSuccess = MoveSuccessCheckBox.IsChecked == true;
            var moveFailed = MoveFailedCheckBox.IsChecked == true;
            var renameMatched = RenameMatchedFilesCheckBox.IsChecked == true;

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

            // Cancel previous operation and create new CancellationTokenSource
            lock (_ctsLock)
            {
                _cts?.Cancel(); // Signal cancellation to any running operation
                _cts = new CancellationTokenSource();
                operationCts = _cts; // Capture the instance for this operation
            }

            ResetOperationStats();
            SetControlsState(false);
            _operationTimer.Restart();

            LogViewer.Clear();
            LogMessage("--- Starting Validation Process ---");
            _mainWindow.UpdateStatusBarMessage("Validation started...");

            // Use the captured CTS instance
            var datLoaded = await LoadDatFileAsync(datFilePath);
            if (!datLoaded)
            {
                ShowError("Failed to load or parse the DAT file. Please check the log for details.");
                _mainWindow.UpdateStatusBarMessage("DAT file load failed.");
                return;
            }

            _mainWindow.UpdateStatusBarMessage("DAT file loaded. Starting ROM validation...");
            await PerformValidationAsync(romsFolderPath, moveSuccess, moveFailed, renameMatched, operationCts.Token);
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

    private async Task PerformValidationAsync(string romsFolderPath, bool moveSuccess, bool moveFailed, bool renameMatched, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var successPath = Path.Combine(romsFolderPath, "_success");
        var failPath = Path.Combine(romsFolderPath, "_fail");
        if (moveSuccess) Directory.CreateDirectory(successPath);
        if (moveFailed) Directory.CreateDirectory(failPath);

        LogMessage($"Move successful files: {moveSuccess}" + (moveSuccess ? $" (to {successPath})" : ""));
        LogMessage($"Move failed/unknown files: {moveFailed}" + (moveFailed ? $" (to {failPath})" : ""));
        LogMessage($"Rename files on hash match: {renameMatched}");

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

        // Sequential Processing to prevent race conditions on file moves
        foreach (var filePath in filesToScan)
        {
            // Check cancellation at the start of each file
            if (token.IsCancellationRequested) break;

            await ProcessFileAsync(filePath, successPath, failPath, moveSuccess, moveFailed, renameMatched, token);

            var processedSoFar = Interlocked.Increment(ref filesActuallyProcessedCount);
            UpdateProgressDisplay(processedSoFar, _totalFilesToProcess, Path.GetFileName(filePath));
            UpdateStatsDisplay();
            UpdateProcessingTimeDisplay();
        }

        _mainWindow.UpdateStatusBarMessage("Validation complete.");
    }

    private static bool IsAccessDeniedError(IOException ex)
    {
        return ex.Message.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("the process cannot access the file", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ProcessFileAsync(string filePath, string successPath, string failPath, bool moveSuccess, bool moveFailed, bool renameMatched, CancellationToken token)
    {
        var fileName = Path.GetFileName(filePath);
        token.ThrowIfCancellationRequested();

        var fileNameMatch = _romDatabase.TryGetValue(fileName, out var expectedRoms);

        // If filename doesn't match, try hash-based lookup
        if (!fileNameMatch && renameMatched)
        {
            var fileInfo = new FileInfo(filePath);

            if (!fileInfo.Exists)
            {
                Interlocked.Increment(ref _failCount);
                LogMessage($"[ERROR] {fileName} - File not found during validation (may have been deleted or moved).");
                return;
            }

            // Compute hashes to find a match
            var (hashMatchedRom, matchedHash) = await FindRomByHashAsync(filePath, fileInfo.Length, token);

            if (hashMatchedRom != null)
            {
                // Found a match by hash! Rename the file
                var directory = Path.GetDirectoryName(filePath);
                var newFilePath = Path.Combine(directory ?? string.Empty, hashMatchedRom.Name);

                try
                {
                    await RenameFileAsync(filePath, newFilePath);
                    Interlocked.Increment(ref _renamedCount);
                    LogMessage($"[RENAMED] {fileName} -> {hashMatchedRom.Name} (matched by {matchedHash})");

                    // Update variables to process renamed file
                    filePath = newFilePath;
                    fileName = hashMatchedRom.Name;
                    expectedRoms = [hashMatchedRom];
                    fileNameMatch = true;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _failCount);
                    LogMessage($"[FAILED] {fileName} - Hash matched {hashMatchedRom.Name} but rename failed: {ex.Message}");
                    if (moveFailed) await MoveFileAsync(filePath, Path.Combine(failPath, fileName));
                    return;
                }
            }
        }

        if (!fileNameMatch || expectedRoms == null || expectedRoms.Count == 0)
        {
            Interlocked.Increment(ref _unknownCount);
            LogMessage($"[UNKNOWN] {fileName} - Not found in DAT file.");
            if (moveFailed) await MoveFileAsync(filePath, Path.Combine(failPath, fileName));
            return;
        }

        var fileInfo2 = new FileInfo(filePath);

        if (!fileInfo2.Exists)
        {
            Interlocked.Increment(ref _failCount);
            LogMessage($"[ERROR] {fileName} - File not found during validation (may have been deleted or moved).");
            return;
        }

        var (hashMatch, matchDetails) = await CheckHashesAsync(filePath, expectedRoms, token);

        if (hashMatch)
        {
            Interlocked.Increment(ref _successCount);
            LogMessage($"[SUCCESS] {fileName} - {matchDetails}");
            if (moveSuccess) await MoveFileAsync(filePath, Path.Combine(successPath, fileName));
        }
        else
        {
            Interlocked.Increment(ref _failCount);
            // Log failure details explicitly to UI
            LogMessage($"[FAILED] {fileName} - {matchDetails}");

            // If it was a critical error (like extraction failure), it will appear in matchDetails
            if (matchDetails.Contains("Archive extraction failed") || matchDetails.Contains("Error"))
            {
                // Optional: Highlight critical errors
                _mainWindow.UpdateStatusBarMessage($"Error processing {fileName}");
            }

            if (moveFailed) await MoveFileAsync(filePath, Path.Combine(failPath, fileName));
        }
    }

    private async Task<(bool IsValid, string Message)> CheckHashesAsync(string filePath, List<Rom> expectedRoms, CancellationToken token)
    {
        try
        {
            using var sha1 = SHA1.Create();
            using var md5 = MD5.Create();
            using var crc = new Crc32Algorithm();

            string? actualSha1 = null;
            string? actualMd5 = null;
            string? actualCrc = null;
            long actualSize;

            // Single-pass read to calculate all hashes (Issue 6 Fix)
            await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
            {
                actualSize = stream.Length;
                var buffer = new byte[65536];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, token)) > 0)
                {
                    sha1.TransformBlock(buffer, 0, bytesRead, null, 0);
                    md5.TransformBlock(buffer, 0, bytesRead, null, 0);
                    crc.TransformBlock(buffer, 0, bytesRead, null, 0);
                }

                sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                crc.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                if (sha1.Hash != null)
                {
                    actualSha1 = Convert.ToHexString(sha1.Hash).ToLowerInvariant();
                    if (md5.Hash != null)
                    {
                        actualMd5 = Convert.ToHexString(md5.Hash).ToLowerInvariant();
                        if (crc.Hash != null)
                        {
                            actualCrc = Convert.ToHexString(crc.Hash).ToLowerInvariant();
                        }
                    }
                }
            }

            // Check against all potential ROM definitions for this filename (Issue 3 Fix)
            var errors = new List<string>();
            foreach (var expectedRom in expectedRoms)
            {
                var sizeMatch = actualSize == expectedRom.Size;
                var sha1Match = actualSha1 != null && (string.IsNullOrEmpty(expectedRom.Sha1) || actualSha1.Equals(expectedRom.Sha1, StringComparison.OrdinalIgnoreCase));
                var md5Match = actualMd5 != null && (string.IsNullOrEmpty(expectedRom.Md5) || actualMd5.Equals(expectedRom.Md5, StringComparison.OrdinalIgnoreCase));
                var crcMatch = actualCrc != null && (string.IsNullOrEmpty(expectedRom.Crc) || actualCrc.Equals(expectedRom.Crc, StringComparison.OrdinalIgnoreCase));

                if (sizeMatch && sha1Match && md5Match && crcMatch)
                {
                    var details = new List<string>();
                    if (!string.IsNullOrEmpty(expectedRom.Sha1)) details.Add($"SHA1: {actualSha1}");
                    if (!string.IsNullOrEmpty(expectedRom.Md5)) details.Add($"MD5: {actualMd5}");
                    if (!string.IsNullOrEmpty(expectedRom.Crc)) details.Add($"CRC32: {actualCrc}");

                    return (true, details.Count > 0 ? string.Join(", ", details) : "Size matched (no hashes in DAT)");
                }

                // Collect error info for the log if no match is found
                var mismatchReason = new List<string>();
                if (!sizeMatch) mismatchReason.Add($"Size (Exp: {expectedRom.Size}, Got: {actualSize})");
                if (!sha1Match) mismatchReason.Add("SHA1 mismatch");
                if (!md5Match) mismatchReason.Add("MD5 mismatch");
                if (!crcMatch) mismatchReason.Add("CRC32 mismatch");
                errors.Add($"[{string.Join(", ", mismatchReason)}]");
            }

            return (false, $"No match found among {expectedRoms.Count} DAT entries: {string.Join(" | ", errors)}");
        }
        catch (OperationCanceledException)
        {
            return (false, "Operation canceled");
        }
        catch (IOException ex)
        {
            // Don't report corrupted/unreadable files as bugs - these are user environment issues
            LoggerService.LogError("Validation", $"IO error reading file '{filePath}': {ex.Message}");
            return (false, $"File I/O error (file may be corrupted or unreadable): {ex.Message}");
        }
        catch (Exception ex)
        {
            // Only report actual application bugs
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

            // Quick check for common incompatible formats - MUST happen before XML parsing
            string firstLine;
            using (var sr = new StreamReader(datFilePath, Encoding.UTF8, true, 1024))
            {
                firstLine = await sr.ReadLineAsync() ?? string.Empty;
            }

            if (firstLine.Contains("clrmamepro", StringComparison.OrdinalIgnoreCase) &&
                !firstLine.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
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
            var allRoms = datafile.Games
                .SelectMany(static g => g.Roms)
                .Where(static r => !string.IsNullOrEmpty(r.Name))
                .ToList();

            _romDatabase = allRoms
                .GroupBy(static r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static g => g.Key, static g => g.ToList());

            // Build hash-based lookup dictionaries
            _romDatabaseBySha1 = allRoms
                .Where(static r => !string.IsNullOrEmpty(r.Sha1))
                .GroupBy(static r => r.Sha1, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.OrdinalIgnoreCase);

            _romDatabaseByMd5 = allRoms
                .Where(static r => !string.IsNullOrEmpty(r.Md5))
                .GroupBy(static r => r.Md5, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.OrdinalIgnoreCase);

            _romDatabaseByCrc = allRoms
                .Where(static r => !string.IsNullOrEmpty(r.Crc))
                .GroupBy(static r => r.Crc, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.OrdinalIgnoreCase);

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
        const int maxRetries = 3;
        var retryDelay = 100;

        using (algorithm)
        {
            for (var attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    // Reinitialize algorithm to clear any partial state from previous attempt
                    algorithm.Initialize();

                    await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                    var hashBytes = await algorithm.ComputeHashAsync(stream, token);
                    return Convert.ToHexString(hashBytes).ToLowerInvariant();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (IOException ex) when (IsAccessDeniedError(ex))
                {
                    if (attempt == maxRetries - 1)
                    {
                        throw new IOException($"File is locked or access denied after {maxRetries} attempts: {filePath}", ex);
                    }

                    await Task.Delay(retryDelay, token);
                    retryDelay *= 2; // Exponential backoff
                }
            }

            throw new InvalidOperationException("Unexpected exit from retry loop");
        }
    }

    private static async Task<string> ComputeCrc32Async(string filePath, CancellationToken token)
    {
        const int maxRetries = 3;
        var retryDelay = 100;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                using var crc32 = new Crc32Algorithm();
                await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                var hashBytes = await crc32.ComputeHashAsync(stream, token);
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException ex) when (IsAccessDeniedError(ex))
            {
                if (attempt == maxRetries - 1)
                {
                    throw new IOException($"File is locked or access denied after {maxRetries} attempts: {filePath}", ex);
                }

                await Task.Delay(retryDelay, token);
                retryDelay *= 2; // Exponential backoff
            }
        }

        throw new InvalidOperationException("Unexpected exit from retry loop");
    }

    private async Task<(Rom? Rom, string HashType)> FindRomByHashAsync(string filePath, long fileSize, CancellationToken token)
    {
        try
        {
            // Quick size filter before computing expensive hashes
            // Try SHA1 first (most reliable), then MD5, then CRC
            if (!string.IsNullOrEmpty(_romDatabaseBySha1.FirstOrDefault().Value?.Sha1))
            {
                var actualSha1 = await ComputeHashAsync(filePath, SHA1.Create(), token);
                if (_romDatabaseBySha1.TryGetValue(actualSha1, out var romBySha1) && romBySha1.Size == fileSize)
                {
                    return (romBySha1, "SHA1");
                }
            }

            token.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(_romDatabaseByMd5.FirstOrDefault().Value?.Md5))
            {
                var actualMd5 = await ComputeHashAsync(filePath, MD5.Create(), token);
                if (_romDatabaseByMd5.TryGetValue(actualMd5, out var romByMd5) && romByMd5.Size == fileSize)
                {
                    return (romByMd5, "MD5");
                }
            }

            token.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(_romDatabaseByCrc.FirstOrDefault().Value?.Crc))
            {
                var actualCrc = await ComputeCrc32Async(filePath, token);
                if (_romDatabaseByCrc.TryGetValue(actualCrc, out var romByCrc) && romByCrc.Size == fileSize)
                {
                    return (romByCrc, "CRC32");
                }
            }

            return (null, string.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggerService.LogError("Validation", $"Error finding ROM by hash for '{filePath}': {ex.Message}");
            return (null, string.Empty);
        }
    }

    private static async Task RenameFileAsync(string sourcePath, string destPath)
    {
        const int maxRetries = 5;
        const int delayMs = 100;

        if (!File.Exists(sourcePath))
        {
            throw new IOException($"Source file not found: {sourcePath}");
        }

        if (File.Exists(destPath))
        {
            throw new IOException($"Destination file already exists: {destPath}");
        }

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await Task.Run(() => File.Move(sourcePath, destPath));
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == maxRetries)
                {
                    throw new IOException($"Failed to rename file after {maxRetries} attempts: {ex.Message}", ex);
                }

                await Task.Delay(delayMs * attempt);
            }
        }
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
        lock (_ctsLock)
        {
            _cts?.Cancel();
        }

        LogMessage("Cancellation requested. Finishing current operations...");
        _mainWindow.UpdateStatusBarMessage("Validation canceled.");
    }

    private void SetControlsState(bool enabled)
    {
        BrowseRomsFolderButton.IsEnabled = enabled;
        BrowseDatFileButton.IsEnabled = enabled;
        MoveSuccessCheckBox.IsEnabled = enabled;
        MoveFailedCheckBox.IsEnabled = enabled;
        RenameMatchedFilesCheckBox.IsEnabled = enabled;
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
        _renamedCount = 0;
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
            RenamedValue.Text = _renamedCount.ToString(CultureInfo.InvariantCulture);
        });
    }

    private void UpdateProcessingTimeDisplay()
    {
        var elapsed = _operationTimer.Elapsed;
        Application.Current.Dispatcher.InvokeAsync(() => { ProcessingTimeValue.Text = $@"{elapsed:hh\:mm\:ss}"; });
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
        LogMessage($"Renamed: {_renamedCount}");
        LogMessage($@"Total time: {_operationTimer.Elapsed:hh\:mm\:ss}");
        _mainWindow.UpdateStatusBarMessage("Validation complete.");

        var summaryText = $"Validation complete.\n\nSuccessful: {_successCount}\nFailed: {_failCount}\nUnknown: {_unknownCount}\nRenamed: {_renamedCount}";
        MessageBox.Show(_mainWindow, summaryText, "Validation Complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowError(string message)
    {
        MessageBox.Show(_mainWindow, message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public void Dispose()
    {
        lock (_ctsLock)
        {
            _cts?.Cancel(); // Cancel any ongoing operation
            _cts?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    #endregion
}