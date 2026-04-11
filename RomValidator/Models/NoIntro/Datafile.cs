using System.Xml.Serialization;

namespace RomValidator.Models.NoIntro;

[XmlRoot("datafile")]
public class Datafile
{
    [XmlElement("header")]
    public Header? Header { get; set; }

    [XmlElement("game")]
    public List<Game> Games { get; set; } = new();
}
