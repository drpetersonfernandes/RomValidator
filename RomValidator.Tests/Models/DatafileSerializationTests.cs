using System.Xml.Serialization;
using RomValidator.Models.NoIntro;
using Xunit;

namespace RomValidator.Tests.Models;

public class DatafileSerializationTests
{
    [Fact]
    public void DatafileSerializesAndDeserializesCorrectly()
    {
        // Arrange
        var original = new Datafile
        {
            Header = new Header
            {
                Name = "Nintendo - Nintendo Entertainment System",
                Description = "Nintendo - Nintendo Entertainment System",
                Version = "20240101-000000",
                Author = "No-Intro",
                Homepage = "https://www.no-intro.org",
                Url = "https://www.no-intro.org"
            },
            Games =
            [
                new Game
                {
                    Name = "Super Mario Bros (USA)",
                    Id = "0001",
                    Description = "Super Mario Bros (USA)",
                    Roms =
                    [
                        new Rom
                        {
                            Name = "Super Mario Bros.nes",
                            Size = 40960,
                            Crc = "a1b2c3d4",
                            Md5 = "d41d8cd98f00b204e9800998ecf8427e",
                            Sha1 = "da39a3ee5e6b4b0d3255bfef95601890afd80709",
                            Sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
                        }
                    ]
                }
            ]
        };

        var serializer = new XmlSerializer(typeof(Datafile));

        // Act - Serialize
        string xml;
        using (var writer = new StringWriter())
        {
            serializer.Serialize(writer, original);
            xml = writer.ToString();
        }

        // Act - Deserialize
        Datafile? deserialized;
        using (var reader = new StringReader(xml))
        {
            deserialized = serializer.Deserialize(reader) as Datafile;
        }

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Header);
        Assert.Equal(original.Header.Name, deserialized.Header.Name);
        Assert.Equal(original.Header.Version, deserialized.Header.Version);
        Assert.Single(deserialized.Games);
        Assert.Equal(original.Games[0].Name, deserialized.Games[0].Name);
        Assert.Single(deserialized.Games[0].Roms);
        Assert.Equal(original.Games[0].Roms[0].Name, deserialized.Games[0].Roms[0].Name);
        Assert.Equal(original.Games[0].Roms[0].Size, deserialized.Games[0].Roms[0].Size);
        Assert.Equal(original.Games[0].Roms[0].Crc, deserialized.Games[0].Roms[0].Crc);
    }

    [Fact]
    public void DatafileDefaultSchemaLocationIsSet()
    {
        var datafile = new Datafile();
        Assert.Contains("no-intro.org", datafile.SchemaLocation);
    }
}
