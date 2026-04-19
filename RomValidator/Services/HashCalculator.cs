using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SharpSevenZip;
using SharpSevenZip.Exceptions;
using RomValidator.Models;

namespace RomValidator.Services;

public static partial class HashCalculator
{
    // Archive file extensions supported - shared regex pattern
    private static readonly Regex SArchiveExtensionRegex = MyRegex();
    private static bool _sevenZipInitialized;
    private static readonly object InitLock = new();

    /// <summary>
    /// Initializes SharpSevenZip by setting the library path.
    /// This is called from App.xaml.cs at startup, but this method ensures
    /// it's initialized before any archive operations if needed.
    /// </summary>
    public static void InitializeSevenZip()
    {
        lock (InitLock)
        {
            if (_sevenZipInitialized) return;
        }

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
                        // Individual entry is corrupted - log it but continue with other files
                        // Send bug report for tracking during development
                        _ = bugReportService?.SendBugReportAsync($"Archive entry extraction failed for '{entry.FileName}' in archive", entryEx);
                        gameFiles.Add(new GameFile
                        {
                            FileName = entry.FileName,
                            GameName = Path.GetFileNameWithoutExtension(entry.FileName),
                            FileSize = (long)entry.Size,
                            ErrorMessage = "File is corrupted within archive",
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
                        gameFiles.Add(gameFile);
                }

                return gameFiles;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ExtractionFailedException archiveEx)
            {
                // Archive is corrupted or has data errors
                // Send bug report for tracking during development
                _ = bugReportService?.SendBugReportAsync($"Archive extraction failed for file '{fileInfo.Name}'", archiveEx);
                return
                [
                    new GameFile
                    {
                        FileName = fileInfo.Name,
                        GameName = Path.GetFileNameWithoutExtension(fileInfo.Name),
                        FileSize = fileInfo.Length,
                        ErrorMessage = "Archive is corrupted or has data errors",
                        Crc32 = "ERROR",
                        Md5 = "ERROR",
                        Sha1 = "ERROR",
                        Sha256 = "ERROR"
                    }
                ];
            }
            catch (Exception ex)
            {
                _ = bugReportService?.SendBugReportAsync($"Archive extraction failed for file '{fileInfo.Name}'", ex);
                // Return an error object for the archive itself so the UI knows extraction failed.
                // We do NOT hash the container anymore.
                return
                [
                    new GameFile
                    {
                        FileName = fileInfo.Name,
                        GameName = Path.GetFileNameWithoutExtension(fileInfo.Name),
                        FileSize = fileInfo.Length,
                        ErrorMessage = $"Archive extraction failed: {ex.Message}",
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
            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            return await ProcessStreamAsync(
                fileStream,
                fileInfo.Name,
                crc32, md5, sha1, sha256,
                cancellationToken,
                bugReportService).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw to allow proper cancellation handling upstream
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
        var retryDelay = 100; // Initial delay in milliseconds

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

                var buffer = new byte[65536];
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
                throw; // Re-throw cancellation so it propagates to the UI
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
        return Convert.ToHexStringLower(hash ?? []);
    }

    private static bool IsAccessDeniedError(IOException ex)
    {
        return ex.Message.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("the process cannot access the file", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\.(zip|7z|rar|gz|tar|bz2|xz|lzma|cab|iso|img|vhd|wim)$", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex MyRegex();
}
