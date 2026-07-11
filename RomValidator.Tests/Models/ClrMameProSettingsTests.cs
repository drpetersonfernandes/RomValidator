using RomValidator.Models.NoIntro;
using Xunit;

namespace RomValidator.Tests.Models;

public class ClrMameProSettingsTests
{
    [Fact]
    public void ClrMameProSettingsDefaultForceNoDumpIsRequired()
    {
        // Arrange & Act
        var settings = new ClrMameProSettings();

        // Assert
        Assert.Equal("required", settings.ForceNoDump);
    }

    [Fact]
    public void ClrMameProSettingsPropertiesCanBeSet()
    {
        // Arrange
        var settings = new ClrMameProSettings
        {
            ForceNoDump = "ignore"
        };

        // Assert
        Assert.Equal("ignore", settings.ForceNoDump);
    }

    [Theory]
    [InlineData("required")]
    [InlineData("ignore")]
    [InlineData("obsolete")]
    public void ClrMameProSettingsForceNoDumpAcceptsCommonValues(string value)
    {
        // Arrange
        var settings = new ClrMameProSettings { ForceNoDump = value };

        // Assert
        Assert.Equal(value, settings.ForceNoDump);
    }
}
