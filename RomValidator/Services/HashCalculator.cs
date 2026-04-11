using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Common;
using RomValidator.Models;

namespace RomValidator.Services;

public static partial class HashCalculator
{
    // Archive file extensions supported - shared regex pattern (DUPLICATION fix)
    private static readonly Regex SArchiveExtensionRegex = MyRegex();

    public static async Task<List<GameFile>> CalculateHashesAsync(string filePath, CancellationToken cancellationToken, BugReportService? bugReportService = null)
    {
        var fileInfo = new FileInfo(filePath);
        var gameFiles = new List<GameFile>();

        // Check if file is an archive
        if (IsArchiveFile(fileInfo.Name))
        {
            try
            {
                // Open archive using SharpCompress (Supports Zip, 7z, Rar, Tar, etc.)
                using var archive = ArchiveFactory.OpenArchive(filePath);

                // Note: SharpCompress archive entries don't implement IDisposable,
                // so explicit disposal is not required. The 'using var archive' handles cleanup.
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;

                    // Create algorithm instances once per archive entry
                    using var crc32 = new Crc32Algorithm();
                    using var md5 = MD5.Create();
                    using var sha1 = SHA1.Create();
                    using var sha256 = SHA256.Create();

                    // OpenEntryStream provides a stream to the uncompressed data
                    await using var entryStream = await entry.OpenEntryStreamAsync(cancellationToken).ConfigureAwait(false);

                    var gameFile = await ProcessStreamAsync(
                        entryStream,
                        entry.Key ?? "Unknown", // SharpCompress uses Key for filename
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
            catch (Exception ex) when (ex is NotSupportedException && fileInfo?.Extension?.Equals(".zip", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Fallback: SharpCompress doesn't support this zip's compression method.
                // Extract to temp file using System.IO.Compression, then hash.
                return await ExtractAndHashZipFallbackAsync(filePath, fileInfo, cancellationToken, bugReportService).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (fileInfo != null)
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

        return gameFiles;
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
        var gameFile = new GameFile
        {
            FileName = fileName,
            GameName = Path.GetFileNameWithoutExtension(fileName),
            FileSize = stream.Length
        };

        const int maxRetries = 3;
        var retryDelay = 100; // Initial delay in milliseconds

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Reinitialize algorithms for each retry attempt (Fix for Issue B)
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

                // Batch hex conversions using local function (PERF fix)
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

    private static async Task<List<GameFile>> ExtractAndHashZipFallbackAsync(
        string filePath,
        FileInfo fileInfo,
        CancellationToken cancellationToken,
        BugReportService? bugReportService = null)
    {
        var gameFiles = new List<GameFile>();
        var tempDir = TempDirectoryHelper.CreateTempDirectory();

        try
        {
            // Fallback: use WriteToDirectory instead of OpenEntryStreamAsync
            // which supports a wider range of compression methods
            using var archive = ArchiveFactory.OpenArchive(filePath);
            archive.WriteToDirectory(tempDir, new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true
            });

            // Hash each extracted file
            foreach (var extractedFile in Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var crc32 = new Crc32Algorithm();
                using var md5 = MD5.Create();
                using var sha1 = SHA1.Create();
                using var sha256 = SHA256.Create();

                var relativePath = Path.GetRelativePath(tempDir, extractedFile);

                var gameFile = await ProcessFileAsync(
                    extractedFile, new FileInfo(extractedFile),
                    crc32, md5, sha1, sha256,
                    cancellationToken,
                    bugReportService).ConfigureAwait(false);

                if (gameFile != null)
                {
                    gameFile.FileName = relativePath;
                    gameFile.GameName = Path.GetFileNameWithoutExtension(relativePath);
                    gameFiles.Add(gameFile);
                }
            }

            return gameFiles;
        }
        catch (Exception ex)
        {
            _ = bugReportService?.SendBugReportAsync($"Zip fallback extraction failed for file '{fileInfo.Name}'", ex);
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
        finally
        {
            TempDirectoryHelper.CleanupTempDirectory(tempDir);
        }
    }

    internal static bool IsArchiveFile(string fileName)
    {
        return SArchiveExtensionRegex.IsMatch(fileName);
    }

    // Local function for efficient hex conversion (PERF fix)
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