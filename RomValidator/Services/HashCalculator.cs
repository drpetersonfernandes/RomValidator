using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using RomValidator.Models;

namespace RomValidator.Services;

public static partial class HashCalculator
{
    // Archive file extensions supported
    private static readonly Regex ArchiveExtensionRegex = MyRegex();

    public static async Task<List<GameFile>> CalculateHashesAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var gameFiles = new List<GameFile>();

        // Check if file is an archive
        if (IsArchiveFile(fileInfo.Name))
        {
            try
            {
                // Open archive using SharpCompress (Supports Zip, 7z, Rar, Tar, etc.)
                using var archive = ArchiveFactory.Open(filePath);

                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;

                    // Create algorithm instances once per archive entry
                    using var crc32 = new Crc32Algorithm();
                    using var md5 = MD5.Create();
                    using var sha1 = SHA1.Create();
                    using var sha256 = SHA256.Create();

                    // OpenEntryStream provides a stream to the uncompressed data
                    await using var entryStream = entry.OpenEntryStream();

                    var gameFile = await ProcessStreamAsync(
                        entryStream,
                        entry.Key ?? "Unknown", // SharpCompress uses Key for filename
                        crc32, md5, sha1, sha256,
                        cancellationToken);

                    if (gameFile != null)
                        gameFiles.Add(gameFile);
                }

                return gameFiles;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
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
                cancellationToken);

            if (gameFile != null)
                gameFiles.Add(gameFile);

            return gameFiles;
        }
    }

    private static async Task<GameFile?> ProcessFileAsync(
        string filePath,
        FileInfo fileInfo,
        Crc32Algorithm crc32, MD5 md5, SHA1 sha1, SHA256 sha256,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            return await ProcessStreamAsync(
                fileStream,
                fileInfo.Name,
                crc32, md5, sha1, sha256,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw to allow proper cancellation handling upstream
        }
        catch (Exception ex)
        {
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
        CancellationToken cancellationToken)
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

                var algorithms = new HashAlgorithm[] { crc32, md5, sha1, sha256 };

                var buffer = new byte[65536];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
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

                gameFile.Crc32 = ToHexString(crc32.Hash);
                gameFile.Md5 = ToHexString(md5.Hash);
                gameFile.Sha1 = ToHexString(sha1.Hash);
                gameFile.Sha256 = ToHexString(sha256.Hash);

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

                await Task.Delay(retryDelay, cancellationToken);
                retryDelay *= 2; // Exponential backoff

                // Reset stream position for retry
                stream.Position = 0;
            }
            catch (Exception ex)
            {
                gameFile.ErrorMessage = ex.Message;
                gameFile.Crc32 = "ERROR";
                gameFile.Md5 = "ERROR";
                gameFile.Sha1 = "ERROR";
                gameFile.Sha256 = "ERROR";
                return gameFile;
            }
        }

        throw new InvalidOperationException("Unexpected exit from retry loop");
    }

    private static bool IsArchiveFile(string fileName)
    {
        return ArchiveExtensionRegex.IsMatch(fileName);
    }

    private static bool IsAccessDeniedError(IOException ex)
    {
        return ex.Message.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("the process cannot access the file", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToHexString(byte[]? bytes)
    {
        if (bytes == null) return string.Empty;

        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"\.(zip|7z|rar|gz|tar|bz2|xz|lzma|cab|iso|img|vhd|wim)$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "pt-BR")]
    private static partial Regex MyRegex();
}