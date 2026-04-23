using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SharpSevenZip;
using SharpSevenZip.Exceptions;
using RomValidator.Models;

namespace RomValidator.Services;

/// <summary>
/// Provides static methods for calculating hash values (CRC32, MD5, SHA1, SHA256) for files and archives.
/// Supports both regular files and archive formats (ZIP, 7Z, RAR) with automatic extraction.
/// </summary>
public static partial class HashCalculator
{
    // Archive file extensions supported - shared regex pattern
    private static readonly Regex SArchiveExtensionRegex = MyRegex();
    private static bool _sevenZipInitialized;
    private static readonly object InitLock = new();
    private const int BufferSize = 65536; // 64KB buffer for file operations
    private const int InitialRetryDelayMs = 100; // Initial delay for retry attempts in milliseconds
    private const int ErrorDiskFull = unchecked((int)0x80070070);

    /// <summary>
    /// Checks if an exception indicates a disk-full error.
    /// </summary>
    private static bool IsDiskFullError(Exception ex)
    {
        if (ex.HResult == ErrorDiskFull) return true;
        var msg = ex.Message;
        return msg.Contains("not enough space", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("disk full", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("ERROR_DISK_FULL", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Initializes the SevenZip library for archive extraction operations.
    /// This method detects the system architecture and sets the appropriate native library path.
    /// This is called from App.xaml.cs at startup, but this method ensures
    /// it's initialized before any archive operations if needed.
    /// </summary>
    public static void InitializeSevenZip()
    {
        lock (InitLock)
        {
            if (_sevenZipInitialized) return;

            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string libraryPath;

            // Detect architecture and set appropriate library path
            // Supports win-x64 and win-arm64
            if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
            {
                libraryPath = Path.Combine(appDirectory, "7z_arm64.dll");
            }
            else
            {
                // Default to x64 for x64 and other architectures
                libraryPath = Path.Combine(appDirectory, "7z_x64.dll");
            }

            if (File.Exists(libraryPath))
            {
                SharpSevenZipBase.SetLibraryPath(libraryPath);
            }
            // If not found, SharpSevenZip will attempt auto-detection

            _sevenZipInitialized = true;
        }
    }

    /// <summary>
    /// Calculates hash values (CRC32, MD5, SHA1, SHA256) for a file or archive.
    /// Supports both regular files and archive formats (ZIP, 7Z, RAR).
    /// </summary>
    /// <param name="filePath">The path to the file or archive to process.</param>
    /// <param name="cancellationToken">Cancellation token to stop the operation.</param>
    /// <param name="bugReportService">Optional bug report service for error tracking.</param>
    /// <returns>A list of <see cref="GameFile"/> objects containing hash results for each file.</returns>
    public static async Task<List<GameFile>> CalculateHashesAsync(string filePath, CancellationToken cancellationToken, BugReportService? bugReportService = null)
    {
        InitializeSevenZip();

        var fileInfo = new FileInfo(filePath);
        var gameFiles = new List<GameFile>();

        // Check if file is an archive
        if (IsArchiveFile(fileInfo.Name))
        {
            try
            {
                // Open archive using SevenZipSharp
                using var extractor = new SharpSevenZipExtractor(filePath);
                cancellationToken.ThrowIfCancellationRequested();

                // Process each file in the archive
                foreach (var entry in extractor.ArchiveFileData)
                {
                    if (entry.IsDirectory) continue;

                    cancellationToken.ThrowIfCancellationRequested();

                    // Create algorithm instances once per archive entry
                    using var crc32 = new Crc32Algorithm();
                    using var md5 = MD5.Create();
                    using var sha1 = SHA1.Create();
                    using var sha256 = SHA256.Create();

                    // Extract to memory stream and hash
                    await using var entryStream = new MemoryStream();

                    try
                    {
                        await extractor.ExtractFileAsync(entry.Index, entryStream);
                    }
            catch (ExtractionFailedException entryEx)
            {
                // Individual entry is corrupted - log warning but do not send bug report for corrupt files
                if (IsDiskFullError(entryEx))
                {
                    _ = bugReportService?.SendBugReportAsync($"Archive entry extraction failed for '{entry.FileName}' in archive '{fileInfo.Name}' - DISK FULL", entryEx);
                }
                LoggerService.LogWarning("HashCalculator", $"Archive entry extraction failed for '{entry.FileName}' in archive '{fileInfo.Name}': {entryEx.Message}");
                gameFiles.Add(new GameFile
                {
                    FileName = entry.FileName,
                    GameName = Path.GetFileNameWithoutExtension(entry.FileName),
                    FileSize = (long)entry.Size,
                    ErrorMessage = "This file is corrupted or damaged within the archive",
                    Crc32 = "ERROR",
                    Md5 = "ERROR",
                    Sha1 = "ERROR",
                    Sha256 = "ERROR"
                });
                continue;
            }
            catch (SharpSevenZipException entryEx)
            {
                // Internal error extracting individual entry - log warning but do not send bug report for corrupt files
                if (IsDiskFullError(entryEx))
                {
                    _ = bugReportService?.SendBugReportAsync($"Internal error extracting entry '{entry.FileName}' from archive '{fileInfo.Name}' - DISK FULL", entryEx);
                }
                LoggerService.LogWarning("HashCalculator", $"Internal error extracting entry '{entry.FileName}' from archive '{fileInfo.Name}': {entryEx.Message}");
                gameFiles.Add(new GameFile
                {
                    FileName = entry.FileName,
                    GameName = Path.GetFileNameWithoutExtension(entry.FileName),
                    FileSize = (long)entry.Size,
                    ErrorMessage = "An error occurred while extracting this file from the archive. It may be corrupted or use an unsupported format.",
                    Crc32 = "ERROR",
                    Md5 = "ERROR",
                    Sha1 = "ERROR",
                    Sha256 = "ERROR"
                });
                continue;
            }

                    entryStream.Position = 0;

                    var gameFile = await ProcessStreamAsync(
                        entryStream,
                        entry.FileName,
                        crc32, md5, sha1, sha256,
                        cancellationToken,
                        bugReportService).ConfigureAwait(false);

                    if (gameFile != null)
                    {
                        // Track the original archive filename for proper DAT generation
                        gameFile.ArchiveFileName = fileInfo.Name;
                        gameFiles.Add(gameFile);
                    }
                }

                return gameFiles;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SharpSevenZipArchiveException archiveEx)
            {
                // Invalid or unrecognized archive format - do not send bug report for corrupt files
                if (IsDiskFullError(archiveEx))
                {
                    _ = bugReportService?.SendBugReportAsync($"Invalid or unrecognized archive format for file '{fileInfo.Name}' - DISK FULL", archiveEx);
                }
                LoggerService.LogWarning("HashCalculator", $"Invalid or unrecognized archive format for file '{fileInfo.Name}': {archiveEx.Message}");
                return
                [
                    new GameFile
                    {
                        FileName = fileInfo.Name,
                        GameName = Path.GetFileNameWithoutExtension(fileInfo.Name),
                        FileSize = fileInfo.Length,
                        ErrorMessage = "The archive file appears to be corrupted, incomplete, or in an unsupported format. The file may be damaged or not a valid archive.",
                        Crc32 = "ERROR",
                        Md5 = "ERROR",
                        Sha1 = "ERROR",
                        Sha256 = "ERROR"
                    }
                ];
            }
            catch (ExtractionFailedException archiveEx)
            {
                // Archive is corrupted or has data errors - do not send bug report for corrupt files
                if (IsDiskFullError(archiveEx))
                {
                    _ = bugReportService?.SendBugReportAsync($"Archive extraction failed for file '{fileInfo.Name}' - DISK FULL", archiveEx);
                }
                LoggerService.LogWarning("HashCalculator", $"Archive extraction failed for file '{fileInfo.Name}': {archiveEx.Message}");
                return
                [
                    new GameFile
                    {
                        FileName = fileInfo.Name,
                        GameName = Path.GetFileNameWithoutExtension(fileInfo.Name),
                        FileSize = fileInfo.Length,
                        ErrorMessage = "The archive is corrupted or has data errors. The file may be incomplete or damaged.",
                        Crc32 = "ERROR",
                        Md5 = "ERROR",
                        Sha1 = "ERROR",
                        Sha256 = "ERROR"
                    }
                ];
            }
            catch (SharpSevenZipException sevenZipEx)
            {
                // Internal SharpSevenZip error during extraction - do not send bug report for corrupt files
                if (IsDiskFullError(sevenZipEx))
                {
                    _ = bugReportService?.SendBugReportAsync($"SharpSevenZip internal error processing archive '{fileInfo.Name}' - DISK FULL", sevenZipEx);
                }
                LoggerService.LogWarning("HashCalculator", $"SharpSevenZip internal error processing archive '{fileInfo.Name}': {sevenZipEx.Message}");
                return
                [
                    new GameFile
                    {
                        FileName = fileInfo.Name,
                        GameName = Path.GetFileNameWithoutExtension(fileInfo.Name),
                        FileSize = fileInfo.Length,
                        ErrorMessage = "An internal error occurred while reading the archive. The file may be corrupted, partially downloaded, or use an unsupported compression method.",
                        Crc32 = "ERROR",
                        Md5 = "ERROR",
                        Sha1 = "ERROR",
                        Sha256 = "ERROR"
                    }
                ];
            }
            catch (Exception ex)
            {
                // Unexpected error - only send bug report for disk-full or truly unexpected errors
                if (IsDiskFullError(ex))
                {
                    _ = bugReportService?.SendBugReportAsync($"Unexpected archive extraction error for file '{fileInfo.Name}' - DISK FULL", ex);
                }
                LoggerService.LogException("HashCalculator", ex, $"Unexpected error processing archive '{fileInfo.Name}'");
                // Return an error object for the archive itself so the UI knows extraction failed.
                // We do NOT hash the container anymore.
                return
                [
                    new GameFile
                    {
                        FileName = fileInfo.Name,
                        GameName = Path.GetFileNameWithoutExtension(fileInfo.Name),
                        FileSize = fileInfo.Length,
                        ErrorMessage = $"An unexpected error occurred while processing the archive: {ex.Message}",
                        Crc32 = "ERROR",
                        Md5 = "ERROR",
                        Sha1 = "ERROR",
                        Sha256 = "ERROR"
                    }
                ];
            }
        }
        else
        {
            // Create algorithm instances once per file
            using var crc32 = new Crc32Algorithm();
            using var md5 = MD5.Create();
            using var sha1 = SHA1.Create();
            using var sha256 = SHA256.Create();

            // Process as regular file
            var gameFile = await ProcessFileAsync(
                filePath, fileInfo,
                crc32, md5, sha1, sha256,
                cancellationToken,
                bugReportService).ConfigureAwait(false);

            if (gameFile != null)
                gameFiles.Add(gameFile);

            return gameFiles;
        }
    }

    private static async Task<GameFile?> ProcessFileAsync(
        string filePath,
        FileInfo fileInfo,
        Crc32Algorithm crc32, MD5 md5, SHA1 sha1, SHA256 sha256,
        CancellationToken cancellationToken,
        BugReportService? bugReportService = null)
    {
        try
        {
            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, true);
            return await ProcessStreamAsync(
                fileStream,
                fileInfo.Name,
                crc32, md5, sha1, sha256,
                cancellationToken,
                bugReportService).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _ = bugReportService?.SendBugReportAsync($"Error processing file '{fileInfo.Name}'", ex);
            return new GameFile
            {
                FileName = fileInfo.Name,
                GameName = Path.GetFileNameWithoutExtension(fileInfo.Name),
                FileSize = fileInfo.Length,
                ErrorMessage = ex.Message,
                Crc32 = "ERROR",
                Md5 = "ERROR",
                Sha1 = "ERROR",
                Sha256 = "ERROR"
            };
        }
    }

    private static async Task<GameFile?> ProcessStreamAsync(
        Stream stream,
        string fileName,
        Crc32Algorithm crc32, MD5 md5, SHA1 sha1, SHA256 sha256,
        CancellationToken cancellationToken,
        BugReportService? bugReportService = null)
    {
        // Some streams may not support Length
        long fileSize = 0;
        try
        {
            if (stream.CanSeek)
            {
                fileSize = stream.Length;
            }
        }
        catch (NotSupportedException)
        {
            // Length not supported, leave as 0
        }

        var gameFile = new GameFile
        {
            FileName = fileName,
            GameName = Path.GetFileNameWithoutExtension(fileName),
            FileSize = fileSize
        };

        const int maxRetries = 3;
        var retryDelay = InitialRetryDelayMs;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Reinitialize algorithms for each retry attempt
                crc32.Initialize();
                md5.Initialize();
                sha1.Initialize();
                sha256.Initialize();

                // Use array instead of Span to allow usage across await boundaries
                HashAlgorithm[] algorithms = [crc32, md5, sha1, sha256];

                var buffer = new byte[BufferSize];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    foreach (var algorithm in algorithms)
                    {
                        algorithm.TransformBlock(buffer, 0, bytesRead, null, 0);
                    }
                }

                foreach (var algorithm in algorithms)
                {
                    algorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                }

                // Batch hex conversions using local function
                gameFile.Crc32 = ToHexLower(crc32.Hash);
                gameFile.Md5 = ToHexLower(md5.Hash);
                gameFile.Sha1 = ToHexLower(sha1.Hash);
                gameFile.Sha256 = ToHexLower(sha256.Hash);

                return gameFile;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException ex) when (IsAccessDeniedError(ex))
            {
                if (attempt == maxRetries - 1)
                {
                    gameFile.ErrorMessage = "File is locked or access denied after retries";
                    return gameFile;
                }

                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                retryDelay *= 2; // Exponential backoff

                // Reset stream position for retry (only works for seekable streams like FileStream)
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }
                else
                {
                    // Archive streams don't support seeking, so we can't retry
                    gameFile.ErrorMessage = "File access error (non-seekable stream)";
                    return gameFile;
                }
            }
            catch (Exception ex)
            {
                _ = bugReportService?.SendBugReportAsync($"Error hashing stream for file '{fileName}'", ex);
                gameFile.ErrorMessage = ex.Message;
                gameFile.Crc32 = "ERROR";
                gameFile.Md5 = "ERROR";
                gameFile.Sha1 = "ERROR";
                gameFile.Sha256 = "ERROR";
                return gameFile;
            }
        }

        _ = bugReportService?.SendBugReportAsync("Unexpected exit from retry loop in ProcessStreamAsync", new InvalidOperationException("Retry loop exceeded max attempts without returning or throwing"));
        gameFile.ErrorMessage = "Unexpected error during hash calculation";
        return gameFile;
    }

    internal static bool IsArchiveFile(string fileName)
    {
        return SArchiveExtensionRegex.IsMatch(fileName);
    }

    // Local function for efficient hex conversion
    private static string ToHexLower(byte[]? hash)
    {
        return hash == null ? string.Empty : Convert.ToHexStringLower(hash);
    }

    private static bool IsAccessDeniedError(IOException ex)
    {
        return ex.Message.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("the process cannot access the file", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\.(zip|7z|rar)$", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex MyRegex();
}
