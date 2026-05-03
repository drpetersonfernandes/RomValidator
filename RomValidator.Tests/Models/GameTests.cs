using RomValidator.Models.NoIntro;
using Xunit;

namespace RomValidator.Tests.Models;

public class GameTests
{
    [Fact]
    public void GameDefaultValuesAreEmpty()
    {
        // Arrange & Act
        var game = new Game();

        // Assert
        Assert.Equal(string.Empty, game.Name);
        Assert.Equal(string.Empty, game.Id);
        Assert.Null(game.CloneOfId);
        Assert.Equal(string.Empty, game.Description);
        Assert.Empty(game.Categories);
        Assert.Empty(game.Roms);
    }

    [Fact]
    public void GamePropertiesCanBeSet()
    {
        // Arrange
        var game = new Game
        {
            Name = "Super Mario Bros",
            Id = "0001",
            CloneOfId = "0000",
            Description = "Super Mario Bros (USA)",
            Categories = ["Platformer", "Action"],
            Roms =
            [
                new Rom { Name = "Super Mario Bros.nes", Size = 40960 }
            ]
        };

        // Assert
        Assert.Equal("Super Mario Bros", game.Name);
        Assert.Equal("0001", game.Id);
        Assert.Equal("0000", game.CloneOfId);
        Assert.Equal("Super Mario Bros (USA)", game.Description);
        Assert.Equal(2, game.Categories.Count);
        Assert.Single(game.Roms);
    }
}
