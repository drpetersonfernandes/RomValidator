using RomValidator.Models.NoIntro;
using Xunit;

namespace RomValidator.Tests.Models;

public class RomTests
{
    [Fact]
    public void RomDefaultValuesAreEmpty()
    {
        // Arrange & Act
        var rom = new Rom();

        // Assert
        Assert.Equal(string.Empty, rom.Name);
        Assert.Equal(0, rom.Size);
        Assert.Equal(string.Empty, rom.Crc);
        Assert.Equal(string.Empty, rom.Md5);
        Assert.Equal(string.Empty, rom.Sha1);
        Assert.Equal(string.Empty, rom.Sha256);
        Assert.Null(rom.Status);
        Assert.Null(rom.Serial);
    }

    [Theory]
    [InlineData("1024", 1024, "1024")]
    [InlineData("0", 0, "0")]
    [InlineData("16777216", 16777216, "16777216")]
    [InlineData("-1", -1, "-1")] // Negative numbers parse successfully
    [InlineData("invalid", 0, "0")] // Invalid parses to 0, getter returns Size.ToString()
    [InlineData("", 0, "0")] // Empty parses to 0, getter returns Size.ToString()
    public void SizeStringParsingWorksCorrectly(string sizeString, long expectedSize, string expectedSizeString)
    {
        // Arrange
        var rom = new Rom { SizeString = sizeString };

        // Assert
        Assert.Equal(expectedSize, rom.Size);
        Assert.Equal(expectedSizeString, rom.SizeString);
    }

    [Fact]
    public void RomPropertiesCanBeSet()
    {
        // Arrange
        var rom = new Rom
        {
            Name = "Super Mario Bros.nes",
            Size = 40960,
            Crc = "a1b2c3d4",
            Md5 = "d41d8cd98f00b204e9800998ecf8427e",
            Sha1 = "da39a3ee5e6b4b0d3255bfef95601890afd80709",
            Sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            Status = "verified",
            Serial = "NES-SM-USA"
        };

        // Assert
        Assert.Equal("Super Mario Bros.nes", rom.Name);
        Assert.Equal(40960, rom.Size);
        Assert.Equal("a1b2c3d4", rom.Crc);
        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", rom.Md5);
        Assert.Equal("da39a3ee5e6b4b0d3255bfef95601890afd80709", rom.Sha1);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", rom.Sha256);
        Assert.Equal("verified", rom.Status);
        Assert.Equal("NES-SM-USA", rom.Serial);
    }
}
