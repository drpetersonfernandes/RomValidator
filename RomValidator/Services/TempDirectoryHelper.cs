using System.Globalization;
using System.IO;
using SharpSevenZip;

namespace RomValidator.Services;

/// <summary>
/// Helper class for managing temporary directories with automatic cleanup.
/// </summary>
public static class TempDirectoryHelper
{
    private static readonly HashSet<string> TrackedDirectories = [];
    private static readonly object TrackLock = new();
    private static readonly HashSet<string> PendingBackgroundRetries = [];
    private static readonly object RetryLock = new();
    private static Timer? _backgroundRetryTimer;

    /// <summary>
    /// Creates a temporary directory with a unique name.
    /// </summary>
    /// <param name="prefix">Prefix for the directory name.</param>
    /// <returns>The full path to the created temporary directory.</returns>
    public static string CreateTempDirectory(string prefix = "romvalidator")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        lock (TrackLock)
        {
            TrackedDirectories.Add(tempDir);
        }

        return tempDir;
    }

    /// <summary>
    /// Safely deletes a temporary directory with retry logic and background fallback.
    /// Logs warnings instead of throwing on failure.
    /// </summary>
    /// <param name="tempDir">Path to the temporary directory to delete.</param>
    public static async Task CleanupTempDirectoryAsync(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
            {
                await DeleteDirectoryWithRetryAsync(tempDir);
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogWarning("Cleanup", $"Failed to delete temp directory '{tempDir}': {ex.Message}");
            ScheduleBackgroundRetry(tempDir);
        }
        finally
        {
            lock (TrackLock)
            {
                TrackedDirectories.Remove(tempDir);
            }
        }
    }

    private static async Task DeleteDirectoryWithRetryAsync(string path)
    {
        var delayMs = 200;
        const int maxRetries = 8;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                Directory.Delete(path, true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt < maxRetries)
                {
                    await Task.Delay(delayMs);
                    delayMs = Math.Min(delayMs * 2, 10000);
                }
            }
        }

        await FallbackCleanupAsync(path);
    }

    private static Task FallbackCleanupAsync(string path)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        try
        {
            TryDeleteFilesIndividually(path);
        }
        catch
        {
            // Continue even if individual deletes fail
        }

        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
            LoggerService.LogWarning("Cleanup", $"Could not fully delete '{path}'. Scheduling background retries.");
            ScheduleBackgroundRetry(path);
        }

        return Task.CompletedTask;
    }

    private static void TryDeleteFilesIndividually(string path)
    {
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch
            {
                // Will be retried by background mechanism
            }
        }
    }

    private static void ScheduleBackgroundRetry(string path)
    {
        lock (RetryLock)
        {
            if (!PendingBackgroundRetries.Add(path) && _backgroundRetryTimer != null) return;

            _backgroundRetryTimer ??= new Timer(BackgroundRetryCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }
    }

    private static void BackgroundRetryCallback(object? state)
    {
        List<string> snapshot;
        lock (RetryLock)
        {
            snapshot = [..PendingBackgroundRetries];
        }

        foreach (var path in snapshot)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    lock (RetryLock) { PendingBackgroundRetries.Remove(path); }
                    continue;
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();

                TryDeleteFilesIndividually(path);
                Directory.Delete(path, true);

                lock (RetryLock) { PendingBackgroundRetries.Remove(path); }
                LoggerService.LogInfo("Cleanup", $"Background retry successfully deleted '{path}'.");
            }
            catch
            {
                // Will retry on next timer tick
            }
        }

        lock (RetryLock)
        {
            if (PendingBackgroundRetries.Count == 0)
            {
                _backgroundRetryTimer?.Dispose();
                _backgroundRetryTimer = null;
            }
        }
    }

    /// <summary>
    /// Cleans up all tracked temporary directories that have not yet been removed.
    /// </summary>
    public static async Task CleanupAllTrackedDirectoriesAsync()
    {
        List<string> toClean;
        lock (TrackLock)
        {
            toClean = [..TrackedDirectories];
        }

        foreach (var dir in toClean)
        {
            await CleanupTempDirectoryAsync(dir);
        }
    }

    /// <summary>
    /// Gets the available free space for the drive containing the specified path.
    /// </summary>
    /// <param name="path">A path on the drive to check.</param>
    /// <returns>The available free space in bytes, or null if the drive cannot be accessed.</returns>
    public static long? GetAvailableFreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return null;

            var drive = new DriveInfo(root);
            if (!drive.IsReady) return null;

            return drive.AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Finds or creates a temporary directory with sufficient free space.
    /// Tries the default temp path first, then the drive containing the context file,
    /// then all other available drives.
    /// </summary>
    /// <param name="requiredBytes">Minimum required free space in bytes.</param>
    /// <param name="contextPath">A file path whose drive should be preferred as a fallback.</param>
    /// <param name="warning">Outputs a warning message if the default drive had insufficient space.</param>
    /// <returns>The path to a created temporary directory, or null if no drive has enough space.</returns>
    public static string? FindTempDirectoryWithSpace(long requiredBytes, string contextPath, out string? warning)
    {
        warning = null;

        // 1. Try default temp path first
        var defaultTemp = Path.GetTempPath();
        var defaultSpace = GetAvailableFreeSpace(defaultTemp);
        if (defaultSpace >= requiredBytes)
        {
            return CreateTempDirectory();
        }

        warning = $"[WARNING] Default temp drive ({Path.GetPathRoot(defaultTemp)}) has insufficient space ({FormatBytes(defaultSpace ?? 0)} available, {FormatBytes(requiredBytes)} required).";

        // 2. Try the drive where the context file is located
        var contextDrive = Path.GetPathRoot(contextPath);
        if (!string.IsNullOrEmpty(contextDrive) &&
            !string.Equals(contextDrive, Path.GetPathRoot(defaultTemp), StringComparison.OrdinalIgnoreCase))
        {
            var contextSpace = GetAvailableFreeSpace(contextDrive);
            if (contextSpace >= requiredBytes)
            {
                var dir = CreateTempDirectoryInPath(contextDrive, "romvalidator");
                return dir;
            }

            warning += $" Context drive ({contextDrive}) also has insufficient space ({FormatBytes(contextSpace ?? 0)} available).";
        }

        // 3. Try all other ready drives
        foreach (var drive in DriveInfo.GetDrives().Where(static d => d.IsReady))
        {
            var driveRoot = drive.Name;
            if (string.Equals(driveRoot, Path.GetPathRoot(defaultTemp), StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(driveRoot, contextDrive, StringComparison.OrdinalIgnoreCase)) continue;

            if (drive.AvailableFreeSpace >= requiredBytes)
            {
                var dir = CreateTempDirectoryInPath(driveRoot, "romvalidator");
                return dir;
            }
        }

        warning += " No available drive has sufficient space.";
        return null;
    }

    /// <summary>
    /// Calculates the total uncompressed size of all files inside an archive.
    /// </summary>
    /// <param name="archivePath">Path to the archive file.</param>
    /// <returns>Total uncompressed size in bytes. Returns 0 if the archive cannot be read.</returns>
    public static long GetArchiveUncompressedSize(string archivePath)
    {
        try
        {
            HashCalculator.InitializeSevenZip();
            using var extractor = new SharpSevenZipExtractor(archivePath);
            return extractor.ArchiveFileData
                .Where(static e => !e.IsDirectory)
                .Sum(static e => (long)e.Size);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Formats a byte count into a human-readable string.
    /// </summary>
    /// <param name="bytes">Number of bytes.</param>
    /// <returns>Human-readable string such as "1.5 GB".</returns>
    public static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{len:0.##} {sizes[order]}");
    }

    /// <summary>
    /// Creates a temporary directory inside a specific path.
    /// </summary>
    private static string CreateTempDirectoryInPath(string basePath, string prefix)
    {
        var altTemp = Path.Combine(basePath, "RomValidatorTemp");
        Directory.CreateDirectory(altTemp);
        var tempDir = Path.Combine(altTemp, $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        lock (TrackLock)
        {
            TrackedDirectories.Add(tempDir);
        }

        return tempDir;
    }
}
