using RomValidator.Services;
using Xunit;

namespace RomValidator.Tests;

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
    public void IsArchiveFile_DetectsArchiveExtensions(string fileName, bool expected)
    {
        // Act
        var result = HashCalculator.IsArchiveFile(fileName);

        // Assert
        Assert.Equal(expected, result);
    }
}
