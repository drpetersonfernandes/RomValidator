using System.Security.Cryptography;
using System.Xml;
using System.Xml.Serialization;

namespace RomValidator;

file static class Program
{
    // Use C# 12 collection expression for initialization.
    private static Dictionary<string, Rom> _romDatabase = [];
    private static int _successCount;
    private static int _failCount;
    private static int _unknownCount;

    // A lock object to prevent console output from getting mixed up during parallel processing.
    private static readonly Lock ConsoleLock = new();

    private static async Task Main(string[] args)
    {
        Console.Title = "ROM File Validator";
        PrintHeader();

        // --- 1. Get User Input ---
        var datFilePath = GetInput("Enter the full path to your .dat file: ", File.Exists);
        var scanFolderPath = GetInput("Enter the full path to the folder you want to scan: ", Directory.Exists);

        // --- 2. Load and Parse the DAT File ---
        if (!LoadDatFile(datFilePath))
        {
            ExitWithError("Failed to load or parse the DAT file. Exiting.");
            return;
        }

        // --- 3. Prepare Output Directories ---
        var successPath = Path.Combine(scanFolderPath, "_success");
        var failPath = Path.Combine(scanFolderPath, "_fail");
        Directory.CreateDirectory(successPath);
        Directory.CreateDirectory(failPath);

        // --- 4. Process Files in Parallel ---
        Console.WriteLine("\nStarting file validation process (in parallel)...");
        var filesToScan = Directory.GetFiles(scanFolderPath);

        // MODERNIZATION: Use Parallel.ForEachAsync to process files concurrently.
        await Parallel.ForEachAsync(filesToScan, async (filePath, cancellationToken) =>
        {
            await ProcessFileAsync(filePath, successPath, failPath);
        });

        // --- 5. Print Summary ---
        PrintSummary();
    }

    /// <summary>
    /// Processes a single file: validates it and moves it to the appropriate folder.
    /// This method is now designed to be called from multiple threads.
    /// </summary>
    private static async Task ProcessFileAsync(string filePath, string successPath, string failPath)
    {
        var fileName = Path.GetFileName(filePath);

        // The logic for checking a single file remains the same.
        if (!_romDatabase.TryGetValue(fileName, out var expectedRom))
        {
            // MODERNIZATION: Use Interlocked.Increment for thread-safe counting.
            Interlocked.Increment(ref _unknownCount);
            LogResult(fileName, "UNKNOWN. Not found in DAT file.", ConsoleColor.Yellow);
            MoveFile(filePath, Path.Combine(failPath, fileName), "fail");
            return;
        }

        var fileInfo = new FileInfo(filePath);

        if (fileInfo.Length != expectedRom.Size)
        {
            Interlocked.Increment(ref _failCount);
            LogResult(fileName, $"FAILED (Size mismatch. Expected: {expectedRom.Size}, Got: {fileInfo.Length})", ConsoleColor.Red, true);
            MoveFile(filePath, Path.Combine(failPath, fileName), "fail");
            return;
        }

        // --- Hash Check ---
        var hashMatch = false;
        var matchDetails = "FAILED (Hash mismatch)"; // Default to fail

        if (!string.IsNullOrEmpty(expectedRom.Sha1))
        {
            var actualSha1 = await ComputeHashAsync(filePath, SHA1.Create());
            if (actualSha1.Equals(expectedRom.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                hashMatch = true;
                matchDetails = $"SUCCESS (SHA1: {actualSha1})";
            }
        }

        if (!hashMatch && !string.IsNullOrEmpty(expectedRom.Md5))
        {
            var actualMd5 = await ComputeHashAsync(filePath, MD5.Create());
            if (actualMd5.Equals(expectedRom.Md5, StringComparison.OrdinalIgnoreCase))
            {
                hashMatch = true;
                matchDetails = $"SUCCESS (MD5: {actualMd5})";
            }
        }

        if (!hashMatch && !string.IsNullOrEmpty(expectedRom.Crc))
        {
            var actualCrc = await ComputeCrc32Async(filePath);
            if (actualCrc.Equals(expectedRom.Crc, StringComparison.OrdinalIgnoreCase))
            {
                hashMatch = true;
                matchDetails = $"SUCCESS (CRC32: {actualCrc})";
            }
        }

        // --- Final Result and File Move ---
        if (hashMatch)
        {
            Interlocked.Increment(ref _successCount);
            LogResult(fileName, matchDetails, ConsoleColor.Green, true);
            MoveFile(filePath, Path.Combine(successPath, fileName), "success");
        }
        else
        {
            Interlocked.Increment(ref _failCount);
            LogResult(fileName, matchDetails, ConsoleColor.Red, true);
            MoveFile(filePath, Path.Combine(failPath, fileName), "fail");
        }
    }

    private static bool LoadDatFile(string datFilePath)
    {
        try
        {
            Console.WriteLine("Loading and parsing DAT file...");
            var serializer = new XmlSerializer(typeof(Datafile));

            using var fileStream = new FileStream(datFilePath, FileMode.Open);
            using var xmlReader = XmlReader.Create(fileStream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null
            });

            var datafile = (Datafile?)serializer.Deserialize(xmlReader);

            if (datafile?.Games is null) return false;

            // MODERNIZATION: Use LINQ to build the dictionary.
            // This is more expressive and handles potential duplicate rom names in the DAT file gracefully.
            _romDatabase = datafile.Games
                .Where(static g => g.Rom is { Name.Length: > 0 }) // Pattern matching for non-null/empty rom
                .GroupBy(static g => g.Rom.Name, StringComparer.OrdinalIgnoreCase) // Group by name to handle duplicates
                .ToDictionary(static g => g.Key, static g => g.First().Rom); // Take the first entry for each name

            WriteColorLine($"Successfully loaded {_romDatabase.Count} unique ROM entries from '{datafile.Header?.Name}'.", ConsoleColor.Green);
            return true;
        }
        catch (Exception ex)
        {
            WriteColorLine($"Error reading DAT file: {ex.Message}", ConsoleColor.Red);
            return false;
        }
    }

    // The hashing and file move methods are already modern and efficient, no changes needed.

    #region Unchanged Helper Methods

    private static async Task<string> ComputeHashAsync(string filePath, HashAlgorithm algorithm)
    {
        using (algorithm)
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            var hashBytes = await algorithm.ComputeHashAsync(stream);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }

    private static async Task<string> ComputeCrc32Async(string filePath)
    {
        var crc32 = new System.IO.Hashing.Crc32();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);

        var buffer = new byte[4096];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
        {
            crc32.Append(buffer.AsSpan(0, bytesRead));
        }

        var hashBytes = crc32.GetCurrentHash();
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }


    private static void MoveFile(string sourcePath, string destPath, string category)
    {
        try
        {
            File.Move(sourcePath, destPath, true);
        }
        catch (Exception ex)
        {
            // Use the lock to safely write the error message.
            lock (ConsoleLock)
            {
                WriteColorLine($"   Action: FAILED to move {Path.GetFileName(sourcePath)}. Error: {ex.Message}", ConsoleColor.DarkRed);
            }
        }
    }

    #endregion

    #region UI and Logging Helpers

    /// <summary>
    /// Logs the result of a file validation to the console in a thread-safe manner.
    /// </summary>
    private static void LogResult(string fileName, string message, ConsoleColor color, bool showSizeMatch = false)
    {
        // MODERNIZATION: Lock the console to prevent interleaved output from multiple threads.
        lock (ConsoleLock)
        {
            Console.WriteLine($"\n-> Processing: {fileName}");
            if (showSizeMatch)
            {
                WriteColorLine("   Size: MATCH", ConsoleColor.Green);
            }

            WriteColorLine($"   Status: {message}", color);
        }
    }

    private static void PrintHeader()
    {
        Console.Clear();
        WriteColorLine("========================================", ConsoleColor.DarkCyan);
        WriteColorLine("==   Modern No-Intro ROM Validator    ==", ConsoleColor.White);
        WriteColorLine("========================================", ConsoleColor.DarkCyan);
        Console.WriteLine();
    }

    private static void PrintSummary()
    {
        Console.WriteLine("\n================= SUMMARY =================");
        WriteColorLine($"  SUCCESS: {_successCount}", ConsoleColor.Green);
        WriteColorLine($"  FAILED:  {_failCount}", ConsoleColor.Red);
        WriteColorLine($"  UNKNOWN: {_unknownCount} (moved to fail folder)", ConsoleColor.Yellow);
        Console.WriteLine("=========================================");
        Console.WriteLine("\nValidation complete. Press any key to exit.");
        Console.ReadKey();
    }

    private static string GetInput(string prompt, Func<string, bool> validator)
    {
        string? input;
        do
        {
            Console.Write(prompt);
            input = Console.ReadLine()?.Trim().Trim('"');
            if (!string.IsNullOrEmpty(input) && validator(input)) continue;

            WriteColorLine("Invalid path. Please try again.", ConsoleColor.Red);
            input = null;
        } while (input == null);

        return input;
    }

    private static void ExitWithError(string message)
    {
        WriteColorLine(message, ConsoleColor.Red);
        Console.WriteLine("Press any key to exit.");
        Console.ReadKey();
    }

    private static void WriteColorLine(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    #endregion
}