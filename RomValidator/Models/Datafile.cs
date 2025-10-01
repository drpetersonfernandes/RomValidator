using System.Xml.Serialization;

namespace RomValidator.Models;

[XmlRoot("datafile")]
public class Datafile
{
    [XmlElement("header")]
    public Header? Header { get; set; }

    [XmlElement("game")]
    public Game[] Games { get; set; } = [];
}