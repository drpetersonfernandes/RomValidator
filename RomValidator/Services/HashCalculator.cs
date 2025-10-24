using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using RomValidator.Models;

namespace RomValidator.Services;

public static class HashCalculator
{
    public static async Task<GameFile> CalculateHashesAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var gameFile = new GameFile
        {
            FileName = fileInfo.Name,
            GameName = Path.GetFileNameWithoutExtension(fileInfo.Name),
            FileSize = fileInfo.Length
        };

        const int maxRetries = 3;
        var retryDelay = 100; // Initial delay in milliseconds

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                using var crc32 = new Crc32Algorithm();
                using var md5 = MD5.Create();
                using var sha1 = SHA1.Create();
                using var sha256 = SHA256.Create();

                var algorithms = new HashAlgorithm[] { crc32, md5, sha1, sha256 };

                await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);

                var buffer = new byte[65536];
                int bytesRead;
                while ((bytesRead = await fileStream.ReadAsync(buffer, cancellationToken)) > 0)
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
            catch (IOException ex) when (ex.Message.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
                                         ex.Message.Contains("the process cannot access the file", StringComparison.OrdinalIgnoreCase))
            {
                if (attempt == maxRetries - 1)
                {
                    gameFile.ErrorMessage = "File is locked or access denied after retries";
                    return gameFile;
                }

                await Task.Delay(retryDelay, cancellationToken);
                retryDelay *= 2; // Exponential backoff
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
