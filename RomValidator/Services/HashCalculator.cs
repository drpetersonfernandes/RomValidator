using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SevenZip;
using RomValidator.Models;

namespace RomValidator.Services;

public static class HashCalculator
{
    // Archive file extensions supported by SevenZipSharp
    private static readonly Regex ArchiveExtensionRegex = new(
        @"\.(zip|7z|rar|gz|tar|bz2|xz|lzma|cab|iso|img|vhd|wim)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<List<GameFile>> CalculateHashesAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var gameFiles = new List<GameFile>();

        // Check if file is an archive (Fix for Issue C)
        if (IsArchiveFile(fileInfo.Name))
        {
            try
            {
                using var extractor = new SevenZipExtractor(filePath);
                
                // Process each file in the archive
                foreach (var archiveEntry in extractor.ArchiveFileData)
                {
                    if (archiveEntry.IsDirectory) continue;

                    using var entryStream = new MemoryStream();
                    extractor.ExtractFile(archiveEntry.Index, entryStream);
                    entryStream.Position = 0;

                    // Create algorithm instances once per archive entry (Fix for Issue B)
                    using var crc32 = new Crc32Algorithm();
                    using var md5 = MD5.Create();
                    using var sha1 = SHA1.Create();
                    using var sha256 = SHA256.Create();

                    var gameFile = await ProcessStreamAsync(
                        entryStream, 
                        archiveEntry.FileName,
                        crc32, md5, sha1, sha256,
                        cancellationToken);
                    
                    if (gameFile != null)
                        gameFiles.Add(gameFile);
                }
                
                return gameFiles;
            }
            catch (Exception ex)
            {
                // If archive processing fails, fall back to hashing the archive itself
                // but mark it with an error indicating it couldn't be extracted
                using var crc32 = new Crc32Algorithm();
                using var md5 = MD5.Create();
                using var sha1 = SHA1.Create();
                using var sha256 = SHA256.Create();

                var fallbackGameFile = await ProcessFileAsync(
                    filePath, fileInfo,
                    crc32, md5, sha1, sha256,
                    cancellationToken);
                
                if (fallbackGameFile != null)
                {
                    fallbackGameFile.ErrorMessage = $"Archive extraction failed: {ex.Message}. Hashed archive container instead of content.";
                    gameFiles.Add(fallbackGameFile);
                }
                
                return gameFiles;
            }
        }
        else
        {
            // Create algorithm instances once per file (Fix for Issue B)
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
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            return await ProcessStreamAsync(
                fileStream,
                fileInfo.Name,
                crc32, md5, sha1, sha256,
                cancellationToken);
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
}