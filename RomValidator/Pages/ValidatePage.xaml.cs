using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;
using RomValidator.Models.NoIntro;
using RomValidator.Services;
using SharpSevenZip;

namespace RomValidator.Pages;

public partial class ValidatePage : IDisposable
{
    private readonly MainWindow _mainWindow;
    private Dictionary<string, List<Rom>> _romDatabase = [];
    private Dictionary<string, Rom> _romDatabaseBySha1 = [];
    private Dictionary<string, Rom> _romDatabaseByMd5 = [];
    private Dictionary<string, Rom> _romDatabaseByCrc = [];
    private Dictionary<string, Rom> _romDatabaseBySha256 = [];
    private string _loadedDatFilePath = string.Empty;
    private DateTime _loadedDatFileTimestamp;
    private CancellationTokenSource? _cts;
    private readonly object _ctsLock = new();

    // Statistics
    private int _totalFilesToProcess;
    private int _successCount;
    private int _failCount;
    private int _unknownCount;
    private int _renamedCount;
    private int _deletedCount;
    private readonly Stopwatch _operationTimer = new();

    public ValidatePage(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeComponent();
        SetupCheckboxHandlers();
        DisplayInstructions();
        ClearDatInfoDisplay();
        _mainWindow.UpdateStatusBarMessage("Ready.");
        _ = CheckForUpdatesOnStartupAsync().ContinueWith(static t =>
        {
            if (t.IsFaulted)
            {
                LoggerService.LogError("Startup", $"Update check failed: {t.Exception?.InnerException?.Message}");
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private void SetupCheckboxHandlers()
    {
        // Make Delete and Move mutually exclusive for safety
        DeleteFailedCheckBox.Checked += (_, _) =>
        {
            if (DeleteFailedCheckBox.IsChecked == true)
            {
                MoveFailedCheckBox.IsChecked = false;
            }
        };

        MoveFailedCheckBox.Checked += (_, _) =>
        {
            if (MoveFailedCheckBox.IsChecked == true)
            {
                DeleteFailedCheckBox.IsChecked = false;
            }
        };
    }

    private void DisplayInstructions()
    {
        LogMessage("Welcome to the ROM Validator.");
        LogMessage("This tool validates your ROM files against NoIntro DAT file to ensure they are accurate and uncorrupted.");
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
            var deleteFailed = DeleteFailedCheckBox.IsChecked == true;
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

            // Confirmation dialog for permanent deletion
            if (deleteFailed)
            {
                var confirmationResult = MessageBox.Show(
                    "WARNING: You have selected to PERMANENTLY DELETE failed/unknown files.\n\n" +
                    "This action CANNOT be undone. Files will be permanently removed from your disk.\n\n" +
                    $"Source folder: {romsFolderPath}\n\n" +
                    "Are you absolutely sure you want to continue?",
                    "Confirm Permanent Deletion",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirmationResult != MessageBoxResult.Yes)
                {
                    _mainWindow.UpdateStatusBarMessage("Validation cancelled - deletion not confirmed.");
                    return;
                }
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
            // Skip re-loading if the same DAT file was already loaded and hasn't changed (Issue 8 fix)
            var datFileInfo = new FileInfo(datFilePath);
            bool datLoaded;
            if (_loadedDatFilePath == datFilePath &&
                _loadedDatFileTimestamp == datFileInfo.LastWriteTimeUtc &&
                _romDatabase.Count > 0)
            {
                LogMessage("DAT file already loaded, skipping reload.");
                datLoaded = true;
            }
            else
            {
                datLoaded = await LoadDatFileAsync(datFilePath);
                if (datLoaded)
                {
                    _loadedDatFilePath = datFilePath;
                    _loadedDatFileTimestamp = datFileInfo.LastWriteTimeUtc;
                }
            }

            if (!datLoaded)
            {
                ShowError("Failed to load or parse the DAT file. Please check the log for details.");
                _mainWindow.UpdateStatusBarMessage("DAT file load failed.");
                return;
            }

            _mainWindow.UpdateStatusBarMessage("DAT file loaded. Starting ROM validation...");
            await PerformValidationAsync(romsFolderPath, moveSuccess, moveFailed, deleteFailed, renameMatched, operationCts.Token);
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

    private async Task PerformValidationAsync(string romsFolderPath, bool moveSuccess, bool moveFailed, bool deleteFailed, bool renameMatched, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var successPath = Path.Combine(romsFolderPath, "_success");
        var failPath = Path.Combine(romsFolderPath, "_fail");
        if (moveSuccess) Directory.CreateDirectory(successPath);
        if (moveFailed) Directory.CreateDirectory(failPath);

        await LogMessageAsync($"Move successful files: {moveSuccess}" + (moveSuccess ? $" (to {successPath})" : ""));
        await LogMessageAsync($"Move failed/unknown files: {moveFailed}" + (moveFailed ? $" (to {failPath})" : ""));
        await LogMessageAsync($"Delete failed/unknown files: {deleteFailed}" + (deleteFailed ? " (⚠️ PERMANENT - files will be deleted!)" : ""));
        await LogMessageAsync($"Rename files on hash match: {renameMatched}");

        var filesToScan = await Task.Run(() => Directory.GetFiles(romsFolderPath), token);
        _totalFilesToProcess = filesToScan.Length;
        await Application.Current.Dispatcher.InvokeAsync(() => ProgressBar.Maximum = _totalFilesToProcess);
        UpdateStatsDisplay();
        await LogMessageAsync($"Found {_totalFilesToProcess} files to validate.");
        _mainWindow.UpdateStatusBarMessage($"Found {_totalFilesToProcess} files. Starting validation...");

        if (_totalFilesToProcess == 0)
        {
            _mainWindow.UpdateStatusBarMessage("No files found to validate.");
            return;
        }

        var filesActuallyProcessedCount = 0;

        // Sequential Processing is intentional to prevent race conditions on file moves.
        // Parallel processing could cause conflicts when multiple threads attempt to move
        // files to the same _success/_fail directories simultaneously.
        foreach (var filePath in filesToScan)
        {
            // Check cancellation at the start of each file
            if (token.IsCancellationRequested) break;

            await ProcessFileAsync(filePath, successPath, failPath, moveSuccess, moveFailed, deleteFailed, renameMatched, token);

            var processedSoFar = Interlocked.Increment(ref filesActuallyProcessedCount);
            UpdateProgressDisplay(processedSoFar, _totalFilesToProcess, Path.GetFileName(filePath));
            UpdateStatsDisplay();
            UpdateProcessingTimeDisplay();

            // Force UI to refresh after each file for real-time log display
            await FlushUiUpdatesAsync();
        }

        _mainWindow.UpdateStatusBarMessage("Validation complete.");
    }

    private async Task ProcessFileAsync(string filePath, string successPath, string failPath, bool moveSuccess, bool moveFailed, bool deleteFailed, bool renameMatched, CancellationToken token)
    {
        var fileName = Path.GetFileName(filePath);
        token.ThrowIfCancellationRequested();

        var fileNameMatch = _romDatabase.TryGetValue(fileName, out var expectedRoms);
        var hashesAlreadyVerified = false;
        string? verifiedMatchDetails = null;

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
            var (hashMatchedRom, matchedHash) = await FindRomByHashAsync(filePath, token);

            if (hashMatchedRom != null)
            {
                // Found a match by hash! Determine new file path
                var directory = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(directory))
                {
                    Interlocked.Increment(ref _failCount);
                    LogMessage($"[FAILED] {fileName} - Could not determine directory for rename operation.");
                    return;
                }

                // Sanitize the ROM name to prevent path traversal attacks
                var sanitizedRomName = Path.GetFileName(hashMatchedRom.Name);
                if (string.IsNullOrEmpty(sanitizedRomName))
                {
                    Interlocked.Increment(ref _failCount);
                    LogMessage($"[FAILED] {fileName} - Invalid ROM name in DAT file.");
                    return;
                }

                string newFilePath;
                string displayName;

                // Check if this is an archive file - preserve archive extension
                if (HashCalculator.IsArchiveFile(fileName))
                {
                    var originalExtension = Path.GetExtension(fileName);
                    var romNameWithoutExt = Path.GetFileNameWithoutExtension(sanitizedRomName);
                    newFilePath = Path.Combine(directory, romNameWithoutExt + originalExtension);
                    displayName = romNameWithoutExt + originalExtension;
                }
                else
                {
                    newFilePath = Path.Combine(directory, sanitizedRomName);
                    displayName = sanitizedRomName;
                }

                // Check if the file already has the correct filename - no need to rename
                if (string.Equals(fileName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    // Filename is already correct, just treat it as a match without renaming
                    expectedRoms = [hashMatchedRom];
                    fileNameMatch = true;
                    hashesAlreadyVerified = true;
                    verifiedMatchDetails = $"{matchedHash}: {GetHashValueByType(hashMatchedRom, matchedHash)}";
                    // Continue to hash validation below (file will be processed as a success without rename)
                }
                else
                {
                    // Filename differs, proceed with rename
                    try
                    {
                        await RenameFileAsync(filePath, newFilePath);

                        // If this is an archive, also rename the file inside to match the DAT entry
                        if (HashCalculator.IsArchiveFile(fileName))
                        {
                            try
                            {
                                await RenameFileInsideArchiveAsync(newFilePath, hashMatchedRom.Name);
                            }
                            catch (Exception archiveEx)
                            {
                                // Log the error but don't fail the entire rename operation
                                // The file was already renamed successfully, only the internal archive rename failed
                                LogMessage($"[WARNING] {fileName} -> {displayName} renamed, but failed to rename content inside archive: {archiveEx.Message}");
                                _ = _mainWindow.BugReportService.SendBugReportAsync($"Error renaming file inside archive '{newFilePath}' to '{hashMatchedRom.Name}'", archiveEx);
                            }
                        }

                        Interlocked.Increment(ref _renamedCount);
                        LogMessage($"[RENAMED] {fileName} -> {displayName} (matched by {matchedHash})");

                        // Update variables to process renamed file
                        filePath = newFilePath;
                        fileName = Path.GetFileName(newFilePath);
                        expectedRoms = [hashMatchedRom];
                        fileNameMatch = true;
                        hashesAlreadyVerified = true; // Hashes were just verified by FindRomByHashAsync (Issue 7 fix)
                        verifiedMatchDetails = $"{matchedHash}: {GetHashValueByType(hashMatchedRom, matchedHash)}";
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _failCount);
                        LogMessage($"[FAILED] {fileName} - Hash matched {hashMatchedRom.Name} but rename failed: {ex.Message}");
                        // Don't send bug report for "destination already exists" - it's a user/data issue, not a code bug
                        if (!ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                        {
                            _ = _mainWindow.BugReportService.SendBugReportAsync($"Error renaming file '{fileName}' to '{hashMatchedRom.Name}'", ex);
                        }

                        if (moveFailed)
                        {
                            await MoveFileAsync(filePath, Path.Combine(failPath, fileName));
                        }
                        else if (deleteFailed)
                        {
                            await DeleteFileAsync(filePath, fileName);
                        }

                        return;
                    }
                } // Close the else block for "filename differs, proceed with rename"
            }
        }

        if (!fileNameMatch || expectedRoms == null || expectedRoms.Count == 0)
        {
            Interlocked.Increment(ref _unknownCount);
            LogMessage($"[UNKNOWN] {fileName} - Not found in DAT file.");
            if (moveFailed)
            {
                await MoveFileAsync(filePath, Path.Combine(failPath, fileName));
            }
            else if (deleteFailed)
            {
                await DeleteFileAsync(filePath, fileName);
            }

            return;
        }

        var fileInfo2 = new FileInfo(filePath);

        if (!fileInfo2.Exists)
        {
            Interlocked.Increment(ref _failCount);
            LogMessage($"[ERROR] {fileName} - File not found during validation (may have been deleted or moved).");
            return;
        }

        // Skip re-hashing if we already verified hashes during FindRomByHashAsync (Issue 7 fix)
        bool hashMatch;
        string matchDetails;
        if (hashesAlreadyVerified && expectedRoms.Count == 1)
        {
            hashMatch = true;
            matchDetails = verifiedMatchDetails ?? "Hash verified during rename matching";
        }
        else
        {
            (hashMatch, matchDetails) = await CheckHashesAsync(filePath, expectedRoms, token);
        }

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
            if (!string.IsNullOrEmpty(matchDetails) && (matchDetails.Contains("Archive extraction failed") || matchDetails.Contains("Error")))
            {
                // Optional: Highlight critical errors
                _mainWindow.UpdateStatusBarMessage($"Error processing {fileName}");
            }

            if (moveFailed)
            {
                await MoveFileAsync(filePath, Path.Combine(failPath, fileName));
            }
            else if (deleteFailed)
            {
                await DeleteFileAsync(filePath, fileName);
            }
        }
    }

    private static string? GetHashValueByType(Rom rom, string hashType)
    {
        return hashType.ToUpperInvariant() switch
        {
            "SHA256" => rom.Sha256,
            "SHA1" => rom.Sha1,
            "MD5" => rom.Md5,
            "CRC32" => rom.Crc,
            _ => null
        };
    }

    private async Task<(bool IsValid, string Message)> CheckHashesAsync(string filePath, List<Rom> expectedRoms, CancellationToken token)
    {
        try
        {
            // Use HashCalculator to properly handle archives - extracts and hashes contents
            var gameFiles = await HashCalculator.CalculateHashesAsync(filePath, token, _mainWindow.BugReportService);

            // Check if extraction failed
            if (gameFiles.Count == 1 && !string.IsNullOrEmpty(gameFiles[0].ErrorMessage))
            {
                return (false, $"Archive extraction failed: {gameFiles[0].ErrorMessage}");
            }

            // For archives with multiple files, we validate each file inside
            // For regular files, there's just one entry
            var successDetails = new List<string>();
            var allErrors = new List<string>();

            foreach (var gameFile in gameFiles)
            {
                // Check this file against all potential ROM definitions
                var fileMatched = false;
                var fileErrors = new List<string>();

                foreach (var expectedRom in expectedRoms)
                {
                    var sizeMatch = gameFile.FileSize == expectedRom.Size;
                    var sha256Match = !string.IsNullOrEmpty(gameFile.Sha256) && (string.IsNullOrEmpty(expectedRom.Sha256) || gameFile.Sha256.Equals(expectedRom.Sha256, StringComparison.OrdinalIgnoreCase));
                    var sha1Match = !string.IsNullOrEmpty(gameFile.Sha1) && (string.IsNullOrEmpty(expectedRom.Sha1) || gameFile.Sha1.Equals(expectedRom.Sha1, StringComparison.OrdinalIgnoreCase));
                    var md5Match = !string.IsNullOrEmpty(gameFile.Md5) && (string.IsNullOrEmpty(expectedRom.Md5) || gameFile.Md5.Equals(expectedRom.Md5, StringComparison.OrdinalIgnoreCase));
                    var crcMatch = !string.IsNullOrEmpty(gameFile.Crc32) && (string.IsNullOrEmpty(expectedRom.Crc) || gameFile.Crc32.Equals(expectedRom.Crc, StringComparison.OrdinalIgnoreCase));

                    if (sizeMatch && sha256Match && sha1Match && md5Match && crcMatch)
                    {
                        var details = new List<string>();
                        if (!string.IsNullOrEmpty(expectedRom.Sha256)) details.Add($"SHA256: {gameFile.Sha256}");
                        if (!string.IsNullOrEmpty(expectedRom.Sha1)) details.Add($"SHA1: {gameFile.Sha1}");
                        if (!string.IsNullOrEmpty(expectedRom.Md5)) details.Add($"MD5: {gameFile.Md5}");
                        if (!string.IsNullOrEmpty(expectedRom.Crc)) details.Add($"CRC32: {gameFile.Crc32}");

                        successDetails.Add($"{gameFile.FileName}: {(details.Count > 0 ? string.Join(", ", details) : "Size matched (no hashes in DAT)")}");
                        fileMatched = true;
                        break;
                    }

                    // Collect error info for this expected ROM
                    var mismatchReason = new List<string>();
                    if (!sizeMatch) mismatchReason.Add($"Size (Exp: {expectedRom.Size}, Got: {gameFile.FileSize})");
                    if (!sha256Match) mismatchReason.Add("SHA256 mismatch");
                    if (!sha1Match) mismatchReason.Add("SHA1 mismatch");
                    if (!md5Match) mismatchReason.Add("MD5 mismatch");
                    if (!crcMatch) mismatchReason.Add("CRC32 mismatch");
                    fileErrors.Add($"[{string.Join(", ", mismatchReason)}]");
                }

                if (!fileMatched)
                {
                    allErrors.Add($"{gameFile.FileName}: {string.Join(" | ", fileErrors)}");
                }
            }

            // Return success if at least one file inside matched
            if (successDetails.Count > 0)
            {
                return (true, string.Join("; ", successDetails));
            }

            return (false, $"No match found among {expectedRoms.Count} DAT entries: {string.Join("; ", allErrors)}");
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
                await using var stream = new FileStream(datFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                using var reader = new StreamReader(stream);
                var buffer = new char[5000];
                var charsRead = await reader.ReadBlockAsync(buffer, 0, 5000);
                datFilePreview = new string(buffer, 0, charsRead);

                if (charsRead == 5000)
                {
                    datFilePreview += "\n\n[... FILE TRUNCATED FOR PREVIEW ...]";
                }
            }
            catch
            {
                datFilePreview = "[Could not read file preview]";
            }

            // Quick check for common incompatible formats - MUST happen before XML parsing

            // 1. Check for ZIP format (magic number "PK")
            if (datFilePreview.StartsWith("PK", StringComparison.Ordinal))
            {
                const string errorMsg = "Incompatible file format.\n\n" +
                                        "This application only supports No-Intro XML DAT files.\n\n" +
                                        "The selected file appears to be a ZIP archive. Please unzip it first and load the .dat or .xml file inside.";
                LogMessage($"Error: {errorMsg}");

                // Send sample to developer
                var detailedError = $"User attempted to load a ZIP file as a DAT file: {Path.GetFileName(datFilePath)}\n\n" +
                                    $"File Preview:\n{datFilePreview}";
                _ = _mainWindow.BugReportService.SendBugReportAsync(detailedError);

                ShowIncompatibleDatFileError(errorMsg);
                ClearRomDatabase();
                _mainWindow.UpdateStatusBarMessage("ZIP files are not supported.");
                return false;
            }

            // 2. Check for HTML format (likely from GitLab/GitHub UI download error)
            if (datFilePreview.Contains("<!DOCTYPE html>", StringComparison.OrdinalIgnoreCase) ||
                datFilePreview.Contains("<html", StringComparison.OrdinalIgnoreCase))
            {
                const string errorMsg = "Incompatible DAT file format.\n\n" +
                                        "This application only supports No-Intro XML DAT files.\n\n" +
                                        "The selected file appears to be an HTML webpage. This usually happens when downloading from GitHub or GitLab using 'Save Link As' instead of downloading the raw file.";
                LogMessage($"Error: {errorMsg}");

                // Send sample to developer
                var detailedError = $"User attempted to load an HTML file as a DAT file: {Path.GetFileName(datFilePath)}\n\n" +
                                    $"File Preview:\n{datFilePreview}";
                _ = _mainWindow.BugReportService.SendBugReportAsync(detailedError);

                ShowIncompatibleDatFileError(errorMsg);
                ClearRomDatabase();
                _mainWindow.UpdateStatusBarMessage("HTML pages are not supported.");
                return false;
            }

            // 3. Scan the first several lines to detect incompatible formats (ClrMamePro, MAME, etc.)
            var isClrMameProFormat = false;
            var isMameFormat = false;
            using (var sr = new StreamReader(datFilePath, Encoding.UTF8, true, 4096))
            {
                for (var i = 0; i < 20; i++) // Scan more lines for better detection
                {
                    var line = await sr.ReadLineAsync();
                    if (line is null) break;

                    var trimmedLine = line.TrimStart();

                    // Detect ClrMamePro text format
                    if (trimmedLine.Contains("clrmamepro (", StringComparison.OrdinalIgnoreCase) &&
                        !trimmedLine.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
                    {
                        isClrMameProFormat = true;
                        break;
                    }

                    // Detect MAME/ClrMamePro XML format (uses <machine> instead of <game>)
                    if (trimmedLine.Contains("<machine", StringComparison.OrdinalIgnoreCase))
                    {
                        isMameFormat = true;
                        break;
                    }

                    // Stop early if we hit No-Intro specific tags to avoid false positives
                    if (trimmedLine.StartsWith("<game", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
            }

            if (isClrMameProFormat || isMameFormat)
            {
                var formatType = isClrMameProFormat ? "ClrMamePro text" : "MAME/ClrMamePro XML";
                var errorMsg = $"Incompatible DAT file format ({formatType}).\n\n" +
                               "The current version of ROM Validator only supports No-Intro XML DAT files.\n\n" +
                               "Please download a compatible XML format DAT file from https://no-intro.org/";
                LogMessage($"Error: {errorMsg}");

                // Send sample to developer
                var detailedError = $"User attempted to load {formatType} format DAT file: {Path.GetFileName(datFilePath)}\n\n" +
                                    $"File Preview:\n{datFilePreview}";
                _ = _mainWindow.BugReportService.SendBugReportAsync(detailedError);

                ShowIncompatibleDatFileError(errorMsg);
                ClearRomDatabase(); // Clear stale data from previous valid DAT (Issue 10 fix)
                _mainWindow.UpdateStatusBarMessage("DAT file format not supported.");
                return false;
            }

            // First validation pass - check for <datafile> root element
            await using (var validationStream = new FileStream(datFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
            {
                using var validationReader = XmlReader.Create(validationStream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });

                if (!validationReader.ReadToFollowing("datafile"))
                {
                    const string errorMsg = "Incompatible DAT file format.\n\n" +
                                            "This application only supports No-Intro XML DAT files.\n\n" +
                                            "The selected file does not contain the required <datafile> root element.\n\n" +
                                            "Please ensure you are using a No-Intro XML DAT file from https://no-intro.org/";
                    LogMessage($"Error: {errorMsg}");

                    // Send sample to developer
                    var detailedError = $"User attempted to load incompatible DAT file: {Path.GetFileName(datFilePath)}\n\n" +
                                        $"Error: Missing <datafile> root element\n\n" +
                                        $"File Preview:\n{datFilePreview}";
                    _ = _mainWindow.BugReportService.SendBugReportAsync(detailedError);

                    ShowIncompatibleDatFileError(errorMsg);
                    ClearRomDatabase(); // Clear stale data from previous valid DAT (Issue 10 fix)
                    _mainWindow.UpdateStatusBarMessage("DAT file format not supported.");
                    return false;
                }
            }

            // Create serializer with our updated models
            var serializer = new XmlSerializer(typeof(Datafile));

            // Deserialize the DAT file
            await using var deserializeStream = new FileStream(datFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            using var xmlReader = XmlReader.Create(deserializeStream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });

            var datafile = await Task.Run(() => (Datafile?)serializer.Deserialize(xmlReader));
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
                ClearRomDatabase(); // Clear stale data from previous valid DAT (Issue 10 fix)
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

            _romDatabaseBySha256 = allRoms
                .Where(static r => !string.IsNullOrEmpty(r.Sha256))
                .GroupBy(static r => r.Sha256, StringComparer.OrdinalIgnoreCase)
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
                ClearRomDatabase(); // Clear stale data from previous valid DAT (Issue 10 fix)
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
            ClearRomDatabase(); // Clear stale data from previous valid DAT (Issue 10 fix)
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
            ClearRomDatabase(); // Clear stale data from previous valid DAT (Issue 10 fix)
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
            ClearRomDatabase(); // Clear stale data from previous valid DAT (Issue 10 fix)
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

    private async Task<(Rom? Rom, string HashType)> FindRomByHashAsync(string filePath, CancellationToken token)
    {
        try
        {
            // Use HashCalculator to properly handle archives - extracts and hashes contents
            var gameFiles = await HashCalculator.CalculateHashesAsync(filePath, token, _mainWindow.BugReportService);

            // Check if extraction failed
            if (gameFiles.Count == 1 && !string.IsNullOrEmpty(gameFiles[0].ErrorMessage))
            {
                return (null, string.Empty);
            }

            // For archives, check each file inside. For regular files, there's just one entry.
            foreach (var gameFile in gameFiles)
            {
                // Try SHA256 first (most reliable), then SHA1, then MD5, then CRC
                if (!string.IsNullOrEmpty(gameFile.Sha256) && _romDatabaseBySha256.TryGetValue(gameFile.Sha256, out var romBySha256))
                {
                    if (romBySha256.Size == gameFile.FileSize)
                    {
                        return (romBySha256, "SHA256");
                    }
                }

                token.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(gameFile.Sha1) && _romDatabaseBySha1.TryGetValue(gameFile.Sha1, out var romBySha1))
                {
                    if (romBySha1.Size == gameFile.FileSize)
                    {
                        return (romBySha1, "SHA1");
                    }
                }

                token.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(gameFile.Md5) && _romDatabaseByMd5.TryGetValue(gameFile.Md5, out var romByMd5))
                {
                    if (romByMd5.Size == gameFile.FileSize)
                    {
                        return (romByMd5, "MD5");
                    }
                }

                token.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(gameFile.Crc32) && _romDatabaseByCrc.TryGetValue(gameFile.Crc32, out var romByCrc))
                {
                    if (romByCrc.Size == gameFile.FileSize)
                    {
                        return (romByCrc, "CRC32");
                    }
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
            _ = _mainWindow.BugReportService.SendBugReportAsync($"Error finding ROM by hash for file '{filePath}'", ex);
            return (null, string.Empty);
        }
    }

    private static async Task RenameFileAsync(string sourcePath, string destPath)
    {
        const int maxRetries = 10;
        const int delayMs = 200;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await Task.Run(() => File.Move(sourcePath, destPath));
                return;
            }
            catch (FileNotFoundException)
            {
                throw new IOException($"Source file not found: {sourcePath}");
            }
            catch (IOException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"Destination file already exists: {destPath}");
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
        const int maxRetries = 10;
        const int delayMs = 200;

        // Generate unique destination path to prevent overwriting existing files (Issue 11 fix)
        destPath = GetUniqueDestPath(destPath);

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);
                await Task.Run(() => File.Move(sourcePath, destPath));
                return;
            }
            catch (FileNotFoundException)
            {
                LogMessage($"   -> File not found, cannot move: {Path.GetFileName(sourcePath)}");
                return;
            }
            catch (IOException ex) when (IsDiskFullError(ex))
            {
                LogMessage($"   -> FAILED to move {Path.GetFileName(sourcePath)}: Disk is full.");
                _mainWindow.UpdateStatusBarMessage("Cannot move file - disk is full.");
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

    private static string GetUniqueDestPath(string destPath)
    {
        if (!File.Exists(destPath))
        {
            return destPath;
        }

        // File already exists, generate a unique name by appending (1), (2), etc.
        var directory = Path.GetDirectoryName(destPath) ?? string.Empty;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(destPath);
        var extension = Path.GetExtension(destPath);
        var counter = 1;

        string newDestPath;
        do
        {
            newDestPath = Path.Combine(directory, $"{fileNameWithoutExt} ({counter}){extension}");
            counter++;
        } while (File.Exists(newDestPath));

        return newDestPath;
    }

    private static bool IsDiskFullError(IOException ex)
    {
        const int errorDiskFull = unchecked((int)0x80070070);
        return ex.HResult == errorDiskFull;
    }

    private async Task DeleteFileAsync(string filePath, string fileName)
    {
        const int maxRetries = 10;
        const int delayMs = 200;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await Task.Run(() => File.Delete(filePath));
                Interlocked.Increment(ref _deletedCount);
                LogMessage($"   -> DELETED: {fileName}");
                return;
            }
            catch (FileNotFoundException)
            {
                LogMessage($"   -> File not found, cannot delete: {fileName}");
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == maxRetries)
                {
                    LogMessage($"   -> FAILED to delete {fileName} after {maxRetries} attempts. Error: {ex.Message}");
                    _mainWindow.UpdateStatusBarMessage($"Failed to delete {fileName}.");
                    _ = _mainWindow.BugReportService.SendBugReportAsync($"Error deleting file '{filePath}' after {maxRetries} attempts", ex);
                    return;
                }

                await Task.Delay(delayMs * attempt);
            }
            catch (Exception ex)
            {
                LogMessage($"   -> FAILED to delete {fileName}. Error: {ex.Message}");
                _mainWindow.UpdateStatusBarMessage($"Failed to delete {fileName}.");
                _ = _mainWindow.BugReportService.SendBugReportAsync($"Error deleting file '{filePath}'", ex);
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
        DeleteFailedCheckBox.IsEnabled = enabled;
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
        }, DispatcherPriority.Background);
    }

    private async Task LogMessageAsync(string message)
    {
        var timestampedMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            LogViewer.AppendText($"{timestampedMessage}{Environment.NewLine}");
            LogViewer.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private static async Task FlushUiUpdatesAsync()
    {
        // Force the dispatcher to process pending UI updates
        await Application.Current.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
    }

    private void ResetOperationStats()
    {
        _totalFilesToProcess = 0;
        _successCount = 0;
        _failCount = 0;
        _unknownCount = 0;
        _renamedCount = 0;
        _deletedCount = 0;
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
            DeletedValue.Text = _deletedCount.ToString(CultureInfo.InvariantCulture);
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

    private void ClearRomDatabase()
    {
        // Clear all ROM lookup dictionaries to prevent stale data (Issue 10 fix)
        _romDatabase = [];
        _romDatabaseBySha1 = [];
        _romDatabaseByMd5 = [];
        _romDatabaseByCrc = [];
        _romDatabaseBySha256 = [];
        _loadedDatFilePath = string.Empty;
        _loadedDatFileTimestamp = DateTime.MinValue;
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
        LogMessage($"Deleted: {_deletedCount}");
        LogMessage($@"Total time: {_operationTimer.Elapsed:hh\:mm\:ss}");
        _mainWindow.UpdateStatusBarMessage("Validation complete.");

        var summaryText = $"Validation complete.\n\nSuccessful: {_successCount}\nFailed: {_failCount}\nUnknown: {_unknownCount}\nRenamed: {_renamedCount}\nDeleted: {_deletedCount}";
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

    #region Archive Helpers

    /// <summary>
    /// Renames the file inside an archive to match the target name specified in the DAT entry.
    /// For 7z files: extracts contents, renames file, repackages as 7z using SevenZipSharp.
    /// For zip files: extracts contents, renames file, repackages as zip using System.IO.Compression.
    /// For other archive types: extracts contents, renames file, creates new 7z archive.
    /// </summary>
    private async Task RenameFileInsideArchiveAsync(string archivePath, string targetFileName)
    {
        var tempDir = TempDirectoryHelper.CreateTempDirectory("romvalidator_rename");
        var is7ZFile = archivePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);
        var archiveFileName = Path.GetFileName(archivePath);

        try
        {
            // Initialize SevenZipSharp
            try
            {
                await Task.Run(static () => HashCalculator.InitializeSevenZip());
            }
            catch (Exception initEx)
            {
                var errorMsg = $"Failed to initialize SevenZipSharp for archive '{archiveFileName}'";
                LoggerService.LogError("RenameInsideArchive", errorMsg);
                _ = _mainWindow.BugReportService.SendBugReportAsync(errorMsg, initEx);
                throw new InvalidOperationException($"SevenZipSharp initialization failed: {initEx.Message}", initEx);
            }

            // Extract archive contents using SevenZipSharp
            try
            {
                await Task.Run(() =>
                {
                    using var extractor = new SharpSevenZipExtractor(archivePath);
                    extractor.ExtractArchive(tempDir);
                });
            }
            catch (Exception extractEx)
            {
                var errorMsg = $"Failed to extract archive '{archiveFileName}' for internal file rename";
                LoggerService.LogError("RenameInsideArchive", errorMsg);
                _ = _mainWindow.BugReportService.SendBugReportAsync(errorMsg, extractEx);
                throw new InvalidOperationException($"Archive extraction failed: {extractEx.Message}", extractEx);
            }

            // Find the extracted file(s)
            var extractedFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
            if (extractedFiles.Length == 0)
            {
                var errorMsg = $"Archive '{archiveFileName}' is empty or extraction failed - no files found in temp directory";
                LoggerService.LogError("RenameInsideArchive", errorMsg);
                _ = _mainWindow.BugReportService.SendBugReportAsync(errorMsg);
                throw new InvalidOperationException("Archive is empty or extraction failed.");
            }

            // Build a mapping of original paths to new names
            var fileMapping = new List<(string OriginalPath, string NewName)>();
            var targetFileRenamed = false;

            foreach (var sourceFile in extractedFiles)
            {
                var sourceFileName = Path.GetFileName(sourceFile);
                string destFileName;

                // Rename the first file to targetFileName
                if (!targetFileRenamed)
                {
                    destFileName = targetFileName;
                    targetFileRenamed = true;
                }
                else
                {
                    // Keep other files with their original names
                    destFileName = sourceFileName;
                }

                fileMapping.Add((sourceFile, destFileName));
            }

            // Delete the original archive
            try
            {
                File.Delete(archivePath);
            }
            catch (Exception deleteEx)
            {
                var errorMsg = $"Failed to delete original archive '{archiveFileName}' before repackaging";
                LoggerService.LogError("RenameInsideArchive", errorMsg);
                _ = _mainWindow.BugReportService.SendBugReportAsync(errorMsg, deleteEx);
                throw new InvalidOperationException($"Cannot delete original archive: {deleteEx.Message}", deleteEx);
            }

            if (is7ZFile)
            {
                // Create new 7z archive using SevenZipSharp compressor
                try
                {
                    await Task.Run(() =>
                    {
                        var compressor = new SharpSevenZipCompressor
                        {
                            ArchiveFormat = OutArchiveFormat.SevenZip,
                            CompressionLevel = SharpSevenZip.CompressionLevel.Normal,
                            CompressionMethod = CompressionMethod.Lzma2
                        };

                        // Create a temporary dictionary mapping file paths to archive entry names
                        // Only include files that actually exist (SharpSevenZip requires all files to exist)
                        var filesDictionary = new Dictionary<string, string>();
                        var missingFiles = new List<string>();
                        foreach (var (originalPath, newName) in fileMapping)
                        {
                            if (File.Exists(originalPath))
                            {
                                filesDictionary[originalPath] = newName;
                            }
                            else
                            {
                                missingFiles.Add($"'{originalPath}' (expected entry name: '{newName}')");
                            }
                        }

                        if (missingFiles.Count > 0)
                        {
                            throw new FileNotFoundException(
                                $"Cannot create archive: {missingFiles.Count} file(s) not found in temp directory: {string.Join(", ", missingFiles)}. " +
                                $"This may indicate the archive structure changed during extraction or files were moved unexpectedly.");
                        }

                        if (filesDictionary.Count == 0)
                        {
                            throw new InvalidOperationException("No files available to create archive. All extracted files are missing from temp directory.");
                        }

                        compressor.CompressFileDictionary(filesDictionary, archivePath);
                    });
                }
                catch (Exception compressEx)
                {
                    var errorMsg = $"Failed to compress 7z archive '{archiveFileName}' after renaming internal file";
                    LoggerService.LogError("RenameInsideArchive", errorMsg);
                    _ = _mainWindow.BugReportService.SendBugReportAsync(errorMsg, compressEx);
                    throw new InvalidOperationException($"Archive compression failed: {compressEx.Message}", compressEx);
                }
            }
            else
            {
                // Create new zip archive using System.IO.Compression
                try
                {
                    await Task.Run(() =>
                    {
                        using var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create);
                        foreach (var (originalPath, newName) in fileMapping)
                        {
                            if (File.Exists(originalPath))
                            {
                                zip.CreateEntryFromFile(originalPath, newName);
                            }
                        }
                    });
                }
                catch (Exception zipEx)
                {
                    var errorMsg = $"Failed to create zip archive '{archiveFileName}' after renaming internal file";
                    LoggerService.LogError("RenameInsideArchive", errorMsg);
                    _ = _mainWindow.BugReportService.SendBugReportAsync(errorMsg, zipEx);
                    throw new InvalidOperationException($"ZIP archive creation failed: {zipEx.Message}", zipEx);
                }
            }
        }
        finally
        {
            // Cleanup temp directory with error logging
            TempDirectoryHelper.CleanupTempDirectory(tempDir);
        }
    }

    #endregion
}