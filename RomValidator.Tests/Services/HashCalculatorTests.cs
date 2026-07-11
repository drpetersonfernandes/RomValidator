using RomValidator.Services;
using Xunit;

namespace RomValidator.Tests.Services;

public class HashCalculatorTests
{
    [Theory]
    [InlineData("game.zip", true)]
    [InlineData("game.7z", true)]
    [InlineData("game.rar", true)]
    [InlineData("GAME.ZIP", true)]
    [InlineData("Game.7Z", true)]
    [InlineData("game.rom", false)]
    [InlineData("game.nes", false)]
    [InlineData("game.smc", false)]
    [InlineData("archive.zip.txt", false)]
    [InlineData("game", false)]
    public void IsArchiveFileDetectsArchiveExtensions(string fileName, bool expected)
    {
        // Act
        var result = HashCalculator.IsArchiveFile(fileName);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("archive.zipp", false)]
    [InlineData("archive.zi", false)]
    [InlineData(".zip", true)]
    [InlineData("path/to/game.zip", true)]
    [InlineData("C:\\Users\\test\\backup.7z", true)]
    [InlineData("archive.ZIP.PART", false)]
    [InlineData("game.nes.zip", true)]
    public void IsArchiveFileHandlesEdgeCases(string fileName, bool expected)
    {
        // Act
        var result = HashCalculator.IsArchiveFile(fileName);

        // Assert
        Assert.Equal(expected, result);
    }
}
