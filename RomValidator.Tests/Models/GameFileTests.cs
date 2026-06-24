using RomValidator.Models;
using Xunit;

namespace RomValidator.Tests.Models;

public class GameFileTests
{
    [Fact]
    public void GameFileDefaultValuesAreEmpty()
    {
        // Arrange & Act
        var gameFile = new GameFile();

        // Assert
        Assert.Equal(string.Empty, gameFile.FileName);
        Assert.Equal(string.Empty, gameFile.GameName);
        Assert.Equal(string.Empty, gameFile.Crc32);
        Assert.Equal(string.Empty, gameFile.Md5);
        Assert.Equal(string.Empty, gameFile.Sha1);
        Assert.Equal(string.Empty, gameFile.Sha256);
        Assert.Equal(0, gameFile.FileSize);
        Assert.Null(gameFile.ErrorMessage);
        Assert.Null(gameFile.ArchiveFileName);
    }

    [Fact]
    public void GameFilePropertiesCanBeSet()
    {
        // Arrange
        var gameFile = new GameFile
        {
            FileName = "Super Mario Bros.nes",
            GameName = "Super Mario Bros",
            FileSize = 40960,
            Crc32 = "a1b2c3d4",
            Md5 = "d41d8cd98f00b204e9800998ecf8427e",
            Sha1 = "da39a3ee5e6b4b0d3255bfef95601890afd80709",
            Sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            ArchiveFileName = "games.zip"
        };

        // Assert
        Assert.Equal("Super Mario Bros.nes", gameFile.FileName);
        Assert.Equal("Super Mario Bros", gameFile.GameName);
        Assert.Equal(40960, gameFile.FileSize);
        Assert.Equal("a1b2c3d4", gameFile.Crc32);
        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", gameFile.Md5);
        Assert.Equal("da39a3ee5e6b4b0d3255bfef95601890afd80709", gameFile.Sha1);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", gameFile.Sha256);
        Assert.Equal("games.zip", gameFile.ArchiveFileName);
    }

    [Fact]
    public void GameFileErrorStateCombinesErrorAndArchive()
    {
        var gameFile = new GameFile
        {
            FileName = "corrupted.nes",
            GameName = "Corrupted Game",
            FileSize = 1024,
            ErrorMessage = "This file is corrupted or damaged within the archive",
            ArchiveFileName = "roms.zip",
            Crc32 = "ERROR",
            Md5 = "ERROR",
            Sha1 = "ERROR",
            Sha256 = "ERROR"
        };

        Assert.Equal("ERROR", gameFile.Crc32);
        Assert.Equal("ERROR", gameFile.Md5);
        Assert.Equal("roms.zip", gameFile.ArchiveFileName);
        Assert.NotNull(gameFile.ErrorMessage);
        Assert.Contains("corrupted", gameFile.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameFileHashFormatIsHexLowercase()
    {
        var gameFile = new GameFile
        {
            Crc32 = "cbf43926",
            Md5 = "25f9e794323b453885f5181f1b624d0b",
            Sha1 = "f7c3bc1d808e04732adf679965ccc34ca7ae3441",
            Sha256 = "15e2b0d3c33891ebb0f1ef609ec419420c20e320ce94c65fbc8c3312448eb225"
        };

        Assert.Equal(gameFile.Crc32, gameFile.Crc32.ToLowerInvariant());
        Assert.Equal(gameFile.Md5, gameFile.Md5.ToLowerInvariant());
        Assert.Equal(gameFile.Sha1, gameFile.Sha1.ToLowerInvariant());
        Assert.Equal(gameFile.Sha256, gameFile.Sha256.ToLowerInvariant());
    }
}
