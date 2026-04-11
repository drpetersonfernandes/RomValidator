using System.IO;

namespace RomValidator.Services;

/// <summary>
/// Helper class for managing temporary directories with automatic cleanup.
/// </summary>
public static class TempDirectoryHelper
{
    /// <summary>
    /// Creates a temporary directory with a unique name.
    /// </summary>
    /// <param name="prefix">Prefix for the directory name.</param>
    /// <returns>The full path to the created temporary directory.</returns>
    public static string CreateTempDirectory(string prefix = "romvalidator")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    /// <summary>
    /// Safely deletes a temporary directory, logging any errors instead of throwing.
    /// </summary>
    /// <param name="tempDir">Path to the temporary directory to delete.</param>
    public static void CleanupTempDirectory(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogError("Cleanup", $"Failed to delete temp directory '{tempDir}': {ex.Message}");
        }
    }
}
