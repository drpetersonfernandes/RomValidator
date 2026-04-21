using RomValidator.Services;
using Xunit;

namespace RomValidator.Tests;

public class TempDirectoryHelperTests
{
    [Fact]
    public void CreateTempDirectory_CreatesDirectory()
    {
        // Act
        var tempDir = TempDirectoryHelper.CreateTempDirectory("test");

        // Assert
        try
        {
            Assert.True(Directory.Exists(tempDir));
            Assert.Contains("test_", tempDir);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void CreateTempDirectory_WithDefaultPrefix_CreatesDirectory()
    {
        // Act
        var tempDir = TempDirectoryHelper.CreateTempDirectory();

        // Assert
        try
        {
            Assert.True(Directory.Exists(tempDir));
            Assert.Contains("romvalidator_", tempDir);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void CleanupTempDirectory_DeletesDirectory()
    {
        // Arrange
        var tempDir = TempDirectoryHelper.CreateTempDirectory("cleanup_test");
        Assert.True(Directory.Exists(tempDir));

        // Act
        TempDirectoryHelper.CleanupTempDirectory(tempDir);

        // Assert
        Assert.False(Directory.Exists(tempDir));
    }

    [Fact]
    public void CleanupTempDirectory_NonExistentDirectory_DoesNotThrow()
    {
        // Arrange
        var nonExistentDir = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}");

        // Act & Assert - should not throw
        TempDirectoryHelper.CleanupTempDirectory(nonExistentDir);
    }

    [Fact]
    public void CreateTempDirectory_GeneratesUniquePaths()
    {
        // Act
        var dir1 = TempDirectoryHelper.CreateTempDirectory("unique");
        var dir2 = TempDirectoryHelper.CreateTempDirectory("unique");

        // Assert
        try
        {
            Assert.NotEqual(dir1, dir2);
            Assert.True(Directory.Exists(dir1));
            Assert.True(Directory.Exists(dir2));
        }
        finally
        {
            Directory.Delete(dir1, true);
            Directory.Delete(dir2, true);
        }
    }
}
