using System.Xml.Serialization;

namespace RomValidator.Models;

[XmlRoot("datafile")]
public class Datafile
{
    [XmlElement("header")]
    public Header? Header { get; set; }

    // Support both <game> and <machine> elements (MAME uses <machine>)
    [XmlElement("game")]
    public List<Game> Games { get; set; } = new();
}