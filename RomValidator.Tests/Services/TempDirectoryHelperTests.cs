using RomValidator.Services;
using Xunit;

namespace RomValidator.Tests.Services;

public class TempDirectoryHelperTests
{
    [Fact]
    public void CreateTempDirectoryCreatesDirectory()
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
    public void CreateTempDirectoryWithDefaultPrefixCreatesDirectory()
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
    public void CleanupTempDirectoryDeletesDirectory()
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
    public void CleanupTempDirectoryNonExistentDirectoryDoesNotThrow()
    {
        // Arrange
        var nonExistentDir = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}");

        // Act & Assert - should not throw
        TempDirectoryHelper.CleanupTempDirectory(nonExistentDir);
    }

    [Fact]
    public void CreateTempDirectoryGeneratesUniquePaths()
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

    [Fact]
    public void GetAvailableFreeSpaceValidPathReturnsPositiveValue()
    {
        // Act
        var freeSpace = TempDirectoryHelper.GetAvailableFreeSpace(Path.GetTempPath());

        // Assert
        Assert.NotNull(freeSpace);
        Assert.True(freeSpace > 0, "Expected positive free space on temp drive");
    }

    [Fact]
    public void GetAvailableFreeSpaceInvalidPathReturnsNull()
    {
        // Act
        var freeSpace = TempDirectoryHelper.GetAvailableFreeSpace(@"\\invalid\path\that\does\not\exist");

        // Assert
        Assert.Null(freeSpace);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(1099511627776, "1 TB")]
    public void FormatBytesReturnsExpectedFormat(long bytes, string expected)
    {
        // Act
        var result = TempDirectoryHelper.FormatBytes(bytes);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindTempDirectoryWithSpaceSufficientSpaceReturnsDirectory()
    {
        // Arrange - request a tiny amount of space that should always be available
        var contextPath = Path.GetTempPath();

        // Act
        var tempDir = TempDirectoryHelper.FindTempDirectoryWithSpace(1, contextPath, out var warning);

        // Assert
        try
        {
            Assert.NotNull(tempDir);
            Assert.True(Directory.Exists(tempDir));
            Assert.Null(warning);
        }
        finally
        {
            if (tempDir != null && Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void FindTempDirectoryWithSpaceInsufficientSpaceReturnsNullWithWarning()
    {
        // Arrange - request more space than any drive can possibly have (1 exabyte)
        var contextPath = Path.GetTempPath();
        const long impossibleSpace = 1024L * 1024 * 1024 * 1024 * 1024 * 1024; // 1 EB

        // Act
        var tempDir = TempDirectoryHelper.FindTempDirectoryWithSpace(impossibleSpace, contextPath, out var warning);

        // Assert
        Assert.Null(tempDir);
        Assert.NotNull(warning);
        Assert.Contains("insufficient space", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CleanupAllTrackedDirectoriesRemovesRemainingDirectories()
    {
        // Arrange - create directories but do not clean them up individually
        var dir1 = TempDirectoryHelper.CreateTempDirectory("tracked1");
        var dir2 = TempDirectoryHelper.CreateTempDirectory("tracked2");
        Assert.True(Directory.Exists(dir1));
        Assert.True(Directory.Exists(dir2));

        // Act
        TempDirectoryHelper.CleanupAllTrackedDirectories();

        // Assert
        Assert.False(Directory.Exists(dir1));
        Assert.False(Directory.Exists(dir2));
    }
}
