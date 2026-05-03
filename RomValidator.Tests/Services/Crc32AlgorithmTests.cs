using System.Text;
using RomValidator.Services;
using Xunit;

namespace RomValidator.Tests.Services;

public class Crc32AlgorithmTests
{
    [Theory]
    [InlineData("", "00000000")]
    [InlineData("The quick brown fox jumps over the lazy dog", "414fa339")]
    [InlineData("123456789", "cbf43926")]
    [InlineData("Hello, World!", "ec4ac3d0")]
    public void ComputeHashReturnsExpectedCrc32(string input, string expectedHex)
    {
        // Arrange
        using var crc32 = new Crc32Algorithm();
        var bytes = Encoding.UTF8.GetBytes(input);

        // Act
        var hash = crc32.ComputeHash(bytes);
        var actualHex = Convert.ToHexStringLower(hash);

        // Assert
        Assert.Equal(expectedHex, actualHex);
    }

    [Fact]
    public void InitializeResetsHashState()
    {
        // Arrange
        using var crc32 = new Crc32Algorithm();
        var bytes = "test data"u8.ToArray();
        crc32.ComputeHash(bytes);

        // Act
        crc32.Initialize();
        var hash = crc32.ComputeHash(bytes);
        var actualHex = Convert.ToHexStringLower(hash);

        // Assert
        var expectedHash = new Crc32Algorithm().ComputeHash(bytes);
        Assert.Equal(Convert.ToHexStringLower(expectedHash), actualHex);
    }

    [Fact]
    public void HashSizeIs32Bits()
    {
        using var crc32 = new Crc32Algorithm();
        Assert.Equal(32, crc32.HashSize);
    }

    [Fact]
    public void ComputeHashReturns4Bytes()
    {
        using var crc32 = new Crc32Algorithm();
        var hash = crc32.ComputeHash([]);
        Assert.Equal(4, hash.Length);
    }
}
