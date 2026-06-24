using RomValidator.Services;
using Xunit;

namespace RomValidator.Tests.Services;

public class HashCalculatorIntegrationTests
{
    [Fact]
    public async Task CalculateHashesAsyncReturnsCorrectHashesForKnownContent()
    {
        var testContent = "123456789"u8.ToArray();
        var tempFile = Path.GetTempFileName();

        try
        {
            await File.WriteAllBytesAsync(tempFile, testContent);

            var results = await HashCalculator.CalculateHashesAsync(tempFile, CancellationToken.None);

            Assert.Single(results);
            var gameFile = results[0];
            Assert.Equal(Path.GetFileName(tempFile), gameFile.FileName);
            Assert.Equal(9, gameFile.FileSize);
            Assert.Empty(gameFile.ErrorMessage ?? string.Empty);
            Assert.Equal("cbf43926", gameFile.Crc32);
            Assert.Equal("25f9e794323b453885f5181f1b624d0b", gameFile.Md5);
            Assert.Equal("f7c3bc1d808e04732adf679965ccc34ca7ae3441", gameFile.Sha1);
            Assert.Equal("15e2b0d3c33891ebb0f1ef609ec419420c20e320ce94c65fbc8c3312448eb225", gameFile.Sha256);
        }
        finally
        {
            try { File.Delete(tempFile); }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public async Task CalculateHashesAsyncEmptyFileReturnsZeroHashes()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            var results = await HashCalculator.CalculateHashesAsync(tempFile, CancellationToken.None);

            Assert.Single(results);
            var gameFile = results[0];
            Assert.Equal(0, gameFile.FileSize);
            Assert.Equal("00000000", gameFile.Crc32);
            Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", gameFile.Md5);
            Assert.Equal("da39a3ee5e6b4b0d3255bfef95601890afd80709", gameFile.Sha1);
            Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", gameFile.Sha256);
        }
        finally
        {
            try { File.Delete(tempFile); }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public async Task CalculateHashesAsyncLargeContentDoesNotThrow()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            var content = new byte[1024 * 1024]; // 1 MB
            new Random(42).NextBytes(content);
            await File.WriteAllBytesAsync(tempFile, content);

            var results = await HashCalculator.CalculateHashesAsync(tempFile, CancellationToken.None);

            Assert.Single(results);
            var gameFile = results[0];
            Assert.Equal(1024 * 1024, gameFile.FileSize);
            Assert.Empty(gameFile.ErrorMessage ?? string.Empty);
            Assert.NotEmpty(gameFile.Crc32);
            Assert.NotEmpty(gameFile.Md5);
            Assert.NotEmpty(gameFile.Sha1);
            Assert.NotEmpty(gameFile.Sha256);
            Assert.False(gameFile.Crc32.StartsWith("ERROR", StringComparison.Ordinal));
        }
        finally
        {
            try { File.Delete(tempFile); }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public void IsArchiveFileDetectsCommonExtensions()
    {
        Assert.True(HashCalculator.IsArchiveFile("game.zip"));
        Assert.True(HashCalculator.IsArchiveFile("game.7z"));
        Assert.True(HashCalculator.IsArchiveFile("game.rar"));
        Assert.True(HashCalculator.IsArchiveFile("GAME.ZIP"));
        Assert.False(HashCalculator.IsArchiveFile("game.nes"));
        Assert.False(HashCalculator.IsArchiveFile("game.smc"));
        Assert.False(HashCalculator.IsArchiveFile("game.rom"));
        Assert.False(HashCalculator.IsArchiveFile("game"));
    }

    [Fact]
    public async Task CalculateHashesAsyncHandlesCancellation()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            var content = new byte[10 * 1024 * 1024]; // 10 MB for slow-enough cancellation test
            await File.WriteAllBytesAsync(tempFile, content);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                HashCalculator.CalculateHashesAsync(tempFile, cts.Token));
        }
        finally
        {
            try { File.Delete(tempFile); }
            catch
            {
                // ignored
            }
        }
    }

    [Fact]
    public async Task CalculateHashesAsyncUnknownFileExtensionDoesNotCrash()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            var content = "Hello"u8.ToArray();
            await File.WriteAllBytesAsync(tempFile, content);

            var results = await HashCalculator.CalculateHashesAsync(tempFile, CancellationToken.None);

            Assert.Single(results);
            Assert.Equal(5, results[0].FileSize);
            Assert.Empty(results[0].ErrorMessage ?? string.Empty);
        }
        finally
        {
            try { File.Delete(tempFile); }
            catch
            {
                // ignored
            }
        }
    }
}
