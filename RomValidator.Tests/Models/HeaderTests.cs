using RomValidator.Models.NoIntro;
using Xunit;

namespace RomValidator.Tests.Models;

public class HeaderTests
{
    [Fact]
    public void HeaderDefaultValuesAreSet()
    {
        // Arrange & Act
        var header = new Header();

        // Assert
        Assert.Null(header.Id);
        Assert.Equal(string.Empty, header.Name);
        Assert.Equal(string.Empty, header.Description);
        Assert.Equal(string.Empty, header.Version);
        Assert.Equal(string.Empty, header.Author);
        Assert.Equal("No-Intro", header.Homepage);
        Assert.Equal("https://www.no-intro.org", header.Url);
        Assert.Null(header.Date);
        Assert.Null(header.Retool);
        Assert.Null(header.Email);
        Assert.Null(header.Comment);
        Assert.Null(header.Category);
        Assert.Null(header.ClrMamePro);
    }

    [Fact]
    public void HeaderPropertiesCanBeSet()
    {
        // Arrange
        var header = new Header
        {
            Id = "dat-001",
            Name = "Nintendo - Nintendo Entertainment System",
            Description = "Nintendo NES ROM set",
            Version = "20240101-000000",
            Author = "No-Intro",
            Homepage = "https://datomatic.no-intro.org",
            Url = "https://datomatic.no-intro.org/download",
            Date = "2024-01-01",
            Retool = "v2.7.2",
            Email = "contact@no-intro.org",
            Comment = "Complete ROM set",
            Category = "Standard",
            ClrMamePro = new ClrMameProSettings { ForceNoDump = "ignore" }
        };

        // Assert
        Assert.Equal("dat-001", header.Id);
        Assert.Equal("Nintendo - Nintendo Entertainment System", header.Name);
        Assert.Equal("Nintendo NES ROM set", header.Description);
        Assert.Equal("20240101-000000", header.Version);
        Assert.Equal("No-Intro", header.Author);
        Assert.Equal("https://datomatic.no-intro.org", header.Homepage);
        Assert.Equal("https://datomatic.no-intro.org/download", header.Url);
        Assert.Equal("2024-01-01", header.Date);
        Assert.Equal("v2.7.2", header.Retool);
        Assert.Equal("contact@no-intro.org", header.Email);
        Assert.Equal("Complete ROM set", header.Comment);
        Assert.Equal("Standard", header.Category);
        Assert.NotNull(header.ClrMamePro);
        Assert.Equal("ignore", header.ClrMamePro.ForceNoDump);
    }

    [Fact]
    public void HeaderNullableFieldsDefaultToNull()
    {
        // Arrange & Act
        var header = new Header();

        // Assert - verify optional fields are null by default
        Assert.Null(header.Id);
        Assert.Null(header.Date);
        Assert.Null(header.Retool);
        Assert.Null(header.Email);
        Assert.Null(header.Comment);
        Assert.Null(header.Category);
        Assert.Null(header.ClrMamePro);
    }
}
